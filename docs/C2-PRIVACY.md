# C2 — 개인정보 문구 (초안)

관련: [확정 기획서](기획확정-일감분배-260907.md) §7-6 (개인정보·보안 방침) · §10 ("절대 자르지 않는 것") /
[A4 글로벌 입력](A4-GLOBAL-INPUT.md) §2 / [ECONOMY-SERVER.md](ECONOMY-SERVER.md) / [A15 도전과제](A15-ACHIEVEMENTS.md)

## 0. 상태 (2026-09-24)

| 항목 | 상태 |
|---|---|
| 짧은 문구 (스토어 요약 · 게임 내 최초 실행) | ☑ 초안 — §1 |
| 전체 문구 (개인정보 처리 안내) | ☑ 초안 — §2 |
| 근거표 (문장마다 코드 위치) | ☑ — §3 |
| **문의처 이메일** | ☐ `[문의 이메일]` 자리 — 사람이 정한다 |
| **A10 (P2P) 전송 목록** | ☑ 2026-09-24 — 코드(`PlayerStateCodec`)와 대조해 §2-5 에 확정. 도감 수집률이 빠져 있던 것을 넣었다. P2P 는 스팀 릴레이만 쓰게 해서(직접 연결 끔) IP 비노출 문장을 넣었다 |
| 게임 내 최초 실행에서 짧은 문구 노출 | ☐ B15 (온보딩)에서 — §7-6 이 "스토어와 게임 내 최초 실행에 같은 문구" 로 정했다 |
| **옵션 "타건 카운트" 끄기** | ☐ **체크박스는 있지만 아무 동작도 안 한다**(설정값 저장만, 읽는 코드 없음 — 9/21 부터 남은 항목). 구현 전에는 §2-1 의 해당 문장을 쓰면 안 된다 — §4 |

**원칙.** 이 게임은 전역 입력을 받는 상주 앱이라 "키로거 아니냐" 가 첫 의심이다. 문구가 한 문장이라도
사실과 다르면 §10 이 "절대 자르지 않는 것" 으로 둔 약속이 거짓말이 된다. 그래서 **모든 문장을 코드로
확인했고(§3), 코드가 바뀌면 이 문서부터 고친다.** 부풀리지도 줄이지도 않는다 — "아무것도 수집하지
않습니다" 같은 문장은 서버가 있는 이상 거짓이다.

---

## 1. 짧은 문구

스토어 페이지 "게임 정보" 의 맨 위, 그리고 게임 내 최초 실행 화면에 **같은 문구**를 쓴다 (§7-6).

### 한국어

> **이 게임은 타이핑과 마우스 클릭의 횟수만 셉니다.** 어떤 키를 눌렀는지, 어떤 버튼을 눌렀는지,
> 마우스가 어디에 있는지 읽지 않고, 저장하지 않으며, 외부로 전송하지 않습니다.
>
> 바나나와 커서 장식을 지키기 위해 게임 서버에 스팀 계정 ID 와 게임 진행 상태(잔액·나무·강화)를
> 저장합니다. 자세한 내용은 개인정보 처리 안내를 참고하세요.

### English

> **This game only counts how many times you type and click.** It never reads which keys or buttons
> you press or where your mouse is — and it never stores or sends that information anywhere.
>
> To keep your bananas and cursor decorations safe, our game server stores your Steam account ID and
> your game progress (balance, tree, upgrades). See the privacy notice for details.

---

## 2. 전체 문구 — 개인정보 처리 안내

스토어 페이지 하단(또는 링크한 페이지)과 게임 옵션 창의 "개인정보" 항목에 둔다.

### 한국어

**PunchMonkey 개인정보 처리 안내** (최종 수정: 2026-09-24)

**1. 입력을 어떻게 세나요**
PunchMonkey 는 바탕화면에 떠 있는 동안 키보드와 마우스 버튼이 **눌렸다는 사실**만 셉니다.
- 어떤 키인지(키 코드), 어떤 마우스 버튼인지, 커서 위치는 **읽지 않습니다.** 읽지 않으므로 저장하거나
  보낼 수도 없습니다.
- 마우스 휠은 세지 않습니다.
- 입력은 별도의 작은 프로그램(`InputHelper.exe`)이 Windows 표준 방식(Raw Input)으로 받습니다. 키보드 훅
  방식은 쓰지 않습니다.
- `[옵션 동작 구현 후 확정: 옵션에서 타건 카운트를 끌 수 있습니다.]`

**2. 이 PC 에 저장하는 것** (`%APPDATA%\PunchMonkey\`)
- 누적 타수, 옵션 설정, 장착한 커서 장식, 마지막 저장 시각
- 실행 기록(로그) 파일 — 문제 해결용이며 스팀 이름과 스팀 계정 ID 가 들어갈 수 있습니다. 외부로 보내지
  않습니다. 문의하실 때 직접 첨부하실 수 있습니다.
- 멀티 로비에서 친구 칸을 옮겨 둔 자리 — 다음에 같은 친구와 만날 때 그 자리에 두려고, 친구의 스팀 계정
  ID 와 화면 위치를 함께 저장합니다. 마지막으로 있던 로비 정보도 저장합니다. 외부로 보내지 않습니다.
- "Windows 시작 시 실행" 을 켜면 Windows 시작 프로그램 목록에 등록됩니다.

**3. 게임 서버에 저장하는 것**
바나나를 임의로 늘리거나 장식을 복제하지 못하게, 재화와 구매는 게임 서버가 확인합니다.
- **스팀 계정 ID** — 누구의 진행인지 구분하는 데 씁니다. 로그인할 때 스팀이 발급한 일회성 인증 티켓을
  Valve 에 보내 확인하며, 비밀번호는 다루지 않습니다.
- **게임 진행 상태** — 바나나 잔액, 강화 단계, 나무 슬롯의 성장 상태, 마지막 동기화 시각
- **구매 기록** — 같은 구매가 두 번 처리되지 않게 하는 요청 기록, 서버가 지급한 장식 목록
- 게임 서버는 Cloudflare 에서 운영합니다. 게임 서버는 IP 주소를 저장하지 않지만, Cloudflare 가 서비스
  제공 과정에서 처리할 수 있습니다.

**4. 스팀(Valve)에 저장되는 것**
- 커서 장식 — 스팀 인벤토리 아이템으로 지급됩니다.
- 도전과제 달성 여부와 누적 타수 통계 — 스팀 프로필 공개 설정에 따라 다른 사람에게 보일 수 있습니다.
- 스팀에 저장되는 정보는 [Valve 개인정보 처리방침](https://store.steampowered.com/privacy_agreement/)을
  따릅니다.

**5. 멀티 로비에서 다른 사람에게 보이는 것**
멀티 로비는 스팀 로비로 동작하며, 우리 게임 서버를 거치지 않습니다.
- 로비 코드를 아는 사람은 스팀 친구가 아니어도 로비에 들어올 수 있습니다.
- 같은 로비의 사람들에게: 스팀 이름과 프로필(스팀이 보여 줍니다), 로비에 들어온 뒤 친 횟수(로비 타수)
- 로비에서 나간 뒤에도 그 로비가 남아 있는 동안에는, 다시 들어왔을 때 이어서 세기 위해 로비 타수가
  스팀 계정 ID 와 함께 로비 정보에 남습니다. 로비 코드를 아는 사람은 로비 정보를 볼 수 있습니다.
  마지막 사람이 나가 로비가 사라지면 함께 사라집니다.
- 스팀 친구에게: 로비에 있는 동안 "게임 참가" 에 쓰이는 로비 정보
- 친구 목록과 친구의 접속 상태(온라인·게임 중·참가 정보)는 로비 창에서 참가·초대를 보여 주는 데만 이
  PC 에서 읽으며, 우리 서버로 보내지 않습니다.
- 같은 로비의 사람들에게, 로비에 있는 동안 0.2초마다: 그 사이 친 횟수, 딴 바나나 수, 누적 타수, 장착한
  커서 장식, 도감 수집률. 친구 화면에서 원숭이와 나무를 움직이는 데만 씁니다. 어떤 키를 눌렀는지는 들어
  있지 않습니다. 스팀 중계 서버를 거쳐 전달되므로 서로의 IP 주소는 보이지 않습니다.

**6. 하지 않는 것**
- 키 내용, 마우스 위치, 화면, 다른 프로그램, 파일을 읽지 않습니다.
- 광고, 분석(애널리틱스), 추적 도구를 넣지 않았습니다.
- 개인정보를 판매하거나 광고 목적으로 제공하지 않습니다.

**7. 삭제**
- 이 PC 의 데이터: 게임을 삭제한 뒤 `%APPDATA%\PunchMonkey\` 폴더를 지우면 됩니다.
- 게임 서버의 데이터: `[문의 이메일]` 로 스팀 계정 ID 와 함께 요청하시면 삭제합니다. 삭제하면 바나나와
  진행 상태가 복구되지 않습니다. 이미 받은 커서 장식은 스팀 인벤토리에 남습니다.

**8. 문의**
`[문의 이메일]`

### English

**PunchMonkey Privacy Notice** (Last updated: 2026-09-24)

**1. How we count your input**
While PunchMonkey sits on your desktop, it only counts **that** a key or mouse button was pressed.
- It never reads which key (key code), which mouse button, or where your cursor is. Since it never reads
  them, it cannot store or send them either.
- Mouse wheel scrolling is not counted.
- Input is received by a small separate program (`InputHelper.exe`) using the standard Windows Raw Input
  API. No keyboard hooks are used.
- `[To be finalized once the option works: You can turn keystroke counting off in Options.]`

**2. Stored on your PC** (`%APPDATA%\PunchMonkey\`)
- Total keystroke count, option settings, equipped cursor decorations, last save time
- Log files for troubleshooting, which may include your Steam name and Steam account ID. They are never sent
  anywhere; you may attach them yourself when contacting us.
- Where you placed your friends' windows in multiplayer lobbies — stored together with each friend's Steam
  account ID so they appear in the same spot next time, plus the last lobby you were in. Never sent anywhere.
- If you enable "Run at Windows startup", the game is added to your Windows startup programs.

**3. Stored on our game server**
To prevent bananas from being inflated or decorations from being duplicated, our game server verifies
your balance and purchases.
- **Steam account ID** — used to identify whose progress it is. At sign-in, a one-time authentication
  ticket issued by Steam is verified with Valve. We never handle your password.
- **Game progress** — banana balance, upgrade levels, tree slot growth, last sync time
- **Purchase records** — request records that prevent the same purchase from being processed twice, and
  the list of decorations the server has granted
- The game server runs on Cloudflare. Our server does not store IP addresses, but Cloudflare may process
  them while providing its service.

**4. Stored by Steam (Valve)**
- Cursor decorations — granted as Steam Inventory items.
- Achievements and your total keystroke statistic — may be visible to others depending on your Steam
  profile privacy settings.
- Data stored by Steam is governed by the [Valve Privacy Policy](https://store.steampowered.com/privacy_agreement/).

**5. What others see in multiplayer lobbies**
Multiplayer lobbies run on Steam lobbies and do not go through our game server.
- Anyone who knows a lobby code can join that lobby, even if they are not your Steam friend.
- To people in the same lobby: your Steam name and profile (shown by Steam), and how many times you have
  typed since joining the lobby (lobby keystrokes)
- After you leave, while the lobby still exists, your lobby keystrokes are kept in the lobby information
  together with your Steam account ID so the count can continue if you rejoin. Anyone who knows the lobby
  code can view the lobby information. It disappears when the last person leaves and the lobby closes.
- To your Steam friends: lobby information used for "Join Game" while you are in a lobby
- Your friends list and your friends' status (online / in game / join information) are read on your PC only
  to show join and invite options in the lobby window, and are never sent to our server.
- To people in the same lobby, every 0.2 seconds while you are in it: how many times you typed in that
  moment, bananas harvested, total keystrokes, equipped cursor decorations, and collection progress. This
  is used only to animate your monkey and tree on their screens. It never contains which keys you pressed.
  It is delivered through Steam's relay servers, so your IP address is not visible to others.

**6. What we don't do**
- We never read key contents, mouse position, your screen, other programs, or files.
- There are no ads, analytics, or tracking tools in the game.
- We never sell your personal information or share it for advertising.

**7. Deletion**
- Data on your PC: uninstall the game and delete the `%APPDATA%\PunchMonkey\` folder.
- Data on our game server: email `[contact email]` with your Steam account ID and we will delete it.
  Deleted bananas and progress cannot be restored. Decorations you already received remain in your Steam
  Inventory.

**8. Contact**
`[contact email]`

---

## 3. 근거표 — 문장마다 어디서 확인했나

코드가 바뀌면 이 표에서 해당 줄을 찾아 문구를 같이 고친다.

| 문구 | 확인한 곳 |
|---|---|
| 횟수만 센다 / 키 코드·버튼·커서 위치를 읽지 않는다 | `platform/InputHelper/RawKeyboardCounter.cs` `HandleRawInput` — 버퍼에서 읽는 값은 타입·누름/뗌 플래그·버튼 눌림 마스크(`!= 0` 비교 한 번)뿐. 스캔 코드(24)·가상 키(30)·좌표(36/40)는 어느 경로에서도 안 읽는다 |
| 휠은 세지 않는다 | 같은 파일 `AnyButtonDown` 마스크 — 휠 비트(0x0400/0x0800) 제외 |
| 별도 프로그램, Raw Input, 훅 아님 | `InputHelper` 프로젝트, `RegisterRawInputDevices`(`RIDEV_INPUTSINK`). `WH_KEYBOARD_LL` 없음 (A4 §2) |
| ~~타건 카운트를 끌 수 있다~~ | **아직 거짓.** `SettingsState.KeystrokeCounting` 은 옵션 창이 저장만 하고 읽는 곳이 없다(2026-09-24 전수 검색) |
| PC 에 저장하는 것 | `shared/Save/SaveData.cs` — totalKeystrokes / settings / inventory.equipped / multi(friendPositions: 친구 스팀 ID → 화면 좌표, lastLobby) / lastQuitUtc. 경로는 `project.godot` 의 `custom_user_dir_name`. 세이브는 `SaveIO` 가 로컬 파일로만 쓴다 |
| 로그에 스팀 이름·ID | `SteamService.TryInit` 의 `[steam] 연결됨 ... user= id=` 출력 → Godot 로그 파일. 멀티 로그(`[net]`)에는 로비 ID 만 남고 **친구의 스팀 ID 는 남기지 않는다** (`SteamNetSession.InviteFriend`) |
| 시작 프로그램 등록 | A6 자동 시작 (레지스트리 Run 키) |
| 서버에 저장하는 것 | `server/schema.sql` — `players`(steam_id, balance, *_level, slot_elapsed_ms, last_sync_utc, created/updated_at), `idempotency_keys`(요청 ID·응답), `owned_items_mirror`(steam_id, item_def_id) |
| 세션 티켓을 Valve 에 확인, 비밀번호 없음 | `server/src/steam.ts` `authenticateUserTicket` (`ISteamUserAuth/AuthenticateUserTicket`) |
| 서버는 IP 를 저장하지 않는다 | `schema.sql` 에 IP 칼럼 없음, 코드에 `CF-Connecting-IP` 등 읽는 곳 없음. 로그는 `console.error` 하나(`wrangler tail` 실시간용, 영구 저장 설정 없음) |
| 외부 통신은 경제 서버뿐, 분석·광고 없음 | C# 에서 HTTP 를 쓰는 곳이 `platform/EconomyClient.cs` 하나 (2026-09-24 전수 검색) |
| 장식은 스팀 인벤토리 | `server/src/steam.ts` `grantInventoryItem`, `platform/SteamInventoryService.cs` |
| 도전과제·누적 타수 통계 | `SteamService.Unlock` / `SetStat(STAT_KEYSTROKES)` (A15) |
| 멀티 로비는 스팀 로비, 로비 타수 | `platform/SteamNetSession.cs` — `SetLobbyMemberData(ks)`, `SetLobbyData(v, created)` |
| 코드를 아는 누구나 입장·로비 정보 조회 | 같은 파일 `CreateLobby(k_ELobbyTypePublic, 4)`. 로비 목록 검색은 안 하지만 ID 로 `JoinLobby`·`RequestLobbyData` 가 된다 — 코드 입장의 사전 확인이 바로 이 `RequestLobbyData` 다 |
| 나간 뒤에도 로비 타수가 스팀 ID 와 함께 남는다 | 같은 파일 `OnLobbyChatUpdate`(방장이 나간 사람의 `s:<steamid>` 기록), `LeaveRoom`(방장 자신의 것). 로비 데이터라 로비가 사라지면 같이 사라진다 |
| 친구에게 참가 정보 | 같은 파일 `SetRichPresence("connect", "+connect_lobby <id>")`, 나가면 `ClearRichPresence` |
| 0.2초 상태에 든 것 (A10) | `shared/Contracts/PlayerStateCodec.cs` — 창 타건 수·수확 수·누적 타수·도감 %·장식 ID 3개가 전부다. 키 코드 필드가 없다. 보내는 곳은 `game/multiplayer/PlayerStateSender.cs` → `SteamNetSession.Broadcast`, **로비 멤버에게만** |
| IP 가 안 보인다 | `SteamNetSession.ForceRelayOnly` — `P2P_Transport_ICE_Enable = Disable`(직접 연결 끔)을 전역으로 걸고 되읽어 확인한다. 걸렸으면 로그 `[net] P2P 는 스팀 릴레이만 쓴다`. 기본값은 직접 연결을 시도할 수 있어서 이 설정 없이는 쓸 수 없는 문장이다 |
| 친구 목록·접속 상태는 PC 에서만 | 같은 파일 `GetFriends` — `GetFriendPersonaName`/`GetFriendPersonaState`/`GetFriendGamePlayed`/`GetFriendRichPresence`(`connect`)/`RequestFriendRichPresence`. 결과를 서버로 보내는 코드 없음 |

---

## 4. 출시 전에 채울 것 · 확인할 것

1. **옵션 "타건 카운트" 를 실제로 동작시키거나, 문장을 뺀다.** 끄면 헬퍼가 입력을 받지 않아야(또는
   적어도 게임이 세지 않아야) "끌 수 있다" 가 참이 된다. 개인정보 문구에 넣을 거라면 **헬퍼가 RawInput
   등록 자체를 푸는 쪽**이 약속으로서 가장 강하다
2. **문의 이메일** — §2-7, §2-8 의 `[문의 이메일]`. 스팀 파트너 사이트의 지원 이메일과 같게 한다
3. ~~**A10 전송 목록**~~ — 2026-09-24 확정(§2-5). §7-6 의 목록에는 **도감 수집률이 빠져 있었다** — 코드
   (`PlayerStateCodec`)가 보내는 것에 맞춰 문구와 §7-6 을 같이 고쳤다. 필드를 늘리면 여기부터 고친다
4. **서버 데이터 삭제 절차** — 지금은 요청이 오면 D1 에서 손으로 지운다(`players` · `idempotency_keys` ·
   `owned_items_mirror` 의 해당 `steam_id`). 요청이 늘면 스크립트로 만든다
5. **스토어 설명문과 따로 쓸 것** — 커뮤니티 마켓은 밸브 승인 전이다. 설명문에서 "마켓 거래 가능" 을
   현재형으로 쓰지 않는다(ECONOMY-SERVER §4). 이 문서는 개인정보만 다룬다
6. **게임 내 노출** — §1 짧은 문구를 최초 실행에(B15), §2 전체 문구를 옵션 창 "개인정보" 에서 열 수 있게
7. 법률 검토를 받은 문구가 아니다. 필요하면 출시 전에 검토를 받는다
