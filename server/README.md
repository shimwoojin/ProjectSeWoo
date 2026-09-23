# PunchMonkey 경제 서버 — 스캐폴딩

Cloudflare Workers + D1. 계약 문서는 [../docs/ECONOMY-SERVER.md](../docs/ECONOMY-SERVER.md) /
[../docs/ECONOMY-SERVER-API.md](../docs/ECONOMY-SERVER-API.md) — 왜 이 서버가
필요하고 왜 이 모양인지는 거기 있다. 이 파일은 **여기 있는 코드를 어떻게
돌리는가**만 다룬다.

## 상태

**2026-09-23 실배포 완료.** `https://punchmonkey-economy.shimwoojin627.workers.dev`
- D1·세션·스팀 직접 호출(`/v1/session`)까지 실제 스팀 키로 확인됨.
아이템 지급(`grantInventoryItem`)은 아직 §3 의 미검증 상태 그대로다.

> ⚠️ **`wrangler secret put` 함정 (실측).** 이 값을 대화형 프롬프트로 넣을
> 때, 특정 터미널 중계 환경에서는 "✨ Success!" 메시지가 떠도 **실제로는
> 빈 문자열이 저장되는 경우가 있었다** (docs/ECONOMY-SERVER.md §9에 전체
> 경위가 있다 - 이것 때문에 며칠 "밸브가 Cloudflare 를 차단한다"는 잘못된
> 결론까지 냈었다). 값이 제대로 들어갔는지 의심되면
> `printf '%s' "<값>" | npx wrangler secret put NAME` 처럼 **파이프로
> 비대화식 입력**하거나, 배포 후 `env.<NAME>?.length` 만 잠깐 찍어보는
> 진단 엔드포인트로 실제 반영을 확인할 것 — `wrangler secret list`는
> 이름만 보여주고 값이나 길이는 안 보여준다.

## 0. 요구 사항

- Node.js 18+
- Cloudflare 계정 (무료 티어로 충분 — docs/ECONOMY-SERVER-API.md §0)
- 스팀 파트너 사이트 Publisher Web API Key (앱 ID `5281130`)

## 1. 처음 설정

```bash
cd server
npm install
npx wrangler login          # Cloudflare 계정 연결

npm run db:create           # D1 데이터베이스 생성 - 출력된 database_id 를
                             # wrangler.toml 의 REPLACE_AFTER_WRANGLER_D1_CREATE 에 붙여넣는다

npm run db:migrate:local    # 로컬 개발용 스키마 적용
npm run db:migrate:remote   # 배포용 스키마 적용 (처음 한 번 + 스키마 바뀔 때마다)

cp .dev.vars.example .dev.vars
# .dev.vars 를 열어 STEAM_PUBLISHER_WEB_API_KEY / SESSION_SIGNING_SECRET 채우기
# (SESSION_SIGNING_SECRET 예: openssl rand -hex 32)
```

## 2. 로컬 실행 / 배포

```bash
npm run dev       # http://localhost:8787 — .dev.vars 를 읽는다
npm run typecheck # tsc --noEmit

# 배포 전에 원격 시크릿을 넣어야 한다 (.dev.vars 는 로컬 전용):
npm run secret:steam-key       # STEAM_PUBLISHER_WEB_API_KEY 를 물어본다
npm run secret:session-secret  # SESSION_SIGNING_SECRET 을 물어본다

npm run deploy
```

## 3. 실제로 쓰기 전에 반드시 확인할 것

1. **`src/steam.ts` 의 `grantInventoryItem`.** 엔드포인트 경로·파라미터명·
   응답 스키마가 전부 미검증 placeholder다. 스팀웍스 파트너 사이트의
   Inventory Service 문서(파트너 로그인 필요)로 정확한 사양을 확인하고
   고칠 것 — 함수 안 주석에 확인할 항목을 적어 뒀다.
2. **아이템 정의(itemdef) 등록.** 커서 장식 16종을 파트너 사이트 Inventory
   Service 에 먼저 등록해야 `grantInventoryItem`이 의미가 있다.
3. **`src/catalog.ts` 는 `game/shop/ShopCatalog.cs` 의 손 사본이다.** 가격표를
   클라이언트가 보내지 않고 서버가 자체 판정하기 위한 것인데, 두 표가 갈라지면
   조용히 갈라진다 — 클라이언트 카탈로그를 고치면 이 파일도 같이 고칠 것.
4. **`owned_items_mirror` 는 캐시일 뿐 진실이 아니다.** 유저가 스팀 마켓에서
   아이템을 팔면 이 표는 자동으로 갱신되지 않는다 — "재구매" 흐름을 만들
   때는 스팀 인벤토리 조회로 주기적 재동기화가 필요하다 (`schema.sql` 주석).
5. **멱등 처리에 경합 창이 있다.** `db.ts` 의 `findIdempotentResponse` 주석
   참고 — 완전한 락은 아니다. 정직한 클라이언트 기준으로는 문제가 없지만,
   프로덕션 전에 D1 트랜잭션 또는 Durable Object 큐로 강화하는 걸 고려한다.
6. **레이트리밋이 코드에 없다.** ECONOMY-SERVER-API.md §3 이 429 응답을
   계약에 넣어 뒀지만 이 스캐폴딩은 아직 구현하지 않았다 — Cloudflare
   Workers 의 Rate Limiting 바인딩이나 D1 카운터로 추가할 것.
7. **자동화된 테스트가 없다.** `economy.ts` 의 `recomputeSlots`/하베스트/
   구매 로직은 순수 함수 위주로 짜서 단위 테스트를 붙이기 쉬운 모양으로
   만들어 뒀다 — 다음 단계에서 `vitest` + `@cloudflare/vitest-pool-workers`
   를 추가하는 걸 권장한다.

## 4. 파일 구성

| 파일 | 역할 |
|---|---|
| `src/index.ts` | HTTP 라우팅. 엔드포인트 목록은 ECONOMY-SERVER-API.md §2 |
| `src/session.ts` | 스팀 티켓 검증 후 발급하는 자체 서명 세션 토큰 |
| `src/steam.ts` | 스팀 Web API 호출 (`AuthenticateUserTicket` 검증됨 / `AddItem` **미검증**) |
| `src/economy.ts` | 잔액·슬롯 성장·강화의 핵심 로직. 순수 계산(`recomputeSlots`)과 DB I/O 를 분리했다 |
| `src/catalog.ts` | 상품표 서버 사본 (§3-3) |
| `src/db.ts` | D1 쿼리 |
| `src/types.ts` | 클라이언트 계약(`shared/Contracts/*.cs`)과 대응하는 DTO |
| `schema.sql` | D1 스키마. `npm run db:migrate:*` 로 적용 |
| `steam-inventory/itemdefs.json` | 스팀 인벤토리 서비스에 등록·게시한 아이템 정의 15종(itemdefid 1~15). 파트너 사이트 Item Definitions 란에 이 내용 그대로 올라가 있다 - 카탈로그를 바꾸면 여기도 고치고 다시 게시할 것 |
| `aws-relay/index.mjs` | **현재 미사용.** Cloudflare 가 스팀 API 에 막혔다고 (오판하고) 만든 AWS Lambda 릴레이. 검증까지 마쳤으나 필요 없어서 뺐다 - 참고용으로 남김 (docs/ECONOMY-SERVER.md §9) |
