<#
.SYNOPSIS
    빌드 머신에 steamcmd 를 설치하고 최초 로그인까지 끝낸다 (A12).

.DESCRIPTION
    Depot 업로드는 steamcmd 가 자격 증명을 **머신마다 한 번** 캐시해야 비대화형으로
    돈다. 그 한 번을 사람이 손으로 하다가 2026-09-16 에 세 번 막혔는데,
    **세 번 다 "멈춘 것처럼" 보였고 원인이 전부 달랐다.** 이 스크립트는 그 셋을
    미리 피하도록 짜여 있다 (docs/A12-BUILD.md §5-5).

      1. TTY 없는 셸에서 돌리면 password: 가 빈 입력을 받고 죽는다  -> §1 에서 거른다
      2. persona name(표시 이름)을 로그인 ID 로 넣으면 "비밀번호가
         틀렸다" 로 나온다 - 원인이 아이디인데 에러는 비밀번호를 가리킨다 -> §3 에서 경고
      3. +quit 을 빼면 로그인 성공 후 Steam> 프롬프트에서 대기한다 -> 항상 붙인다

    끝나면 tools\build-release.ps1 로 뽑은 빌드를 바로 올릴 수 있다.

.PARAMETER Account
    스팀 **로그인 ID**. 생략하면 물어본다.

.PARAMETER InstallDir
    steamcmd 설치 위치. 기본 C:\Tools\steamcmd (Godot 과 같은 자리).

.PARAMETER SkipLogin
    설치만 하고 로그인은 나중에.

.NOTES
    더블클릭으로 쓰려면 옆의 setup-steamcmd.cmd 를 쓴다 — .ps1 은 더블클릭해도
    실행되지 않고 편집기로 열리거나 실행 정책에 막힌다.
#>
[CmdletBinding()]
param(
    [string]$Account,
    [string]$InstallDir = "C:\Tools\steamcmd",
    [switch]$SkipLogin
)

$ErrorActionPreference = 'Stop'

function Step($n, $t) { Write-Host "`n[$n] $t" -ForegroundColor Cyan }
function Ok($t)        { Write-Host "      $t" -ForegroundColor Green }
function Warn($t)      { Write-Host "      $t" -ForegroundColor Yellow }
function Info($t)      { Write-Host "      $t" -ForegroundColor Gray }

Write-Host ""
Write-Host "PunchMonkey 빌드 머신 준비 — steamcmd" -ForegroundColor White

# ------------------------------------------------- 1. 진짜 터미널인지 먼저 본다
Step 1 "실행 환경 확인"

# 에이전트 셸·파이프·리다이렉션 아래에서는 steamcmd 가 비밀번호를 못 받는다.
# 프롬프트가 빈 입력을 받고 ERROR (Invalid Password) 로 즉사하거나, 그 전에
# 오지 않는 stdin 을 기다리며 멈춘 것처럼 보인다. 설치까지 다 해놓고 마지막에
# 알게 되면 원인을 엉뚱한 데서 찾게 되므로 **맨 앞에서 거른다.**
$noTty = [Console]::IsInputRedirected
if ($noTty -and -not $SkipLogin) {
    Write-Host ""
    Write-Host "  입력이 리다이렉트된 환경이다 (에이전트 셸/파이프)." -ForegroundColor Red
    Write-Host "  steamcmd 로그인은 비밀번호와 Steam Guard 코드를 직접 타이핑해야 한다." -ForegroundColor Red
    Write-Host ""
    Write-Host "  PowerShell 창을 직접 열어서 다시 실행하거나, 옆의" -ForegroundColor Yellow
    Write-Host "  setup-steamcmd.cmd 를 더블클릭해라." -ForegroundColor Yellow
    Write-Host "  설치만 먼저 하려면 -SkipLogin 을 준다." -ForegroundColor Yellow
    Write-Host ""
    exit 1
}
if ($noTty) { Warn "비대화형 (입력 리다이렉트됨) - -SkipLogin 이라 계속한다" }
else        { Ok "대화형 콘솔" }

# -------------------------------------------------------------- 2. steamcmd 설치
Step 2 "steamcmd"

$exe = Join-Path $InstallDir "steamcmd.exe"
if (Test-Path $exe) {
    Ok "이미 있다: $exe"
} else {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    $zip = Join-Path $env:TEMP "steamcmd.zip"
    Info "내려받는 중..."
    Invoke-WebRequest -Uri "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip" `
        -OutFile $zip -UseBasicParsing -TimeoutSec 180
    Expand-Archive -Path $zip -DestinationPath $InstallDir -Force
    Remove-Item $zip -Force -EA SilentlyContinue
    if (-not (Test-Path $exe)) { throw "압축을 풀었는데 steamcmd.exe 가 없다: $InstallDir" }
    Ok "설치: $exe"
}

# 첫 실행은 자체 업데이트를 하고 **종료 코드 7 로 끝난다.** 오류가 아니라
# "업데이트 후 재실행" 이라는 뜻이고, 두 번째 실행부터 0 이다.
Info "부트스트랩 (첫 실행은 자체 업데이트로 몇십 초 걸린다)..."
& $exe +quit | Out-Null
& $exe +quit | Out-Null
Ok "동작 확인"

# --------------------------------------------- 3. 빌드도 뽑으려면 두 개가 더 필요
Step 3 "빌드 머신 준비 상태 (확인만 한다)"

# 업로드만 할 거면 여기 둘은 없어도 된다. 다만 **없다는 걸 금요일 데모 직전에
# 알게 되면 그때는 설치할 시간이 없다** - 기가 단위 다운로드다.
$tplOk = @(Get-ChildItem (Join-Path $env:APPDATA "Godot\export_templates") -Directory -EA SilentlyContinue |
    Where-Object { Test-Path (Join-Path $_.FullName "windows_release_x86_64.exe") }).Count -gt 0
if ($tplOk) { Ok "Godot export 템플릿 있음" }
else { Warn "Godot export 템플릿 없음 - 에디터 > 편집 > 내보내기 템플릿 관리 에서 설치 (엔진과 같은 버전)" }

$vswhere = "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe"
$msvcOk = $false
if (Test-Path $vswhere) {
    $found = & $vswhere -latest -products * `
        -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath 2>$null
    $msvcOk = -not [string]::IsNullOrWhiteSpace($found)
}
if ($msvcOk) { Ok "MSVC C++ 빌드 도구 있음" }
else { Warn "MSVC C++ 빌드 도구 없음 - InputHelper 를 NativeAOT 로 못 뽑는다. Visual Studio Installer > 'C++를 사용한 데스크톱 개발'" }

if (-not ($tplOk -and $msvcOk)) {
    Info "둘 다 없어도 업로드는 된다. 빌드를 직접 뽑으려면 필요하다 (docs/A12-BUILD.md §2)."
}

if ($SkipLogin) {
    Write-Host ""
    Write-Host "설치·점검만 했다. 로그인은 이 스크립트를 -SkipLogin 없이 다시 돌려라." -ForegroundColor Cyan
    exit 0
}

# ------------------------------------------------------------------ 4. 로그인
Step 4 "최초 로그인 (이 머신에서 한 번만)"

$cfg = Join-Path $InstallDir "config\config.vdf"
$cfgText = Get-Content $cfg -Raw -EA SilentlyContinue

if (-not $Account) {
    Write-Host ""
    Write-Host "  스팀 **로그인 ID** 를 넣어라 (표시 이름이 아니다)." -ForegroundColor Yellow
    Write-Host "  둘이 다른 경우가 많고, 표시 이름을 넣으면 스팀이" -ForegroundColor Yellow
    Write-Host "  'Invalid Password' 로 답한다 - 원인이 아이디인데 에러는" -ForegroundColor Yellow
    Write-Host "  비밀번호를 가리켜서 한참 헤매게 된다." -ForegroundColor Yellow
    Write-Host ""
    $Account = Read-Host "  로그인 ID"
}
if ([string]::IsNullOrWhiteSpace($Account)) { throw "로그인 ID 가 비었다." }

if ($cfgText -and $cfgText -match [regex]::Escape($Account)) {
    Ok "'$Account' 자격 증명이 이미 캐시돼 있다 - 건너뛴다"
} else {
    Write-Host ""
    Info "아래 프롬프트에 비밀번호 -> Steam Guard 코드 순으로 입력한다."
    Info "비밀번호는 화면에 아무것도 안 찍힌다(별표도 없다). 멈춘 게 아니다."
    Write-Host ""

    # +quit 을 반드시 붙인다. 없으면 로그인을 마치고도 Steam> 프롬프트에서
    # 계속 기다려서, 로그인 단계에서 멈춘 것처럼 보인다.
    & $exe +login $Account +quit

    # 종료 코드로 판정하지 않는다 - steamcmd 는 자체 업데이트 등으로 0 이 아닌
    # 값을 자주 돌려준다. **상태(자격 증명이 캐시됐는가)를 본다.**
    $cfgText = Get-Content $cfg -Raw -EA SilentlyContinue
    if ($cfgText -and $cfgText -match [regex]::Escape($Account)) {
        Ok "로그인 성공 - '$Account' 캐시됨"
    } else {
        Write-Host ""
        Write-Host "  자격 증명이 캐시되지 않았다. 로그인이 끝나지 않은 것이다." -ForegroundColor Red
        Write-Host "  - 로그인 ID 가 맞는지 (표시 이름 아님)" -ForegroundColor Yellow
        Write-Host "  - Steam Guard 코드가 만료되지 않았는지" -ForegroundColor Yellow
        Write-Host "  확인하고 다시 실행해라." -ForegroundColor Yellow
        exit 1
    }
}

# ------------------------------------------------------------------------ 안내
Write-Host ""
Write-Host "준비 끝. 앞으로는 이 두 줄이다:" -ForegroundColor Green
Write-Host ""
Write-Host "  powershell -ExecutionPolicy Bypass -File tools\build-release.ps1"
Write-Host "  $exe +login $Account +run_app_build <repo>\build\steampipe\app_build_5281130.vdf +quit"
Write-Host ""
Write-Host "업로드해도 아무에게도 안 보인다 - 브랜치 지정은 파트너 사이트에서" -ForegroundColor Yellow
Write-Host "사람이 직접 한다 (docs/A12-BUILD.md §5-3)." -ForegroundColor Yellow
Write-Host ""
