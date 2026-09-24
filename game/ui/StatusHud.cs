using Godot;

namespace ProjectSeWoo.Game;

/// <summary>
/// 게임 내 상태 표시 — 레벨 · 누적 타수 · 바나나 (§6 "화면 표시", §2-3 "타수 카운터").
///
/// <b>한글을 쓴다 (B4 이후).</b> 그전에는 Godot 기본 테마 폰트에 한글 글리프가
/// 없어서 전부 ASCII 였는데, B4 가 Pretendard 를 번들하고 <c>gui/theme/custom</c>
/// 으로 걸면서 제약이 풀렸다 (assets/ui/theme.tres).
///
/// <b>글자에 외곽선을 넣는 이유</b>는 폴리싱이 아니라 기능이다. 이 창 뒤는 유저의
/// 바탕화면이라 배경색을 우리가 못 정한다 - 흰 글자만 놓으면 밝은 배경에서 아예
/// 안 보인다.
///
/// <b>진행 바 폭을 컨테이너에 맡기지 않는다.</b> <c>VBoxContainer</c> 는 자식을
/// 가로로 늘리는데, 그러면 바의 빈 구간이 나무 위까지 뻗어서 화면을 가로지르는
/// 선으로 보인다. 글자 폭에 맞춰 84px 로 고정하고 왼쪽에 붙인다 (실제로 띄워
/// 보고서야 드러난 것이다).
///
/// <b>바 옆에 "이 레벨 안에서 친 타수 / 이 레벨에 필요한 타수"를 적는다.</b> 레벨
/// 한 칸이 1.35배씩 무거워져서(<see cref="KeystrokeLevel"/>) 바만으로는 "얼마나
/// 남았나"가 안 읽힌다. 누적 기준(1,167 / 1,390)이 아니라 구간 기준(163 / 386)인
/// 것은 목표가 한눈에 들어오게 하려는 것이다 - 누적은 바로 아래 줄에 따로 있다.
///
/// B2 가 <see cref="PopBananas"/> 를 붙였다. 레벨업 순간엔 레벨 글자가 튄다
/// (<see cref="PopLevel"/>). 남은 것은 자릿수가 바뀔 때의 강조다 (§2-3).
/// </summary>
public partial class StatusHud : VBoxContainer
{
    private Label _level;
    private ProgressBar _levelBar;
    private Label _levelCount;
    private Label _keystrokes;
    private Label _bananas;
    private Label _collection;
    private Label _notice;
    private Tween _bananaPop;
    private Tween _levelPop;

    /// <summary>지난 <see cref="SetKeystrokes"/> 의 레벨. 0 = 아직 안 그림 (로드 때는 튀지 않는다).</summary>
    private int _shownLevel;

    public override void _Ready()
    {
        _level = GetNode<Label>("Level");
        _levelBar = GetNode<ProgressBar>("LevelRow/LevelBar");
        _levelCount = GetNode<Label>("LevelRow/LevelCount");
        _keystrokes = GetNode<Label>("Keystrokes");
        _bananas = GetNode<Label>("Bananas");
        _collection = GetNode<Label>("Collection");
        _notice = GetNode<Label>("Notice");
    }

    /// <summary>
    /// 맨 아래 안내 한 줄 (A14). null 이면 숨긴다. 서버에 못 붙어 슬롯을 하나도
    /// 못 받았을 때 쓴다 - 안내가 없으면 오프라인 첫 실행이 "나무가 고장 났다" 로 보인다.
    /// </summary>
    public void SetNotice(string text)
    {
        _notice.Text = text ?? string.Empty;
        _notice.Visible = text != null;
    }

    /// <summary>보유 바나나. 유일한 재화다 (§3-2).</summary>
    public void SetBananas(long bananas)
    {
        _bananas.Text = $"바나나 {bananas:N0}";
    }

    /// <summary>
    /// 도감 수집률 (§3-3 "수집률 %를 메인 화면 구석에 표시", B7).
    ///
    /// <b>구석의 작은 숫자로 둔다.</b> 이 창은 바탕화면 위에 상시로 떠 있는
    /// 것이라 크게 넣으면 일하는 내내 거슬린다 - 자랑 지표는 상점의 도감 탭이
    /// 맡고, 여기는 "아직 다 안 모았다" 만 알려 주면 된다.
    /// </summary>
    public void SetCollection(int owned, int total)
    {
        _collection.Text = total <= 0
            ? string.Empty
            : $"도감 {owned}/{total} ({owned * 100 / total}%)";
    }

    /// <summary>바나나가 늘어난 순간 숫자를 한 번 튕긴다 (§2-3 "카운터 숫자 증가 애니").</summary>
    public void PopBananas()
    {
        _bananaPop?.Kill();
        _bananas.Scale = Vector2.One;
        _bananaPop = CreateTween();
        _bananaPop.TweenProperty(_bananas, "scale", Vector2.One * 1.18f, 0.07)
            .SetTrans(Tween.TransitionType.Quad);
        _bananaPop.TweenProperty(_bananas, "scale", Vector2.One, 0.14)
            .SetTrans(Tween.TransitionType.Quad);
    }

    /// <summary>
    /// 누적 타수와 거기서 환산한 레벨 (§6).
    ///
    /// 레벨을 인자로 받지 않고 여기서 다시 구하는 것은 <see cref="KeystrokeLevel"/>
    /// 이 상태 없는 순수 함수라 두 곳에서 불러도 답이 갈릴 수 없기 때문이다.
    /// </summary>
    public void SetKeystrokes(long total)
    {
        int level = KeystrokeLevel.LevelFor(total);
        long from = KeystrokeLevel.ThresholdFor(level);
        long to = KeystrokeLevel.ThresholdFor(level + 1);

        _level.Text = $"Lv.{level}";
        _levelBar.Value = KeystrokeLevel.ProgressInLevel(total) * 100.0;
        _levelCount.Text = $"{total - from:N0} / {to - from:N0}";
        _keystrokes.Text = $"{total:N0}타";

        if (_shownLevel > 0 && level > _shownLevel)
        {
            PopLevel();
        }

        _shownLevel = level;
    }

    /// <summary>레벨이 오른 순간 레벨 글자를 한 번 크게 튕기고 잠깐 노랗게 빛낸다 (§2-3 레벨업 연출).</summary>
    private void PopLevel()
    {
        _levelPop?.Kill();
        // 라벨은 VBox 폭만큼 늘어나 있고 글자는 왼쪽에 붙어 있다 - 가운데 피벗이면
        // 글자가 왼쪽으로 밀려난다.
        _level.PivotOffset = new Vector2(0f, _level.Size.Y / 2f);
        _level.Scale = Vector2.One;
        _level.Modulate = Colors.White;

        _levelPop = CreateTween();
        _levelPop.TweenProperty(_level, "scale", Vector2.One * 1.35f, 0.1)
            .SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.Out);
        _levelPop.Parallel()
            .TweenProperty(_level, "modulate", new Color(1f, 0.85f, 0.3f), 0.1);
        _levelPop.TweenProperty(_level, "scale", Vector2.One, 0.25)
            .SetTrans(Tween.TransitionType.Quad);
        _levelPop.Parallel()
            .TweenProperty(_level, "modulate", Colors.White, 0.6);
    }
}
