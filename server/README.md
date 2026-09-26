# PunchMonkey 경제 서버 — 스캐폴딩

Cloudflare Workers + D1. 계약 문서는 [../docs/ECONOMY-SERVER.md](../docs/ECONOMY-SERVER.md) /
[../docs/ECONOMY-SERVER-API.md](../docs/ECONOMY-SERVER-API.md) — 왜 이 서버가
필요하고 왜 이 모양인지는 거기 있다. 이 파일은 **여기 있는 코드를 어떻게
돌리는가**만 다룬다.

## 상태

**2026-09-23 실배포 완료.** `https://punchmonkey-economy.shimwoojin627.workers.dev`
- D1·세션·스팀 직접 호출(`/v1/session`)·**아이템 지급(`grantInventoryItem`)**
까지 실제 구매 한 건으로 끝까지 확인됨(§3 참고 - `itemdefid[0]` 배열 형식 +
`itempropsjson` 필수 파라미터가 빠져 있던 게 원인이었다, 지금은 고쳐졌다).

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
                  # predev 가 db:migrate:local 을 먼저 돌린다 (schema.sql 이 전부
                  # IF NOT EXISTS 라 매번 돌려도 안전)
                  # 게임은 VS 의 "Godot 게임 - 로컬 서버" 프로필로 붙인다
npm run typecheck # tsc --noEmit

# 배포 전에 원격 시크릿을 넣어야 한다 (.dev.vars 는 로컬 전용):
npm run secret:steam-key       # STEAM_PUBLISHER_WEB_API_KEY 를 물어본다
npm run secret:session-secret  # SESSION_SIGNING_SECRET 을 물어본다

npm run deploy
```

## 3. 실제로 쓰기 전에 반드시 확인할 것

1. ~~`src/steam.ts` 의 `grantInventoryItem`이 미검증 placeholder다~~ **2026-09-23
   검증 완료** - 실제 구매 한 건으로 지급까지 확인했다. 처음엔 `itemdefid`를
   단일값+`quantity`로 보냈는데 밸브가 `X-eresult: 8 "No items specified."`
   로 거부했다 - 진짜 계약은 `itemdefid[0]`(배열) + `itempropsjson`(필수)
   이고, 성공 판정은 HTTP 상태가 아니라 `X-eresult` 응답 헤더로 한다.
2. ~~아이템 정의(itemdef) 등록~~ **완료** - 15종을 파트너 사이트에 등록·
   게시했다(`steam-inventory/itemdefs.json`). 처음엔 스팀 인벤토리 조회가
   `k_EResultFail`이었는데, 파트너 사이트에서 **"Inventory Service 활성화"**
   체크박스를 따로 켜야 했다 - JSON 게시만으로는 부족했다.
3. **`src/catalog.ts` 는 `game/shop/ShopCatalog.cs` 의 손 사본이다.** 가격표를
   클라이언트가 보내지 않고 서버가 자체 판정하기 위한 것인데, 두 표가 갈라지면
   조용히 갈라진다 — 클라이언트 카탈로그를 고치면 이 파일도 같이 고칠 것.
4. **`owned_items_mirror` 는 캐시일 뿐 진실이 아니다.** 유저가 스팀 마켓에서
   아이템을 팔면 이 표는 자동으로 갱신되지 않는다 — "재구매" 흐름을 만들
   때는 스팀 인벤토리 조회로 주기적 재동기화가 필요하다 (`schema.sql` 주석).
5. **멱등 처리에 경합 창이 있다.** `db.ts` 의 `findIdempotentResponse` 주석
   참고 — 완전한 락은 아니다. 정직한 클라이언트 기준으로는 문제가 없지만,
   프로덕션 전에 D1 트랜잭션 또는 Durable Object 큐로 강화하는 걸 고려한다.
6. ~~레이트리밋이 코드에 없다~~ **2026-09-26 추가** (`src/ratelimit.ts`) — 429 + `Retry-After: 60`.
   - 세션 발급: IP 기준 분당 30(PC방·회사 NAT 를 생각해 10 에서 올림), **D1 카운터**(`rate_limits` 표, `migrations/0003`)로 정확히 센다 — 요청마다 밸브 API 를 부른다.
     분당 10 으로 배포했을 때 11번째 요청부터 429 확인
   - 나머지 API: 스팀 ID 기준 분당 120, 워커 인스턴스 카운터(근사치 — 한 클라이언트 요청이 여러 인스턴스로 나뉘어 실제로는
     더 느슨하다). 가장 잦은 요청이라 D1 쓰기를 안 붙였다
   - Workers Rate Limiting 바인딩(`[[unsafe.bindings]]`)도 겹으로 걸어 뒀지만 **재 보니 이 계정에선 막지 않았다**(1분 57번에도
     `success=true`). 켜져 있어도 해는 없다
   - 어느 겹이든 없거나 실패하면 통과 — 레이트리밋 때문에 서버가 멈추지 않는다
7. ~~자동화된 테스트가 없다~~ **2026-09-26 `npm test`** (vitest, Node — 워커 풀 없이) — 가격표(`items.json` 과 일치,
   기본 지급품, 강화 표), `recomputeSlots`(오프라인 성장·상한·늘어난 슬롯·시계 역행), 세션 토큰(위조·만료), 라우터
   (400·401·429, 인증 실패는 한도를 안 셈), 레이트리밋 카운터. **D1 을 쓰는 경로(수확·구매)는 아직 로컬 워커 + curl** 이다 —
   필요해지면 `@cloudflare/vitest-pool-workers` 로

## 4. 파일 구성

| 파일 | 역할 |
|---|---|
| `src/index.ts` | HTTP 라우팅. 엔드포인트 목록은 ECONOMY-SERVER-API.md §2 |
| `src/session.ts` | 스팀 티켓 검증 후 발급하는 자체 서명 세션 토큰 |
| `src/steam.ts` | 스팀 Web API 호출 (`AuthenticateUserTicket`·`AddItem` 둘 다 실측 검증됨) |
| `src/economy.ts` | 잔액·슬롯 성장·강화의 핵심 로직. 순수 계산(`recomputeSlots`)과 DB I/O 를 분리했다 |
| `src/catalog.ts` | 상품표 서버 사본 (§3-3) |
| `src/db.ts` | D1 쿼리 |
| `src/types.ts` | 클라이언트 계약(`shared/Contracts/*.cs`)과 대응하는 DTO |
| `schema.sql` | D1 스키마. `npm run db:migrate:*` 로 적용 |
| `steam-inventory/itemdefs.json` | 스팀 인벤토리 서비스에 등록·게시한 아이템 정의 15종(itemdefid 1~15). 파트너 사이트 Item Definitions 란에 이 내용 그대로 올라가 있다 - 카탈로그를 바꾸면 여기도 고치고 다시 게시할 것 |
| `aws-relay/index.mjs` | **현재 미사용.** Cloudflare 가 스팀 API 에 막혔다고 (오판하고) 만든 AWS Lambda 릴레이. 검증까지 마쳤으나 필요 없어서 뺐다 - 참고용으로 남김 (docs/ECONOMY-SERVER.md §9) |
