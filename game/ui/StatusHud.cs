using Godot;

namespace ProjectSeWoo.Game;

/// <summary>
/// 게임 내 상태 표시 — 닉네임 · 누적 타수 · 바나나 (§2-3 "타수 카운터").
///
/// <b>세 줄만 둔다 (2026-09-27).</b> 바탕화면에 늘 떠 있는 창이라 숫자가 많을수록 일하는 내내
/// 거슬린다. 예전엔 레벨 · 레벨 진행 바 · 도감 % 까지 다섯 줄이었는데, 레벨은 누적 타수를
/// 로그 곡선으로 바꾼 표시일 뿐 재화·강화·도전과제 어디에도 쓰이지 않아서 뺐고(도전과제는 타수
/// 문턱을 직접 본다 - <see cref="Shared.AchievementIds.KeystrokeMilestones"/>), 도감 % 는 메뉴의 도감 탭이
/// 맡는다. 로비 줄과 안내 줄은 그때만 나타난다.
///
/// <b>한글을 쓴다 (B4 이후).</b> B4 가 Pretendard 를 번들하고 <c>gui/theme/custom</c> 으로
/// 걸면서 한글 글리프 제약이 풀렸다 (assets/ui/theme.tres).
///
/// <b>글자에 외곽선을 넣는 이유</b>는 폴리싱이 아니라 기능이다. 이 창 뒤는 유저의
/// 바탕화면이라 배경색을 우리가 못 정한다 - 흰 글자만 놓으면 밝은 배경에서 아예
/// 안 보인다.
/// </summary>
public partial class StatusHud : VBoxContainer
{
    private Label _name;
    private Label _keystrokes;
    private Label _bananas;
    private Label _room;
    private Label _notice;
    private Tween _bananaPop;

    public override void _Ready()
    {
        _name = GetNode<Label>("Name");
        _keystrokes = GetNode<Label>("Keystrokes");
        _bananas = GetNode<Label>("Bananas");
        _notice = GetNode<Label>("Notice");
        _room = GetNode<Label>("Room");
        _room.Visible = false;
        _name.Visible = false;
    }

    /// <summary>내 닉네임 (<see cref="Shared.INetSession.SelfName"/>). 비어 있으면 줄을 숨긴다.</summary>
    public void SetPlayerName(string name)
    {
        bool show = !string.IsNullOrEmpty(name);
        _name.Visible = show;
        _name.Text = name ?? string.Empty;
    }

    /// <summary>
    /// 멀티 룸 한 줄 (§4-2 "룸 내 랭킹"). 룸 창을 닫아 둬도 내 순위는 보이게 한다.
    /// null 이나 빈 문자열이면 줄을 숨긴다 - 룸 밖에서는 자리도 차지하지 않는다.
    /// </summary>
    public void SetRoom(string text)
    {
        bool show = !string.IsNullOrEmpty(text);
        _room.Visible = show;
        if (show && _room.Text != text)
        {
            _room.Text = text;
        }
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

    /// <summary>누적 타수 (§6). 재화가 아니라 기록이다.</summary>
    public void SetKeystrokes(long total)
    {
        _keystrokes.Text = $"{total:N0}타";
    }
}
