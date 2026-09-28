using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using ProjectSeWoo.Shared;
using ProjectSeWoo.Shared.Mocks;

namespace ProjectSeWoo.Platform;

/// <summary>
/// 트레일러 30초 원판 (docs/C1-STORE.md §10). <b>디버그 빌드 전용 무인 실행이다.</b>
/// <code>Godot.exe --path . --fixed-fps 30 -- --trailer=&lt;폴더&gt;</code>
///
/// 스크린샷(<see cref="RunStoreShot"/>)과 같은 목 상태에서, <b>매 프레임</b> 떠 있는 창(메인·커서·친구)을 PNG 로 저장한다.
/// 합성·인코딩은 <c>tools/make-store-trailer.py</c> 가 한다 - 바탕화면을 녹화하지 않으므로 개인 화면이 섞이지 않는다.
///
/// <b><c>--fixed-fps 30</c> 을 꼭 준다.</b> 그래야 PNG 를 쓰느라 한 프레임이 오래 걸려도 게임 시간은 1/30초씩만 간다 -
/// 애니메이션·타이머가 영상 시간과 맞는다. 타건·버튼·커서는 전부 <b>프레임 번호</b>로 넣는다(<see cref="TrailerAt"/>) -
/// 실제 키보드·마우스를 읽지 않아서 매번 똑같이 나온다.
///
/// 커서 경로와 클릭 자리는 <b>게임 화면 왼쪽 위(<see cref="ContentOrigin"/>) 기준 창 px</b> 로 적는다. 합성도 같은 기준으로
/// 창을 놓으므로 메뉴 칸이 열려 창이 넓어져도 버튼 위에 커서가 온다.
/// </summary>
public partial class OverlayShell
{
    private const string TrailerPrefix = "--trailer=";

    /// <summary>30fps 로 900 프레임 = 30초. 마지막 2초(840~)는 합성이 로고를 그린다 - 게임 창은 그 전까지.</summary>
    private const int TrailerFps = 30;
    private const int TrailerFrames = 840;

    private static bool IsTrailerRun() =>
        OS.IsDebugBuild() && Array.Exists(OS.GetCmdlineUserArgs(), a => a.StartsWith(TrailerPrefix, StringComparison.Ordinal));

    private static string TrailerDir()
    {
        string arg = Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith(TrailerPrefix, StringComparison.Ordinal));
        return arg?[TrailerPrefix.Length..];
    }

    /// <summary>지금 커서 끝이 있을 자리 (게임 화면 기준 창 px). 장면이 바꾼다.</summary>
    private Vector2 _trailerCursor = new(-330, 260);

    /// <summary>커서가 <see cref="_trailerCursor"/> 로 따라가는 속도 (0~1, 프레임당). 1 이면 순간이동.</summary>
    private float _trailerCursorEase = 0.25f;

    private Vector2 _trailerCursorNow = new(-330, 260);

    private async void StartTrailer()
    {
        string dir = TrailerDir();
        if (dir == null)
        {
            return;
        }

        try
        {
            await RunTrailer(dir);
            GD.Print($"[trailer] 끝 - {dir}");
            GetTree().Quit(0);
        }
        catch (Exception e)
        {
            GD.PrintErr($"[trailer] 실패 - {e}");
            GetTree().Quit(1);
        }
    }

    private async Task RunTrailer(string dir)
    {
        Directory.CreateDirectory(dir);
        using var manifest = new StreamWriter(Path.Combine(dir, "frames.tsv"));
        manifest.WriteLine("frame\twindow\tx\ty\tw\th\tcontentX\tcontentY\tbalance");

        OS.LowProcessorUsageMode = false;
        Engine.MaxFps = 0;
        SetScale(StoreShotScale);
        SetOpacity(1.0f);
        _hud.Visible = false;

        // 커서는 대본대로 - 실제 마우스를 안 읽는다. 게임 화면 기준 좌표를 매 틱 화면 좌표로 바꾼다.
        _cursor.Script = () => (Vector2)ContentOrigin + _trailerCursorNow;

        // 송이: 서로 다른 단계에서 시작해 9초(270 프레임)까지 전부 익는다. 0·2 번이 황금.
        double[] start = { 0.62, 0.5, 0.42, 0.3, 0.18 };
        bool[] golden = { true, false, true, false, false };
        var economy = _economy as MockEconomyService;
        var net = _net as MockNetSession;
        const int RipeAt = 262;

        // 기동 동기화와 텍스처 로드 (녹화 전).
        for (int i = 0; i < 45; i++)
        {
            SetSlots(economy, start, golden, 0);
            await ToSignal(RenderingServer.Singleton, "frame_post_draw");
        }

        for (int f = 0; f < TrailerFrames; f++)
        {
            if (f < RipeAt)
            {
                SetSlots(economy, start, golden, f / (double)RipeAt);
            }
            else if (f == RipeAt)
            {
                SetSlots(economy, start, golden, 1.0);
            }

            TrailerAt(f, net);

            _trailerCursorNow = _trailerCursorNow.Lerp(_trailerCursor, _trailerCursorEase);
            await ToSignal(RenderingServer.Singleton, "frame_post_draw");
            SaveTrailerFrame(dir, manifest, f, economy?.Balance ?? 0);
        }
    }

    private static void SetSlots(MockEconomyService economy, double[] start, bool[] golden, double t)
    {
        if (economy == null)
        {
            return;
        }

        for (int i = 0; i < start.Length; i++)
        {
            double grown = start[i] + (1.0 - start[i]) * Math.Clamp(t, 0.0, 1.0);
            economy.DebugSetSlot(i, Math.Min(grown, 1.0), golden[i]);
        }
    }

    /// <summary>
    /// 프레임 번호별 연출. 장면 나눔은 C1 §10-1 콘티와 같다:
    /// 0~119 첫 타 · 120~269 익어 감 · 270~389 수확 · 390~569 커서 원숭이 · 570~719 메뉴 · 720~839 로비.
    /// </summary>
    private void TrailerAt(int f, MockNetSession net)
    {
        // --- 타건 ---
        bool type = f switch
        {
            < 15 => false,
            < 120 => f % 5 == 0,              // 초당 6타
            < 270 => f % 6 == 0,              // 익어 가는 동안 조금 느긋하게
            < 390 => f % 3 == 0,              // 수확 - 빨리 쳐서 10타를 채운다
            < 540 => false,                   // 커서 장면은 마우스만
            < 570 => f % 3 == 0,              // 신난 원숭이
            < 720 => false,                   // 메뉴는 클릭
            _ => f % 5 == 0,                  // 로비에서 같이 친다
        };
        if (type)
        {
            _input.DebugInject(1);
        }

        // --- 커서 ---
        switch (f)
        {
            case 0:
                _trailerCursor = new Vector2(-330, 260);
                _trailerCursorNow = _trailerCursor;
                break;
            case 390:
                _trailerCursor = new Vector2(-460, 330);
                _trailerCursorEase = 0.12f;
                break;
            case 480:
                // 세게 휘두른다 - 원숭이가 손을 놓쳤다가 다시 잡는다.
                _trailerCursorEase = 0.6f;
                break;
            case 515:
                _trailerCursor = new Vector2(-420, 300);
                _trailerCursorEase = 0.15f;
                break;
            case 570:
                _trailerCursor = new Vector2(-300, 150);
                _trailerCursorEase = 0.15f;
                PressKey(Key.B);                                 // 메뉴 → 상점 (원숭이 탭)
                break;
            case 590:
                _trailerCursor = new Vector2(-49, 146);          // 첫 줄 [장착] 위로
                break;
            case 620:
                PressFirstGameButton("장착");                    // 갈색 원숭이로 바꿔 끼운다 - 커서가 바로 바뀐다
                break;
            case 650:
                _trailerCursor = new Vector2(-325, 28);          // 탭 줄 [강화]
                break;
            case 672:
                PressGameButton("강화");
                break;
            case 700:
                _trailerCursor = new Vector2(-29, 28);           // [닫기]
                break;
            case 712:
                PressGameButton("닫기");
                break;
            case 715:
                if (net != null)
                {
                    StartTrailerLobby(net);
                }

                _trailerCursor = new Vector2(-260, 380);
                _trailerCursorEase = 0.1f;
                break;
        }

        // 커서 장면 - 리사주로 쓸고 다닌다 (390~515). 480~515 는 빠르고 크게.
        if (f is > 400 and < 515)
        {
            double t = (f - 400) / (double)TrailerFps;
            float r = f < 480 ? 150f : 330f;
            float speed = f < 480 ? 1.6f : 5.0f;
            _trailerCursor = new Vector2(-460, 330) + new Vector2(
                Mathf.Sin((float)t * speed) * r,
                Mathf.Sin((float)t * speed * 1.4f) * r * 0.45f);
        }
    }

    private async void StartTrailerLobby(MockNetSession net)
    {
        net.SimulatePeerActivity = true;
        await net.CreateRoom();
        string[] names = { "바나나킹", "타자왕", "고릴라" };
        long[] keys = { 1_842, 1_310, 655 };
        for (int i = 0; i < names.Length; i++)
        {
            var peer = new PeerId(9001 + (ulong)i);
            net.SimulateJoin(peer, names[i]);
            net.SimulateKeystrokes(peer, keys[i]);
        }

        net.AddKeystrokes(1_527);
    }

    /// <summary>글자가 <paramref name="text"/> 인 버튼 중 화면 맨 위의 것 - 상점 첫 줄의 [장착].</summary>
    private void PressFirstGameButton(string text)
    {
        Button best = null;
        Node root = GetTree().GetFirstNodeInGroup(SceneGroups.GameRoot);
        foreach (Node node in root?.FindChildren("*", "Button", true, false) ?? new Godot.Collections.Array<Node>())
        {
            if (node is Button b && b.Text == text && b.IsVisibleInTree() && !b.Disabled
                && (best == null || b.GlobalPosition.Y < best.GlobalPosition.Y))
            {
                best = b;
            }
        }

        best?.EmitSignal(BaseButton.SignalName.Pressed);
    }

    private void SaveTrailerFrame(string dir, StreamWriter manifest, int frame, long balance)
    {
        var windows = new List<Window> { GetTree().Root };
        foreach (Node node in GetTree().Root.FindChildren("*", "Window", true, false))
        {
            if (node is Window w)
            {
                windows.Add(w);
            }
        }

        Vector2I content = ContentOrigin;
        foreach (Window w in windows)
        {
            if (!w.Visible)
            {
                continue;
            }

            Image image = w.GetTexture().GetImage();
            image.SavePng(Path.Combine(dir, $"{frame:D4}__{w.Name}.png"));
            manifest.WriteLine($"{frame}\t{w.Name}\t{w.Position.X}\t{w.Position.Y}\t{image.GetWidth()}\t{image.GetHeight()}"
                + $"\t{content.X}\t{content.Y}\t{balance}");
        }
    }
}
