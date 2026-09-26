-- PunchMonkey 경제 서버 D1 스키마 (docs/ECONOMY-SERVER-API.md).
-- 적용: npm run db:migrate:local  /  npm run db:migrate:remote

CREATE TABLE IF NOT EXISTS players (
  steam_id        TEXT PRIMARY KEY,
  balance         INTEGER NOT NULL DEFAULT 0,
  power_level     INTEGER NOT NULL DEFAULT 0,
  cycle_level     INTEGER NOT NULL DEFAULT 0,
  slots_level     INTEGER NOT NULL DEFAULT 0,
  -- 슬롯별 경과 ms 를 JSON 배열 문자열로 둔다 (예: "[0,480000,120000]").
  -- 슬롯 수가 slots_level 강화로 늘어나면 economy.ts 의 recomputeSlots 가
  -- 배열 길이를 그때그때 맞춘다 - 마이그레이션 없이 늘어난다.
  slot_elapsed_ms TEXT    NOT NULL DEFAULT '[0,0,0]',
  -- B13 (2026-09-24). 이미 있는 DB 는 CREATE TABLE IF NOT EXISTS 가 칸을 안 더하므로
  -- migrations/0002_b13_upgrades.sql 을 한 번 돌린다. power_level 은 예전 "파워" 축이라 안 쓴다.
  golden_level    INTEGER NOT NULL DEFAULT 0,
  slot_golden     TEXT    NOT NULL DEFAULT '[]',
  last_sync_utc   TEXT    NOT NULL DEFAULT (datetime('now')),
  created_at      TEXT    NOT NULL DEFAULT (datetime('now')),
  updated_at      TEXT    NOT NULL DEFAULT (datetime('now'))
);

-- 하베스트/구매 요청의 멱등 처리 (ECONOMY-SERVER-API.md §2-2). 같은
-- client_request_id 가 재시도로 다시 오면 저장된 응답을 그대로 돌려준다.
CREATE TABLE IF NOT EXISTS idempotency_keys (
  client_request_id TEXT PRIMARY KEY,
  steam_id           TEXT NOT NULL,
  response_json      TEXT NOT NULL,
  created_at         TEXT NOT NULL DEFAULT (datetime('now'))
);

-- 스팀 인벤토리 소유권의 로컬 미러. **진실은 스팀이다** - 이 표는 "already_owned"
-- 사전 검사를 스팀 왕복 없이 빠르게 하기 위한 캐시일 뿐이다. 유저가 마켓에서
-- 아이템을 팔면 이 표는 갱신되지 않는다 (TODO: 재구매 흐름을 만들 때
-- IInventoryService.Refresh 결과로 주기적 재동기화할 것 - server/README.md 참고).
CREATE TABLE IF NOT EXISTS owned_items_mirror (
  steam_id     TEXT NOT NULL,
  item_def_id  TEXT NOT NULL,
  granted_at   TEXT NOT NULL DEFAULT (datetime('now')),
  PRIMARY KEY (steam_id, item_def_id)
);

-- 세션 발급 레이트리밋 카운터 (migrations/0003_rate_limits.sql, src/ratelimit.ts D1Limiter).
CREATE TABLE IF NOT EXISTS rate_limits (
  key          TEXT PRIMARY KEY,
  window_start INTEGER NOT NULL,
  count        INTEGER NOT NULL
);
