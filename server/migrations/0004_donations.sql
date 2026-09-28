-- B20 기부 (2026-09-28, docs/B20-DONATION.md). 누적 기부 바나나 - 칭호와 기부 도전과제가 이 값을 믿는다.
-- schema.sql 로 처음 만든 DB 에는 이미 있는 칸이다 - 그 DB 에서 돌리면 "duplicate column" 으로 실패하는데, 정상이다.
-- **B20 이전에 만든 DB 에 한 번만**, 그리고 **서버를 배포하기 전에** 돌린다 (savePlayer 가 이 칸을 쓴다 -
-- 칸 없이 새 서버가 뜨면 모든 저장이 실패한다):
--   npm run db:migrate:donate:local   /   npm run db:migrate:donate:remote
-- 원격은 --file 이 아니라 --command 로 돈다 (package.json): 2026-09-28 --file 은 D1 import API 를 타는데 OAuth 로그인에서
-- "Authentication error [code: 10000]" 로 실패했다 - 같은 계정의 --command(query API)는 된다. 문장이 하나라 그대로 옮겼다.
ALTER TABLE players ADD COLUMN donated_total INTEGER NOT NULL DEFAULT 0;
