<#
.SYNOPSIS
    렌더러 A/B 무인 측정 — docs/DAY1-2-SPIKE.md §4 "메모리 초과 시 렌더러 A/B"

.DESCRIPTION
    forward_plus/d3d12 기준선이 메모리 150MB를 넘겼을 때, 어느 렌더링 경로가
    상주 앱으로 쓸 만한지 고르기 위한 스윕이다.

    project.godot을 네 번 고치고 네 번 재시작하는 대신, Godot의
    --rendering-method / --rendering-driver 커맨드라인 오버라이드를 쓴다.
    프로젝트 파일이 전혀 바뀌지 않으므로 측정 중에 작업 트리가 더러워지지 않고,
    "어느 행이 무엇이었는지" 헷갈릴 여지도 없다.

    각 조건마다 게임을 standalone으로 띄우고(=디버거 없음), 워밍 구간이 지난 뒤
    측정하고, 리포트를 파일로 뱉고 스스로 종료한다.

.PARAMETER Seconds
    조건당 총 실행 시간(워밍 포함). 메모리는 30~60초에 안정되므로 기본 60초면 된다.
    CPU 유휴 판정을 이 스크립트로 대신하려는 게 아니다 — 그건 §3-1의 10분 측정이다.

.NOTES
    ★ 투명이 깨지는지는 자동으로 알 수 없다. ★
    특히 gl_compatibility는 렌더링 경로가 통째로 바뀌므로 per-pixel 투명이
    그대로 되는지 반드시 눈으로 봐야 한다. 메모리가 절반이 되어도 투명이 깨지면
    쓸 수 없다. 그래서 이 스크립트는 창을 계속 띄워두고, 조건마다 무엇을 볼지
    먼저 알린다. 결과표의 "투명" 열은 사람이 채운다.
#>
[CmdletBinding()]
param(
    [string]$Godot = "C:\Tools\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe",

    # 익스포트한 릴리스 바이너리를 잴 때 쓴다. 지정하면 -Godot 대신 이걸 띄우고
    # --path 를 붙이지 않는다(익스포트 바이너리는 pck 를 자기가 들고 있다).
    # Debug 와 Release 의 차이를 보는 것이 이 인자의 목적이다.
    [string]$Exe,

    [int]$Seconds = 60,
    [int]$Warmup = 15,
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) { $OutDir = Join-Path $env:TEMP "projectsewoo-measure" }
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

$useExport = [bool]$Exe
if ($useExport) {
    if (-not (Test-Path $Exe)) { throw "익스포트 바이너리를 찾지 못했다: $Exe" }
    $target = (Resolve-Path $Exe).Path
    $buildKind = 'ExportRelease'
} else {
    if (-not (Test-Path $Godot)) {
        throw "Godot .NET(mono) 빌드를 찾지 못했다: $Godot`n-Godot 인자로 경로를 넘겨라."
    }
    $target = $Godot
    $buildKind = 'Debug'
}

# 에디터가 떠 있으면 프로세스가 둘이 되어 어느 쪽이 게임인지 구분이 안 된다.
$procName = [System.IO.Path]::GetFileNameWithoutExtension($target)
$editors = @(Get-Process -Name "Godot*" -ErrorAction SilentlyContinue |
    Where-Object { $_.ProcessName -ne $procName })
if ($editors.Count -gt 0) {
    Write-Warning "Godot 프로세스가 이미 $($editors.Count)개 떠 있다. 에디터를 닫고 다시 돌려라."
    foreach ($e in $editors) { Write-Warning "  PID $($e.Id)  $($e.ProcessName)" }
}

$combos = @(
    [pscustomobject]@{ N = 1; Method = 'forward_plus';     Driver = 'd3d12';   Note = '현재 = 기준선' }
    [pscustomobject]@{ N = 2; Method = 'forward_plus';     Driver = 'vulkan';  Note = '드라이버만 교체' }
    [pscustomobject]@{ N = 3; Method = 'mobile';           Driver = 'vulkan';  Note = '' }
    [pscustomobject]@{ N = 4; Method = 'gl_compatibility'; Driver = 'opengl3'; Note = '가장 가벼울 후보' }
)

Write-Host ""
Write-Host "렌더러 A/B — 조건 $($combos.Count)개 x $($Seconds)초 = 약 $([Math]::Ceiling($combos.Count * ($Seconds + 8) / 60))분" -ForegroundColor Cyan
Write-Host "대상: $target  ($buildKind)"
Write-Host "결과 폴더: $OutDir"
Write-Host ""
Write-Host "★ 각 조건이 도는 동안 창을 보고 있어라. 볼 것은 딱 하나 —" -ForegroundColor Yellow
Write-Host "  마스코트 주변의 투명해야 할 영역이 불투명한 사각형으로 보이는가." -ForegroundColor Yellow
Write-Host ""

$rows = @()

foreach ($c in $combos) {
    $label = "$($c.Method)/$($c.Driver)"
    $report = Join-Path $OutDir ("r{0}-{1}-{2}.txt" -f $c.N, $c.Method, $c.Driver)
    if (Test-Path $report) { Remove-Item $report -Force }

    Write-Host ("[{0}/{1}] {2} ... " -f $c.N, $combos.Count, $label) -ForegroundColor Cyan -NoNewline

    $argList = @()
    if (-not $useExport) { $argList += @('--path', $repo) }
    $argList += @(
        '--rendering-method', $c.Method,
        '--rendering-driver', $c.Driver,
        '--', "--report=$report", "--seconds=$Seconds", "--warmup=$Warmup"
    )

    $proc = Start-Process -FilePath $target -ArgumentList $argList -PassThru

    # 종료 직전에 OS 카운터를 한 번 찍어 HUD 값과 교차 검증한다.
    # priv 가 서로 크게 다르면 PerfProbe 쪽을 의심해야 한다.
    $osPriv = $null
    $osPrivWs = $null
    Start-Sleep -Seconds ([Math]::Max(1, $Seconds - 5))
    if (-not $proc.HasExited) {
        $perf = Get-CimInstance Win32_PerfFormattedData_PerfProc_Process `
            -Filter "IDProcess=$($proc.Id)" -ErrorAction SilentlyContinue
        if ($perf) {
            $osPriv = [int]($perf.PrivateBytes / 1MB)
            $osPrivWs = [int]($perf.WorkingSetPrivate / 1MB)
        }
    }

    try {
        Wait-Process -Id $proc.Id -Timeout ($Seconds + 45) -ErrorAction Stop
    } catch {
        Write-Host "타임아웃 — 강제 종료" -ForegroundColor Red
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    }

    if (-not (Test-Path $report)) {
        Write-Host "실패 (리포트 없음 — 렌더러 초기화 실패로 보인다)" -ForegroundColor Red
        $rows += [pscustomobject]@{
            N = $c.N; Cond = $label; Priv = '-'; Ws = '-'; CpuAvg = '-'
            OsPriv = '-'; OsPrivWs = '-'; Actual = '기동 실패'; Note = $c.Note
        }
        continue
    }

    $text = Get-Content $report -Raw

    $priv = '-'; $ws = '-'; $cpu = '-'; $actual = '-'
    if ($text -match 'memory\s+(\d+) MB private commit / (\d+) MB working set') {
        $priv = $Matches[1]; $ws = $Matches[2]
    }
    if ($text -match 'cpu avg/peak\s+([\d.]+)%') { $cpu = $Matches[1] }
    if ($text -match 'renderer\s+(\S+) / (\S+)')  { $actual = "$($Matches[1])/$($Matches[2])" }

    # Godot 은 요청한 렌더러를 못 쓰면 조용히 폴백한다. 이걸 놓치면
    # 전혀 다른 조건의 숫자를 비교하게 된다.
    $fellBack = ($actual -ne '-') -and ($actual -ne $label)
    $color = if ($fellBack) { 'Yellow' } else { 'Green' }
    $suffix = if ($fellBack) { "  ← 요청은 $label, 실제는 $actual (폴백)" } else { "" }
    Write-Host ("priv {0}MB / ws {1}MB / cpu {2}%{3}" -f $priv, $ws, $cpu, $suffix) -ForegroundColor $color

    $rows += [pscustomobject]@{
        N = $c.N; Cond = $label; Priv = $priv; Ws = $ws; CpuAvg = $cpu
        OsPriv = $(if ($null -ne $osPriv) { "$osPriv" } else { '-' })
        OsPrivWs = $(if ($null -ne $osPrivWs) { "$osPrivWs" } else { '-' })
        Actual = $actual; Note = $c.Note
    }
}

$md = @()
$md += "| # | 요청 렌더러 | 실제 렌더러 | ``priv`` | ``ws`` | OS ``PrivCommit`` | OS ``PrivWS`` | cpu avg | 투명 정상? | 비고 |"
$md += "|---|---|---|---|---|---|---|---|---|---|"
foreach ($r in $rows) {
    $verdict = if ($r.Priv -match '^\d+$') {
        if ([int]$r.Priv -lt 150) { "**$($r.Priv)**" } else { $r.Priv }
    } else { $r.Priv }
    $md += "| $($r.N) | ``$($r.Cond)`` | ``$($r.Actual)`` | $verdict | $($r.Ws) | $($r.OsPriv) | $($r.OsPrivWs) | $($r.CpuAvg)% | (눈으로) | $($r.Note) |"
}
$md += ""
$md += "기준: ``priv`` < 150MB. ``ws`` 는 공유 DLL 페이지를 포함하므로 기준에 대지 않는다."
$md += "조건당 $($Seconds)초(워밍 $($Warmup)초 제외하고 $($Seconds - $Warmup)초 측정), ``$buildKind`` 빌드."

$table = $md -join "`r`n"
$tablePath = Join-Path $OutDir "renderer-ab.md"
$table | Out-File -FilePath $tablePath -Encoding utf8

Write-Host ""
Write-Host $table
Write-Host ""
Write-Host "표: $tablePath  (DAY1-2-SPIKE.md §4 에 붙여넣어라)" -ForegroundColor Cyan
Write-Host "'투명 정상?' 열은 자동으로 채울 수 없다. 직접 채워라." -ForegroundColor Yellow
