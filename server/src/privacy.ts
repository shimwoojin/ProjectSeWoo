// 개인정보 처리 안내 - 스팀 스토어의 "개인정보처리방침 URL" 이 가리키는 페이지 (GET /privacy).
// 문구의 원본은 docs/C2-PRIVACY.md §2 다. 문장마다 코드 근거가 거기 §3 에 있으니, 문구를 고칠 때는
// C2 를 먼저 고치고 이 파일을 그대로 옮긴다 - 여기서만 고치면 근거표와 갈라진다.

const CONTACT = "ggoggal@gmail.com";
const VALVE_PRIVACY = "https://store.steampowered.com/privacy_agreement/";

const KO = `
<h1>PunchMonkey 개인정보 처리 안내</h1>
<p class="meta">최종 수정: 2026-09-28 · <a href="#en">English</a></p>

<h2>1. 입력을 어떻게 세나요</h2>
<p>PunchMonkey 는 바탕화면에 떠 있는 동안 키보드와 마우스 버튼이 <strong>눌렸다는 사실</strong>만 셉니다.</p>
<ul>
  <li>어떤 키인지(키 코드), 어떤 마우스 버튼인지, 커서 위치는 <strong>읽지 않습니다.</strong> 읽지 않으므로 저장하거나 보낼 수도 없습니다.</li>
  <li>마우스 휠은 세지 않습니다.</li>
  <li>입력은 별도의 작은 프로그램(<code>InputHelper.exe</code>)이 Windows 표준 방식(Raw Input)으로 받습니다. 키보드 훅 방식은 쓰지 않습니다.</li>
  <li>게임을 끄면 입력을 받는 프로그램도 함께 꺼져서 아무것도 세지 않습니다.</li>
</ul>

<h2>2. 이 PC 에 저장하는 것 (<code>%APPDATA%\\PunchMonkey\\</code>)</h2>
<ul>
  <li>누적 타수, 옵션 설정, 장착한 커서 장식, 마지막 저장 시각</li>
  <li>실행 기록(로그) 파일 — 문제 해결용이며 스팀 이름과 스팀 계정 ID 가 들어갈 수 있습니다. 외부로 보내지 않습니다. 문의하실 때 직접 첨부하실 수 있습니다.</li>
  <li>멀티 로비에서 친구 칸을 옮겨 둔 자리 — 다음에 같은 친구와 만날 때 그 자리에 두려고, 친구의 스팀 계정 ID 와 화면 위치를 함께 저장합니다. 마지막으로 있던 로비 정보도 저장합니다. 외부로 보내지 않습니다.</li>
  <li>"Windows 시작 시 실행" 을 켜면 Windows 시작 프로그램 목록에 등록됩니다.</li>
</ul>

<h2>3. 게임 서버에 저장하는 것</h2>
<p>바나나를 임의로 늘리거나 장식을 복제하지 못하게, 재화와 구매는 게임 서버가 확인합니다.</p>
<ul>
  <li><strong>스팀 계정 ID</strong> — 누구의 진행인지 구분하는 데 씁니다. 로그인할 때 스팀이 발급한 일회성 인증 티켓을 Valve 에 보내 확인하며, 비밀번호는 다루지 않습니다.</li>
  <li><strong>게임 진행 상태</strong> — 바나나 잔액, 강화 단계, 나무 슬롯의 성장 상태, 마지막 동기화 시각</li>
  <li><strong>구매 기록</strong> — 같은 구매가 두 번 처리되지 않게 하는 요청 기록, 서버가 지급한 장식 목록</li>
  <li>게임 서버는 Cloudflare 에서 운영합니다. 게임 서버는 IP 주소를 저장하지 않지만, Cloudflare 가 서비스 제공 과정에서 처리할 수 있습니다.</li>
</ul>

<h2>4. 스팀(Valve)에 저장되는 것</h2>
<ul>
  <li>커서 장식 — 스팀 인벤토리 아이템으로 지급됩니다.</li>
  <li>도전과제 달성 여부와 누적 타수 통계 — 스팀 프로필 공개 설정에 따라 다른 사람에게 보일 수 있습니다.</li>
  <li>스팀에 저장되는 정보는 <a href="${VALVE_PRIVACY}">Valve 개인정보 처리방침</a>을 따릅니다.</li>
</ul>

<h2>5. 멀티 로비에서 다른 사람에게 보이는 것</h2>
<p>멀티 로비는 스팀 로비로 동작하며, 우리 게임 서버를 거치지 않습니다.</p>
<ul>
  <li>로비 코드를 아는 사람은 스팀 친구가 아니어도 로비에 들어올 수 있습니다.</li>
  <li>같은 로비의 사람들에게: 스팀 이름과 프로필(스팀이 보여 줍니다), 로비에 들어온 뒤 친 횟수(로비 타수)</li>
  <li>로비에서 나간 뒤에도 그 로비가 남아 있는 동안에는, 다시 들어왔을 때 이어서 세기 위해 로비 타수가 스팀 계정 ID 와 함께 로비 정보에 남습니다. 로비 코드를 아는 사람은 로비 정보를 볼 수 있습니다. 마지막 사람이 나가 로비가 사라지면 함께 사라집니다.</li>
  <li>스팀 친구에게: 로비에 있는 동안 "게임 참가" 에 쓰이는 로비 정보</li>
  <li>친구 목록과 친구의 접속 상태(온라인·게임 중·참가 정보)는 로비 창에서 참가·초대를 보여 주는 데만 이 PC 에서 읽으며, 우리 서버로 보내지 않습니다.</li>
  <li>같은 로비의 사람들에게, 로비에 있는 동안 0.2초마다: 그 사이 친 횟수, 딴 바나나 수, 누적 타수, 장착한 커서 장식, 도감 수집률, 나무 단계(강화 정도에 따라 바뀌는 나무 모습). 친구 화면에서 원숭이와 나무를 움직이는 데만 씁니다. 어떤 키를 눌렀는지는 들어 있지 않습니다. 스팀 중계 서버를 거쳐 전달되므로 서로의 IP 주소는 보이지 않습니다.</li>
</ul>

<h2>6. 하지 않는 것</h2>
<ul>
  <li>키 내용, 마우스 위치, 화면, 다른 프로그램, 파일을 읽지 않습니다.</li>
  <li>광고, 분석(애널리틱스), 추적 도구를 넣지 않았습니다.</li>
  <li>개인정보를 판매하거나 광고 목적으로 제공하지 않습니다.</li>
</ul>

<h2>7. 삭제</h2>
<ul>
  <li>이 PC 의 데이터: 게임을 삭제한 뒤 <code>%APPDATA%\\PunchMonkey\\</code> 폴더를 지우면 됩니다.</li>
  <li>게임 서버의 데이터: <a href="mailto:${CONTACT}">${CONTACT}</a> 로 스팀 계정 ID 와 함께 요청하시면 삭제합니다. 삭제하면 바나나와 진행 상태가 복구되지 않습니다. 이미 받은 커서 장식은 스팀 인벤토리에 남습니다.</li>
</ul>

<h2>8. 문의</h2>
<p><a href="mailto:${CONTACT}">${CONTACT}</a></p>
`;

const EN = `
<h1 id="en">PunchMonkey Privacy Notice</h1>
<p class="meta">Last updated: 2026-09-28</p>

<h2>1. How we count your input</h2>
<p>While PunchMonkey sits on your desktop, it only counts <strong>that</strong> a key or mouse button was pressed.</p>
<ul>
  <li>It never reads which key (key code), which mouse button, or where your cursor is. Since it never reads them, it cannot store or send them either.</li>
  <li>Mouse wheel scrolling is not counted.</li>
  <li>Input is received by a small separate program (<code>InputHelper.exe</code>) using the standard Windows Raw Input API. No keyboard hooks are used.</li>
  <li>When you close the game, the input program closes with it and nothing is counted.</li>
</ul>

<h2>2. Stored on your PC (<code>%APPDATA%\\PunchMonkey\\</code>)</h2>
<ul>
  <li>Total keystroke count, option settings, equipped cursor decorations, last save time</li>
  <li>Log files for troubleshooting, which may include your Steam name and Steam account ID. They are never sent anywhere; you may attach them yourself when contacting us.</li>
  <li>Where you placed your friends' windows in multiplayer lobbies — stored together with each friend's Steam account ID so they appear in the same spot next time, plus the last lobby you were in. Never sent anywhere.</li>
  <li>If you enable "Run at Windows startup", the game is added to your Windows startup programs.</li>
</ul>

<h2>3. Stored on our game server</h2>
<p>To prevent bananas from being inflated or decorations from being duplicated, our game server verifies your balance and purchases.</p>
<ul>
  <li><strong>Steam account ID</strong> — used to identify whose progress it is. At sign-in, a one-time authentication ticket issued by Steam is verified with Valve. We never handle your password.</li>
  <li><strong>Game progress</strong> — banana balance, upgrade levels, tree slot growth, last sync time</li>
  <li><strong>Purchase records</strong> — request records that prevent the same purchase from being processed twice, and the list of decorations the server has granted</li>
  <li>The game server runs on Cloudflare. Our server does not store IP addresses, but Cloudflare may process them while providing its service.</li>
</ul>

<h2>4. Stored by Steam (Valve)</h2>
<ul>
  <li>Cursor decorations — granted as Steam Inventory items.</li>
  <li>Achievements and your total keystroke statistic — may be visible to others depending on your Steam profile privacy settings.</li>
  <li>Data stored by Steam is governed by the <a href="${VALVE_PRIVACY}">Valve Privacy Policy</a>.</li>
</ul>

<h2>5. What others see in multiplayer lobbies</h2>
<p>Multiplayer lobbies run on Steam lobbies and do not go through our game server.</p>
<ul>
  <li>Anyone who knows a lobby code can join that lobby, even if they are not your Steam friend.</li>
  <li>To people in the same lobby: your Steam name and profile (shown by Steam), and how many times you have typed since joining the lobby (lobby keystrokes)</li>
  <li>After you leave, while the lobby still exists, your lobby keystrokes are kept in the lobby information together with your Steam account ID so the count can continue if you rejoin. Anyone who knows the lobby code can view the lobby information. It disappears when the last person leaves and the lobby closes.</li>
  <li>To your Steam friends: lobby information used for "Join Game" while you are in a lobby</li>
  <li>Your friends list and your friends' status (online / in game / join information) are read on your PC only to show join and invite options in the lobby window, and are never sent to our server.</li>
  <li>To people in the same lobby, every 0.2 seconds while you are in it: how many times you typed in that moment, bananas harvested, total keystrokes, equipped cursor decorations, collection progress, and your tree stage (how your tree looks, based on your upgrades). This is used only to animate your monkey and tree on their screens. It never contains which keys you pressed. It is delivered through Steam's relay servers, so your IP address is not visible to others.</li>
</ul>

<h2>6. What we don't do</h2>
<ul>
  <li>We never read key contents, mouse position, your screen, other programs, or files.</li>
  <li>There are no ads, analytics, or tracking tools in the game.</li>
  <li>We never sell your personal information or share it for advertising.</li>
</ul>

<h2>7. Deletion</h2>
<ul>
  <li>Data on your PC: uninstall the game and delete the <code>%APPDATA%\\PunchMonkey\\</code> folder.</li>
  <li>Data on our game server: email <a href="mailto:${CONTACT}">${CONTACT}</a> with your Steam account ID and we will delete it. Deleted bananas and progress cannot be restored. Decorations you already received remain in your Steam Inventory.</li>
</ul>

<h2>8. Contact</h2>
<p><a href="mailto:${CONTACT}">${CONTACT}</a></p>
`;

export const PRIVACY_HTML = `<!doctype html>
<html lang="ko">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>PunchMonkey 개인정보 처리 안내 / Privacy Notice</title>
<style>
  :root { color-scheme: light dark; }
  body { max-width: 760px; margin: 0 auto; padding: 24px 16px 64px; font: 16px/1.65 system-ui, "Malgun Gothic", sans-serif; }
  h1 { font-size: 1.6em; margin-top: 1.2em; }
  h2 { font-size: 1.15em; margin-top: 1.6em; }
  .meta { opacity: .7; }
  hr { margin: 48px 0; border: 0; border-top: 1px solid currentColor; opacity: .25; }
  code { font-size: .92em; }
</style>
</head>
<body>
${KO}
<hr>
${EN}
</body>
</html>
`;
