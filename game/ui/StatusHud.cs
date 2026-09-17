using Godot;

namespace ProjectSeWoo.Game;

/// <summary>
/// 게임 내 상태 표시 — 레벨 · 누적 타수 · 바나나 (§6 "화면 표시", §2-3 "타수 카운터").
///
/// <b>표시 문자열은 전부 ASCII 다.</b> Godot 기본 테마 폰트에 한글 글리프가 없어서
/// 한글을 넣으면 두부(□)로 나온다 - platform/DebugHud.cs 가 Week 0 에 확인해 둔
/// 그 제약이고, 게임 UI 도 한글 폰트를 번들하기 전까지는 같은 제약을 받는다.
/// 폰트 번들은 B4(씬 정리) / B9(1차 에셋)에서 다룬다.
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
/// 애니메이션(자릿수가 바뀔 때 강조, 레벨업 연출)은 여기 없다. §2-3 의 마이크로
/// 피드백은 B2 일감이고, 그때 이 노드에 붙는다.
/// </summary>
public partial class StatusHud : VBoxContainer
{
    private Label _level;
    private ProgressBar _levelBar;
    private Label _keystrokes;
    private Label _bananas;

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
        _bananas.Text = $"bananas {bananas:N0}";
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
        _keystrokes.Text = $"{total:N0} keys";
    }
}
