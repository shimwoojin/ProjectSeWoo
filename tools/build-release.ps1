<#
.SYNOPSIS
    A12 — 스팀 Depot 에 올릴 릴리스 빌드를 뽑는다.

.DESCRIPTION
    Godot 익스포트만으로는 **돌아가는 빌드가 안 나온다.** 우리 패키지는 세 조각이고
    그중 둘을 Godot 이 모른다:

      PunchMonkey.exe + .pck + data_.../   Godot 익스포트 산출물 (엔진 + .NET 런타임)
      steam_api64.dll                      네이티브. Godot 은 안 옮긴다 (A8 §6)
      InputHelper.exe                      별도 프로세스. Godot 은 존재조차 모른다 (A4)

    2026-09-16 에 손으로 익스포트했을 때 **두 개가 다 빠져 있었다.** dll 이 없으면
    스팀이 안 붙고, 헬퍼가 없으면 전역 타건을 못 받는다 — 즉 게임의 핵심 입력이
    죽는다. 그 순서를 스크립트로 굳힌 것이 이 파일이다.

    출력은 -OutDir 하나에 모인다. **그 폴더가 그대로 Depot 콘텐츠 루트다** —
    빌드 부산물이 섞이면 그대로 유저에게 배포되므로 매번 비우고 다시 채운다.

.PARAMETER DepotId
    build/steampipe/ 에 SteamPipe VDF 두 개를 생성한다. 기본값은 확인된 실제
    depot(5281131). 0 을 주면 VDF 를 만들지 않는다.

.PARAMETER SkipVerify
    §5 검증을 건너뛴다. 스팀이 안 떠 있는 환경(CI)에서만 쓴다.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\build-release.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\build-release.ps1 -DepotId 5281131

.NOTES
    전제: Godot .NET(mono) 빌드, export 템플릿(엔진과 같은 버전), .NET SDK,
    그리고 **MSVC C++ 빌드 도구** — InputHelper 를 NativeAOT 로 뽑기 때문이다.
    왜 AOT 여야 하는지는 §2 주석 참고.
#>
[CmdletBinding()]
param(
    [string]$Godot = $(if ($env:GODOT) { $env:GODOT } else {
        "C:\Tools\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe" }),

    [string]$Preset = "Windows Desktop",
    [string]$OutDir = "build\dist",

    # 2026-09-16 파트너 사이트에서 확인한 값. 앱 ID+1 이지만 그건 결과이지
    # 규칙이 아니다 - 새 depot 을 만들면 번호가 이어지지 않는다.
    # 0 을 주면 VDF 를 생성하지 않는다 (빌드만 뽑고 싶을 때).
    [uint32]$DepotId = 5281131,

    [switch]$SkipVerify
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

function Step($n, $text) { Write-Host "`n[$n] $text" -ForegroundColor Cyan }
function Ok($text)        { Write-Host "      $text" -ForegroundColor Green }
function Warn($text)      { Write-Host "      $text" -ForegroundColor Yellow }

<#
.SYNOPSIS
    프로세스를 돌리고 종료 코드와 출력을 같이 받아 온다.

.DESCRIPTION
    Start-Process -PassThru 를 쓰면 안 된다. Windows PowerShell 5.1 에서는
    **-Wait 를 준 경우에만 .ExitCode 가 채워지고**, WaitForExit() 을 직접 불러도
    비어 있다 (검증 3종으로 확인: -Wait 만 7 을 돌려주고 나머지 둘은 빈 값).
    그런데 -Wait 에는 타임아웃이 없어서 빌드가 영원히 멈출 수 있다.

    "종료 코드가 빈 값" 은 비교에서 조용히 거짓이 되므로, 검증이 통과했는데도
    FAIL 로 찍히거나 그 반대가 된다 - 판정이 말없이 무의미해지는 자리다.
    .NET Process 를 직접 쓰면 타임아웃과 종료 코드를 둘 다 얻는다.
#>
function Invoke-Native {
    param([string]$File, [string]$Arguments, [int]$TimeoutMs = 120000)

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $File
    $psi.Arguments = $Arguments
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true

    $proc = [System.Diagnostics.Process]::Start($psi)
    # 스트림을 먼저 비동기로 걸어 둔다. 파이프 버퍼가 차면 프로세스가 쓰다가
    # 막혀서, 기다리는 쪽과 서로를 붙잡는 교착이 된다.
    $so = $proc.StandardOutput.ReadToEndAsync()
    $se = $proc.StandardError.ReadToEndAsync()

    $exited = $proc.WaitForExit($TimeoutMs)
    if (-not $exited) { try { $proc.Kill() } catch { } }

    [pscustomobject]@{
        ExitCode = $(if ($exited) { $proc.ExitCode } else { $null })
        TimedOut = -not $exited
        Output   = $so.Result + $se.Result
    }
}

$appId = 0
$steamSrc = Join-Path $repo "platform\SteamService.cs"
if ((Get-Content $steamSrc -Raw) -match 'DefaultAppId\s*=\s*(\d+)') { $appId = [uint32]$Matches[1] }

Write-Host ""
Write-Host "PunchMonkey 릴리스 빌드 (A12)" -ForegroundColor White
Write-Host "  앱 ID    : $appId  (SteamService.DefaultAppId 에서 읽음)"
Write-Host "  산출 위치: $OutDir"

# ---------------------------------------------------------------- 1. 전제 확인
Step 1 "전제 확인"

if (-not (Test-Path $Godot)) {
    throw "Godot 콘솔 빌드를 찾지 못했다: $Godot`n  GODOT 환경변수나 -Godot 인자로 경로를 넘겨라."
}
Ok "Godot: $Godot"

# 템플릿이 없으면 익스포트가 '설정 오류' 로 즉사한다. 여기서 먼저 잡아야
# 5분 뒤에 알게 되는 일이 없다.
$tplDir = Join-Path $env:APPDATA "Godot\export_templates"
$tpl = Get-ChildItem $tplDir -Directory -EA SilentlyContinue |
    Where-Object { Test-Path (Join-Path $_.FullName "windows_release_x86_64.exe") } |
    Select-Object -First 1
if (-not $tpl) {
    throw "Windows 릴리스 export 템플릿이 없다 ($tplDir).`n  에디터 > 편집 > 내보내기 템플릿 관리 에서 설치한다. 엔진과 버전이 정확히 같아야 한다."
}
Ok "export 템플릿: $($tpl.Name)"

# NativeAOT 링크 단계가 vswhere 로 MSVC 를 찾는데, PATH 에 없으면 링커 경로를
# 조립하다 실패한다. 설치돼 있는데도 실패하는 흔한 자리라 여기서 미리 붙인다.
$vsInstaller = "C:\Program Files (x86)\Microsoft Visual Studio\Installer"
if (Test-Path (Join-Path $vsInstaller "vswhere.exe")) {
    if ($env:PATH -notlike "*$vsInstaller*") { $env:PATH = "$vsInstaller;$env:PATH" }
    Ok "vswhere: PATH 에 추가"
} else {
    Warn "vswhere 를 못 찾았다. InputHelper NativeAOT 링크가 실패할 수 있다."
}

$dll = Join-Path $repo "steam_api64.dll"
if (-not (Test-Path $dll)) { throw "steam_api64.dll 이 저장소 루트에 없다." }
Ok "steam_api64.dll 확인"

# 더러운 트리로 뽑은 빌드는 나중에 "이 빌드가 어느 커밋이냐"를 못 따진다.
# 막지는 않는다 — 급할 때 손을 묶으면 스크립트를 안 쓰게 된다.
$dirty = @(git status --porcelain 2>$null)
$commit = (git rev-parse --short HEAD 2>$null)
if ($dirty.Count -gt 0) { Warn "작업 트리에 커밋 안 된 변경 $($dirty.Count)건. 빌드는 계속한다." }
Ok "커밋: $commit"

# ---------------------------------------------------- 2. InputHelper (NativeAOT)
Step 2 "InputHelper 빌드 (NativeAOT)"

# **프레임워크 의존으로 뽑으면 안 된다.** 기본 publish 는 150KB 짜리 apphost +
# InputHelper.dll 이고, 실행하려면 유저 PC 에 .NET 8 런타임이 설치돼 있어야 한다.
# 게임 본체는 Godot 이 런타임을 통째로 동봉해서(data_.../coreclr.dll 등, 약 76MB)
# 상관없지만, **헬퍼는 그 런타임을 공유하지 못한다** — hostfxr 이 Program Files 의
# 공용 런타임을 찾기 때문이다. 클린 PC 에서 헬퍼만 조용히 안 뜨고, 그러면
# 전역 타건이 0 이 된다. 게임의 핵심 루프가 죽는데 에러는 안 난다.
#
# AOT 면 런타임 의존이 사라지고 1.8MB 로 끝난다. 자체 포함(self-contained)
# 단일 파일도 의존은 없애지만 70MB 를 더 얹고, 실행할 때마다 임시 폴더에 자기를
# 풀어 놓는다 — 상주 앱 헬퍼로는 AOT 가 확실히 낫다 (A13 백신 검증에도 유리).
$aotOut = Join-Path $env:TEMP "punchmonkey-inputhelper"
if (Test-Path $aotOut) { Remove-Item $aotOut -Recurse -Force }

$publishLog = & dotnet publish (Join-Path $repo "platform\InputHelper\InputHelper.csproj") `
    -c Release -r win-x64 -p:PublishAot=true -o $aotOut --nologo 2>&1
$helperExe = Join-Path $aotOut "InputHelper.exe"
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $helperExe)) {
    $publishLog | Select-Object -Last 15 | ForEach-Object { Write-Host "      $_" -ForegroundColor DarkGray }
    throw "InputHelper NativeAOT 빌드 실패.`n  MSVC C++ 빌드 도구가 필요하다 (Visual Studio Installer > 'C++를 사용한 데스크톱 개발').`n  **프레임워크 의존 빌드로 우회하지 말 것** - 클린 PC 에서 헬퍼가 안 뜬다 (위 주석)."
}

# apphost 가 아니라 진짜 네이티브인지 확인한다. AOT 가 조용히 안 걸리면
# 150KB 짜리 apphost 가 같은 이름으로 나오므로 크기로 가른다.
$helperKb = [math]::Round((Get-Item $helperExe).Length / 1KB)
if ($helperKb -lt 500) {
    throw "InputHelper.exe 가 ${helperKb}KB 다 - AOT 가 아니라 apphost 로 보인다. 런타임 의존이 남아 있다."
}
Ok "InputHelper.exe  ${helperKb}KB (네이티브)"

# --------------------------------------------------------- 3. 스테이징 폴더 정리
Step 3 "스테이징 정리"

$dist = Join-Path $repo $OutDir
if (Test-Path $dist) { Get-ChildItem $dist -Force | Remove-Item -Recurse -Force }
else { New-Item -ItemType Directory -Path $dist -Force | Out-Null }
Ok "$OutDir 비움"

# ------------------------------------------------------------- 4. Godot 익스포트
Step 4 "Godot 릴리스 익스포트"

$gameExe = Join-Path $dist "PunchMonkey.exe"
$log = Join-Path $env:TEMP "punchmonkey-export.log"
Remove-Item $log, "$log.err" -Force -EA SilentlyContinue

# Start-Process 는 -ArgumentList 원소를 따옴표로 안 묶는다. 프리셋 이름에 공백이
# 있으므로 원소 안에 따옴표를 직접 넣어야 한다 - 안 그러면 "Windows" 와
# "Desktop" 이 따로 가서 'Invalid export preset name: Windows.' 로 즉사한다.
$exportArgs = @('--headless', '--path', $repo, '--export-release', "`"$Preset`"", $gameExe)
$p = Start-Process -FilePath $Godot -ArgumentList $exportArgs -PassThru -NoNewWindow `
    -RedirectStandardOutput $log -RedirectStandardError "$log.err"

# 헤드리스 익스포트가 패킹을 끝내고도 안 죽은 사례가 있다 (DAY1-2-SPIKE.md §5).
# 지금은 재현되지 않지만 CI 를 멈추게 할 종류의 실패라 감시는 남긴다.
$marker = 'DONE ' + [char]93 + ' savepack'
$waited = 0
while ($waited -lt 300 -and -not $p.HasExited) {
    Start-Sleep -Seconds 2; $waited += 2
    $txt = Get-Content $log -Raw -EA SilentlyContinue
    if ($txt -and $txt.Contains($marker)) { break }
}
Start-Sleep -Seconds 3
if (-not $p.HasExited) {
    Stop-Process -Id $p.Id -Force -EA SilentlyContinue
    Warn "패킹 완료 후에도 종료되지 않아 강제 종료했다 (${waited}s)."
}

$err = Get-Content "$log.err" -Raw -EA SilentlyContinue
if (-not (Test-Path $gameExe)) {
    if ($err) { $err -split "`n" | Select-Object -Last 10 | ForEach-Object { Write-Host "      $_" -ForegroundColor DarkGray } }
    throw "익스포트 실패 - $gameExe 가 안 나왔다."
}
Ok "PunchMonkey.exe / .pck / data_*"

# ---------------------------------------------------------- 5. Godot 이 모르는 것
Step 5 "네이티브 · 헬퍼 동봉"

Copy-Item $dll (Join-Path $dist "steam_api64.dll") -Force
Ok "steam_api64.dll"
Copy-Item $helperExe (Join-Path $dist "InputHelper.exe") -Force
Ok "InputHelper.exe  (게임 exe 옆 - HelperInputSource.ResolveHelperPath 가 여기서 찾는다)"

# ------------------------------------------------------------------- 6. 검증
if (-not $SkipVerify) {
    Step 6 "검증"

    # 셀프테스트는 스팀 연결 + 헬퍼 경로를 한 번에 본다. 종료 코드는
    # IsInitialized 기준이다 - 도전과제 통계는 A15 전까지 FAIL 이 정상이라
    # 여기서 판정에 넣지 않는다 (A8-STEAM.md §4-3).
    $v = Invoke-Native -File $gameExe -Arguments '-- --steam-selftest' -TimeoutMs 120000
    $vout = $v.Output
    if ($v.TimedOut) { Warn "셀프테스트 타임아웃" }

    if ($v.ExitCode -eq 0) { Ok "스팀 초기화 PASS (종료 코드 0)" }
    else { Warn "스팀 초기화 FAIL (종료 코드 $($v.ExitCode)). 스팀 클라이언트가 떠 있는지 확인." }

    if ($vout -match 'appid=(\d+)') {
        if ([uint32]$Matches[1] -eq $appId) { Ok "앱 ID $($Matches[1]) 일치" }
        else { Warn "앱 ID 불일치: 빌드 $appId / 실행 $($Matches[1])" }
    }

    # 헬퍼가 빠진 채로 배포되는 것이 이 스크립트가 막으려는 주된 사고다.
    if ($vout -match '헬퍼 실행 파일 없음') { throw "InputHelper 를 못 찾았다 - 동봉이 실패했다." }
    Ok "헬퍼 경로 해석됨"

    # **파일이 있는 것과 도는 것은 다르다.** NativeAOT 는 리플렉션·마샬링이
    # 걸린 코드를 조용히 망가뜨릴 수 있고, 헬퍼가 죽어도 게임은 멀쩡히 뜬다 -
    # 타수가 0 일 뿐이다. 그래서 짧게 띄워서 심장박동까지 확인한다.
    # --report= 는 무인 실행이라 세이브·레지스트리·트레이를 안 건드린다.
    $hlog = Join-Path $env:TEMP "punchmonkey-helper.txt"
    Remove-Item $hlog -Force -EA SilentlyContinue
    $null = Invoke-Native -File $gameExe `
        -Arguments "-- `"--report=$hlog`" --seconds=12 --warmup=3" -TimeoutMs 90000

    $hout = Get-Content $hlog -Raw -EA SilentlyContinue
    if ($hout -match '헬퍼 정상') { Ok "헬퍼 실행 확인 (심장박동 수신)" }
    elseif ($hout -match 'input off \[([^\]]+)\]') {
        throw "헬퍼가 동봉됐지만 돌지 않는다: $($Matches[1])`n  NativeAOT 빌드가 깨졌을 수 있다. 전역 타건이 0 이 되므로 이 빌드는 올리면 안 된다."
    }
    else { Warn "헬퍼 상태를 읽지 못했다 (리포트 없음). 수동 확인 필요." }
}

# ------------------------------------------------------- 7. SteamPipe VDF (선택)
if ($DepotId -gt 0) {
    Step 7 "SteamPipe VDF 생성"

    $pipeDir = Join-Path $repo "build\steampipe"
    New-Item -ItemType Directory -Path $pipeDir -Force | Out-Null
    $contentRoot = (Resolve-Path $dist).Path

    # depot VDF: 콘텐츠 루트를 통째로 넣고 .pdb 만 뺀다. LocalPath "*" 는
    # 재귀 포함이다 - 파일을 하나 추가할 때마다 VDF 를 고치지 않아도 되게 한다.
    @"
"DepotBuildConfig"
{
    "DepotID" "$DepotId"
    "ContentRoot" "$contentRoot"
    "FileMapping"
    {
        "LocalPath" "*"
        "DepotPath" "."
        "recursive" "1"
    }
    "FileExclusion" "*.pdb"
}
"@ | Out-File (Join-Path $pipeDir "depot_build_$DepotId.vdf") -Encoding utf8

    # app VDF: SetLive 는 비워 둔다. 채워 넣으면 업로드가 곧바로 그 브랜치에
    # 공개된다 - 검수 전 빌드가 유저에게 나가는 사고를 스크립트가 만들면 안 된다.
    @"
"appbuild"
{
    "appid" "$appId"
    "desc" "PunchMonkey $commit"
    "buildoutput" "..\steampipe_output"
    "contentroot" "$contentRoot"
    "setlive" ""
    "depots"
    {
        "$DepotId" "depot_build_$DepotId.vdf"
    }
}
"@ | Out-File (Join-Path $pipeDir "app_build_$appId.vdf") -Encoding utf8

    Ok "build\steampipe\app_build_$appId.vdf"
    Ok "build\steampipe\depot_build_$DepotId.vdf"
    Write-Host ""
    Write-Host "  업로드:" -ForegroundColor Cyan
    Write-Host "    steamcmd +login <빌드계정> +run_app_build `"$pipeDir\app_build_$appId.vdf`" +quit"
    Write-Host "  setlive 는 비워 뒀다. 브랜치 공개는 파트너 사이트에서 직접 한다." -ForegroundColor Yellow
}

# -------------------------------------------------------------------- 매니페스트
$files = Get-ChildItem $dist -Recurse -File
$totalMb = [math]::Round(($files | Measure-Object Length -Sum).Sum / 1MB, 1)

Write-Host ""
Write-Host "완료 — $OutDir  (파일 $($files.Count)개, $totalMb MB, 커밋 $commit)" -ForegroundColor Green
Get-ChildItem $dist -Force | ForEach-Object {
    $size = if ($_.PSIsContainer) {
        "{0,8:N1} MB  (폴더)" -f ((Get-ChildItem $_.FullName -Recurse -File | Measure-Object Length -Sum).Sum / 1MB)
    } else {
        "{0,8:N1} MB" -f ($_.Length / 1MB)
    }
    Write-Host ("  {0}  {1}" -f $size, $_.Name)
}
Write-Host ""
if ($DepotId -eq 0) {
    Write-Host "-DepotId 0 이라 VDF 를 만들지 않았다. 업로드하려면 인자 없이 다시 돌려라." -ForegroundColor Cyan
}
