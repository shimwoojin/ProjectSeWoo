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
 * ⚠ **미검증 — 실제 구현 전에 반드시 스팀웍스 파트너 사이트(Inventory Service
 * 문서, 파트너 로그인 필요)에서 정확한 인터페이스/메서드명·파라미터를
 * 재확인할 것.** 여기 있는 엔드포인트 경로와 파라미터명은 스캐폴딩 단계의
 * 최선 추정치이지 검증된 사실이 아니다 - docs/ECONOMY-SERVER.md 가 이 문서를
 * 만들 때부터 그렇게 명시했다.
 *
 * 확인해야 할 것:
 *   1. 정확한 인터페이스/메서드 이름과 버전 (`v1` 이 맞는지)
 *   2. 요청 파라미터 이름 (itemdefid/quantity/steamid 표기가 맞는지)
 *   3. 아이템 정의(itemdef)를 파트너 사이트에 먼저 등록해야 하는지, 등록
 *      안 된 id 로 호출하면 어떤 에러가 오는지
 *   4. 응답 스키마 (성공/실패를 어떤 필드로 구분하는지)
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
  url.searchParams.set("itemdefid", String(steamItemDefId));
  url.searchParams.set("quantity", "1");

  const res = await fetch(url.toString(), { method: "POST" });
  if (!res.ok) {
    return { ok: false, error: `http_${res.status}` };
  }

  // TODO: 실제 응답 스키마 확인 후 성공 판정 조건을 여기에 맞게 고칠 것.
  const body = (await res.json().catch(() => null)) as { success?: boolean } | null;
  if (!body?.success) {
    return { ok: false, error: "grant_failed" };
  }

  return { ok: true };
}
