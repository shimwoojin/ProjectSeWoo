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
/// B2 가 <see cref="PopBananas"/> 를 붙였다. 남은 것은 자릿수가 바뀔 때의 강조와
/// 레벨업 연출이다 (§2-3).
/// </summary>
public partial class StatusHud : VBoxContainer
{
    private Label _level;
    private ProgressBar _levelBar;
    private Label _keystrokes;
    private Label _bananas;
    private Tween _bananaPop;

    public override void _Ready()
    {
        _level = GetNode<Label>("Level");
        _levelBar = GetNode<ProgressBar>("LevelBar");
        _keystrokes = GetNode<Label>("Keystrokes");
        _bananas = GetNode<Label>("Bananas");
    }

    /// <summary>보유 바나나. 유일한 재화다 (§3-2).</summary>
    public void SetBananas(long bananas)
    {
        _bananas.Text = $"바나나 {bananas:N0}";
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
        _level.Text = $"Lv.{KeystrokeLevel.LevelFor(total)}";
        _levelBar.Value = KeystrokeLevel.ProgressInLevel(total) * 100.0;
        _keystrokes.Text = $"{total:N0}타";
    }
}
