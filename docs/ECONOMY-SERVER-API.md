# ECONOMY-SERVER-API — 백엔드 REST 계약 (구현 없음, 계약만)

관련: [ECONOMY-SERVER.md](ECONOMY-SERVER.md) (왜 이 서버가 필요한가) /
`shared/Contracts/IEconomyService.cs` / `shared/Contracts/IInventoryService.cs`

**이 문서가 기술하는 백엔드는 아직 구현되지 않았다.** 클라이언트 인터페이스
(`IEconomyService`/`IInventoryService`)와 나중에 만들 서버 코드가 같은 계약을
보게 하려고 지금 API 모양을 먼저 고정한다 — A1 이 인터페이스 시그니처를
먼저 커밋한 것과 같은 이유다.

---

## 0. 호스팅 — 비용 없이 가는 구조

| 구성요소 | 선택 | 이유 |
|---|---|---|
| 함수 실행 | Cloudflare Workers (무료 티어: 하루 10만 요청) | 상시 구동 프로세스가 아니라 요청당 과금. 하베스트/구매는 초당 몇 건 수준의 트래픽이라 무료 한도 안에 들 가능성이 높다 |
| 원장 저장소 | Cloudflare D1 (SQLite, 무료 5GB) | 유저당 row 하나(잔액 + 슬롯 타이머 + 강화 레벨 + `lastSyncUtc`) 수준이라 용량이 문제 될 규모가 아니다 |
| 시크릿 보관 | Cloudflare Workers Secrets | 스팀 Publisher Web API Key. **클라이언트에는 절대 내려주지 않는다** |
| 아이템 커스터디 | 스팀 인벤토리 서비스 (밸브 호스팅) | 우리가 아이템 저장소를 따로 만들 필요가 없다 — 이게 이 구조의 핵심 이득 |

대안으로 AWS Lambda + DynamoDB, Supabase(Postgres) 등도 같은 모양으로
간다 — 선택 기준은 "상시 구동 서버가 필요 없다"는 성질이지 특정 벤더가 아니다.

**바나나가 실시간 스트림일 필요가 없는 이유.** 나무 슬롯 성장은 "경과
시간 × 성장 주기"로 결정론적이다(기획서 §2-2). 서버는 `lastSyncUtc`
하나만 원장에 들고 있다가, 요청이 올 때마다 `now - lastSyncUtc`로 슬롯
상태를 재계산하면 된다 — 틱을 도는 백그라운드 잡이 없어도 항상 정확하다.
이게 서버리스 함수(요청이 없으면 아예 안 도는)로 이 문제를 풀 수 있는 이유다.

---

## 1. 인증

```
클라이언트                              서버                         스팀
  GetAuthSessionTicket()  ──────────▶
  POST /v1/session
    { steamAppId, ticket }  ────────▶  ISteamUserAuth
                                       /AuthenticateUserTicket  ──▶ 밸브
                                          (Publisher Web API Key로 호출)
                                       ◀──────────────────────────  steamId
                            ◀────────  { sessionToken, expiresAt }
```

- `sessionToken`은 서버가 서명한 단기 토큰(예: 1시간)이다. 이후 모든 호출은
  이 토큰을 `Authorization: Bearer <token>`으로 싣는다 — 매 요청마다
  `AuthenticateUserTicket`을 부르면 밸브 쪽 레이트리밋에 걸리기 쉽다.
- 토큰 만료 시 클라이언트가 새 세션 티켓으로 `/v1/session`을 다시 부른다.
  `IEconomyService.IsAvailable`이 이 재인증 실패를 반영한다.

---

## 2. 엔드포인트

### 2-1. `GET /v1/economy/state`

서버가 계산한 최신 상태를 돌려준다. `Sync()`가 이걸 부른다.

```json
{
  "balance": 132,
  "slots": [
    { "elapsedMs": 210000, "growthMs": 480000, "golden": false },
    { "elapsedMs": 480000, "growthMs": 480000, "golden": true },
    { "elapsedMs": 0,      "growthMs": 480000, "golden": false }
  ],
  "upgrades": { "golden": 0, "cycle": 0, "slots": 0 },
  "lastSyncUtc": "2026-09-23T04:00:00Z"
}
```

응답을 받는 시점에 서버가 `lastSyncUtc` 기준 경과 시간을 각 슬롯에 미리
반영해서 보낸다 — 클라이언트는 받은 값을 그대로 그리기 시작하면 된다.

**`golden` (B13, 2026-09-24)** — 그 슬롯의 이번 송이가 황금 바나나인가. 서버가 송이가 자라기
시작할 때(수확 직후, 슬롯이 새로 생길 때) 황금 강화 단계의 확률로 굴려 정한다 — 클라이언트는
고를 수 없다. 게임은 익었을 때만 황금으로 그린다. 강화 3축의 단계·가격은
[B13-UPGRADES.md](B13-UPGRADES.md), 표는 `server/src/catalog.ts` 의 `UPGRADES`.

### 2-2. `POST /v1/economy/harvest`

```json
// 요청
{ "slotIndex": 1, "clientRequestId": "01JXYZ..." }

// 응답 (성공) - gained 는 보통 1, 황금 송이면 5
{ "accepted": true, "gained": 5, "balance": 137, "slots": [ ... ] }

// 응답 (거부 - 아직 안 자랐다)
{ "accepted": false, "reason": "not_ready", "gained": 0, "balance": 132, "slots": [ ... ] }
```

- `clientRequestId`는 클라이언트가 생성하는 UUID다. **같은 id로 재시도가
  들어오면 서버는 두 번째 요청을 멱등 처리한다** (네트워크 재시도로 바나나가
  중복 지급되는 것을 막는다). 서버는 최근 N분의 `clientRequestId`를 원장과
  함께 보관한다.
- 서버는 응답 시점에 `now - lastSyncUtc`로 슬롯을 재계산한 뒤 `slotIndex`가
  `ready`인지 검사한다. 아니면 `accepted: false`로 거부하고 최신 상태만
  돌려준다 — `IEconomyService.OnStateChanged`가 이 최신 상태로 정정한다.

### 2-3. `POST /v1/economy/purchase/item`

```json
// 요청
{ "itemDefId": "cursor_hang_monkey_04", "clientRequestId": "01JAB1..." }

// 응답 (성공)
{ "outcome": "success", "balance": 40, "grantedItemDefId": "cursor_hang_monkey_04" }

// 응답 (실패)
{ "outcome": "insufficient_balance", "balance": 40 }
```

서버 내부 순서: ① `clientRequestId` 멱등 검사 → ② 가격표 조회(가격은 서버가
들고 있다. 클라이언트가 가격을 같이 보내지 않는다 — 보내게 하면 클라이언트가
가격을 조작할 여지가 생긴다) → ③ 잔액 검사·차감 → ④
`ISteamInventoryService/AddItem` 호출 → ⑤ ④가 실패하면 ③을 롤백하고
`outcome: "server_error"`로 응답(잔액을 깎았는데 아이템이 안 나가는 상태를
만들지 않는다 — 트랜잭션 순서가 중요하다).

`outcome` 값은 클라이언트의 `PurchaseOutcome` enum과 1:1 대응한다
(`success` / `insufficient_balance` / `item_unknown` / `already_owned` /
`max_level` / `rejected`). `max_level` 은 강화 전용(2-4). `server_unavailable`은 HTTP 레벨 실패(타임아웃 등)에 대응하는
것이라 이 JSON 안에는 없다 — 클라이언트가 요청 자체가 실패했을 때 채운다.

### 2-4. `POST /v1/economy/purchase/upgrade`

```json
// 요청
{ "axis": "cycle", "clientRequestId": "01JCD2..." }

// 응답 (성공) - 슬롯 수·성장 시간이 바뀌므로 새로 계산한 슬롯을 같이 보낸다
{ "outcome": "success", "balance": 10, "upgrades": { "golden": 0, "cycle": 1, "slots": 0 },
  "slots": [ { "elapsedMs": 130, "growthMs": 420000, "golden": false }, ... ] }

// 응답 (이미 최대 단계)
{ "outcome": "max_level", "balance": 4106, "upgrades": { "golden": 4, "cycle": 1, "slots": 3 } }
```

- `axis` 는 `golden` / `cycle` / `slots` (B13). 예전 `power` 는 **400 으로 거절**한다 — 황금
  바나나로 바뀌었다
- 가격은 서버가 자기 표(`UPGRADES`)로 판정한다. 클라이언트가 가격을 보내지 않는다
- 레벨을 올리기 **전에** 지금까지 흐른 시간을 옛 레벨로 확정한다(안 그러면 같은 구간을 두 번
  더한다 — b7696d3). 슬롯이 늘면 새 슬롯은 0 부터, 성장 시간이 짧아지면 이미 넘은 슬롯은 곧바로
  익는다

---

## 3. 에러·재시도

| 상황 | 클라이언트 동작 |
|---|---|
| HTTP 타임아웃/5xx | `IEconomyService.IsAvailable = false`로 전환. 하베스트는 로컬 예측을 계속 쌓고, 구매 버튼은 비활성화 (§7 ECONOMY-SERVER.md) |
| 401 (세션 만료) | `/v1/session` 재호출 후 원래 요청 재시도 |
| 429 (레이트리밋) | 지수 백오프 후 재시도. 하베스트/구매는 사람의 조작 속도를 넘지 않으므로 정상 플레이에서 429를 볼 일이 없다 — 보인다면 매크로 의심 신호로 로그만 남긴다 |

모든 쓰기 엔드포인트(`harvest`, `purchase/*`)가 `clientRequestId`를 받는
이유가 이 표 때문이다 — 타임아웃 뒤의 재시도가 중복 처리로 이어지면 안 된다.

---

## 4. 스팀 인벤토리 서비스 — 클라이언트 직접 호출과의 경계

`IInventoryService.Refresh()`는 위 백엔드를 거치지 않고 **스팀 클라이언트
SDK(`ISteamInventory`)를 직접 불러도 된다** — 조회는 신뢰 문제가 없다(내가
가진 것을 내가 보는 것뿐이다). 반면 지급(`AddItem`)은 반드시 서버가
Publisher Web API Key로 해야 한다 — 클라이언트가 스스로에게 아이템을 지급하는
경로를 열면 마켓 거래가 곧 화폐 위조 경로가 된다. 이 비대칭이 §2-3의
설계 이유다.
