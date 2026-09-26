import { describe, expect, it } from "vitest";
import worker from "../src/index";
import { D1Limiter, WindowLimiter, type RateLimiter } from "../src/ratelimit";
import { issueSessionToken, verifySessionToken } from "../src/session";
import type { Env } from "../src/types";

// 라우터 - DB·스팀까지 안 가는 경로만: 입력 검사, 인증, 레이트리밋(429). D1 이 필요한 경로는 로컬 워커 + curl 로 본다
// (server/README.md).

const STEAM_ID = "76561198000000001";

class FakeLimiter implements RateLimiter {
  keys: string[] = [];
  constructor(private readonly allowUntil: number) {}
  async limit({ key }: { key: string }) {
    this.keys.push(key);
    return { success: this.keys.length <= this.allowUntil };
  }
}

function env(over: Partial<Env> = {}): Env {
  return {
    DB: undefined as unknown as D1Database,   // 여기서 부르는 경로는 DB 까지 안 간다
    STEAM_PUBLISHER_WEB_API_KEY: "test-key",
    STEAM_APP_ID: "5281130",
    SESSION_SIGNING_SECRET: "test-secret",
    ...over,
  };
}

const base = "https://economy.test";

describe("세션 토큰", () => {
  it("발급한 토큰은 검증된다", async () => {
    const { token } = await issueSessionToken(env(), STEAM_ID);
    expect(await verifySessionToken(env(), token)).toEqual({ ok: true, steamId: STEAM_ID });
  });

  it("다른 키로 서명한 토큰·손댄 토큰·만료된 토큰은 거절", async () => {
    const { token } = await issueSessionToken(env({ SESSION_SIGNING_SECRET: "other" }), STEAM_ID);
    expect((await verifySessionToken(env(), token)).ok).toBe(false);

    const good = (await issueSessionToken(env(), STEAM_ID)).token;
    const [payload, sig] = good.split(".");
    const forged = `${btoa(`76561198999999999.${Date.now() + 3_600_000}`).replace(/=+$/, "")}.${sig}`;
    expect((await verifySessionToken(env(), forged)).ok).toBe(false);
    expect(payload).toBeTruthy();

    const expired = (await issueSessionToken(env(), STEAM_ID, -10)).token;
    expect((await verifySessionToken(env(), expired)).ok).toBe(false);
  });
});

describe("라우터", () => {
  it("티켓 없는 세션 요청은 400", async () => {
    const res = await worker.fetch(new Request(`${base}/v1/session`, { method: "POST", body: "{}" }), env());
    expect(res.status).toBe(400);
  });

  it("토큰 없는 API 요청은 401", async () => {
    const res = await worker.fetch(new Request(`${base}/v1/economy/state`), env());
    expect(res.status).toBe(401);
    expect(await res.json()).toEqual({ error: "missing_token" });
  });

  it("세션 발급은 IP 기준 레이트리밋 - 넘으면 429 + retry-after", async () => {
    const limiter = new FakeLimiter(0);
    const req = new Request(`${base}/v1/session`, {
      method: "POST",
      body: JSON.stringify({ ticket: "00" }),
      headers: { "CF-Connecting-IP": "203.0.113.7" },
    });
    const res = await worker.fetch(req, env({ SESSION_LIMITER: limiter }));
    expect(res.status).toBe(429);
    expect(res.headers.get("retry-after")).toBe("60");
    expect(limiter.keys).toEqual(["ip:203.0.113.7"]);
  });

  it("API 는 스팀 ID 기준 레이트리밋 - 인증 뒤, DB 에 가기 전에 막는다", async () => {
    const limiter = new FakeLimiter(0);
    const { token } = await issueSessionToken(env(), STEAM_ID);
    const req = new Request(`${base}/v1/economy/state`, { headers: { authorization: `Bearer ${token}` } });
    const res = await worker.fetch(req, env({ API_LIMITER: limiter }));
    expect(res.status).toBe(429);
    expect(limiter.keys).toEqual([`steam:${STEAM_ID}`]);
  });

  it("인증 실패는 레이트리밋을 세지 않는다 (남의 토큰 없는 요청이 내 한도를 못 쓴다)", async () => {
    const limiter = new FakeLimiter(0);
    const res = await worker.fetch(new Request(`${base}/v1/economy/state`), env({ API_LIMITER: limiter }));
    expect(res.status).toBe(401);
    expect(limiter.keys).toEqual([]);
  });

  it("바인딩이 실패하면 막지 않는다", async () => {
    const broken: RateLimiter = { limit: async () => { throw new Error("down"); } };
    const res = await worker.fetch(new Request(`${base}/v1/session`, { method: "POST", body: "{}" }),
      env({ SESSION_LIMITER: broken }));
    expect(res.status).toBe(400);   // 레이트리밋을 지나 입력 검사까지 갔다
  });
});

describe("WindowLimiter (인스턴스 카운터)", () => {
  it("창 안에서 max 번까지 허용하고 넘으면 거절, 창이 지나면 다시 센다", async () => {
    let t = 0;
    const lim = new WindowLimiter(3, 1000, 100, () => t);
    const results = [];
    for (let i = 0; i < 5; i++) {
      results.push((await lim.limit({ key: "a" })).success);
    }

    expect(results).toEqual([true, true, true, false, false]);
    expect((await lim.limit({ key: "b" })).success).toBe(true);   // 키마다 따로
    t = 1000;
    expect((await lim.limit({ key: "a" })).success).toBe(true);
  });

  it("키가 상한을 넘으면 비워서 메모리가 안 큰다", async () => {
    let t = 0;
    const lim = new WindowLimiter(1, 1000, 3, () => t);
    for (const k of ["a", "b", "c", "d"]) {
      await lim.limit({ key: k });
    }

    expect((await lim.limit({ key: "a" })).success).toBe(true);   // 비워졌으니 a 는 새로 센다
  });
});

/** D1 의 upsert 한 문장만 흉내 낸다 - D1Limiter 가 쓰는 SQL 의 뜻(창이 지나면 1 부터, 아니면 +1). */
function fakeDb() {
  const rows = new Map<string, { window_start: number; count: number }>();
  return {
    rows,
    prepare(sql: string) {
      return {
        bind(...args: unknown[]) {
          return {
            async first() {
              const [key, t, period] = args as [string, number, number];
              const r = rows.get(key);
              if (!r || t - r.window_start >= period) {
                rows.set(key, { window_start: t, count: 1 });
              } else {
                r.count++;
              }

              return { count: rows.get(key)!.count };
            },
            async run() {
              expect(sql).toContain("DELETE");
            },
          };
        },
      };
    },
  };
}

describe("D1Limiter (세션 발급)", () => {
  it("창 안에서 max 번까지, 넘으면 거절, 창이 지나면 다시", async () => {
    let t = 0;
    const db = fakeDb();
    const lim = new D1Limiter(db as unknown as D1Database, 2, 1000, () => t);
    const r = [];
    for (let i = 0; i < 3; i++) {
      r.push((await lim.limit({ key: "ip:1" })).success);
    }

    expect(r).toEqual([true, true, false]);
    t = 1000;
    expect((await lim.limit({ key: "ip:1" })).success).toBe(true);
  });

  it("표가 없어도(마이그레이션 전) 세션 발급이 죽지 않는다 - 통과", async () => {
    const res = await worker.fetch(new Request(`${base}/v1/session`, { method: "POST", body: "{}" }), env());
    expect(res.status).toBe(400);
  });
});

