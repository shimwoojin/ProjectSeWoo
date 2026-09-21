<#
.SYNOPSIS
    B8 — AI 에셋 파이프라인. 매니페스트 → 생성 → 배경제거 → 정리 → Godot 임포트.

.DESCRIPTION
    tools/assetgen/assets.manifest.json 한 곳에 적힌 프롬프트·시드·크기·피벗대로
    에셋을 뽑아 res://assets/ 에 배치하고, Godot 이 읽을 수 있게 임포트까지 끝낸다.

      ① 프롬프트   assets.manifest.json  (+ gen.py 의 스타일 공통 프롬프트)
      ② 생성       ComfyUI  SDXL + StickersRedmond LoRA, 1024², 장당 약 22초
      ③ 배경 제거  같은 그래프 안 INSPYRENET(MIT) — RMBG-2.0 은 CC BY-NC 라 못 쓴다
      ④ 정리       알파 임계 → 부스러기 제거 → 크롭 → LANCZOS → 피벗 배치
      ⑤ 임포트     godot --headless --import  →  .godot/imported/ 생성

    **⑤ 가 빠지면 새 PNG 가 빌드에 안 들어간다.** Godot 은 임포트된 것만 .pck 에
    넣는다 (A12 §가 .godot/imported/ 를 빌드에서 제외하면 안 된다고 적은 것과 같은
    얘기다). 손으로 에디터를 한 번 열어 주는 것으로 때워 왔는데, 그러면 "스크립트로
    뽑는다" 가 성립하지 않는다.

    기본은 **없는 것만** 생성한다. 마음에 안 드는 한 장만 다시 뽑으려면
    -Id 와 -Force 를 같이 준다 — 나머지 그림은 시드가 고정돼 있어 안 흔들린다.

.PARAMETER Only
    그룹 하나만 처리한다. entities | cursor

.PARAMETER Id
    매니페스트의 id 하나만 처리한다.

.PARAMETER Force
    이미 있는 파일도 다시 생성한다.

.PARAMETER SkipGen
    생성을 건너뛰고 _raw/ 에 있는 것으로 후처리부터 한다. 피벗·알파 임계값을
    만질 때 쓴다 — 22초짜리 생성을 다시 돌릴 이유가 없다.

.PARAMETER SkipImport
    Godot 임포트를 건너뛴다.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\build-assets.ps1 -Only entities

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\build-assets.ps1 -Id banana -Force

.NOTES
    전제: ComfyUI 가 127.0.0.1:8188 에 떠 있어야 한다 (C:\Tools\start_comfy.bat).
    후처리는 ComfyUI 의 venv 파이썬으로 돈다 — 시스템 파이썬에는 PIL 이 없다.
#>
[CmdletBinding()]
param(
    [ValidateSet("entities", "cursor")]
    [string]$Only,

    [string]$Id,

    [string]$Python = $(if ($env:COMFY_PYTHON) { $env:COMFY_PYTHON } else {
        "C:\Tools\ComfyUI\.venv\Scripts\python.exe" }),

    [string]$Godot = $(if ($env:GODOT) { $env:GODOT } else {
        "C:\Tools\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe" }),

    [switch]$Force,
    [switch]$SkipGen,
    [switch]$SkipImport
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

# 파이썬 쪽 출력이 한글이다. 콘솔 코드 페이지가 cp949 면 그대로 깨진다.
$env:PYTHONUTF8 = "1"
[Console]::OutputEncoding = [Text.Encoding]::UTF8

function Step($n, $text) { Write-Host "`n[$n] $text" -ForegroundColor Cyan }
function Ok($text)        { Write-Host "      $text" -ForegroundColor Green }
function Warn($text)      { Write-Host "      $text" -ForegroundColor Yellow }

$gen   = Join-Path $PSScriptRoot "assetgen\gen.py"
$post  = Join-Path $PSScriptRoot "assetgen\postprocess.py"
$slice = Join-Path $PSScriptRoot "assetgen\slice.py"

$sel = @()
if ($Only) { $sel += @("--only", $Only) }
if ($Id)   { $sel += @("--id",   $Id)   }

# ------------------------------------------------------------------ 0. 전제 확인
Step 0 "전제 확인"
if (-not (Test-Path $Python)) {
    throw "ComfyUI venv 파이썬을 찾지 못했다: $Python`n  COMFY_PYTHON 환경변수나 -Python 인자로 경로를 넘겨라."
}
Ok "python: $Python"

# ------------------------------------------------------------- 1. 시트 슬라이스
# 엔티티 아트는 '생성' 이 아니라 '받아서 자르기' 가 정식 경로다 - 원숭이 펀치처럼
# 같은 캐릭터가 여러 포즈로 나와야 하는 것은 단발 생성으로 안 된다 (문서 §2-1).
if ($Only -eq "cursor") {
    Step 1 "시트 슬라이스 건너뜀 (-Only cursor)"
} else {
    Step 1 "시트 슬라이스 — assets/_source/ → assets/entities/"
    & $Python $slice
    if ($LASTEXITCODE -ne 0) { throw "슬라이스 실패 (exit $LASTEXITCODE)" }
}

# ----------------------------------------------------------------- 2. 생성 (②③)
if ($SkipGen) {
    Step 2 "생성 건너뜀 (-SkipGen)"
} else {
    Step 2 "ComfyUI 생성 + 배경 제거"
    $genArgs = @($gen) + $sel
    if ($Force) { $genArgs += "--force" }
    & $Python @genArgs
    if ($LASTEXITCODE -ne 0) { throw "생성 실패 (exit $LASTEXITCODE)" }

    # ------------------------------------------------------------- 2-1. 정리 (④)
    Step "2-1" "후처리 — 알파 정리 / 크롭 / 리사이즈 / 피벗"
    & $Python @(@($post) + $sel)
    if ($LASTEXITCODE -ne 0) { throw "후처리 실패 (exit $LASTEXITCODE)" }
}

# ------------------------------------------------------------------ 3. 임포트 (⑤)
if ($SkipImport) {
    Step 3 "Godot 임포트 건너뜀 (-SkipImport)"
    Warn "임포트하지 않은 PNG 는 빌드(.pck)에 안 들어간다."
} else {
    Step 3 "Godot 임포트"
    if (-not (Test-Path $Godot)) {
        Warn "Godot 을 찾지 못했다: $Godot"
        Warn "임포트를 건너뛴다. 에디터를 한 번 열면 같은 일이 일어나지만,"
        Warn "그러면 이 스크립트만으로 빌드가 재현되지 않는다."
    } else {
        # --headless --import: 에디터를 띄우지 않고 임포트만 하고 빠진다.
        # 리소스가 많으면 몇 분 걸릴 수 있어 타임아웃을 넉넉히 준다.
        # Godot 임포트 로그는 파일당 한 줄씩 나와서 길다. 실패했을 때만 보여준다.
        $log = Join-Path $env:TEMP "punchmonkey-import.log"
        $p = Start-Process -FilePath $Godot -ArgumentList @("--headless", "--import", "--path", $repo) `
            -PassThru -NoNewWindow -RedirectStandardOutput $log -RedirectStandardError "$log.err"

        # **이 줄을 지우면 아래 ExitCode 가 빈 값이 된다.** Start-Process -PassThru 가
        # 돌려주는 객체는 핸들을 안 쥐고 있어서, 프로세스가 끝나면 종료 코드를 읽을
        # 대상이 사라진다. .Handle 을 한 번 건드려 캐시해 두면 그 뒤로 읽힌다.
        $null = $p.Handle

        if (-not $p.WaitForExit(600000)) {
            $p.Kill()
            throw "Godot 임포트가 10분을 넘겼다. 로그: $log"
        }
        $p.WaitForExit()
        if ($p.ExitCode -ne 0) {
            Get-Content "$log.err" -Tail 20 -ErrorAction SilentlyContinue | ForEach-Object { Warn $_ }
            throw "Godot 임포트 실패 (exit $($p.ExitCode)). 로그: $log"
        }
        Ok "임포트 완료"

        # --headless --import 는 에디터 레이아웃까지 불러오면서 **스크립트 편집기에
        # 열려 있던 .cs 를 제 들여쓰기 설정(탭)으로 다시 저장한다.** 2026-09-21 에
        # game/GameRoot.cs 306줄이 공백->탭으로 통째로 바뀌었다 - 내용은 한 글자도
        # 안 바뀌었는데 diff 는 전체 파일이 된다. 조용히 넘어가면 그대로 커밋된다.
        #
        # 자동으로 되돌리지는 않는다. 진짜 공백만 고친 작업이 섞여 있을 수 있고,
        # 그건 여기서 구분할 방법이 없다.
        $mangled = @()
        foreach ($f in (git diff --name-only -- '*.cs')) {
            git diff --quiet --ignore-all-space -- $f
            if ($LASTEXITCODE -eq 0) { $mangled += $f }   # 공백 말고는 안 바뀜
        }
        if ($mangled) {
            Warn "임포트가 아래 파일의 들여쓰기만 바꿔 놨다 (내용 변화 없음):"
            $mangled | ForEach-Object { Warn "  $_" }
            Warn "되돌리려면:  git checkout -- $($mangled -join ' ')"
        }
    }
}

# --------------------------------------------------------------------- 4. 요약
Step 4 "결과"
# assets/_source/ 는 .gdignore 로 일부러 임포트에서 빼 둔 원본 시트다. 게임은 잘라
# 놓은 것만 쓰므로 원본이 .pck 에 실리면 2.9MB 가 그냥 낭비다. 집계에서도 뺀다 -
# 안 빼면 아래 "임포트 안 됨" 경고가 매번 거짓으로 뜬다.
$pngs = Get-ChildItem -Path (Join-Path $repo "assets") -Filter *.png -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '[\\/]_source[\\/]' }
if (-not $pngs) {
    Warn "assets/ 에 PNG 가 없다."
} else {
    $total = ($pngs | Measure-Object -Property Length -Sum).Sum
    foreach ($g in $pngs | Group-Object { Split-Path (Split-Path $_.FullName -Parent) -Leaf }) {
        $sz = ($g.Group | Measure-Object -Property Length -Sum).Sum
        Ok ("{0,-10} {1,2}장  {2,7:N1} KB" -f $g.Name, $g.Count, ($sz / 1KB))
    }
    Ok ("합계       {0,2}장  {1,7:N1} KB" -f $pngs.Count, ($total / 1KB))

    $imported = Get-ChildItem -Path (Join-Path $repo "assets") -Filter *.png.import -Recurse `
        -ErrorAction SilentlyContinue
    if ($imported.Count -lt $pngs.Count) {
        Warn "$($pngs.Count - $imported.Count)장이 아직 임포트되지 않았다 — 빌드에 안 들어간다."
    }
}
