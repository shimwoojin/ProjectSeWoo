using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using ProjectSeWoo.Shared;
using ProjectSeWoo.Shared.Mocks;

namespace ProjectSeWoo.Platform;

/// <summary>
/// C1 스토어 스크린샷 원판 (docs/C1-STORE.md §3). <b>디버그 빌드 전용 무인 실행이다.</b>
/// <code>Godot.exe --path . -- --store-shot=&lt;폴더&gt;</code>
///
/// 목 상태(잔액·강화·익은 송이·황금 송이·보유 장식·장착·가짜 친구)를 매번 같게 세우고, 펀치·수확·상점·로비
/// 장면마다 <b>떠 있는 창(메인·커서·친구)을 창 텍스처째 알파가 살아 있는 PNG 로</b> 저장한다. 바탕화면을
/// 찍지 않으므로 개인 화면이 섞이지 않고, 합성은 <c>tools/make-store-screenshots.py</c> 가 한다.
///
/// 무인 실행(<see cref="IsUnattendedRun"/>)이라 세이브·레지스트리·트레이·스팀을 건드리지 않는다.
/// 입력 헬퍼도 안 띄운다 - 찍는 동안 실제 키보드가 섞이면 장면이 흔들린다. 타건은
/// <see cref="HelperInputSource.DebugInject"/> 로 넣는다.
/// </summary>
public partial class OverlayShell
{
    private const string StoreShotPrefix = "--store-shot=";

    /// <summary>
    /// 창 배율 (옵션 "크기" 와 같은 값). 1920x1080 스크린샷에 1.0 으로 넣으면 스토어 썸네일에서 원숭이가 점이
    /// 된다 - 150% 배율 모니터에서 보이는 크기로 찍는다. 옵션 범위(0.5~2.0) 안이라 실제로 보이는 모습이다.
    /// </summary>
    private const float StoreShotScale = 1.5f;

    /// <summary>찍을 때 보유한 것으로 둘 장식 (도감이 "절반 넘게" 로 보이게).</summary>
    private static readonly string[] StoreShotOwned =
    {
        "monkey_01", "monkey_02", "monkey_03", "monkey_04", "monkey_05",
        "banana_01", "banana_02", "banana_03",
        "leaf_01", "chunk_01", "spark_01", "leaf_02",
        "halo_01", "ring_01",
    };

    private static bool IsStoreShotRun() =>
        OS.IsDebugBuild() && Array.Exists(OS.GetCmdlineUserArgs(), a => a.StartsWith(StoreShotPrefix, StringComparison.Ordinal));

    private static string StoreShotDir()
    {
        string arg = Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith(StoreShotPrefix, StringComparison.Ordinal));
        return arg?[StoreShotPrefix.Length..];
    }

    /// <summary>
    /// 게임 레이어에 플랫폼을 넘기기 <b>전에</b> 부른다 - 게임이 기동 때 세이브의 장착과 목 보유 목록을
    /// 읽어 커서에 거므로 그 전에 채워 둬야 한다. 세이브는 무인 실행이라 메모리에서만 바뀐다.
    /// </summary>
    private void PrepareStoreShot()
    {
        if (!IsStoreShotRun())
        {
            return;
        }

        if (_economy is not MockEconomyService economy || _inventoryService is not MockInventoryService inventory)
        {
            GD.PrintErr("[store-shot] 목 경제가 아니다 - --real-economy 와 같이 쓰지 말 것");
            return;
        }

        foreach (string id in StoreShotOwned)
        {
            inventory.SeedOwned(id);
        }

        // 강화: 황금 2단계(10%), 가지 2단계(5송이), 빨리 익기 1단계. 사고 남는 잔액이 1,284.
        economy.GrantBananas(1_284 + 50 + 150 + 60 + 200 + 40);
        foreach (UpgradeAxis axis in new[] { UpgradeAxis.Golden, UpgradeAxis.Golden, UpgradeAxis.Slots, UpgradeAxis.Slots, UpgradeAxis.Cycle })
        {
            economy.PurchaseUpgrade(axis);
        }

        // 송이 5개: 황금(익음) · 보통(익음) · 황금(익음) · 자라는 중 · 막 달림.
        // 수확은 앞에서부터 하므로 0 번이 수확 장면에서 떨어지는 황금 송이다.
        economy.DebugSetSlot(0, 1.0, golden: true);
        economy.DebugSetSlot(1, 1.0, golden: false);
        economy.DebugSetSlot(2, 1.0, golden: true);
        economy.DebugSetSlot(3, 0.6, golden: false);
        economy.DebugSetSlot(4, 0.25, golden: false);

        SaveData data = _save.Data;
        data.TotalKeystrokes = 48_213;
        data.OnboardingSeen = int.MaxValue;   // 처음 안내(B15)가 장면을 덮지 않게 - 안내는 따로 찍는다
        data.Inventory.Equipped.Monkey = "monkey_05";
        data.Inventory.Equipped.Banana = "banana_03";
        data.Inventory.Equipped.Deco = "spark_01";

        GD.Print($"[store-shot] 상태 준비 - 잔액 {economy.Balance}, 슬롯 {economy.Slots.Count}개, 보유 {StoreShotOwned.Length}종");
    }

    private async void StartStoreShot()
    {
        string dir = StoreShotDir();
        if (dir == null)
        {
            return;
        }

        try
        {
            await RunStoreShot(dir);
            GD.Print($"[store-shot] 끝 - {dir}");
            GetTree().Quit(0);
        }
        catch (Exception e)
        {
            GD.PrintErr($"[store-shot] 실패 - {e}");
            GetTree().Quit(1);
        }
    }

    private async Task RunStoreShot(string dir)
    {
        Directory.CreateDirectory(dir);
        string manifest = Path.Combine(dir, "manifest.tsv");
        File.WriteAllText(manifest, "shot\twindow\tx\ty\tw\th\n");

        // 창을 매 프레임 그리게 한다 - 저전력 모드에서는 바뀐 게 없으면 프레임이 안 와서 캡처가 멈춘다.
        OS.LowProcessorUsageMode = false;
        Engine.MaxFps = 60;
        SetScale(StoreShotScale);
        SetOpacity(1.0f);
        _hud.Visible = false;   // 디버그 빌드는 계측 HUD 가 켜진 채로 뜬다

        // 기동 동기화(목이라 즉시)와 커서 장식 텍스처 로드를 기다린다.
        await Seconds(2.0);
        await Capture(dir, manifest, "idle");

        // 1) 펀치 - 빈 줄기를 치는 게 아니라 익은 송이를 치는 중. 팔이 닿는 순간 앞뒤로 여러 장.
        _input.DebugInject(1);
        for (int k = 0; k < 6; k++)
        {
            await Seconds(0.05);
            await Capture(dir, manifest, $"punch_{k}");
        }

        // 2) 수확 - 송이는 10번 맞아야 떨어진다(위에서 1번 쳤다). 8번 더 치고 마지막 한 번 뒤 낙하를 찍는다.
        for (int i = 0; i < 8; i++)
        {
            await Seconds(0.25);
            _input.DebugInject(1);
        }

        await Seconds(0.6);
        _input.DebugInject(1);
        for (int k = 0; k < 6; k++)
        {
            await Seconds(0.08);
            await Capture(dir, manifest, $"harvest_{k}");
        }

        await Seconds(1.5);

        // 3) 상점 - 탭마다.
        PressKey(Key.B);
        await Seconds(0.5);
        TabContainer tabs = FindGameNode<TabContainer>();
        int tabCount = tabs?.GetTabCount() ?? 0;
        for (int t = 0; t < tabCount; t++)
        {
            tabs.CurrentTab = t;
            await Seconds(0.3);
            await Capture(dir, manifest, $"shop_tab{t}");
        }

        PressKey(Key.B);   // 닫기 - Esc 는 셸 디버그 키에서 종료다
        await Seconds(0.3);

        // 4) 로비 - 가짜 친구 3명이 계속 치고 딴다. 로비 타수는 랭킹이 보이게 벌려 둔다.
        if (_net is MockNetSession net)
        {
            net.SimulatePeerActivity = true;
            await net.CreateRoom();
            string[] names = { "바나나킹", "타자왕", "고릴라" };
            long[] lobbyKeys = { 1_842, 1_310, 655 };
            for (int i = 0; i < names.Length; i++)
            {
                var peer = new PeerId(9001 + (ulong)i);
                net.SimulateJoin(peer, names[i]);
                net.SimulateKeystrokes(peer, lobbyKeys[i]);
            }

            net.AddKeystrokes(1_527);
            await Seconds(3.0);

            for (int k = 0; k < 4; k++)
            {
                _input.DebugInject(1);
                await Seconds(0.1);
                await Capture(dir, manifest, $"lobby_{k}");
                await Seconds(0.6);
            }

            PressKey(Key.M);
            await Seconds(0.6);
            await Capture(dir, manifest, "lobby_window");
            PressKey(Key.M);
        }

        // 5) 처음 안내(B15) 세 장. 게임 타입을 모르므로 노드 이름으로 찾아 열고 [다음] 버튼을 누른다.
        Node onboarding = GetTree().GetFirstNodeInGroup(SceneGroups.GameRoot)?.GetNodeOrNull("OnboardingWindow");
        if (onboarding != null)
        {
            await Seconds(0.3);
            onboarding.Call("Open");
            for (int page = 0; page < 3; page++)
            {
                await Seconds(0.4);
                await Capture(dir, manifest, $"onboarding_{page}");
                foreach (Node node in onboarding.FindChildren("*", "Button", true, false))
                {
                    if (node is Button { Text: "다음" } next)
                    {
                        next.EmitSignal(BaseButton.SignalName.Pressed);
                        break;
                    }
                }
            }
        }
    }

    /// <summary>보이는 창 전부(메인·커서·친구)를 PNG 로 저장하고 화면 위치를 목록에 적는다.</summary>
    private async Task Capture(string dir, string manifest, string shot)
    {
        await ToSignal(RenderingServer.Singleton, "frame_post_draw");

        var windows = new List<Window> { GetTree().Root };
        foreach (Node node in GetTree().Root.FindChildren("*", "Window", true, false))
        {
            if (node is Window w)
            {
                windows.Add(w);
            }
        }

        var lines = new List<string>();
        foreach (Window w in windows)
        {
            if (!w.Visible)
            {
                continue;
            }

            Image image = w.GetTexture().GetImage();
            string file = $"{shot}__{w.Name}.png";
            image.SavePng(Path.Combine(dir, file));
            lines.Add($"{shot}\t{w.Name}\t{w.Position.X}\t{w.Position.Y}\t{image.GetWidth()}\t{image.GetHeight()}");
        }

        File.AppendAllLines(manifest, lines);
    }

    private async Task Seconds(double s) =>
        await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);

    /// <summary>게임 레이어의 디버그 키를 누른 것처럼 흘린다 (상점 B, 로비 M - 다시 누르면 닫힌다).</summary>
    private static void PressKey(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = false });
    }

    /// <summary>게임 씬 안에서 타입으로 첫 노드를 찾는다 - 게임 타입을 모르는 채로 상점 탭을 넘기려고.</summary>
    private T FindGameNode<T>() where T : Node
    {
        Node root = GetTree().GetFirstNodeInGroup(SceneGroups.GameRoot);
        foreach (Node node in root?.FindChildren("*", typeof(T).Name, true, false) ?? new Godot.Collections.Array<Node>())
        {
            if (node is T found)
            {
                return found;
            }
        }

        return null;
    }
}
