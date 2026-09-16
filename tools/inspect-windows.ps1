<#
.SYNOPSIS
    실행 중인 프로세스의 최상위 창을 OS 에 직접 물어본다 — A2 커서 추종 창 검증용.

.DESCRIPTION
    Godot 이 "OS 창 만들었다"고 말하는 것과 실제로 만들어진 것은 다른 문제다.
    특히 클릭 통과(WS_EX_TRANSPARENT)는 안 걸려도 화면상으로는 멀쩡해 보이고,
    유저가 장식을 클릭했을 때 비로소 드러난다. 상주 앱에서 가장 치명적인
    실패 모드가 조용히 통과하는 것이라 API 레벨에서 확인한다.

    보는 것:
      LAYERED    per-pixel 투명이 걸렸는가
      TRANSPARENT 클릭이 통과하는가  <- 커서 창에 반드시 있어야 한다
      TOPMOST    항상 위인가
      NOACTIVATE 포커스를 뺏지 않는가 <- 타이핑 중에 포커스를 뺏으면 게임이 아니라 사고다

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools/inspect-windows.ps1 -Name PunchMonkey
#>
[CmdletBinding()]
param(
    [string]$Name = "PunchMonkey",
    [int]$ProcessId
)

$ErrorActionPreference = 'Stop'

if (-not ("Win32WindowProbe" -as [type])) {
    Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public class Win32WindowProbe {
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr64(IntPtr h, int i);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

    /// 지정한 화면 좌표에서 OS 가 실제로 어느 창을 집는지 물어본다.
    /// exstyle 을 읽는 것과 다르다 - 이게 유저가 클릭할 때 실제로 일어나는 일이다.
    public static string HitTest(int x, int y) {
        POINT p; p.X = x; p.Y = y;
        IntPtr h = WindowFromPoint(p);
        return "0x" + h.ToInt64().ToString("X");
    }

    public delegate bool EnumProc(IntPtr h, IntPtr p);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    public class Info {
        public string Handle, Class, Title, Rect, Styles;
        public bool Visible;
        public long ExStyle;
    }

    public static List<Info> ForProcess(uint want) {
        var list = new List<Info>();
        EnumWindows((h, p) => {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid != want) return true;

            var cls = new StringBuilder(256); GetClassNameW(h, cls, 256);
            var txt = new StringBuilder(256); GetWindowTextW(h, txt, 256);
            RECT r; GetWindowRect(h, out r);
            long ex = GetWindowLongPtr64(h, -20).ToInt64();

            var flags = new List<string>();
            if ((ex & 0x00080000) != 0) flags.Add("LAYERED");
            if ((ex & 0x00000020) != 0) flags.Add("TRANSPARENT");
            if ((ex & 0x00000008) != 0) flags.Add("TOPMOST");
            if ((ex & 0x08000000) != 0) flags.Add("NOACTIVATE");
            if ((ex & 0x00000080) != 0) flags.Add("TOOLWINDOW");

            list.Add(new Info {
                Handle  = "0x" + h.ToInt64().ToString("X"),
                Class   = cls.ToString(),
                Title   = txt.ToString(),
                Visible = IsWindowVisible(h),
                Rect    = r.Left + "," + r.Top + " " + (r.Right - r.Left) + "x" + (r.Bottom - r.Top),
                ExStyle = ex,
                Styles  = flags.Count > 0 ? string.Join("|", flags) : "-"
            });
            return true;
        }, IntPtr.Zero);
        return list;
    }
}
"@
}

if (-not $ProcessId) {
    $procs = @(Get-Process -Name $Name -ErrorAction SilentlyContinue)
    if ($procs.Count -eq 0) { throw "프로세스를 찾지 못했다: $Name" }
    if ($procs.Count -gt 1) { Write-Warning "$Name 프로세스가 $($procs.Count)개다. 첫 번째를 쓴다." }
    $ProcessId = $procs[0].Id
}

$wins = [Win32WindowProbe]::ForProcess([uint32]$ProcessId)

# Godot 의 *_console.exe 는 게임을 자식 프로세스로 띄우는 래퍼다. 래퍼 PID 를
# 그대로 검사하면 창이 하나도 안 나오거나 엉뚱한 창이 나온다. 실제로 한 번 밟았다.
if ($wins.Count -le 1) {
    $kids = @(Get-CimInstance Win32_Process -Filter "ParentProcessId=$ProcessId" -ErrorAction SilentlyContinue)
    foreach ($k in $kids) {
        $kw = [Win32WindowProbe]::ForProcess([uint32]$k.ProcessId)
        if ($kw.Count -gt $wins.Count) {
            Write-Host "PID $ProcessId 는 래퍼였다. 자식 PID $($k.ProcessId) ($($k.Name)) 로 내려간다." -ForegroundColor Yellow
            $ProcessId = [int]$k.ProcessId
            $wins = $kw
        }
    }
}

Write-Host ""
Write-Host "PID $ProcessId — 최상위 창 $($wins.Count)개" -ForegroundColor Cyan
$wins | Format-Table Handle, Class, Title, Visible, Rect, Styles -AutoSize

# --- 히트테스트: 각 창의 중심을 OS 에 물어본다 -------------------------------
#
# exstyle 만 보고 판정하면 안 된다. Godot 은 WS_EX_TRANSPARENT 대신
# WM_NCHITTEST 에서 HTTRANSPARENT 를 돌려주는 방식으로도 클릭 통과를 구현할 수
# 있고, 그 경우 스타일에는 아무것도 안 나타난다. 실제로 이 프로젝트에서
# 스타일만 보고 "클릭 통과 실패"로 오판할 뻔했다.

Write-Host "히트테스트 (창 중심 좌표에서 OS 가 집는 창)" -ForegroundColor Cyan
$rows = @()
foreach ($w in ($wins | Where-Object { $_.Visible -and $_.Rect -notmatch '^0,0 0x0$' })) {
    if ($w.Rect -match '^(-?\d+),(-?\d+) (\d+)x(\d+)$') {
        $cx = [int]$Matches[1] + [int]$Matches[3] / 2
        $cy = [int]$Matches[2] + [int]$Matches[4] / 2
        $hit = [Win32WindowProbe]::HitTest([int]$cx, [int]$cy)
        $rows += [pscustomobject]@{
            Window  = $w.Handle
            Size    = "$($Matches[3])x$($Matches[4])"
            Center  = "$([int]$cx),$([int]$cy)"
            HitsBack = $hit
            Verdict = if ($hit -eq $w.Handle) { "이 창이 클릭을 먹는다" } else { "통과 (뒤 창이 잡힘)" }
        }
    }
}
$rows | Format-Table -AutoSize

Write-Host "읽는 법" -ForegroundColor Yellow
Write-Host "  - 나무/마스코트 창(420x560)은 '이 창이 클릭을 먹는다'가 정상이다."
Write-Host "    passthrough 폴리곤이 마스코트 영역만 활성이라, 중심이 그 영역이면 잡히는 게 맞다."
Write-Host "  - 커서 장식 창(128x128)은 반드시 '통과' 여야 한다. 여기가 유저 클릭을 먹으면"
Write-Host "    마우스가 가는 곳마다 클릭이 죽는다. 상주 앱에서 가장 치명적인 실패 모드다."
Write-Host ""
Write-Host "LAYERED 가 없어도 투명 실패가 아니다." -ForegroundColor Yellow
Write-Host "  Godot 은 Windows 에서 WS_EX_LAYERED 가 아니라 DWM 합성으로 per-pixel 투명을"
Write-Host "  구현한다. 이 프로젝트의 셸 창도 LAYERED 없이 투명이 정상 동작한다."
Write-Host ""
Write-Host "최종 판정은 손으로 한다 - 장식 위에서 드래그/선택/우클릭이 뒤 창에 먹히는지." -ForegroundColor Yellow
