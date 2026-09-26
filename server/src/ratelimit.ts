// 레이트리밋 (docs/ECONOMY-SERVER-API.md §3 의 429).
//
// 두 겹이다. **둘 다 허용해야 통과한다.**
//  1. Cloudflare Workers Rate Limiting 바인딩 (wrangler.toml 의 [[unsafe.bindings]] type = "ratelimit").
//  2. 워커 인스턴스 안에서 직접 세는 창(window) 카운터 — WindowLimiter.
//
// 2 를 둔 이유: 2026-09-26 배포해서 재 보니 **바인딩은 붙어 있는데 1분에 57번을 보내도 늘 success=true** 였다(진단 로그로
// 확인). 바인딩은 위치별 근사치이고 요금제에 따라 다르게 도는 것으로 보인다. 인스턴스 카운터도 인스턴스마다 따로 세는 근사치지만,
// 한 클라이언트가 몰아 보내는 요청은 같은 인스턴스로 가서 실제로 막힌다(배포 후 12번째부터 429 확인). D1 카운터는 쓰지 않는다 -
// 가장 잦은 요청(수확)마다 쓰기가 한 번 더 붙는다.
//
// 두 종류:
//  - 세션 발급(/v1/session) — IP 기준, 분당 30 (IP 하나를 여럿이 쓰는 PC방·회사를 생각해 10 에서 올렸다). **여기만 D1 카운터로 정확히 센다**(D1Limiter - 아래). 요청마다 밸브 AuthenticateUserTicket 을 부르므로, 막지 않으면 우리 Web API 키의
//    밸브 쪽 한도가 남의 요청으로 닳는다.
//  - 나머지 전부 — 스팀 ID 기준, 분당 120. 정상 플레이(수확·구매·재동기화)는 분당 수십 번을 안 넘는다.
//
// 바인딩이 없거나 실패하면 그 겹은 통과시킨다 — 레이트리밋 때문에 서버가 멈추면 안 된다.

/** Workers Rate Limiting 바인딩 중 여기서 쓰는 부분. WindowLimiter 도 같은 모양이다. */
export interface RateLimiter {
  limit(options: { key: string }): Promise<{ success: boolean }>;
}

/**
 * 키마다 고정 창(periodMs) 안의 요청 수를 센다. 워커 인스턴스 하나의 메모리 — 인스턴스가 바뀌면 처음부터 센다.
 * 키가 maxKeys 를 넘으면 창이 끝난 키부터, 그래도 넘으면 전부 비운다(메모리가 계속 크지 않게).
 */
export class WindowLimiter implements RateLimiter {
  private readonly windows = new Map<string, { start: number; count: number }>();

  constructor(
    private readonly max: number,
    private readonly periodMs: number,
    private readonly maxKeys = 10_000,
    private readonly now: () => number = Date.now,
  ) {}

  async limit({ key }: { key: string }): Promise<{ success: boolean }> {
    const t = this.now();
    let w = this.windows.get(key);
    if (!w || t - w.start >= this.periodMs) {
      if (!w && this.windows.size >= this.maxKeys) {
        this.sweep(t);
      }

      w = { start: t, count: 0 };
      this.windows.set(key, w);
    }

    w.count++;
    return { success: w.count <= this.max };
  }

  private sweep(t: number): void {
    for (const [k, w] of this.windows) {
      if (t - w.start >= this.periodMs) {
        this.windows.delete(k);
      }
    }

    if (this.windows.size >= this.maxKeys) {
      this.windows.clear();
    }
  }
}

export const SESSION_LIMIT = 30;   // PC방·회사처럼 IP 하나를 여럿이 쓰는 곳 - 10 은 빠듯했다
export const API_LIMIT = 120;
export const PERIOD_MS = 60_000;

/**
 * D1 카운터 - **세션 발급 전용.** 인스턴스 카운터는 한 클라이언트의 요청이 여러 인스턴스로 나뉘어 가서 느슨했다(배포 후
 * 20번째에야 첫 429). 세션 발급은 요청마다 밸브 API 를 부르므로 정확히 센다. 드문 요청(클라이언트당 한 시간에 한 번꼴)이라
 * 쓰기 한 번이 더 붙어도 괜찮다. 가장 잦은 API(수확)에는 쓰지 않는다.
 */
export class D1Limiter implements RateLimiter {
  constructor(
    private readonly db: D1Database,
    private readonly max: number,
    private readonly periodMs: number,
    private readonly now: () => number = Date.now,
  ) {}

  async limit({ key }: { key: string }): Promise<{ success: boolean }> {
    const t = this.now();
    const row = await this.db
      .prepare(
        `INSERT INTO rate_limits (key, window_start, count) VALUES (?1, ?2, 1)
         ON CONFLICT(key) DO UPDATE SET
           count = CASE WHEN ?2 - window_start >= ?3 THEN 1 ELSE count + 1 END,
           window_start = CASE WHEN ?2 - window_start >= ?3 THEN ?2 ELSE window_start END
         RETURNING count`,
      )
      .bind(key, t, this.periodMs)
      .first<{ count: number }>();

    // 가끔 창이 한참 지난 행을 지운다 - 표가 IP 수만큼 계속 크지 않게
    if (Math.random() < 0.01) {
      await this.db.prepare("DELETE FROM rate_limits WHERE window_start < ?1").bind(t - 3_600_000).run();
    }

    return { success: (row?.count ?? 1) <= this.max };
  }
}

/** 이 인스턴스의 카운터 (나머지 API). 모듈 전역이라 같은 인스턴스의 요청끼리 공유한다. */
export const localApiLimiter = new WindowLimiter(API_LIMIT, PERIOD_MS);

/**
 * 허용이면 true. 바인딩과 우리 카운터(인스턴스 또는 D1) 둘 다 허용해야 한다. 어느 겹이든 없거나 실패하면 그 겹은 통과 -
 * 예: D1 표가 아직 없을 때(마이그레이션 전) 세션 발급이 500 으로 죽으면 안 된다.
 */
export async function allow(binding: RateLimiter | undefined, local: RateLimiter, key: string): Promise<boolean> {
  let localOk = true;
  try {
    localOk = (await local.limit({ key })).success;
  } catch (err) {
    console.error("[economy-server] rate limit counter error - allowing", err);
  }
  if (!binding) {
    return localOk;
  }

  try {
    const { success } = await binding.limit({ key });
    return success && localOk;
  } catch (err) {
    console.error("[economy-server] rate limiter binding error - using local counter only", err);
    return localOk;
  }
}

/** 세션 발급의 키. Cloudflare 가 붙여 주는 접속 IP. 없으면(로컬) 하나로 묶는다. */
export function sessionKey(request: Request): string {
  return `ip:${request.headers.get("CF-Connecting-IP") ?? "unknown"}`;
}

/** 429 응답을 받았을 때 몇 초 뒤에 다시 하라는지 - 창 길이와 같다. */
export const RETRY_AFTER_SECONDS = PERIOD_MS / 1000;
