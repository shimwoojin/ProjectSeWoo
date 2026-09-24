import type { Env, PlayerRow } from "./types";

/** 없으면 기본값(잔액 0, 슬롯 3개 · 480000ms, 황금 없음)으로 만든다. */
export async function getOrCreatePlayer(env: Env, steamId: string): Promise<PlayerRow> {
  const existing = await env.DB
    .prepare("SELECT * FROM players WHERE steam_id = ?")
    .bind(steamId)
    .first<PlayerRow>();

  if (existing) {
    return existing;
  }

  const nowIso = new Date().toISOString();
  await env.DB
    .prepare(
      `INSERT INTO players (steam_id, balance, power_level, golden_level, cycle_level, slots_level, slot_elapsed_ms, slot_golden, last_sync_utc)
       VALUES (?, 0, 0, 0, 0, 0, '[0,0,0]', '[false,false,false]', ?)`,
    )
    .bind(steamId, nowIso)
    .run();

  return {
    steam_id: steamId,
    balance: 0,
    power_level: 0,
    golden_level: 0,
    cycle_level: 0,
    slots_level: 0,
    slot_elapsed_ms: "[0,0,0]",
    slot_golden: "[false,false,false]",
    last_sync_utc: nowIso,
  };
}

export async function savePlayer(env: Env, player: PlayerRow): Promise<void> {
  await env.DB
    .prepare(
      `UPDATE players
       SET balance = ?, golden_level = ?, cycle_level = ?, slots_level = ?,
           slot_elapsed_ms = ?, slot_golden = ?, last_sync_utc = ?, updated_at = datetime('now')
       WHERE steam_id = ?`,
    )
    .bind(
      player.balance,
      player.golden_level,
      player.cycle_level,
      player.slots_level,
      player.slot_elapsed_ms,
      player.slot_golden,
      player.last_sync_utc,
      player.steam_id,
    )
    .run();
}

export async function isOwned(env: Env, steamId: string, itemDefId: string): Promise<boolean> {
  const row = await env.DB
    .prepare("SELECT 1 FROM owned_items_mirror WHERE steam_id = ? AND item_def_id = ?")
    .bind(steamId, itemDefId)
    .first();

  return row !== null;
}

export async function markOwned(env: Env, steamId: string, itemDefId: string): Promise<void> {
  await env.DB
    .prepare(
      "INSERT OR IGNORE INTO owned_items_mirror (steam_id, item_def_id) VALUES (?, ?)",
    )
    .bind(steamId, itemDefId)
    .run();
}

/**
 * 멱등 저장소 조회. 있으면 저장된 응답 문자열을 그대로 돌려준다 - 호출부가
 * JSON.parse 해서 그대로 HTTP 응답에 싣는다 (재계산하지 않는다).
 *
 * **경합 창이 있다.** 같은 clientRequestId 로 거의 동시에 두 요청이 오면 둘 다
 * "없음"으로 보고 둘 다 처리를 진행할 수 있다 - D1 은 여기서 락을 걸어주지
 * 않는다. 스캐폴딩 단계의 알려진 한계다 (server/README.md 참고). 정직한
 * 클라이언트는 같은 요청을 동시에 두 번 보내지 않으므로 실제 위험은 "네트워크
 * 재시도가 원래 요청과 겹치는" 드문 경우로 좁다.
 */
export async function findIdempotentResponse(env: Env, clientRequestId: string): Promise<string | null> {
  const row = await env.DB
    .prepare("SELECT response_json FROM idempotency_keys WHERE client_request_id = ?")
    .bind(clientRequestId)
    .first<{ response_json: string }>();

  return row?.response_json ?? null;
}

export async function storeIdempotentResponse(
  env: Env,
  clientRequestId: string,
  steamId: string,
  responseJson: string,
): Promise<void> {
  await env.DB
    .prepare(
      "INSERT OR REPLACE INTO idempotency_keys (client_request_id, steam_id, response_json) VALUES (?, ?, ?)",
    )
    .bind(clientRequestId, steamId, responseJson)
    .run();
}
