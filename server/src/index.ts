import { getState, harvest, purchaseItem, purchaseUpgrade } from "./economy";
import { authenticateUserTicket } from "./steam";
import { bearerTokenFrom, issueSessionToken, verifySessionToken } from "./session";
import type { Env, UpgradeAxis } from "./types";

function json(data: unknown, status = 200): Response {
  return new Response(JSON.stringify(data), {
    status,
    headers: { "content-type": "application/json" },
  });
}

const UPGRADE_AXES: readonly UpgradeAxis[] = ["golden", "cycle", "slots"];

function isUpgradeAxis(value: unknown): value is UpgradeAxis {
  return typeof value === "string" && (UPGRADE_AXES as readonly string[]).includes(value);
}

/** 인증 필요 라우트 앞에 붙인다. 성공하면 steamId, 실패하면 그대로 돌려줄 Response. */
async function requireSession(request: Request, env: Env): Promise<{ steamId: string } | Response> {
  const token = bearerTokenFrom(request);
  const result = await verifySessionToken(env, token);
  if (!result.ok) {
    return json({ error: result.error }, 401);
  }

  return { steamId: result.steamId };
}

async function readJson<T>(request: Request): Promise<T | null> {
  try {
    return (await request.json()) as T;
  } catch {
    return null;
  }
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const url = new URL(request.url);

    try {
      // -------------------------------------------------------------- 세션 (인증 불필요)
      if (request.method === "POST" && url.pathname === "/v1/session") {
        const body = await readJson<{ ticket?: string }>(request);
        if (!body?.ticket) {
          return json({ error: "missing_ticket" }, 400);
        }

        const auth = await authenticateUserTicket(env, body.ticket);
        if (!auth.ok) {
          return json({ error: auth.error }, 401);
        }

        const session = await issueSessionToken(env, auth.steamId);
        return json(session);
      }

      // -------------------------------------------------------------- 이하 전부 인증 필요
      const session = await requireSession(request, env);
      if (session instanceof Response) {
        return session;
      }
      const { steamId } = session;

      if (request.method === "GET" && url.pathname === "/v1/economy/state") {
        return json(await getState(env, steamId));
      }

      if (request.method === "POST" && url.pathname === "/v1/economy/harvest") {
        const body = await readJson<{ slotIndex?: number; clientRequestId?: string }>(request);
        if (typeof body?.slotIndex !== "number" || !body.clientRequestId) {
          return json({ error: "bad_request" }, 400);
        }

        return json(await harvest(env, steamId, body.slotIndex, body.clientRequestId));
      }

      if (request.method === "POST" && url.pathname === "/v1/economy/purchase/item") {
        const body = await readJson<{ itemDefId?: string; clientRequestId?: string }>(request);
        if (!body?.itemDefId || !body.clientRequestId) {
          return json({ error: "bad_request" }, 400);
        }

        return json(await purchaseItem(env, steamId, body.itemDefId, body.clientRequestId));
      }

      if (request.method === "POST" && url.pathname === "/v1/economy/purchase/upgrade") {
        const body = await readJson<{ axis?: string; clientRequestId?: string }>(request);
        if (!isUpgradeAxis(body?.axis) || !body?.clientRequestId) {
          return json({ error: "bad_request" }, 400);
        }

        return json(await purchaseUpgrade(env, steamId, body.axis, body.clientRequestId));
      }

      return json({ error: "not_found" }, 404);
    } catch (err) {
      console.error("[economy-server] unhandled error", err);
      return json({ error: "server_error" }, 500);
    }
  },
};
