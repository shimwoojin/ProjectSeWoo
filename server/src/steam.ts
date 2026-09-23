import type { Env } from "./types";

/**
 * 클라이언트가 `GetAuthSessionTicket()`으로 받은 티켓을 서버가 검증한다.
 * 공개 문서화된 안정적인 Web API 다 - Publisher Web API Key 만 있으면 되고
 * 파트너 사이트 개별 승인이 필요 없다.
 *
 * https://partner.steamgames.com/doc/webapi/ISteamUserAuth#AuthenticateUserTicket
 *
 * **2026-09-23 - Cloudflare Workers 에서 직접 부른다. 문제 없다.** 한때
 * "밸브/Akamai 가 Cloudflare 의 발신 IP 를 막는다"고 결론 내고 AWS Lambda
 * 릴레이(`server/aws-relay/`)로 우회한 적이 있는데, 그 403 의 진짜 원인은
 * IP 차단이 아니라 **`wrangler secret put`의 대화형 프롬프트가 이 세션의
 * 터미널 중계 방식에서 값을 빈 문자열로 저장한 것**이었다 - 빈 키를 밸브
 * 엣지가 형식 오류로 거부한 것이 403 이었다. `printf '%s' "<값>" | wrangler
 * secret put NAME` 처럼 파이프로 비대화식 입력하고 나서 진짜 키로 다시
 * 시험하니 Cloudflare Workers 에서도 정상 응답이 왔다(200 + 밸브 JSON).
 * 자세한 경위는 docs/ECONOMY-SERVER.md §9.
 */
export async function authenticateUserTicket(
  env: Env,
  ticket: string,
): Promise<{ ok: true; steamId: string } | { ok: false; error: string }> {
  const url = new URL("https://partner.steam-api.com/ISteamUserAuth/AuthenticateUserTicket/v1/");
  url.searchParams.set("key", env.STEAM_PUBLISHER_WEB_API_KEY);
  url.searchParams.set("appid", env.STEAM_APP_ID);
  url.searchParams.set("ticket", ticket);

  const res = await fetch(url.toString());
  if (!res.ok) {
    return { ok: false, error: `http_${res.status}` };
  }

  const body = (await res.json()) as {
    response?: {
      params?: { result?: string; steamid?: string; vacbanned?: boolean; publisherbanned?: boolean };
      error?: { errorcode: number; errordesc: string };
    };
  };

  const params = body.response?.params;
  if (!params || params.result !== "OK" || !params.steamid) {
    return { ok: false, error: body.response?.error?.errordesc ?? "auth_failed" };
  }

  if (params.vacbanned || params.publisherbanned) {
    return { ok: false, error: "banned" };
  }

  return { ok: true, steamId: params.steamid };
}

/**
 * ✅ **2026-09-23 실측 검증됨** - 실제 아이템 지급까지 확인했다
 * (docs/ECONOMY-SERVER.md §9). 처음엔 `itemdefid`를 단일 값으로, `quantity`를
 * 같이 보냈는데 밸브가 `X-eresult: 8 "No items specified."`로 거부했다.
 * 스팀웍스 파트너 문서(IInventoryService/AddItem)로 확인한 진짜 계약:
 *
 *   - `itemdefid`는 **배열**이다 - `itemdefid[0]`, `itemdefid[1]`, ... 로
 *     이름 붙은 여러 파라미터로 보낸다. 같은 아이템 두 개를 주려면
 *     `itemdefid[0]`과 `itemdefid[1]`에 같은 값을 반복한다 - `quantity`
 *     파라미터 자체가 없다.
 *   - `itempropsjson`이 **필수**다 (빈 아이템 속성이면 `"{}"`).
 *   - 성공 여부는 HTTP 상태가 아니라 `X-eresult` 응답 헤더로 판정한다
 *     (`1` = OK). `item_json`은 지급된 아이템 배열이 **JSON 문자열로 다시
 *     인코딩된 것**이라 한 번 더 파싱해야 한다.
 */
export async function grantInventoryItem(
  env: Env,
  steamId: string,
  steamItemDefId: number,
): Promise<{ ok: true } | { ok: false; error: string }> {
  const url = new URL("https://partner.steam-api.com/IInventoryService/AddItem/v1/");
  url.searchParams.set("key", env.STEAM_PUBLISHER_WEB_API_KEY);
  url.searchParams.set("appid", env.STEAM_APP_ID);
  url.searchParams.set("steamid", steamId);
  // 스팀은 우리 문자열 id 를 모른다 - 정수 itemdefid 만 받는다. 이 번호가
  // catalog.ts 의 steamItemDefId 이고, platform/SteamInventoryService.cs 가
  // 클라이언트에서 다시 문자열로 되돌린다.
  url.searchParams.set("itemdefid[0]", String(steamItemDefId));
  url.searchParams.set("itempropsjson", "{}");

  const res = await fetch(url.toString(), { method: "POST" });

  if (!res.ok) {
    return { ok: false, error: `http_${res.status}` };
  }

  // eresult 1 = k_EResultOK. HTTP 200 이어도 이 헤더가 1 이 아니면 실패다
  // (예: "No items specified" 도 HTTP 200 으로 왔었다).
  const eresult = res.headers.get("x-eresult");
  if (eresult !== "1") {
    return { ok: false, error: `eresult_${eresult ?? "missing"}` };
  }

  return { ok: true };
}
