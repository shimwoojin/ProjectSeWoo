<#
.SYNOPSIS
    A2 커서 추종 창 — 이동 주기 vs CPU 스윕 (기획확정-일감분배-260907.md §1.3-1)

.DESCRIPTION
    커서를 따라가는 비용은 좌표를 읽는 데서 나오지 않는다. MouseGetPosition 은 싸다.
    비용은 **창을 실제로 옮기는 OS 호출**에서 나온다. 그래서 이 스윕의 독립변수는
    폴링 주기이고, 실제로 보는 값은 그 결과로 발생한 moves 수와 CPU 다.

    합성 경로(--cursor-sim)를 쓴다. 사람이 마우스를 안 흔들면 창이 안 움직이고,
    창이 안 움직이면 이 기능의 부하가 0 으로 측정된다. 즉 손 놓고 재는 순간
    측정이 무의미해진다. 합성 경로는 커서가 한순간도 쉬지 않는 **최악 조건**이라
    실사용보다 비싼 값이 나오지만, 재현 가능하고 조건 간 비교가 성립한다.

.NOTES
    이 스윕이 답하지 못하는 것 (사람이 봐야 한다):
      - 고무줄 현상이 거슬리는가, 아니면 "매달린" 연출이 되는가
      - 멀티모니터 경계를 넘어갈 때
      - 전체화면 게임 / 브라우저 / 작업표시줄 위에서의 동작
      - 클릭 통과 체감 (드래그·선택·우클릭) -> tools/inspect-windows.ps1 로 1차 확인
#>
[CmdletBinding()]
param(
    [string]$Godot = "C:\Tools\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe",
    [string]$Exe,
    [int]$Seconds = 40,
    [int]$Warmup = 10,
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) { $OutDir = Join-Path $env:TEMP "projectsewoo-cursor" }
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

$useExport = [bool]$Exe
if ($useExport) {
    if (-not (Test-Path $Exe)) { throw "익스포트 바이너리를 찾지 못했다: $Exe" }
    $target = (Resolve-Path $Exe).Path
    $buildKind = 'ExportRelease'
} else {
    if (-not (Test-Path $Godot)) { throw "Godot 을 찾지 못했다: $Godot" }
    $target = $Godot
    $buildKind = 'Debug'
}

# 기준선(커서 off)이 첫 줄이어야 "커서 창이 얼마를 더 먹는가"를 읽을 수 있다.
$conds = @(
    [pscustomobject]@{ N=1; Label='cursor off (기준선)'; On=$false; Interval=0;   Mode='direct' }
    [pscustomobject]@{ N=2; Label='direct / 매 프레임';  On=$true;  Interval=0;   Mode='direct' }
    [pscustomobject]@{ N=3; Label='direct / 16ms';       On=$true;  Interval=16;  Mode='direct' }
    [pscustomobject]@{ N=4; Label='direct / 33ms';       On=$true;  Interval=33;  Mode='direct' }
    [pscustomobject]@{ N=5; Label='direct / 50ms';       On=$true;  Interval=50;  Mode='direct' }
    [pscustomobject]@{ N=6; Label='direct / 100ms';      On=$true;  Interval=100; Mode='direct' }
    [pscustomobject]@{ N=7; Label='spring / 16ms';       On=$true;  Interval=16;  Mode='spring' }
    [pscustomobject]@{ N=8; Label='lazy / 33ms (폴백안)'; On=$true; Interval=33;  Mode='lazy'   }
)

Write-Host ""
Write-Host "커서 추종 스윕 — $($conds.Count)개 x $($Seconds)초 = 약 $([Math]::Ceiling($conds.Count * ($Seconds + 8) / 60))분" -ForegroundColor Cyan
Write-Host "대상: $target  ($buildKind)"
Write-Host "결과 폴더: $OutDir"
Write-Host ""

$rows = @()

foreach ($c in $conds) {
    $report = Join-Path $OutDir ("c{0}.txt" -f $c.N)
    if (Test-Path $report) { Remove-Item $report -Force }

    Write-Host ("[{0}/{1}] {2} ... " -f $c.N, $conds.Count, $c.Label) -ForegroundColor Cyan -NoNewline

    $argList = @()
    if (-not $useExport) { $argList += @('--path', $repo) }
    $argList += @('--', "--report=$report", "--seconds=$Seconds", "--warmup=$Warmup")
    if ($c.On) {
        $argList += @('--cursor', '--cursor-sim', "--cursor-interval=$($c.Interval)", "--cursor-mode=$($c.Mode)")
    }

    $proc = Start-Process -FilePath $target -ArgumentList $argList -PassThru
    try {
        Wait-Process -Id $proc.Id -Timeout ($Seconds + 45) -ErrorAction Stop
    } catch {
        Write-Host "타임아웃 — 강제 종료" -ForegroundColor Red
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    }

    if (-not (Test-Path $report)) {
        Write-Host "실패 (리포트 없음)" -ForegroundColor Red
        $rows += [pscustomobject]@{ N=$c.N; Label=$c.Label; Cpu='-'; Peak='-'; Priv='-'; Moves='-'; Skip='-'; Rate='-' }
        continue
    }

    $text = Get-Content $report -Raw
    $cpu='-'; $peak='-'; $priv='-'; $moves='-'; $skip='-'; $measured=[Math]::Max(1, $Seconds-$Warmup)

    if ($text -match 'cpu avg/peak\s+([\d.]+)% / ([\d.]+)%')            { $cpu=$Matches[1]; $peak=$Matches[2] }
    if ($text -match 'memory\s+(\d+) MB private commit')                 { $priv=$Matches[1] }
    if ($text -match 'moves (\d+), skip (\d+)')                          { $moves=$Matches[1]; $skip=$Matches[2] }
    if ($text -match 'uptime\s+(\d+)s')                                  { $measured=[Math]::Max(1,[int]$Matches[1]) }

    # 초당 창 이동 횟수. 이게 부하의 실제 원인이고, 주기 설정은 이걸 조절하는 손잡이일 뿐이다.
    $rate = if ($moves -match '^\d+$') { [Math]::Round([int]$moves / $measured, 1) } else { '-' }

    Write-Host ("cpu {0}% / peak {1}% / priv {2}MB / {3} moves ({4}/s)" -f $cpu,$peak,$priv,$moves,$rate) -ForegroundColor Green
    $rows += [pscustomobject]@{ N=$c.N; Label=$c.Label; Cpu=$cpu; Peak=$peak; Priv=$priv; Moves=$moves; Skip=$skip; Rate=$rate }
}

$base = $rows | Where-Object { $_.N -eq 1 }

$md = @()
$md += "| # | 조건 | cpu avg | cpu peak | ``priv`` | 창 이동 | 이동/초 | 기준선 대비 cpu |"
$md += "|---|---|---|---|---|---|---|---|"
foreach ($r in $rows) {
    $delta = '-'
    if ($base -and $r.Cpu -match '^[\d.]+$' -and $base.Cpu -match '^[\d.]+$') {
        $d = [double]$r.Cpu - [double]$base.Cpu
        $delta = if ($r.N -eq 1) { '기준선' } else { "{0:+0.00;-0.00;0.00}%p" -f $d }
    }
    $md += "| $($r.N) | $($r.Label) | $($r.Cpu)% | $($r.Peak)% | $($r.Priv)MB | $($r.Moves) | $($r.Rate) | $delta |"
}
$md += ""
$md += "합성 경로(``--cursor-sim``) = 커서가 한순간도 안 쉬는 최악 조건. 실사용은 이보다 싸다."
$md += "조건당 $($Seconds)초(워밍 $($Warmup)초 제외), ``$buildKind`` 빌드."

$table = $md -join "`r`n"
$tablePath = Join-Path $OutDir "cursor-sweep.md"
$table | Out-File -FilePath $tablePath -Encoding utf8

Write-Host ""
Write-Host $table
Write-Host ""
Write-Host "표: $tablePath" -ForegroundColor Cyan
