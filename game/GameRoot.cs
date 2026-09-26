using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using ProjectSeWoo.Shared;
using ProjectSeWoo.Shared.Mocks;

namespace ProjectSeWoo.Game;

public partial class GameRoot : Node2D, IInteractiveArea, IPlatformConsumer
{
    /// <summary>디버그 키가 한 번에 앞당기는 성장 시간.</summary>
    private const long DebugGrowMs = 60_000;

    /// <summary>수확한 바나나가 떨어져 착지하는 높이. 원숭이 발치다.</summary>
    private const float GroundY = 404f;

    /// <summary>
    /// 플랫폼 실물 묶음. 셸이 <see cref="AttachPlatform"/> 으로 넘긴다.
    /// 지금 읽는 곳은 <c>Input</c>/<c>Save</c> 뿐이지만 나머지도 부를 자리가
    /// 정해져 있다 - <c>Cursor</c> 는 B6(상점/장착), <c>Shell</c> 은 B4(안전 영역
    /// 배치), <c>Achievements</c> 는 B3/B7(마일스톤·도감 100%), <c>Net</c> 은
    /// B10~B12(룸 화면)다.
    /// </summary>
    private IPlatformServices _platform;

    /// <summary>
    /// <b>단독 실행일 때만</b> 채워진다 - 에디터에서 <c>Shell.tscn</c> 없이 이 씬만
    /// 열어 돌리는 경우다. 실물이 왔으면 널로 남고, 널인지 아닌지가 곧
    /// "지금 목으로 도는가"의 답이다. 목에만 있는 <c>Tick</c>/<c>Feed</c> 를
    /// 부를 자격도 여기에 묶여 있다.
    /// </summary>
    private MockPlatformServices _standalone;

    [Export] private PackedScene _fallingBananaScene;

    [Export] private PackedScene _punchEffectScene;

    private Tree _tree;
    private Monkey _monkey;
    private StatusHud _hud;
    private ShopWindow _shop;
    private Button _shopButton;
    private RoomWindow _roomWindow;
    private Button _roomButton;

    /// <summary>옵션 창을 여는 버튼. 창은 셸 소유라 <see cref="IShell.ToggleOptions"/> 로 연다.</summary>
    private Button _optionsButton;

    /// <summary>처음 안내 (B15). 처음 켤 때 저절로 열리고, [?] 로 다시 연다.</summary>
    private OnboardingWindow _onboarding;
    private Button _helpButton;

    /// <summary>
    /// 룸 창과 멀티 세션 사이 배선 (B12). <see cref="AttachPlatform"/> 전까지는 널이다 -
    /// 세션(<see cref="IPlatformServices.Net"/>)이 있어야 만들 수 있다.
    /// </summary>
    private RoomController _room;

    /// <summary>내 상태를 200ms 창으로 로비에 뿌린다 (A10). 룸 컨트롤러와 같은 때 만든다.</summary>
    private PlayerStateSender _stateSender;

    /// <summary>친구 칸 창들 (B10). 로비에 친구가 있으면 친구마다 끌어서 옮길 수 있는 작은 창을 띄운다.</summary>
    private FriendWindows _friends;

    /// <summary>
    /// 구매·장착 규칙 (B6). <see cref="AttachPlatform"/> 전까지는 널이다 -
    /// 세이브와 커서 레이어가 둘 다 있어야 만들 수 있다.
    /// </summary>
    private Inventory _inventory;

    /// <summary>
    /// 아직 안 딴 가장 낮은 타수 마일스톤의 인덱스 (§6,
    /// <see cref="AchievementIds.KeystrokeMilestones"/>). 표 끝까지 갔으면 전부 딴 것이다.
    ///
    /// 배열이 오름차순이라 앞에서부터 하나씩 밀면 되고, 매 배치마다 표 전체를
    /// 훑을 필요가 없다.
    /// </summary>
    private int _nextMilestone;


    /// <summary>
    /// 도감 100% 도전과제를 이 세션에 이미 처리했는가 (§3-3, B7).
    ///
    /// 세이브가 이미 100% 인 채로 켜지면 <b>해금을 부르지 않고 이 값만 세운다</b> -
    /// 마일스톤(<see cref="_nextMilestone"/>)과 같은 규칙이다. 스팀이 중복 해금을
    /// 무시하기는 하지만, 켤 때마다 예전에 딴 것을 다시 부를 이유가 없고 진행도
    /// 토스트가 엉뚱하게 뜬다.
    /// </summary>
    private bool _collectionDone;

    /// <summary>
    /// 기동 때 서버/스팀 응답을 기다리는 상한(초). 넘으면 있는 값으로 화면을 세운다 -
    /// 스팀 인벤토리 콜백이 안 오면 <see cref="LoadGameState"/> 가 영영 안 불려서
    /// HUD·상점·커서 장식이 전부 빈 채로 남는다.
    /// </summary>
    private const double StartupWaitSec = 10.0;

    /// <summary>
    /// 서버 재동기화 주기(초). 정상일 때는 드물게(로컬 예측 보정), 서버를 못 붙었거나
    /// 슬롯을 하나도 못 받았을 때는 자주 - 오프라인으로 켜면 슬롯이 0개라 하베스트가
    /// 안 나가고, 그러면 재연결할 계기가 없어서 세션 내내 빈 나무였다.
    /// </summary>
    private const double ResyncSec = 300.0;
    private const double ResyncRetrySec = 30.0;

    private bool _loaded;

    /// <summary>
    /// 스팀 통계가 준비된 뒤 "이미 달성한 조건" 을 한 번 훑었는가 (A15, <see cref="SyncAchievements"/>).
    /// 보유 목록이 늦게 오면 다시 훑는다.
    /// </summary>
    private bool _achievementsSynced;
    private string _shownNotice;
    private bool _syncing;
    private double _sinceSync;

    /// <summary>
    /// 이 세션에서 디버그 지급(Shift+B)을 썼는가.
    ///
    /// <b>썼으면 도전과제를 해금하지 않는다.</b> 스팀은 이미 붙어 있고(A8), A15 로
    /// 스키마가 등록되는 순간 치트로 받은 도감 100% 가 **진짜 도전과제로 나간다** -
    /// 되돌릴 수 없는 종류의 사고다. 릴리스 빌드에는 키 자체가 없지만
    /// (<see cref="OS.IsDebugBuild"/>), 개발 중에 스팀을 켜 놓고 있는 시간이 길어서
    /// 그것만으로는 부족하다.
    /// </summary>
    private bool _cheated;

    /// <summary>직전 프레임의 레벨. 레벨업 순간을 잡는 데만 쓴다.</summary>
    private int _level = 1;

    /// <summary>
    /// 세이브 객체의 소유자. <b>셸과 같은 인스턴스를 본다</b>
    /// (shared/Contracts/ISaveStore.cs) - 여기를 고치고 <c>MarkDirty()</c> 를
    /// 부르면 디스크 쓰기는 플랫폼이 알아서 한다.
    /// <see cref="AttachPlatform"/> 전까지는 널이다.
    /// </summary>
    private ISaveStore _store;

    private SaveData Save => _store.Data;

    public override void _Ready()
    {
        AddToGroup(SceneGroups.GameRoot);

        _tree = GetNode<Tree>("Tree");
        _monkey = GetNode<Monkey>("Monkey");
        _hud = GetNode<StatusHud>("StatusHud");
        _shop = GetNode<ShopWindow>("ShopWindow");
        _shopButton = GetNode<Button>("ShopButton");
        _roomWindow = GetNode<RoomWindow>("RoomWindow");
        _roomButton = GetNode<Button>("RoomButton");
        _optionsButton = GetNode<Button>("OptionsButton");
        _onboarding = GetNode<OnboardingWindow>("OnboardingWindow");
        _helpButton = GetNode<Button>("HelpButton");

        _shopButton.Pressed += ToggleShop;
        _roomButton.Pressed += ToggleRoom;
        _optionsButton.Pressed += () => _platform?.Shell.ToggleOptions();
        _helpButton.Pressed += OpenOnboarding;
        _onboarding.Finished += OnOnboardingFinished;
        _shop.UpgradeRequested += OnUpgradeRequested;
        _shop.BuyRequested += OnBuyRequested;
        _shop.Opened += OnShopOpened;
        _shop.EquipRequested += OnEquipRequested;

        // **여기서 세이브를 읽거나 입력을 구독하면 안 된다.** Godot 은 자식의
        // _Ready 를 부모보다 먼저 부르는데 실물을 만드는 것은 부모(OverlayShell)라,
        // 이 시점에는 _platform 이 아직 비어 있다
        // (shared/Contracts/IPlatformServices.cs 의 ⚠).
        //
        // 프레임 끝에 한 번 확인해서 그래도 비어 있으면 셸이 없는 실행이다 -
        // 그때만 목으로 돈다.
        Callable.From(FallBackToMocks).CallDeferred();
    }

    /// <summary>
    /// <see cref="IPlatformConsumer.AttachPlatform"/>. 실물이 필요한 배선은 전부 여기서 한다.
    /// </summary>
    public void AttachPlatform(IPlatformServices platform)
    {
        if (_platform != null)
        {
            GD.PushWarning("[game] AttachPlatform 이 두 번 왔다 - 먼저 온 것을 유지한다");
            return;
        }

        _platform = platform;
        _store = platform.Save;
        _platform.Input.OnKeystrokes += OnKeystrokes;
        _platform.Economy.OnStateChanged += OnEconomyStateChanged;

        // 목 경제는 가격표를 받아야 판다. 실물은 서버가 자기 사본(server/src/catalog.ts)으로
        // 판정하지만 목에는 표가 없어서, 9/23 리와이어 뒤로 **디버그 상점 구매가 전부
        // ItemUnknown 으로 실패하고 있었다**(2026-09-24 A15 시험 중 발견). 원본인
        // ShopCatalog 를 그대로 넣는다.
        if (_platform.Economy is MockEconomyService mockEconomy)
        {
            foreach (ShopCatalog.Item item in ShopCatalog.All)
            {
                if (!item.IsStarter)
                {
                    mockEconomy.RegisterItemPrice(item.Id, item.Price);
                }
            }
        }
        _room = new RoomController(_platform.Net, _roomWindow, _hud, _store);
        _stateSender = new PlayerStateSender(_platform.Net, SnapshotForPeers);

        _friends = new FriendWindows { Name = "Friends" };
        AddChild(_friends);
        _friends.Bind(_platform.Net, _platform.Shell, _store);
        _platform.Net.OnRoomChanged += OnRoomChangedForAchievement;

        LoadGameStateAsync();
    }

    /// <summary>
    /// 실물이면 서버/스팀에서 최신 상태를 받아온 뒤 <see cref="LoadGameState"/>
    /// 로 넘긴다 - 목은 이미 채워져 있어서 <c>Sync</c>/<c>Refresh</c> 둘 다
    /// 즉시 끝난다(<c>Task.CompletedTask</c>).
    ///
    /// <b>순서가 중요하다.</b> <see cref="Inventory.ApplyEquippedToCursor"/> 는
    /// <see cref="IInventoryService.Owns"/> 로 소유권을 확인하는데, 실물이
    /// 아직 <see cref="IInventoryService.Refresh"/> 전이면 전부 "안 가진 것"으로
    /// 오판해서 세이브에 남은 장착을 전부 풀어 버린다 - 그래서 <c>Refresh</c>
    /// 완료를 기다린 뒤에 <see cref="LoadGameState"/> 를 부른다.
    /// </summary>
    private async void LoadGameStateAsync()
    {
        try
        {
            await WithTimeout(_platform.Economy.Sync(), "경제 동기화");
            await WithTimeout(_platform.Inventory.Refresh(), "인벤토리 조회");
        }
        catch (Exception e)
        {
            // 여기서 죽으면 LoadGameState 가 안 불려 게임이 빈 껍데기로 남는다.
            GD.PushWarning($"[game] 기동 동기화 실패, 있는 값으로 시작 - {e.GetType().Name}: {e.Message}");
        }

        LoadGameState();
        _loaded = true;

        if (OS.IsDebugBuild())
        {
            await RunTestPurchaseIfRequestedAsync();
        }
    }

    /// <summary>기동 대기에 상한을 건다. 늦은 쪽은 버리지 않고 나중에 끝나도록 둔다.</summary>
    private async Task WithTimeout(Task task, string what)
    {
        Task first = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(StartupWaitSec)));
        if (first != task)
        {
            GD.PushWarning($"[game] {what}이(가) {StartupWaitSec:F0}초 안에 안 끝났다 - 기다리지 않고 진행");
            return;
        }

        await task;
    }

    /// <summary>
    /// 보유 목록을 기동 뒤에야 받았다. 세이브를 믿고 걸어 둔 장착을 이제 진짜로
    /// 확인하고(<see cref="Inventory.ApplyEquippedToCursor"/>), 도감·상점을 다시 그린다.
    /// 도감 100% 는 해금하지 않고 상태만 맞춘다 - 이미 다 모은 유저가 켤 때마다
    /// "방금 완성" 으로 잡히면 안 된다 (<see cref="LoadGameState"/> 와 같은 규칙).
    /// </summary>
    private void OnInventoryLoadedLate()
    {
        _inventory.ApplyEquippedToCursor();
        _collectionDone = _inventory.IsComplete;
        _achievementsSynced = false;
        RefreshCollectionHud();
        PersistNow();
        GD.Print($"[game] 보유 목록 늦게 도착 - 보유 장식 {_inventory.OwnedCount}/{ShopCatalog.All.Length}");
    }

    /// <summary>
    /// 슬롯을 하나도 못 받은 동안 HUD 에 이유를 적는다 (A14). 슬롯은 서버가 주는
    /// 것이라 오프라인 첫 실행은 빈 나무로 시작하고, 30초마다 다시 붙어 본다.
    /// 한 번이라도 받은 뒤 끊기면 안내하지 않는다 - 로컬 예측으로 계속 자란다.
    /// </summary>
    private void UpdateOfflineNotice()
    {
        string notice = !_loaded ? "서버에 연결하는 중..."
            : _platform.Economy.Slots.Count == 0 ? "오프라인 - 연결되면 바나나가 열린다"
            : null;

        if (notice != _shownNotice)
        {
            _shownNotice = notice;
            _hud.SetNotice(notice);
        }
    }

    /// <summary>상점을 열 때 서버에 안 붙어 있으면 바로 한 번 붙어 본다 - 세션 토큰이
    /// 막 만료된 것뿐인데 다음 재동기화까지 "오프라인" 으로 보이면 안 된다.</summary>
    private void OnShopOpened()
    {
        if (_loaded && !_syncing && !_platform.Economy.IsAvailable)
        {
            _sinceSync = 0.0;
            _ = ResyncAsync(wasHealthy: false);
        }
    }

    /// <summary>
    /// 주기 재동기화. 서버 권위 값(잔액·슬롯)을 다시 받아 로컬 예측을 바로잡고,
    /// 오프라인으로 켰다면 연결이 돌아온 뒤 슬롯을 처음으로 받는다.
    /// </summary>
    private void TickResync(double delta)
    {
        if (_loaded && !_achievementsSynced && _platform.Achievements.IsAvailable)
        {
            SyncAchievements();
        }

        if (!_loaded || _syncing)
        {
            return;
        }

        _sinceSync += delta;
        bool healthy = _platform.Economy.IsAvailable && _platform.Economy.Slots.Count > 0
            && _platform.Inventory.IsLoaded;
        if (_sinceSync < (healthy ? ResyncSec : ResyncRetrySec))
        {
            return;
        }

        _sinceSync = 0.0;
        _ = ResyncAsync(healthy);
    }

    private async Task ResyncAsync(bool wasHealthy)
    {
        _syncing = true;
        try
        {
            await _platform.Economy.Sync();
            if (!wasHealthy && _platform.Economy.Slots.Count > 0)
            {
                GD.Print($"[game] 서버 재연결 - 잔액 {_platform.Economy.Balance}, 슬롯 {_platform.Economy.Slots.Count}개");
            }

            // 스팀이 늦게 붙었으면(자동 시작이 스팀보다 먼저) 보유 목록을 이제야 받는다.
            // 기동 때 한 번만 물어서, 전에는 그 세션 내내 산 장식이 상점에 안 보였다.
            if (!_platform.Inventory.IsLoaded && _platform.Inventory.IsAvailable)
            {
                await _platform.Inventory.Refresh();
                if (_platform.Inventory.IsLoaded)
                {
                    OnInventoryLoadedLate();
                }
            }

            if (_shop.IsOpen)
            {
                _shop.Refresh();
            }
        }
        catch (Exception e)
        {
            GD.PushWarning($"[game] 재동기화 실패 - {e.GetType().Name}: {e.Message}");
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>
    /// [디버그] <c>--test-purchase=&lt;itemId&gt;</c> 로 실행하면 UI 클릭 없이
    /// 구매 한 건을 곧바로 시도하고 결과를 로그로 남긴다 - 실물
    /// (<c>server/src/steam.ts</c>의 <c>grantInventoryItem</c>)이 실제로
    /// 아이템을 지급하는지 확인하려고 만들었다. <c>--steam-selftest</c>와
    /// 같은 자리의 도구다 - 릴리스 빌드에는 없다.
    /// </summary>
    private async Task RunTestPurchaseIfRequestedAsync()
    {
        const string Prefix = "--test-purchase=";
        string arg = Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith(Prefix, StringComparison.Ordinal));
        if (arg == null)
        {
            return;
        }

        string itemId = arg[Prefix.Length..];
        GD.Print($"[game][테스트] 구매 시도 - {itemId} (잔액 {_platform.Economy.Balance})");

        PurchaseResult result = await _platform.Economy.PurchaseItem(itemId);
        GD.Print($"[game][테스트] 구매 결과 - {result.Outcome}, 잔액 {result.NewBalance}"
            + (result.GrantedItemDefId != null ? $", 지급 {result.GrantedItemDefId}" : string.Empty));

        await _platform.Inventory.Refresh();
        GD.Print($"[game][테스트] 인벤토리 재조회 - Owns({itemId}) = {_platform.Inventory.Owns(itemId)}");
    }

    /// <summary>
    /// 세이브에서 게임 상태를 세운다 (B5). 나무 슬롯의 오프라인 성장은 더 이상
    /// 여기서 계산하지 않는다 - <see cref="IEconomyService"/> 쪽(서버/목)이
    /// 이미 경과 시간을 반영한 값을 들고 있다 (docs/ECONOMY-SERVER.md).
    /// </summary>
    private void LoadGameState()
    {
        _tree.SyncSlots(_platform.Economy.Slots);

        // **세이브의 장착 상태를 커서 레이어에 처음으로 밀어 넣는 자리다.** B9 까지
        // 이걸 부르는 코드가 없어서, 세이브에 hang:monkey_01 이 있어도 켜면 아무
        // 장식도 안 붙었다 (docs/A5-CURSOR-COSMETICS.md §2-1).
        _inventory = new Inventory(Save.Inventory.Equipped, _platform.Economy, _platform.Inventory, _platform.Cursor);
        _inventory.ApplyEquippedToCursor();
        _shop.Bind(_inventory);

        // 이미 넘어선 마일스톤은 세션 시작 시점에 지나간 것으로 잡는다. 안 그러면
        // 켤 때마다 예전에 딴 도전과제를 다시 Unlock 한다 - 스팀이 무시하긴 하지만
        // 부를 이유가 없고, 진행도 토스트가 엉뚱한 구간에서 뜬다.
        _level = KeystrokeLevel.LevelFor(Save.TotalKeystrokes);
        while (_nextMilestone < AchievementIds.KeystrokeMilestones.Length
            && Save.TotalKeystrokes >= AchievementIds.KeystrokeMilestones[_nextMilestone].Threshold)
        {
            _nextMilestone++;
        }

        _hud.SetBananas(_platform.Economy.Balance);
        _hud.SetKeystrokes(Save.TotalKeystrokes);
        RefreshCollectionHud();

        // 이미 다 모은 세이브면 해금은 건너뛰고 상태만 맞춘다 (위 주석 참고).
        _collectionDone = _inventory.IsComplete;

        GD.Print($"[game] 세이브 로드 - 바나나 {_platform.Economy.Balance}, 누적 {Save.TotalKeystrokes}타"
            + $" (Lv.{_level}), 슬롯 {_platform.Economy.Slots.Count}개"
            + $", 보유 장식 {_inventory.OwnedCount}/{ShopCatalog.All.Length}");

        // 처음 켰으면(또는 안내가 새 판이면) 안내부터 (B15). 첫 장이 개인정보 문구다 (§7-6).
        if (Save.OnboardingSeen < OnboardingWindow.Version)
        {
            OpenOnboarding();
        }

        if (OS.IsDebugBuild())
        {
            GD.Print("[game] 디버그 키 - G 성장 앞당기기 / B 상점 / 2·3·4 슬롯 장착 순환"
                + " / Shift+B 전 상품 지급 / Shift+R 인벤토리 초기화"
                + " / M 멀티 로비 / Shift+M 가짜 친구 입장·타건 / Ctrl+M 가짜 친구 퇴장");
        }
    }

    /// <summary>
    /// <see cref="IEconomyService.OnStateChanged"/>. 잔액이 서버 확인이든 로컬
    /// 낙관적 갱신이든 구분 없이 HUD 를 다시 그린다 - 계약이 그렇게 합쳐서
    /// 부르기로 돼 있다 (shared/Contracts/IEconomyService.cs).
    /// </summary>
    private void OnEconomyStateChanged() => _hud.SetBananas(_platform.Economy.Balance);

    /// <summary>
    /// 셸이 실물을 안 넘겼으면 목으로 돈다. <see cref="_Ready"/> 가 프레임 끝으로
    /// 미뤄 두고 부른다 - 그때는 부모의 <c>_Ready</c> 까지 전부 끝나 있다.
    /// </summary>
    private void FallBackToMocks()
    {
        if (_platform != null)
        {
            return;
        }

        GD.Print("[game] 플랫폼 미연결 - 목으로 돈다 (Shell.tscn 없이 단독 실행)");
        _standalone = new MockPlatformServices();
        AttachPlatform(_standalone);
    }

    public override void _Process(double delta)
    {
        // 실물의 폴링은 셸이 돌린다. 목일 때만 우리가 굴린다.
        _standalone?.Tick(delta);

        // 나무 시각은 매 프레임 서버(또는 목) 값을 그대로 받아 그린다 - 진실이
        // 여기 없으므로 로컬 틱이 없다 (docs/ECONOMY-SERVER.md, game/entities/Tree.cs).
        if (_platform != null)
        {
            _tree.SyncSlots(_platform.Economy.Slots);
            TickResync(delta);
            UpdateOfflineNotice();
            _room.Tick(delta);
            _stateSender.Tick(delta);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key || _platform == null)
        {
            return;
        }

        // 8분을 기다리지 않고 수확까지 확인하려고 둔 debug 키다. 셸이 쓰는 키
        // (F1~F12 / 1~4 / [ ] - = O H / Esc)와 겹치지 않는 자리를 골랐다.
        // 강화 UI(B7)가 생기면 그쪽이 이 자리를 대신한다.
        //
        // **실물 경제 서버에는 없다.** 성장 시계의 진실이 서버에 있으므로
        // (IEconomyService), 앞당기는 것도 서버(목)의 일이다 - 목일 때만 동작한다.
        if (key.Keycode == Key.G)
        {
            if (_platform.Economy is MockEconomyService mockEconomy)
            {
                mockEconomy.DebugAdvanceAll(DebugGrowMs);
            }
            else
            {
                GD.PushWarning("[game][디버그] 실물 경제 서버에는 성장 앞당기기가 없다 - 목일 때만 동작한다");
            }

            return;
        }

        // 상점. 셸이 쓰는 키(F1~F12 / 1~4 / [ ] - = O H / Esc)와 안 겹치는 자리다.
        // **목 먹이기보다 먼저 가로챈다** - 안 그러면 상점을 여는 키가 타건으로도
        // 세어져서, 창을 열 때마다 원숭이가 한 대씩 친다.
        if (key.Keycode == Key.B)
        {
            if (key.ShiftPressed && OS.IsDebugBuild())
            {
                DebugGrantAll();
            }
            else
            {
                ToggleShop();
            }

            return;
        }

        // 멀티 룸 (B12). 상점과 같은 이유로 목 먹이기보다 먼저 가로챈다.
        // Shift/Ctrl 은 목일 때 가짜 친구를 움직이는 디버그 키다.
        if (key.Keycode == Key.M)
        {
            if (key.ShiftPressed && OS.IsDebugBuild())
            {
                _room.DebugSimulateActivity();
            }
            else if (key.CtrlPressed && OS.IsDebugBuild())
            {
                _room.DebugSimulateLeave();
            }
            else
            {
                ToggleRoom();
            }

            return;
        }

        // 커서 슬롯 장착 순환. **셸에서 옮겨 온 키다** (2026-09-21) - 예전에는
        // platform/OverlayShell.DebugKeys 가 ICursorLayer.Equip 을 직접 불러서
        // 세이브도 인벤토리도 모르는 채 커서만 바뀌었고, 그래서 상점에는 이전
        // 것이 "장착 중" 으로 남아 있었다. 이제 인벤토리를 거친다.
        if (key.Keycode is Key.Key2 or Key.Key3 or Key.Key4)
        {
            CycleEquip(key.Keycode switch
            {
                Key.Key2 => CursorSlot.Monkey,
                Key.Key3 => CursorSlot.Banana,
                _ => CursorSlot.Deco,
            });
            return;
        }

        // [디버그] 첫 실행 상태로 되돌린다. 구매 흐름과 도감 100% 발화를 다시
        // 보려면 되돌릴 길이 있어야 한다.
        if (key.Keycode == Key.R && key.ShiftPressed && OS.IsDebugBuild())
        {
            DebugResetInventory();
            return;
        }

        if (key.Keycode == Key.Escape && _roomWindow.IsOpen)
        {
            _roomWindow.Back();
            return;
        }

        if (key.Keycode == Key.Escape && _shop is { IsOpen: true })
        {
            _shop.Close();
            return;
        }

        // 목은 수동으로 먹여야 타건이 생긴다. **실물일 때는 부르지 않는다** -
        // A4 는 포커스 없이 전역으로 이미 세고 있어서, 여기서 또 먹이면 창에
        // 포커스가 있는 동안만 두 배로 수확된다.
        _standalone?.Feed(1);
    }

    private void OnKeystrokes(int count)
    {
        // 펀치 연출과 나무 반응은 수확 여부와 무관하게 항상 돈다 - 빈 나무를 쳐도
        // 반응이 있어야 한다는 것이 §2-3 의 P0 요구다.
        double contact = _monkey.Punch();
        GetTree().CreateTimer(contact).Timeout += _tree.Shake;
        GetTree().CreateTimer(contact).Timeout += SpawnPunchEffect;

        // 누적 타수는 재화가 아니라 기록이다 (§6). 수확 여부와 무관하게 센다 -
        // 빈 나무를 쳐도 타수는 늘어야 "논 시간" 이 레벨에 반영된다. 레벨 환산과
        // 마일스톤 도전과제(AchievementIds)는 B3 가 이 값 위에 올린다.
        Save.TotalKeystrokes += count;
        PushKeystrokeStat();

        // 룸 랭킹은 누적이 아니라 룸에서 친 타수다 - 세는 것은 세션이 한다
        // (INetSession.AddKeystrokes). 룸 밖이면 세션이 버린다.
        _platform.Net.AddKeystrokes(count);
        _stateSender.AddKeystrokes(count);

        // 수확을 애니메이션 타이밍이 아니라 입력에 직접 건다. §2-3 검토 노트의
        // "키 입력과 애니메이션을 1:1 고정 대응시키지 말 것"이 이 뜻이고,
        // 연출이 끊기거나 겹쳐도 수확 개수가 흔들리지 않는다.
        //
        // **재화는 여기서 직접 더하지 않는다.** 익은 바나나는 한 번에 떨어지지 않고
        // <see cref="Tree.HitsToDrop"/> 번 맞아야 떨어진다 - 타건 하나가 가장 앞의
        // 익은 송이를 한 대 치고, 마지막 타격에서만 수확을 요청한다.
        // <see cref="IEconomyService.RequestHarvest"/> 가 낙관적으로 잔액을 올리고
        // <see cref="OnEconomyStateChanged"/> 가 HUD 를 다시 그린다
        // (docs/ECONOMY-SERVER.md) - 그래서 이 메서드는 "어느 슬롯을 땄는가"만
        // 정하고 재화 계산은 서버(또는 목)에 맡긴다.
        int harvested = 0;
        int flashIndex = -1;
        IReadOnlyList<SlotState> slots = _platform.Economy.Slots;

        // 나무 그림을 지금 슬롯에 먼저 맞춘다. 타건은 셸의 _Process(입력 헬퍼)에서 오고 나무 동기화는 이 노드의
        // _Process 에서 하므로, 슬롯 수가 바뀐 프레임(기동 직후 서버 동기화, "가지 늘리기" 강화)에 타건이 먼저 오면
        // Tree.Hit 이 모르는 인덱스를 받아 IndexOutOfRange 가 났다 (2026-09-26 갑이 기동 로그에서 봤다).
        _tree.SyncSlots(slots);
        for (int k = 0; k < count; k++)
        {
            int readyIndex = FindReadySlot(slots);
            if (readyIndex < 0)
            {
                break;
            }

            if (!_tree.Hit(readyIndex))
            {
                // 한 배치에 여러 타가 와도 반짝임은 한 번이면 된다 - 겹쳐 걸어 봐야
                // 앞의 트윈을 죽이고 다시 시작할 뿐이다.
                flashIndex = readyIndex;
                continue;
            }

            Vector2 fruitPosition = _tree.PositionOf(readyIndex);
            bool golden = slots[readyIndex].Golden;
            _platform.Economy.RequestHarvest(readyIndex);
            DropBanana(fruitPosition, contact, golden);
            harvested++;

            // 낙관적 갱신을 즉시 다시 읽는다 - 방금 딴 슬롯이 이번 배치의 다음
            // 반복에서 또 "열려 있다"로 잡히면 같은 슬롯이 중복 수확된다.
            slots = _platform.Economy.Slots;
        }

        if (flashIndex >= 0)
        {
            // 흔들림(Shake)과 같이 팔이 닿는 순간에 맞춘다.
            GetTree().CreateTimer(contact).Timeout += () => _tree.FlashHit(flashIndex);
        }

        if (harvested > 0)
        {
            _hud.PopBananas();
            _stateSender.AddHarvests(harvested);
        }

        _hud.SetKeystrokes(Save.TotalKeystrokes);
        CheckLevelUp();
        CheckMilestones();

        // 수확이 없어도 누적 타수가 늘었으므로 저장 대상이다.
        _store.MarkDirty();
    }

    private static int FindReadySlot(IReadOnlyList<SlotState> slots)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].Ready)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// 레벨이 올랐으면 알린다. 지금은 로그 한 줄이고, 연출은 B2 가 붙인다 (§2-3).
    /// </summary>
    private void CheckLevelUp()
    {
        int now = KeystrokeLevel.LevelFor(Save.TotalKeystrokes);
        if (now == _level)
        {
            return;
        }

        _level = now;
        GD.Print($"[game] Lv.{now} ({Save.TotalKeystrokes:N0}타)");
    }

    /// <summary>
    /// 누적 타수 마일스톤 도전과제 (§6, <see cref="AchievementIds"/>).
    ///
    /// <b>해금 조건을 아는 것은 게임 레이어이고 스팀에 쓰는 것은 플랫폼이다</b> -
    /// 그래서 여기서 <see cref="IAchievements"/> 만 부르고 <c>SteamUserStats</c> 는
    /// 모른다 (shared/Contracts/IAchievements.cs). 스팀이 안 붙어 있으면 호출이
    /// 조용히 버려지므로 분기하지 않는다.
    ///
    /// <b>A15 전까지는 실물에서도 해금이 안 된다.</b> 파트너 사이트에 스키마가
    /// 없어서 통계 수신이 실패하고 <c>IsAvailable</c> 이 false 다(docs/A8-STEAM.md).
    /// 조건 로직이 맞는지는 <c>MockAchievements</c> 로만 확인할 수 있다.
    /// </summary>
    private void CheckMilestones()
    {
        (string Id, int Threshold)[] table = AchievementIds.KeystrokeMilestones;

        while (_nextMilestone < table.Length
            && Save.TotalKeystrokes >= table[_nextMilestone].Threshold)
        {
            string id = table[_nextMilestone].Id;
            _platform.Achievements.Unlock(id);
            GD.Print($"[game] 마일스톤 해금 {id} ({Save.TotalKeystrokes:N0}타)");

            _nextMilestone++;
        }

        // 진행도 토스트(IndicateProgress)는 띄우지 않는다 (2026-09-24). 스팀은 그것을 해금
        // 토스트와 같은 우하단 자리에 띄워서 "안 깼는데 업적 알림이 뜬다" 로 보였다 - 켠 뒤
        // 첫 타건마다, 다음 마일스톤까지 5% 마다. 토스트는 해금할 때만 뜬다. 커뮤니티 페이지의
        // 진행 막대는 STAT_KEYSTROKES(PushKeystrokeStat)가 그리므로 영향이 없다.
    }

    /// <summary>
    /// 수확한 바나나가 떨어지는 연출 (§2-3). 재화는 이미 입력 시점에 더해졌고
    /// 이건 눈에 보이는 쪽만 한다.
    /// </summary>
    /// <summary>주먹이 줄기에 닿은 자리에서 타격 이펙트를 한 번 터뜨린다. 이펙트는 스스로 사라진다.</summary>
    private void SpawnPunchEffect()
    {
        var effect = _punchEffectScene.Instantiate<PunchEffect>();
        effect.Position = _monkey.ImpactPoint;
        AddChild(effect);
    }

    private void DropBanana(Vector2 from, double delay, bool golden)
    {
        var banana = _fallingBananaScene.Instantiate<FallingBanana>();
        banana.Position = from;
        if (golden)
        {
            banana.MakeGolden();
        }

        AddChild(banana);

        // 팔이 닿기 전에 떨어지면 원인과 결과가 뒤집혀 보인다.
        GetTree().CreateTimer(delay).Timeout += () => banana.Drop(GroundY);
    }

    public override void _ExitTree()
    {
        if (_platform == null)
        {
            return;
        }

        // 실물(HelperInputSource)은 이 노드보다 오래 살 수 있다 - 셸이 들고 있고
        // 셸은 _ExitTree 가 더 늦게 돈다. 구독을 남긴 채 나가지 않는다.
        _platform.Input.OnKeystrokes -= OnKeystrokes;
        _platform.Economy.OnStateChanged -= OnEconomyStateChanged;
        _room?.Dispose();

        // 마지막 상태를 써 넣는다. 디스크 쓰기는 셸의 _ExitTree 가 FlushNow 로
        // 마무리하지만, 그 순서를 가정하지 않으려고 여기서도 한 번 흘려보낸다 -
        // 이미 쓴 뒤라면 아무 일도 안 한다.
        _store.MarkDirty();
        _store.FlushNow();
    }

    /// <summary>
    /// 구매 (§3-2). <b>비동기다</b> - 서버가 잔액을 깎고 스팀 인벤토리에 지급하는
    /// 것까지 한 트랜잭션으로 처리하므로(<see cref="Inventory.TryBuy"/>) 응답을
    /// 기다려야 결과(성공/실패)를 안다.
    ///
    /// 성공하면 즉시 저장한다. §7-5 가 자동 저장을 "60초 주기 + 수확/구매 시
    /// 즉시" 로 못 박았다 - 로컬 장착 상태가 구매 직후 세이브에 안 남으면
    /// 강제 종료 시 잃는다.
    /// </summary>
    private async void OnBuyRequested(ShopCatalog.Item item)
    {
        PurchaseOutcome outcome = await _inventory.TryBuy(item);
        if (outcome != PurchaseOutcome.Success)
        {
            // 연타로 같은 요청이 두 번 들어온 경우(AlreadyOwned)는 조용히 버린다.
            // 나머지는 이유를 보여 준다 - 전에는 오프라인 구매가 아무 반응 없이 실패했다.
            string message = outcome switch
            {
                PurchaseOutcome.ServerUnavailable => "서버에 연결하지 못했다. 잠시 뒤 다시 시도해 줘",
                PurchaseOutcome.InsufficientBalance => "바나나가 부족하다",
                PurchaseOutcome.AlreadyOwned => null,
                _ => "구매가 거절됐다. 바나나는 그대로다",
            };

            GD.Print($"[game] 구매 실패 {item.Id} - {outcome}");
            if (message != null)
            {
                _shop.ShowPurchaseMessage(message);
            }

            return;
        }

        // 목은 구매 성공과 소유권이 자동으로 연결돼 있다(MockEconomyService.LinkInventory).
        // 실물(EconomyClient + SteamInventoryService)은 서로 남남이라 - 서버가 성공을
        // 답해도 스팀 인벤토리 캐시는 다시 물어봐야 갱신된다 (docs/ECONOMY-SERVER.md §5-6).
        await _platform.Inventory.Refresh();

        GD.Print($"[game] 구매 {item.Id} (-{item.Price}) 잔액 {_platform.Economy.Balance}");

        if (TrustEconomyForAchievements)
        {
            TryUnlock(AchievementIds.FirstPurchase, "첫 구매");
        }

        RefreshCollectionHud();
        _shop.Refresh();
        CheckCollection();
        PersistNow();
    }

    /// <summary>
    /// 강화 한 단계 (B13). 서버가 잔액을 깎고 레벨을 올리고, 슬롯 수·성장 시간이 바뀐 새 슬롯을
    /// 돌려준다 - 나무는 다음 프레임 SyncSlots 가 알아서 다시 그린다.
    /// </summary>
    private async void OnUpgradeRequested(UpgradeAxis axis)
    {
        PurchaseOutcome outcome = await _inventory.TryUpgrade(axis);
        if (outcome != PurchaseOutcome.Success)
        {
            string message = outcome switch
            {
                PurchaseOutcome.ServerUnavailable => "서버에 연결하지 못했다. 잠시 뒤 다시 시도해 줘",
                PurchaseOutcome.InsufficientBalance => "바나나가 부족하다",
                PurchaseOutcome.MaxLevel => "이미 최대 단계다",
                _ => "강화가 거절됐다. 바나나는 그대로다",
            };

            GD.Print($"[game] 강화 실패 {axis} - {outcome}");
            _shop.ShowPurchaseMessage(message);
            return;
        }

        GD.Print($"[game] 강화 {axis} → Lv.{_inventory.UpgradeLevel(axis)} 잔액 {_platform.Economy.Balance}");
        _shop.Refresh();
    }

    /// <summary>슬롯 하나의 장착을 가진 것들 사이에서 한 칸 돌린다 (2/3/4 키).</summary>
    private void CycleEquip(CursorSlot slot)
    {
        if (_inventory == null)
        {
            return;
        }

        string now = _inventory.CycleEquipped(slot);
        _shop.Refresh();
        PersistNow();

        GD.Print($"[game] 장착 순환 {ShopCatalog.SlotName(slot)} = {now ?? "(비움)"}");
    }

    /// <summary>
    /// [디버그, Shift+B] 전 상품 지급 + 바나나. C1 스크린샷처럼 장식 조합을
    /// 이것저것 갈아끼워 봐야 할 때 쓴다. 정상 플레이로 지금의 16종을 다 모으려면
    /// 6,600 바나나(기본 획득량 기준 약 300시간)가 든다.
    ///
    /// <b>릴리스 빌드에는 이 경로가 없다</b> (<see cref="OS.IsDebugBuild"/>).
    /// 그리고 이걸 쓴 세션은 도전과제를 해금하지 않는다 - 위 <see cref="_cheated"/> 참고.
    /// </summary>
    private void DebugGrantAll()
    {
        _cheated = true;

        int granted = 0;
        foreach (ShopCatalog.Item item in ShopCatalog.All)
        {
            if (_inventory.DebugGrant(item.Id))
            {
                granted++;
            }
        }

        if (_platform.Economy is MockEconomyService mockEconomy)
        {
            mockEconomy.GrantBananas(10_000);
        }
        else
        {
            GD.PushWarning("[game][디버그] 실물 경제 서버에는 바나나 직접 지급이 없다 - 목일 때만 동작한다");
        }

        RefreshCollectionHud();
        _shop.Refresh();
        PersistNow();

        GD.Print($"[game][디버그] 전 상품 지급 - 새로 {granted}개"
            + $" (보유 {_inventory.OwnedCount}/{ShopCatalog.All.Length}),"
            + $" 바나나 {_platform.Economy.Balance:N0}."
            + " **이 세션은 도전과제를 해금하지 않는다.**");
    }

    /// <summary>[디버그, Shift+R] 인벤토리를 첫 실행 상태로 되돌린다.</summary>
    private void DebugResetInventory()
    {
        _inventory.DebugResetToStarter();

        // 해금 처리 상태도 같이 되돌린다. 안 그러면 되돌린 뒤 다시 모아도
        // 100% 가 안 뜬다.
        _collectionDone = false;

        RefreshCollectionHud();
        _shop.Refresh();
        PersistNow();

        GD.Print($"[game][디버그] 인벤토리 초기화 - 보유"
            + $" {_inventory.OwnedCount}/{ShopCatalog.All.Length}"
            + (_cheated ? " (이 세션은 여전히 도전과제를 해금하지 않는다)" : string.Empty));
    }

    private void RefreshCollectionHud() =>
        _hud.SetCollection(_inventory.OwnedCount, ShopCatalog.All.Length);

    /// <summary>
    /// 재화·소유에서 나온 도전과제(구매·슬롯 완성·도감)를 진짜로 해금해도 되는가 (A15).
    ///
    /// <b>디버그 빌드는 목 경제로 돌면서도 스팀이 켜져 있으면 도전과제만 진짜로 간다</b>
    /// (<c>OverlayShell</c> 이 <c>SteamService</c> 를 꽂는다). 그대로 두면 목 바나나로 산
    /// 장식이 개발 계정의 도감 100% 를 실제로 해금한다 - 되돌릴 수 없다. 디버그 지급
    /// (<see cref="_cheated"/>)과 같은 이유로 막는다. 도전과제 쪽도 목이면(단독 실행 시험)
    /// 막을 이유가 없어 허용한다. 누적 타수는 실제 입력이라 이 규칙 밖이다.
    /// </summary>
    private bool TrustEconomyForAchievements =>
        !_cheated && (_platform.Achievements is MockAchievements || _platform.Economy is not MockEconomyService);

    /// <summary>룸 참가 도전과제를 해금해도 되는가. 목 멀티의 가짜 룸은 안 된다 - 위와 같은 이유.</summary>
    private bool TrustNetForAchievements =>
        _platform.Achievements is MockAchievements || _platform.Net is not MockNetSession;

    /// <summary>
    /// 아직 안 딴 것만 해금한다. 스팀이 없으면 아무것도 안 한다 - 그때 놓친 것은
    /// <see cref="SyncAchievements"/> 가 스팀이 붙은 뒤 주워 담는다.
    /// </summary>
    private void TryUnlock(string id, string why)
    {
        if (!_platform.Achievements.IsAvailable || _platform.Achievements.IsUnlocked(id))
        {
            return;
        }

        _platform.Achievements.Unlock(id);
        GD.Print($"[game] 도전과제 해금 {id} ({why})");
    }

    /// <summary>슬롯 하나의 장식을 전부 가졌으면 그 슬롯의 도전과제를 해금한다.</summary>
    private void UnlockCompletedSlots()
    {
        foreach (CursorSlot slot in Enum.GetValues<CursorSlot>())
        {
            if (_inventory.OwnedInSlot(slot) < ShopCatalog.CountInSlot(slot))
            {
                continue;
            }

            // API Name 은 파트너 사이트에서 굳어서 못 바꾼다 - 뜻만 새 칸으로 옮겼다 (docs/B17-CURSOR-REWORK.md §3-4).
            // HANG = 원숭이 수집가, TRAIL = 장식 수집가, BASE = 바나나 수집가.
            string id = slot switch
            {
                CursorSlot.Monkey => AchievementIds.SlotCompleteHang,
                CursorSlot.Deco => AchievementIds.SlotCompleteTrail,
                CursorSlot.Banana => AchievementIds.SlotCompleteBase,
                _ => null,
            };

            if (id != null)
            {
                TryUnlock(id, $"{ShopCatalog.SlotName(slot)} 슬롯 완성");
            }
        }
    }

    /// <summary>
    /// 이미 달성한 조건을 훑어서 아직 안 풀린 도전과제를 해금한다 (A15).
    ///
    /// <b>해금은 조건을 넘는 순간 한 번만 부른다.</b> 그 순간 스팀이 없으면(자동 시작이
    /// 스팀보다 먼저 뜬 경우 등) <c>Unlock</c> 이 조용히 버려지고, 다음 실행부터는 "이미
    /// 지난 마일스톤" 으로 건너뛰어서 <b>그 도전과제는 영영 안 풀렸다.</b> 그래서 스팀
    /// 통계가 처음 준비됐을 때 한 번 훑는다. <see cref="TryUnlock"/> 이 이미 딴 것은
    /// 건너뛰므로 스팀에 중복 호출이 가지 않는다.
    /// </summary>
    private void SyncAchievements()
    {
        _achievementsSynced = true;

        // 스팀이 없는 동안 친 타수도 통계에 올린다 - 타건마다 부르는 쪽은 그때 버려졌다.
        PushKeystrokeStat();

        foreach ((string id, int threshold) in AchievementIds.KeystrokeMilestones)
        {
            if (Save.TotalKeystrokes >= threshold)
            {
                TryUnlock(id, $"누적 {threshold:N0}타 - 놓친 것 회수");
            }
        }

        // 보유 목록을 아직 못 받았으면 소유 기반 과제는 판단하지 않는다 - 빈 목록을
        // "아무것도 없다" 로 읽으면 안 된다(IInventoryService.IsLoaded). 목록이 오면
        // OnInventoryLoadedLate 가 다시 훑게 한다.
        if (_inventory == null || !_platform.Inventory.IsLoaded || !TrustEconomyForAchievements)
        {
            return;
        }

        if (_inventory.OwnedCount > 1)
        {
            // 기본 지급품(1개) 말고 하나라도 있으면 산 적이 있는 것이다.
            TryUnlock(AchievementIds.FirstPurchase, "보유 장식 있음 - 놓친 것 회수");
        }

        UnlockCompletedSlots();

        if (_inventory.IsComplete)
        {
            TryUnlock(AchievementIds.Collection100, "도감 100% - 놓친 것 회수");
        }
    }

    /// <summary>
    /// 누적 타수를 스팀 통계(<see cref="StatIds.Keystrokes"/>)에 비춘다. 누적 타수 도전과제의
    /// Progress Stat 이라 커뮤니티 페이지 진행 막대가 이 값을 쓴다. 로컬 캐시만 바꾸고 서버로는
    /// SteamService 가 1분마다 모아 보낸다. INT 라 21억에서 멈춘다(초당 캡 10 으로 6년 넘게 걸린다).
    /// </summary>
    private void PushKeystrokeStat() =>
        _platform.Achievements.SetStat(StatIds.Keystrokes, (int)Math.Min(int.MaxValue, Save.TotalKeystrokes));

    /// <summary>룸에 들어갔으면(만들었거나 참가했거나) 첫 참가 도전과제를 해금한다.</summary>
    private void OnRoomChangedForAchievement()
    {
        if (_platform.Net.Current != null && TrustNetForAchievements)
        {
            TryUnlock(AchievementIds.RoomFirstJoin, "멀티 룸 첫 참가");
        }
    }

    /// <summary>
    /// 도감 100% 도전과제 (§3-3). <b>기획서가 유일하게 명시한 도전과제다.</b>
    ///
    /// 해금 조건을 아는 것은 게임 레이어이고 스팀에 쓰는 것은 플랫폼이다 -
    /// <see cref="CheckMilestones"/> 와 같은 경계다. 스팀이 안 붙어 있으면 호출이
    /// 조용히 버려지므로 분기하지 않는다.
    ///
    /// 진행도는 구매마다 한 번씩만 움직인다(지금 16종이라 한 칸이 6.25%다). 그래서
    /// 마일스톤처럼 구간을 따로 끊지 않고 그대로 올린다.
    /// </summary>
    private void CheckCollection()
    {
        if (_collectionDone)
        {
            return;
        }

        if (!TrustEconomyForAchievements)
        {
            // 디버그로 받았거나 목 경제로 산 것이라 진행도조차 올리지 않는다 - 스팀 통계에 남는다.
            return;
        }

        UnlockCompletedSlots();

        int owned = _inventory.OwnedCount;
        int total = ShopCatalog.All.Length;

        // 다 모으기 전에는 아무것도 안 띄운다 - 구매마다 "N/16" 진행도 토스트가 떴었다
        // (CheckMilestones 의 주석과 같은 이유).
        if (!_inventory.IsComplete)
        {
            return;
        }

        _collectionDone = true;
        _platform.Achievements.Unlock(AchievementIds.Collection100);
        GD.Print($"[game] 도감 100% 해금 ({owned}/{total})");
    }

    /// <summary>장착/해제. 커서 레이어에 미는 것은 <see cref="Inventory"/> 가 한다.</summary>
    private void OnEquipRequested(CursorSlot slot, string id)
    {
        if (!_inventory.Equip(slot, id))
        {
            return;
        }

        GD.Print($"[game] 장착 {slot} = {id ?? "(비움)"}");
        _shop.Refresh();
        PersistNow();
    }

    /// <summary>
    /// 디스크 쓰기를 요청한다. 실제로 언제 쓸지는 플랫폼이 정한다
    /// (<see cref="ISaveStore"/>). 장착(로컬)이 바뀐 뒤에 부른다 - 바나나·소유권은
    /// 이제 세이브에 없으므로 여기서 반영할 것이 없다.
    /// </summary>
    private void PersistNow() => _store.MarkDirty();

    /// <summary>
    /// 친구에게 보일 내 상태 중 창과 무관한 것 (A10). 창 안의 타건·수확 수는
    /// <see cref="PlayerStateSender"/> 가 채운다. 인벤토리가 서기 전이면 장식은 비어 간다.
    /// </summary>
    private PlayerState SnapshotForPeers() => new()
    {
        TotalKeystrokes = Save.TotalKeystrokes,
        EquippedMonkey = _inventory?.EquippedIn(CursorSlot.Monkey),
        EquippedBanana = _inventory?.EquippedIn(CursorSlot.Banana),
        EquippedDeco = _inventory?.EquippedIn(CursorSlot.Deco),
        CollectionPercent = _inventory == null
            ? (byte)0
            : (byte)(_inventory.OwnedCount * 100 / ShopCatalog.All.Length),
    };

    /// <summary>
    /// 클릭을 받을 영역 (<see cref="IInteractiveArea"/>).
    ///
    /// <b>상점이 열린 동안은 창 전체를 신고한다.</b> 셸은 옵션 창을 열 때
    /// passthrough 를 통째로 끄지만(platform/OverlayShell.Visibility.cs), 게임
    /// 레이어는 이 계약으로만 말할 수 있다 - 그래서 "창 전체" 를 이 좌표계로
    /// 옮겨서 돌려준다. 플랫폼 코드는 한 줄도 안 바뀐다.
    /// </summary>
    public Rect2 GetClickableBounds()
    {
        if (_shop is { IsOpen: true } || _roomWindow is { IsOpen: true } || _onboarding is { IsOpen: true })
        {
            return ViewportInParentSpace();
        }

        Rect2 bounds = _tree.GetBounds().Merge(_monkey.GetBounds());

        // 상점·멀티 버튼도 클릭을 받아야 한다. 나무·원숭이 바로 아래에 나란히 둔 이유가
        // 이것이다 - Rect2.Merge 는 외접 사각형이라, 버튼이 화면 반대편에 있으면
        // 그 사이의 빈 공간까지 전부 클릭을 먹는다.
        //
        // **Size 를 그대로 믿으면 안 된다.** 레이아웃이 돌기 전에는 (0,0) 이라
        // 버튼 자리에 점 하나만 합쳐지고, 그러면 버튼 가운데가 클릭 영역 밖으로
        // 빠져서 **눌러도 아무 일이 안 일어난다** - 실제로 그 상태를 밟았고,
        // 타이밍에 따라 되기도 하고 안 되기도 해서 원인 찾기가 고약했다.
        bounds = bounds.Merge(ButtonRect(_shopButton)).Merge(ButtonRect(_roomButton)).Merge(ButtonRect(_optionsButton))
            .Merge(ButtonRect(_helpButton));

        return Transform * bounds;
    }

    private static Rect2 ButtonRect(Button button) =>
        new(button.Position, button.Size.Max(button.GetCombinedMinimumSize()));

    /// <summary>
    /// 처음 안내를 연다 (B15). 상점·로비가 열려 있으면 닫는다 - 셋 다 창 전체를 덮는 CanvasLayer 라 겹치면 아래
    /// 것이 가려진 채로 남는다 (<see cref="ToggleShop"/> 와 같은 규칙).
    /// </summary>
    private void OpenOnboarding()
    {
        if (_shop.IsOpen)
        {
            _shop.Close();
        }

        if (_roomWindow.IsOpen)
        {
            _roomWindow.Close();
        }

        _onboarding.Open();
    }

    /// <summary>안내를 끝까지 봤거나 건너뛰었다. 다음부터는 안 뜬다 - 이미 본 판이면 쓸 것이 없다.</summary>
    private void OnOnboardingFinished()
    {
        if (Save.OnboardingSeen >= OnboardingWindow.Version)
        {
            return;
        }

        Save.OnboardingSeen = OnboardingWindow.Version;
        PersistNow();
        GD.Print($"[game] 처음 안내 봄 (판 {OnboardingWindow.Version})");
    }

    /// <summary>
    /// 상점과 룸 창은 한 번에 하나만 연다 - 둘 다 창 전체를 덮는 CanvasLayer 라
    /// 겹쳐 열면 아래 것이 가려진 채로 클릭을 기다린다.
    /// </summary>
    private void ToggleShop()
    {
        _onboarding.Close();
        if (_roomWindow.IsOpen)
        {
            _roomWindow.Close();
        }

        _shop.Toggle();
    }

    private void ToggleRoom()
    {
        _onboarding.Close();
        if (_shop.IsOpen)
        {
            _shop.Close();
        }

        _roomWindow.Toggle();
    }

    /// <summary>
    /// 창 전체를 <see cref="IInteractiveArea"/> 가 요구하는 좌표계(부모 로컬)로 옮긴다.
    ///
    /// 셸 루트에 배율이 걸려 있고 플랫폼이 그 배율을 다시 곱하므로
    /// (<c>OverlayShell.CurrentHitRect</c>), 여기서는 역변환으로 되돌려야 값이
    /// 한 바퀴 돌아 제자리에 온다. 네 모서리를 각각 옮겨 감싸는 것은 회전이
    /// 걸렸을 때도 축에 정렬된 사각형을 얻기 위해서다.
    /// </summary>
    private Rect2 ViewportInParentSpace()
    {
        Rect2 viewport = GetViewportRect();

        if (GetParent() is not Node2D parent)
        {
            return viewport;
        }

        Transform2D toLocal = parent.GlobalTransform.AffineInverse();

        var rect = new Rect2(toLocal * viewport.Position, Vector2.Zero);
        rect = rect.Expand(toLocal * new Vector2(viewport.End.X, viewport.Position.Y));
        rect = rect.Expand(toLocal * new Vector2(viewport.Position.X, viewport.End.Y));
        rect = rect.Expand(toLocal * viewport.End);
        return rect;
    }
}
