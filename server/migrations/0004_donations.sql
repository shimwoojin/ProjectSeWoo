-- B20 기부 (2026-09-28, docs/B20-DONATION.md). 누적 기부 바나나 - 칭호와 기부 도전과제가 이 값을 믿는다.
-- schema.sql 로 처음 만든 DB 에는 이미 있는 칸이다 - 그 DB 에서 돌리면 "duplicate column" 으로 실패하는데, 정상이다.
-- **B20 이전에 만든 DB 에 한 번만**, 그리고 **서버를 배포하기 전에** 돌린다 (savePlayer 가 이 칸을 쓴다 -
-- 칸 없이 새 서버가 뜨면 모든 저장이 실패한다):
--   npm run db:migrate:donate:local   /   npm run db:migrate:donate:remote
ALTER TABLE players ADD COLUMN donated_total INTEGER NOT NULL DEFAULT 0;
