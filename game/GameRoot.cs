using System;
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
    /// 아무 일이 없어도 이 간격으로 세이브 객체를 한 번 갱신한다.
    ///
    /// 나무는 수확이 없어도 계속 자라므로, 변경이 있을 때만 알리면 성장 진행이
    /// 오래 안 실린다. 실제 디스크 쓰기는 플랫폼이 또 한 번 묶으므로
    /// (<see cref="ISaveStore"/>) 이 간격이 곧 쓰기 주기는 아니다.
    /// </summary>
    private const double SyncIntervalSec = 5.0;

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

    private Tree _tree;
    private Monkey _monkey;
    private StatusHud _hud;

    /// <summary>
    /// 아직 안 딴 가장 낮은 타수 마일스톤의 인덱스 (§6,
    /// <see cref="AchievementIds.KeystrokeMilestones"/>). 표 끝까지 갔으면 전부 딴 것이다.
    ///
    /// 배열이 오름차순이라 앞에서부터 하나씩 밀면 되고, 매 배치마다 표 전체를
    /// 훑을 필요가 없다.
    /// </summary>
    private int _nextMilestone;

    /// <summary>
    /// 마지막으로 진행도 토스트를 띄운 구간. <see cref="IAchievements.IndicateProgress"/>
    /// 는 <b>매 타건마다 부르라고 만든 API 가 아니다</b>(계약 주석) - 구간을 넘을 때만 부른다.
    /// </summary>
    private int _shownProgressBucket = -1;

    /// <summary>진행도 토스트를 몇 구간으로 끊을지. 20 = 5%마다 한 번.</summary>
    private const int ProgressBuckets = 20;

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

    private double _sinceSync;

    public override void _Ready()
    {
        AddToGroup(SceneGroups.GameRoot);

        _tree = GetNode<Tree>("Tree");
        _monkey = GetNode<Monkey>("Monkey");
        _hud = GetNode<StatusHud>("StatusHud");

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

        LoadGameState();
    }

    /// <summary>
    /// 세이브에서 게임 상태를 세우고, 꺼져 있던 동안 자란 만큼을 반영한다 (B5, §2-2).
    /// </summary>
    private void LoadGameState()
    {
        _tree.Configure(Save.Tree);

        int ripened = _tree.AdvanceOffline(OfflineMs());

        // 이미 넘어선 마일스톤은 세션 시작 시점에 지나간 것으로 잡는다. 안 그러면
        // 켤 때마다 예전에 딴 도전과제를 다시 Unlock 한다 - 스팀이 무시하긴 하지만
        // 부를 이유가 없고, 진행도 토스트가 엉뚱한 구간에서 뜬다.
        _level = KeystrokeLevel.LevelFor(Save.TotalKeystrokes);
        while (_nextMilestone < AchievementIds.KeystrokeMilestones.Length
            && Save.TotalKeystrokes >= AchievementIds.KeystrokeMilestones[_nextMilestone].Threshold)
        {
            _nextMilestone++;
        }

        _hud.SetBananas(Save.Bananas);
        _hud.SetKeystrokes(Save.TotalKeystrokes);

        GD.Print($"[game] 세이브 로드 - 바나나 {Save.Bananas}, 누적 {Save.TotalKeystrokes}타"
            + $" (Lv.{_level}), 슬롯 {Save.Tree.Slots}개, 오프라인에 {ripened}개 열림");

        // 오프라인 성장분을 바로 한 번 받아 적는다. 안 해도 다음 실행이 같은 계산을
        // 다시 하므로 손해는 없지만, 세이브 파일만 열어 봐도 지금 상태가 보이는 편이
        // 디버깅에 낫다.
        SyncToSave();
    }

    /// <summary>
    /// 앱이 꺼져 있던 시간(ms). 기준점은 <see cref="SaveData.LastQuitUtc"/> 이고,
    /// 실제 의미는 "마지막으로 저장한 시각" 이다 (platform/SaveStore.cs 참고) -
    /// 그래서 정상 종료와 강제 종료가 같은 경로를 탄다.
    ///
    /// 두 경우를 막는다. 첫 실행(<c>lastQuitUtc</c> 가 UnixEpoch)이면 0이고,
    /// <b>시계를 뒤로 돌렸으면</b>(음수) 역시 0이다 - 음수를 그대로 더하면
    /// 자라던 나무가 거꾸로 간다.
    ///
    /// 상한은 성장 주기 하나다. 어차피 슬롯마다 주기에서 상한에 걸리므로
    /// (<see cref="Tree.AdvanceOffline"/>) 그 이상은 의미가 없고, 몇 년 된 세이브의
    /// 거대한 차이가 <c>long</c> 범위를 넘는 것도 여기서 막힌다.
    /// </summary>
    private long OfflineMs()
    {
        if (Save.LastQuitUtc <= DateTime.UnixEpoch)
        {
            return 0;
        }

        double ms = (DateTime.UtcNow - Save.LastQuitUtc).TotalMilliseconds;
        return (long)Math.Clamp(ms, 0.0, Save.Tree.GrowthMs);
    }

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

        _tree.Tick(delta);

        if (_store == null)
        {
            return;
        }

        _sinceSync += delta;
        if (_sinceSync >= SyncIntervalSec)
        {
            _sinceSync = 0.0;
            SyncToSave();
            _store.MarkDirty();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        // 8분을 기다리지 않고 수확까지 확인하려고 둔 debug 키다. 셸이 쓰는 키
        // (F1~F12 / 1~4 / [ ] - = O H / Esc)와 겹치지 않는 자리를 골랐다.
        // 강화 UI(B7)가 생기면 그쪽이 이 자리를 대신한다.
        if (key.Keycode == Key.G)
        {
            _tree.DebugAdvance(DebugGrowMs);
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

        // 누적 타수는 재화가 아니라 기록이다 (§6). 수확 여부와 무관하게 센다 -
        // 빈 나무를 쳐도 타수는 늘어야 "논 시간" 이 레벨에 반영된다. 레벨 환산과
        // 마일스톤 도전과제(AchievementIds)는 B3 가 이 값 위에 올린다.
        Save.TotalKeystrokes += count;

        // 수확을 애니메이션 타이밍이 아니라 입력에 직접 건다. §2-3 검토 노트의
        // "키 입력과 애니메이션을 1:1 고정 대응시키지 말 것"이 이 뜻이고,
        // 연출이 끊기거나 겹쳐도 수확 개수가 흔들리지 않는다.
        int harvested = 0;
        while (harvested < count && _tree.TryHarvest(out Vector2 fruitPosition))
        {
            DropBanana(fruitPosition, contact);
            harvested++;
        }

        if (harvested > 0)
        {
            Save.Bananas += harvested;   // §2-2: 펀치 1회당 열린 바나나 1개
            _hud.SetBananas(Save.Bananas);
            _hud.PopBananas();
        }

        _hud.SetKeystrokes(Save.TotalKeystrokes);
        CheckLevelUp();
        CheckMilestones();

        // 수확이 없어도 누적 타수가 늘었으므로 저장 대상이다.
        SyncToSave();
        _store.MarkDirty();
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
            _shownProgressBucket = -1;
        }

        if (_nextMilestone >= table.Length)
        {
            return;
        }

        // 다음 마일스톤까지의 진행도. 구간을 넘을 때만 띄운다.
        (string nextId, int threshold) = table[_nextMilestone];
        var current = (int)Math.Min(Save.TotalKeystrokes, threshold);

        int bucket = current * ProgressBuckets / threshold;
        if (bucket == _shownProgressBucket)
        {
            return;
        }

        _shownProgressBucket = bucket;
        _platform.Achievements.IndicateProgress(nextId, current, threshold);
    }

    /// <summary>
    /// 지금 상태를 세이브 객체에 반영한다. <b>디스크를 건드리지 않는다</b> -
    /// 언제 쓸지는 <see cref="ISaveStore"/> 쪽이 정한다.
    ///
    /// 재화와 누적 타수는 이미 <see cref="Save"/> 를 직접 고치고 있으므로 여기서
    /// 할 일은 나무 타이머를 받아 적는 것뿐이다. 나무가 자기 타이머를 들고 있는
    /// 것은 상태를 두 군데 두지 않기 위해서다 (game/entities/TreeSlot.cs 참고).
    /// </summary>
    private void SyncToSave() => _tree.WriteTo(Save.Tree);

    /// <summary>
    /// 수확한 바나나가 떨어지는 연출 (§2-3). 재화는 이미 입력 시점에 더해졌고
    /// 이건 눈에 보이는 쪽만 한다.
    /// </summary>
    private void DropBanana(Vector2 from, double delay)
    {
        var banana = _fallingBananaScene.Instantiate<FallingBanana>();
        banana.Position = from;
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

        // 마지막 상태를 써 넣는다. 디스크 쓰기는 셸의 _ExitTree 가 FlushNow 로
        // 마무리하지만, 그 순서를 가정하지 않으려고 여기서도 한 번 흘려보낸다 -
        // 이미 쓴 뒤라면 아무 일도 안 한다.
        SyncToSave();
        _store.MarkDirty();
        _store.FlushNow();
    }

    public Rect2 GetClickableBounds() => Transform * _tree.GetBounds().Merge(_monkey.GetBounds());
}
