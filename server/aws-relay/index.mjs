// PunchMonkey 스팀 릴레이 (docs/ECONOMY-SERVER.md §9).
//
// Cloudflare Workers 에서 스팀 partner API 를 직접 부르면 403 이 난다(밸브/
// Akamai 가 Cloudflare 의 발신 IP 대역을 막는 것으로 추정 - 진짜 키로 통제한
// 실험에서 로컬 PC 와 GitHub Actions(Azure)는 정상, Cloudflare Workers 만
// 막힘을 확인했다). 그래서 스팀을 실제로 부르는 딱 두 호출만 이 Lambda로
// 옮긴다 - 나머지(D1, 세션 토큰, 하베스트/구매 원장 로직)는 그대로
// Cloudflare Workers 에 남는다.
//
// AWS Lambda 콘솔의 인라인 코드 편집기에 그대로 붙여넣는 걸 전제로 짰다 -
// 외부 npm 패키지가 없다. Node.js 18+ 런타임에 내장된 전역 fetch 를 쓴다.
//
// 인증: Cloudflare Worker 만 이 Lambda 를 부를 수 있어야 한다 - Function URL
// 자체는 인증 없이(AuthType NONE) 공개되므로, 공유 비밀(X-Relay-Secret
// 헤더)로 우리끼리만 아는 값을 확인한다. 이게 없으면 이 URL 을 아는 아무나
// 우리 스팀 키 쿼터로 요청을 날릴 수 있다.

const STEAM_KEY = process.env.STEAM_PUBLISHER_WEB_API_KEY;
const RELAY_SECRET = process.env.RELAY_SHARED_SECRET;

export const handler = async (event) => {
  const headers = lowerCaseHeaders(event.headers ?? {});
  if (!RELAY_SECRET || headers["x-relay-secret"] !== RELAY_SECRET) {
    return json(401, { ok: false, error: "unauthorized" });
  }

  const path = event.rawPath ?? "";
  let body = {};
  try {
    const raw = event.isBase64Encoded
      ? Buffer.from(event.body ?? "", "base64").toString("utf-8")
      : event.body ?? "{}";
    body = JSON.parse(raw);
  } catch {
    return json(400, { ok: false, error: "bad_request" });
  }

  if (path === "/authenticate-ticket") {
    return authenticateTicket(body);
  }

  if (path === "/grant-item") {
    return grantItem(body);
  }

  return json(404, { ok: false, error: "not_found" });
};

async function authenticateTicket({ appid, ticket }) {
  if (!appid || !ticket) {
    return json(400, { ok: false, error: "bad_request" });
  }

  const url = new URL("https://partner.steam-api.com/ISteamUserAuth/AuthenticateUserTicket/v1/");
  url.searchParams.set("key", STEAM_KEY);
  url.searchParams.set("appid", appid);
  url.searchParams.set("ticket", ticket);

  const res = await fetch(url);
  if (!res.ok) {
    return json(200, { ok: false, error: `http_${res.status}` });
  }

  const data = await res.json();
  const params = data.response?.params;
  if (!params || params.result !== "OK" || !params.steamid) {
    return json(200, { ok: false, error: data.response?.error?.errordesc ?? "auth_failed" });
  }

  if (params.vacbanned || params.publisherbanned) {
    return json(200, { ok: false, error: "banned" });
  }

  return json(200, { ok: true, steamId: params.steamid });
}

async function grantItem({ appid, steamId, steamItemDefId }) {
  if (!appid || !steamId || !steamItemDefId) {
    return json(400, { ok: false, error: "bad_request" });
  }

  const url = new URL("https://partner.steam-api.com/IInventoryService/AddItem/v1/");
  url.searchParams.set("key", STEAM_KEY);
  url.searchParams.set("appid", appid);
  url.searchParams.set("steamid", steamId);
  url.searchParams.set("itemdefid", String(steamItemDefId));
  url.searchParams.set("quantity", "1");

  const res = await fetch(url, { method: "POST" });
  if (!res.ok) {
    return json(200, { ok: false, error: `http_${res.status}` });
  }

  // TODO: 실제 응답 스키마 확인 후 성공 판정 조건을 여기에 맞게 고칠 것
  // (server/src/steam.ts 의 같은 TODO 참고 - 아직 미검증).
  const data = await res.json().catch(() => null);
  if (!data?.success) {
    return json(200, { ok: false, error: "grant_failed" });
  }

  return json(200, { ok: true });
}

function json(statusCode, obj) {
  return {
    statusCode,
    headers: { "content-type": "application/json" },
    body: JSON.stringify(obj),
  };
}

function lowerCaseHeaders(headers) {
  const out = {};
  for (const [k, v] of Object.entries(headers)) {
    out[k.toLowerCase()] = v;
  }
  return out;
}
