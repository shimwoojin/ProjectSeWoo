# ECONOMY-SERVER — 서버 권위 경제 + 스팀 인벤토리 피벗

관련: [기획확정-일감분배-260907.md](기획확정-일감분배-260907.md) §3(커서 꾸미기) · §4-1(**이 문서가 뒤집는 지점**) · §5(강화) · §7-5(세이브) /
[A1-CONTRACTS.md](A1-CONTRACTS.md) / [A8-STEAM.md](A8-STEAM.md) /
[ECONOMY-SERVER-API.md](ECONOMY-SERVER-API.md) (백엔드 REST 계약)

---

## 0. 이 문서가 왜 있는가

기획서 §4-1 은 "재화 잔액 · 나무 성장 타이머 · 강화 수치는 동기화하지 않는다.
경제는 전부 로컬이다"를 멀티를 4주 안에 넣을 수 있게 하는 **유일한 이유**로
못 박아 뒀다. **이 문서는 그 결정을 뒤집는다.**

계기: 바나나로 산 커서 장식을 스팀 인벤토리 서비스로 옮기고, 스팀 커뮤니티
마켓에서 유저 간에 거래되게 한다. 아이템이 실제 돈으로 거래될 수 있는 순간,
"그 아이템을 산 바나나가 진짜였는가"가 더 이상 이 게임 혼자만의 문제가 아니게
된다 — 로컬 세이브 파일을 손으로 고쳐 만든 바나나로 산 장식이 마켓에서 거래되면
그건 우리 게임의 밸런스 문제가 아니라 **위조 화폐로 실제 금전 거래를 하는
문제**다. 그래서 §4-1 의 전제("경제가 로컬이라 서버가 필요 없다")가 이 결정과
정면으로 충돌하고, 이 문서가 그 자리를 새로 정의한다.

> **이 프로젝트의 기존 일정·역할 분담(§8, §9, §10 컷 라인)은 이 트랙에
> 적용하지 않는다.** 일정은 자율로 관리한다. 이 문서는 "무엇을 만들 것인가"만
> 고정한다.

### 상태 (2026-09-23)

| 산출물 | 위치 | 상태 |
|---|---|---|
| `IEconomyService` (+ 타입) | `shared/Contracts/IEconomyService.cs` | ☑ 커밋 |
| `IInventoryService` (+ 타입) | `shared/Contracts/IInventoryService.cs` | ☑ 커밋 |
| `IPlatformServices.Economy` / `.Inventory` | `shared/Contracts/IPlatformServices.cs` | ☑ 커밋 |
| 목 구현 2종 | `shared/Mocks/MockEconomyService.cs`, `MockInventoryService.cs` | ☑ 커밋 |
| 플랫폼 실물 자리 (당장은 목) | `platform/OverlayShell.cs` | ☑ 커밋 — §5-3 |
| 백엔드 API 계약 | [ECONOMY-SERVER-API.md](ECONOMY-SERVER-API.md) | ☑ 문서만. 구현 없음 |
| 백엔드 스캐폴딩 (Cloudflare Workers + D1) | `server/` | ☑ 커밋. 타입체크·번들 통과. **배포 전 필수 확인 사항은 `server/README.md` §3** — 특히 아이템 지급 호출은 스팀웍스 파트너 문서로 미검증 |
| `IEconomyService` 실물 (HTTP 클라이언트) | `platform/EconomyClient.cs` | ☑ 커밋. 빌드 확인됨(Steamworks.NET 실제 API로 컴파일 통과) — §5-4 |
| `IInventoryService` 실물 (스팀 인벤토리 직접 조회) | `platform/SteamInventoryService.cs` | ☑ 커밋. 빌드 확인됨 — §5-5. id↔itemdefid 매핑을 `server/src/catalog.ts`와 손으로 맞췄다 |
| `OverlayShell`에 실물 연결 | `platform/OverlayShell.cs` | ☑ 완료 (2026-09-23) — §5-6. 릴리스는 항상 실물, 디버그는 기본 목(`--real-economy`로 실물 강제). `--real-economy --steam`으로 실측: 세션 발급·`GET /v1/economy/state`(슬롯 3개 수신) 확인됨. 스팀 인벤토리 조회는 아직 `k_EResultFail`(itemdef 전파 대기 중으로 추정) |
| Cloudflare 계정 셋업 + 실제 배포 | `punchmonkey-economy.shimwoojin627.workers.dev` | ☑ 배포됨. D1·시크릿·라우팅·스팀 직접 호출까지 전부 정상 확인 — §9 |
| ~~스팀 Web API 호출이 워커에서 막힘~~ | `server/src/steam.ts` | ✅ **오판이었다 (§9).** 진짜 원인은 `wrangler secret put`의 대화형 입력이 빈 값을 저장한 것 - IP 차단은 없었다. AWS Lambda 릴레이(`server/aws-relay/`)는 만들어서 검증까지 했지만 필요 없어서 다시 뺐다(코드는 참고용으로 남겨둠) |
| 스팀 인벤토리 서비스 아이템 정의 등록 | `server/steam-inventory/itemdefs.json` | ☑ 15종(itemdefid 1~15) 등록·게시 완료. 아이콘은 GitHub raw URL(공개 저장소) 사용 — 더 안정적인 호스팅으로 나중에 옮기는 걸 고려할 것. `marketable: false`로 등록(§4 밸브 승인 전까지) |
| 스팀 커뮤니티 마켓 신청 | 파트너 사이트 | ⬜ 미착수 — **가장 먼저 넣어야 하는 항목 (§4)** |
| `game/shop/Inventory.cs` · `GameRoot.cs` · `Tree.cs` 리와이어 | `game/` | ☑ 커밋 (2026-09-23) — §6. 세이브 스키마 v4(§6-1), 목으로 헤드리스 리포트 스모크 테스트 통과 |

---

## 1. 무엇을, 왜 바꾸는가

| 이전 (§4-1) | 이후 (이 문서) |
|---|---|
| 바나나 잔액 = 로컬 세이브 필드 | 바나나 잔액 = **서버 원장**. 로컬 값은 예측/캐시일 뿐 |
| 나무 슬롯 성장 = 로컬에서 계산, 세이브에 기록 | 나무 슬롯 성장 = **서버가 경과 시간으로 계산**. 로컬은 그 미러를 그린다 |
| 강화 레벨 = 로컬 세이브 필드 | 강화 레벨 = **서버 원장**. 슬롯 수·성장 주기에 직접 영향을 주므로 잔액과 같은 신뢰 등급이 필요하다 |
| 커서 장식 소유권 = 로컬 세이브의 `inventory.owned` 배열 | 커서 장식 소유권 = **스팀 인벤토리 서비스**(밸브 호스팅). 장착 상태만 로컬에 남는다 |
| 멀티 = 경제와 무관 (§4-1 그대로 유지) | 안 바뀜. `INetSession`/`PlayerState` 는 그대로다 — 이 피벗과 별개 트랙 |

**바뀌지 않는 것을 분명히 한다.** 관전형 멀티(로비·P2P 브로드캐스트,
`INetSession`)는 이 피벗과 독립이다. 경제를 서버로 옮기는 이유는 마켓 거래
신뢰성 때문이지 멀티 때문이 아니다 — 둘을 섞으면 "멀티 넣으려고 서버 만들었다"는
잘못된 인과가 문서에 남는다.

---

## 2. 범위

**이번 피벗에 들어가는 것**: 바나나 잔액, 나무 슬롯 성장 상태, 강화 3축 레벨,
커서 장식 16종의 소유권.

**안 들어가는 것**: 관전형 멀티의 상태 브로드캐스트(`PlayerState`) — 그대로
로컬 파생값을 실어 보낸다. 누적 타수(§6)도 재화가 아니라 기록이므로 서버
원장에 안 들어간다 — 조작돼도 실제 금전과 연결되지 않는다.

**"아이템"의 정의를 커서 장식 16종으로 좁힌다.** 강화는 스팀 인벤토리 아이템이
**아니다** — 파워/주기/슬롯 레벨을 마켓에서 거래 가능한 아이템으로 만들면
사실상 페이투윈 아이템을 되파는 구조가 되고, 이건 원래 기획에도 없던 것이다.
강화 구매는 §5 그대로 "레벨을 올리는 것"이고, 다만 그 레벨의 진실이 로컬에서
서버로 옮겨갈 뿐이다.

---

## 3. 아키텍처

```
클라이언트                    서버리스 백엔드                    스팀
  (game/, platform/)         (docs/ECONOMY-SERVER-API.md)      (Steamworks)

  RequestHarvest(slot)  ──▶  POST /economy/harvest  ──▶  경과시간으로 슬롯 재계산
       │  (낙관적으로 즉시                                → ready 면 잔액 +N, 슬롯 리셋
       │   로컬 반영, 응답 안 기다림)
       │
  PurchaseItem(id)      ──▶  POST /economy/purchase/item ─▶ 잔액 검증/차감
       │  (응답을 기다린다 -                                 └▶ ISteamInventoryService
       │   구매 버튼 자체가 결과다)                              /AddItem 호출 (밸브)
       │
  IInventoryService.Refresh() ─▶ (직접 또는 백엔드 경유) ──▶ 스팀 인벤토리 조회
```

**서버는 상시 구동 프로세스가 아니다.** 나무 슬롯 성장이 "경과 시간 × 성장
주기"로 결정론적이므로(기획서 §2-2 가 원래도 이렇게 설계했다 — 오프라인 성장이
슬롯 상한으로 캡된다), 서버는 요청이 올 때마다 `lastSyncUtc` 로부터 경과 시간을
계산해서 슬롯 상태를 재구성하면 된다. **타건 스트림을 실시간으로 받을 필요가
없다.** 이게 무료 서버리스 아키텍처를 가능하게 하는 핵심 근거다 —
[ECONOMY-SERVER-API.md](ECONOMY-SERVER-API.md) §0 에 비용 구조를 정리했다.

**낙관적 갱신 + 서버 정정.** 하베스트는 즉각 피드백이 P0(§2-3)이므로 로컬에서
바로 반영하고 서버 확인은 백그라운드로 보낸다. 구매는 다르다 — "성공했는가"
자체가 UI 결과(장착 화면에 새 아이템이 뜨는가)이므로 응답을 기다린다. 이 차이는
`IEconomyService.RequestHarvest`(fire-and-forget)와 `.PurchaseItem`/
`.PurchaseUpgrade`(`Task<PurchaseResult>`)의 시그니처 차이로 그대로 드러난다.

---

## 4. 스팀 커뮤니티 마켓 — 가장 큰 리스크, 가장 먼저 할 일

**마켓에 아이템을 올릴 수 있게 하는 "marketable" 플래그는 셀프서비스가
아니다.** 스팀 인벤토리 서비스로 아이템을 옮기는 것과 그 아이템이 커뮤니티
마켓에서 거래되는 것은 별개 단계이고, 후자는 밸브가 앱 단위로 재량 승인한다.
**우리 개발 속도와 무관하게 승인 여부·시점이 결정된다.**

그래서:

1. **기술 작업(§6)과 무관하게 지금 바로 파트너 사이트에 마켓 활성화를 신청한다.**
   승인 대기가 병목이므로 개발 완료를 기다릴 이유가 없다 — 개발과 병렬로 간다.
2. 아이템은 처음부터 스팀 인벤토리 서비스 구조로 넣는다(§5). 승인이 나면
   추가 개발 없이 `Marketable` 플래그만 켜진다 — `InventoryItem.Marketable`
   이 그 값을 그대로 읽는다.
3. **승인이 출시 전에 안 나올 가능성을 기본 전제로 잡는다.** 마켓 UI(스팀
   오버레이가 제공하는 표준 마켓 화면)는 우리가 만드는 게 아니라 밸브 쪽이므로,
   승인 전에는 "거래 가능"이 그냥 안 보이는 것뿐 — 게임 쪽 코드가 승인 여부로
   분기할 일은 표시 문구(§7 개인정보/설명 문구에 "마켓 거래 지원 예정" 식으로
   현재형 대신 미래형을 쓰는 것) 정도다.

---

## 5. 인터페이스

전체 시그니처는 소스가 원본이다 — 여기서는 왜 이렇게 갈랐는지만 남긴다.

### 5-1. `IEconomyService` — 서버 원장

`shared/Contracts/IEconomyService.cs`. 바나나 잔액 + 나무 슬롯 + 강화 레벨을
**한 인터페이스로 묶었다** — 셋 다 같은 서버 원장, 같은 신뢰 경계이기 때문이다.
쪼개면 "슬롯은 서버가 맞는데 강화는 로컬"같은 절반짜리 신뢰 경계가 생기고,
그게 정확히 지금 고치려는 구멍이다.

`RequestHarvest`가 반환값 없이 즉시 끝나는 것과 `PurchaseItem`/
`PurchaseUpgrade`가 `Task<PurchaseResult>`인 것의 차이는 §3 의 낙관적 갱신
설명 그대로다.

### 5-2. `IInventoryService` — 스팀 소유권

`shared/Contracts/IInventoryService.cs`. **소유권만 다룬다.** 장착은 여기 없다 -
스팀은 "장착"을 모르는 시스템이고, 그걸 억지로 스팀 아이템 인스턴스의 커스텀
필드에 넣으면 이 인벤토리 서비스가 풀려는 문제(소유권의 단일 진실)와 무관한
복잡도만 늘어난다. **2026-09-23 리와이어링 때 `Equip`/`EquippedIn`을 인터페이스에서
뺐다** — 처음 커밋에는 있었는데, 장착의 진실은 로컬(`SaveData.EquippedState`)에
두기로 한 결정과 인터페이스가 어긋나 있었다. 실제로 쓴 곳이 없어서(=아무도
호환성을 요구하지 않아서) 바로 고쳤다.

구매는 이 인터페이스에 없다 — `IEconomyService.PurchaseItem`이 하고, 성공하면
`IInventoryService.OnItemsChanged`가 뒤따라 불린다. 두 인터페이스가 같은
트랜잭션의 양면이라는 것을 목 구현(`MockEconomyService.LinkInventory`)이
그대로 재현한다.

장착을 로컬에 두는 대신 **누가 그 로컬 상태를 들고 다니는가**는 게임 레이어
(`game/shop/Inventory.cs`)다 - `SaveData.EquippedState`를 직접 받아서
읽고 쓰고, `Owns` 판정만 이 인터페이스에 위임한다 (§6).

### 5-3. `OverlayShell`은 지금 둘 다 목을 쓴다

`platform/OverlayShell.cs` 의 `_economy`/`_inventoryService` 필드가
`MockEconomyService`/`MockInventoryService` 다. `INetSession` 이 A9~A11 전까지
`MockNetSession`인 것과 같은 패턴 — **실물이 없다고 게임 레이어에 널을 넘기지
않는다**는 `IPlatformServices`의 기존 규칙을 그대로 따른다. 백엔드가 생기면
이 두 필드의 타입만 바뀌고 게임 레이어는 안 바뀐다.

### 5-4. `EconomyClient` — `IEconomyService` 실물이 있지만 아직 안 꽂았다

`platform/EconomyClient.cs`. ECONOMY-SERVER-API.md 를 그대로 구현한다 - 스팀
세션 티켓(`SteamUser.GetAuthSessionTicket`)으로 `/v1/session`을 받고, 이후
호출에 그 토큰만 싣는다. `RequestHarvest`는 계약대로 로컬을 먼저 바꾸고
백그라운드로 서버 확인을 보낸다(`ReconcileHarvestAsync`).

**컴파일로 검증됐다.** `SteamUser.GetAuthSessionTicket(byte[], int, out uint,
ref SteamNetworkingIdentity)` 시그니처는 문서로 찾은 게 아니라 실제 설치된
`Steamworks.NET 2024.8.0` 어셈블리를 상대로 `dotnet build`가 통과할 때까지
맞춘 것이다 - `steam.ts`의 밸브 파트너 Web API 호출(HTTP 문자열이라 컴파일러가
못 잡는다)과 다르게, 여기는 틀리면 바로 빌드가 깨진다.

### 5-5. `SteamInventoryService` — `IInventoryService` 실물

`platform/SteamInventoryService.cs`. §4 가 예고한 대로 **우리 백엔드를 거치지
않고 스팀 클라이언트 SDK(`ISteamInventory`)를 직접 부른다** - 조회는 신뢰
문제가 없어서 서버를 한 번 더 왕복시킬 이유가 없다.

`GetAllItems`가 비동기 요청을 걸고, `SteamInventoryResultReady_t` 콜백이
오면(스팀 콜백 펌프는 `SteamService.Tick`이 이미 매 프레임 돌리고 있다)
`GetResultItems`로 실제 항목을 받아온다 - 표준적인 "핸들 걸고 콜백 기다리기"
패턴이라 `SteamService`의 도전과제 통계 수신(`UserStatsReceived_t`)과 같은 모양이다.

**id ↔ itemdefid 매핑이 세 곳에 나뉘어 있다.** 우리 카탈로그는 문자열
id("monkey_02")를 쓰는데 스팀 아이템 정의는 정수다. 이 번호는 우리가
정하는 값이라(밸브가 강제하지 않는다) `server/src/catalog.ts`의
`steamItemDefId`, `platform/SteamInventoryService.cs`의 `Catalog` 표, 그리고
**스팀 파트너 사이트에 실제로 등록할 번호**가 전부 정확히 같아야 한다. 지금은
`monkey_02`~`ring_02` 15종에 1~15를 순서대로 매겼다(`ShopCatalog.All` 순서,
기본 지급품 `monkey_01` 제외) - 자동 생성 없이 손으로 맞춘 상태이고, 세
곳 중 하나만 바뀌면 조용히 다른 아이템으로 잡히거나 아무것도 안 잡힌다.

**`Tradable`/`Marketable` 판정은 미검증이다.** `m_unFlags`의
`k_ESteamItemNoTrade` 비트로 `Tradable`을 추정했는데, `Marketable`은 아이템
인스턴스가 아니라 **아이템 정의**의 속성(`GetItemDefinitionProperty`로 조회)일
가능성이 있어 지금은 항상 `false`로 고정해 뒀다 - 스팀 파트너 문서로
재확인해야 하는 항목이다(`steam.ts`의 `grantInventoryItem`과 같은 급의 미검증).

### 5-6. `OverlayShell`에 실제로 꽂혔다 (2026-09-23)

`platform/OverlayShell.cs`의 `ShouldUseRealEconomy()`가 실물/목을 고른다 -
**릴리스 빌드는 항상 실물**, **디버그 빌드는 기본 목**이고 `--real-economy`
로 디버그에서도 실물을 강제할 수 있다(`--economy-url=`로 다른 서버도 겨냥
가능). 기본을 목으로 둔 이유는 `game/GameRoot.cs`의 Shift+B/Shift+R/G
디버그 키가 전부 목 전용 캐스팅이라, 기본을 실물로 바꾸면 그 키들이 조용히
죽기 때문이다.

**꽂으면서 실측으로 드러난 버그 2개, 그 자리에서 고쳤다:**

1. **세션 발급이 항상 예외로 죽었다.** `EconomyClient`가 서버의 만료 시각을
   `DateTimeStyles.RoundtripKind | DateTimeStyles.AdjustToUniversal`로
   파싱했는데, .NET 은 `RoundtripKind`를 다른 보정 플래그와 같이 쓰는 걸
   막는다 - `--real-economy`로 처음 돌려보고서야 드러났다(헤드리스
   selftest/typecheck 로는 안 잡히는 종류다). `RoundtripKind` 하나로 줄여서
   고쳤다.
2. **나무 슬롯이 항상 0개였다.** `GameRoot`가 로드 시점에
   `IEconomyService.Sync()`/`IInventoryService.Refresh()`를 한 번도 안
   불렀다 - 목은 생성자에서 이미 3슬롯을 채워 두니 안 드러났지만, 실물은
   서버에 물어봐야 알 수 있는 값이라 빈 채로 시작했다. `AttachPlatform`이
   `LoadGameStateAsync()`로 바뀌어 `Sync()`/`Refresh()`를 먼저 기다린 뒤
   `LoadGameState()`를 부른다.
3. (버그는 아니지만 같이 고침) **구매 성공 후 상점 화면이 실물에서 안
   갱신될 뻔했다.** 목은 `MockEconomyService.LinkInventory`로 구매→지급이
   자동 연결되지만, 실물 둘(`EconomyClient`/`SteamInventoryService`)은
   서로 남남이다 - `GameRoot.OnBuyRequested`가 구매 성공 직후
   `IInventoryService.Refresh()`를 명시적으로 부르도록 추가했다.

**실측 결과** (`--real-economy --steam`, 헤드리스):
세션 발급 성공, `GET /v1/economy/state`로 슬롯 3개 수신 확인. 스팀 인벤토리
조회(`GetAllItems`)는 아직 `k_EResultFail`인데, 아이템 정의를 같은 날 막
등록·게시한 직후라 전파 대기 중으로 추정한다(§ 아래, A8 문서의 같은 패턴).

---

## 6. 게임 레이어 리와이어링 (2026-09-23 완료)

| 파일 | 전 | 후 |
|---|---|---|
| `game/shop/Inventory.cs` | `SaveData.Bananas`/`InventoryState.Owned`를 직접 읽고 쓴다 | `IEconomyService`(잔액·구매)와 `IInventoryService`(소유권) 호출로 대체. 장착만 `SaveData.EquippedState`에 남는다 - 스팀이 모르는 로컬 개념이라 |
| `game/GameRoot.cs` | `Save.Bananas += harvested` 직접 가산, 나무 슬롯 타이머를 로컬에서 계산 | `IEconomyService.RequestHarvest` 호출, HUD는 `OnStateChanged` 이벤트로 갱신. `_UnhandledInput`/`_Process`가 매 프레임 `Tree.SyncSlots(Economy.Slots)`로 시각만 반영 |
| `game/entities/Tree.cs` | `_timers`/`_msCarry`를 들고 매 프레임 자체 계산 (`Tick`/`AdvanceOffline`/`WriteTo`) | 계산은 안 하고 `SyncSlots(IReadOnlyList<SlotState>)`로 받은 값만 그린다. 슬롯 개수가 바뀌면(강화) 자식 노드를 다시 짠다 |
| `shared/Save/SaveData.cs` | `bananas`/`tree`/`upgrades`/`inventory.owned`가 세이브 필드 | **스키마 v4** - 이 넷을 걷어냈다. 출시 전이라 실사용 세이브가 없어서 값 이전 없이 필드만 제거했다(`SaveSchema.MigrateV3ToV4`). `inventory.equipped`/`settings`/`totalKeystrokes`/`lastQuitUtc`는 그대로 로컬 |
| `game/ui/ShopWindow.cs` | - | **변경 없음.** `Inventory`가 `Bananas`/`Owns`/`EquippedIn`/`OwnedCount` 등 같은 이름의 프로퍼티를 그대로 유지해서 파사드 뒤가 바뀐 걸 몰라도 된다 |

### 6-1. 알게 된 것 / 남긴 것

**하베스트가 "슬롯 스캔"으로 바뀌었다.** 예전엔 `Tree.TryHarvest`가 타이머
소유자로서 즉석에서 판정+소비를 같이 했다. 이제 `GameRoot`가
`IEconomyService.Slots`를 훑어 `Ready`인 슬롯을 찾고, `RequestHarvest`를
부른 뒤 **같은 반복 안에서 `Slots`를 다시 읽는다** - 목(그리고 계약을 지키는
모든 구현)이 `RequestHarvest` 안에서 그 슬롯을 동기적으로 비우기 때문에,
한 번의 키 배치(100ms) 안에서 같은 슬롯이 중복 수확되지 않는다.

**디버그 키 3종(G/Shift+B/Shift+R)이 전부 "목일 때만" 동작하도록 다시 짰다.**
성장 앞당기기·전 상품 지급·인벤토리 초기화 전부 서버(스팀)가 진실을 들고
있는 값을 조작하는 것이라, 클라이언트가 스스로에게 부여하는 통로를 열면
docs/ECONOMY-SERVER-API.md §4 가 막으려는 구멍과 같은 모양이 된다. 그래서
`_platform.Economy`/`_platform.Inventory`를 구체 목 타입으로 캐스팅해서
성공할 때만 동작하고, 실물이 붙으면 자동으로 막힌다(경고 로그만 남기고 조용히
실패).

**"오프라인에 N개 열림" 로그가 없어졌다 — 알고 뺐다.** 예전엔 켤 때마다
`Tree.AdvanceOffline`이 꺼져 있던 동안 새로 익은 개수를 세서 보여줬다. 서버가
`lastSyncUtc` 기준으로 이미 캐치업된 슬롯 상태를 돌려주므로(§3) 클라이언트가
따로 "오프라인 동안 몇 개"를 계산할 방법이 없다 - `GET /v1/economy/state`
응답에 그 델타가 없기 때문이다. 되살리려면 ECONOMY-SERVER-API.md §2-1
응답에 필드를 하나 추가해야 한다. 지금은 기능 손실을 감수했다.

**목의 잔액은 앱을 다시 켤 때마다 0으로 돌아간다 - 이건 버그가 아니라
정직한 상태다.** 목은 메모리에만 있고 어디에도 저장하지 않는다. 로컬 세이브에
백업 삼아 되살려 두면 "화면엔 서버 권위라고 적혀 있는데 실제로는 로컬 세이브가
잔액을 되살린다"는 거짓말이 된다. 실물 백엔드가 붙으면 D1 원장이 재시작과
무관하게 값을 들고 있으므로 이 증상 자체가 사라진다.

---

## 7. 오프라인 동작 — 설계 전제가 깨지는 지점

기획서 §7-1/§2-1 은 "스팀이 없어도, 인터넷이 없어도 게임이 온전하다"를
전제로 깔았다. 서버 권위 경제는 이 전제와 정면으로 부딪힌다.

**타협안**: 하베스트는 오프라인에서도 로컬 예측을 계속 쌓는다(§3) — 슬롯
성장 공식이 서버와 동일하므로 재접속 시 `Sync()`가 대부분 그대로 확정한다.
**구매만 온라인 필수로 만든다** — 상점 화면에서 `IEconomyService.IsAvailable
== false`면 구매 버튼을 비활성화하고 "인터넷 연결 필요" 같은 문구를 띄운다.
방치형의 핵심(자리를 비워도 자란다)은 안 깨지고, 새로 생기는 제약은 "당장 이
순간 사려면 온라인이어야 한다"는 것뿐이다.

---

## 8. 비용 — 요약

상세 근거는 [ECONOMY-SERVER-API.md](ECONOMY-SERVER-API.md) §0. 요약하면:
아이템 커스터디(스토리지)는 밸브가 무료로 호스팅하고(스팀 인벤토리 서비스),
우리가 만드는 것은 인증+원장 검증만 하는 서버리스 함수 하나다. 트래픽이
낮은(구매는 저빈도, 하베스트는 배치) 인디 게임 규모에서는 서버리스
공급자(Cloudflare Workers 등)의 무료 티어 안에 들어갈 가능성이 높다.

---

## 9. 실배포에서 쫓은 유령 — "Cloudflare 가 막혔다"는 오판이었다

**2026-09-23, 첫 실배포 검증 중 발견.** 배포·D1·시크릿·라우팅까지 전부
정상인데, `/v1/session`(스팀 티켓 검증)만 항상 `403 Forbidden`이 났다.
디버깅 끝에 **AWS Lambda 릴레이까지 만들어서 우회했다가, 결국 그 릴레이가
불필요했다는 것을 알고 되돌렸다.** 시행착오를 그대로 남긴다 - 같은 함정을
또 밟지 않기 위해서다.

### 1차 결론(틀림) — 키를 고정한 "통제 실험"조차 통제가 안 돼 있었다

당시 세운 표는 이랬다:

| 발신 위치 | 키 | 결과 |
|---|---|---|
| 로컬 PC (curl 직접) | 진짜 키 | ✅ `200` |
| **Cloudflare Workers (배포된 워커)** | "진짜 키" | ❌ `403` |
| GitHub Actions (Azure, 리포지토리 시크릿) | "진짜 키" | ✅ `200` |

키를 고정했다고 생각했지만 **틀렸다** - Cloudflare 쪽 "진짜 키"는
`wrangler secret put`으로 넣은 값이었는데, **이 세션의 터미널 중계
방식(`! <command>`)이 wrangler 의 대화형(마스킹) 프롬프트에 빈 문자열을
넘기고 있었다.** `wrangler secret list`로는 시크릿이 "있다"는 것만 보이고
값은 절대 안 보이므로, 몇 시간 동안 "설정했다"고 믿은 값이 전부 빈
문자열이었다는 걸 몰랐다. 즉 Cloudflare Workers 가 보낸 건 진짜 키가 아니라
**빈 키**였고, 밸브 엣지(Akamai)가 빈/형식이 이상한 키를 정적 HTML
403(`Please verify your key= parameter`)으로 거부한 것 - 로컬에서
`key=test`(가짜 값)를 보냈을 때와 완전히 같은 현상이었다.

**발견 경위**: AWS Lambda 릴레이(§ 아래)의 환경변수도 처음엔 똑같이 안
맞았는데, 그건 AWS 콘솔의 눈에 보이는 입력창이라 사용자가 직접 보고
"공백이 있었다"고 잡아냈다. 그 뒤 Cloudflare Worker에 진단용 엔드포인트를
심어서 `env.STEAM_PUBLISHER_WEB_API_KEY?.length`를 직접 찍어보니 **0** 이
나왔다 - 그제서야 며칠간의 "403 = IP 차단"이라는 결론 전체가 무너졌다.

### 2차 결론(맞음) — Cloudflare Workers 에서 직접 불러도 아무 문제 없다

`printf '%s' "<값>" | npx wrangler secret put NAME` 처럼 **대화형 프롬프트를
거치지 않는 파이프 입력**으로 다시 넣고 나서 같은 실험을 다시 했다:

| 발신 위치 | 키 | 결과 |
|---|---|---|
| Cloudflare Workers (배포된 워커, 파이프로 넣은 진짜 키) | 진짜 키 | ✅ `200` + 밸브 JSON 에러(`"Invalid parameter"` - 가짜 티켓이라 정상) |

**Cloudflare Workers 의 발신 IP 를 밸브/Akamai 가 막는다는 근거는 처음부터
없었다.** 문제는 인프라가 아니라 **이 세션에서 대화형 시크릿 입력을 중계하는
방식**이었다.

> **교훈 - `wrangler secret put`처럼 마스킹된 대화형 프롬프트를 `!` 로
> 중계해서 넣을 때는 값을 신뢰하지 말 것.** 성공 메시지(`✨ Success!`)가
> 떠도 값이 비어 있을 수 있다. 이후로는 `printf '%s' "<값>" | wrangler
> secret put NAME`처럼 파이프로 비대화식 입력하거나, 넣은 직후 길이만
> 노출하는 임시 진단 엔드포인트로 실제 반영을 확인한다.

### AWS Lambda 릴레이 — 만들었지만 다시 뺐다

원인 규명 전에 "작은 릴레이를 추가"하는 방향으로 이미 진행해서, 실제로
`server/aws-relay/index.mjs`(AWS Lambda, Function URL, `X-Relay-Secret`
공유 비밀 인증)를 만들고 배포까지 해서 **정상 동작을 확인했다.** 진짜 원인이
밝혀진 뒤 `server/src/steam.ts`를 다시 스팀 직접 호출로 되돌렸고, Cloudflare
쪽의 `STEAM_RELAY_URL`/`STEAM_RELAY_SECRET` 시크릿도 지웠다.

**Lambda 함수 자체는 지우지 않고 남겨뒀다** - 코드는 `server/aws-relay/`에
참고용으로 있고, AWS 콘솔의 `punchmonkey-steam-relay` 함수도 그대로 있다
(유휴 비용 없음). 나중에 정말로 어떤 발신 IP 대역이 막히는 상황이 생기면
바로 꺼내 쓸 수 있는 검증된 대안이다.

### 영향 범위 (정정)

**없다.** D1, 세션 토큰, 하베스트/구매 원장 로직, 스팀 직접 호출 전부
정상이다. `server/src/steam.ts`는 최초 스캐폴딩 버전(직접 호출)으로 되돌아갔고,
`server/src/types.ts`의 `Env`도 `STEAM_PUBLISHER_WEB_API_KEY` 하나로
단순화된 원래 모양이다.
