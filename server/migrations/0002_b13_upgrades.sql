-- B13 강화 재설계 (2026-09-24, docs/B13-UPGRADES.md).
-- schema.sql 로 처음 만든 DB 에는 이미 있는 칸이다 - 그 DB 에서 돌리면 "duplicate column" 으로
-- 실패하는데, 그건 정상이다. **B13 이전에 만든 DB 에 한 번만** 돌린다:
--   npm run db:migrate:b13:local   /   npm run db:migrate:b13:remote
--
-- 황금 칸이 빈 배열인 기존 행은 economy.ts 의 recomputeSlots 가 다음 요청 때 굴려서 채운다.
-- 예전 "파워"(power_level)는 황금 바나나로 바뀌며 안 쓴다 - 올린 레벨은 넘기지 않는다
-- (출시 전이라 개발 계정뿐이다).
ALTER TABLE players ADD COLUMN golden_level INTEGER NOT NULL DEFAULT 0;
ALTER TABLE players ADD COLUMN slot_golden TEXT NOT NULL DEFAULT '[]';
