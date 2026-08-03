#Requires -Version 7
# ADR-006 의 구조 게이트를 검사한다. 위반이 있으면 exit 1.
#
# A. 코드비하인드 금지  — *.xaml.cs 는 InitializeComponent() 호출만 (CLAUDE.md §2)
# B. 참조 방향          — FlexDir.App 은 FlexDir.Shell 을 참조하지 않는다 (CLAUDE.md §1)
# C. Core 순수성        — FlexDir.Core 는 다른 프로젝트와 Windows 타겟을 갖지 않는다 (CLAUDE.md §1)
#
# B·C 를 스크립트로 검사하는 이유: ADR-006 은 "App → Shell 직접 참조 시 컴파일 에러"
# 라고 적었지만, 실제로는 ProjectReference 를 한 줄 추가하면 그냥 컴파일된다.
# 컴파일이 막아주는 것은 '참조가 없는 상태' 뿐이므로, 참조를 추가하는 행위는 여기서 막는다.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# 실패 사유는 harness 실행기가 error_message 로 잡아 다음 시도 프롬프트에 넣는다.
# 콘솔 코드페이지(949)로 나가면 읽을 수 없게 깨지므로 UTF-8 로 고정한다.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$root = Split-Path -Parent $PSScriptRoot
$violations = [System.Collections.Generic.List[string]]::new()

# csproj 의 XML 주석은 검사에서 제외한다. 주석에 적힌 금지 대상의 이름을
# 참조로 오인하면(예: "FlexDir.Shell 을 참조하지 않는다") 게이트가 상시 실패한다.
function Get-ProjectBody([string]$path) {
    return [regex]::Replace((Get-Content -LiteralPath $path -Raw), '<!--.*?-->', '',
        [System.Text.RegularExpressions.RegexOptions]::Singleline)
}

# ── A. 코드비하인드 ────────────────────────────────────────────────
# 허용 목록 방식이다. 아래 어느 패턴에도 맞지 않는 줄은 전부 위반으로 본다.
# 금지 목록 방식으로 쓰면 새로운 문법이 조용히 빠져나간다.
$allowed = @(
    '^\s*$'                                                        # 빈 줄
    '^\s*(//|/\*|\*)'                                              # 주석
    '^\s*#'                                                        # #nullable · #pragma · #region
    '^\s*using\s+[\w\.]+\s*;\s*$'                                  # using System;
    '^\s*using\s+\w+\s*=\s*[\w\.<>,\[\]\s]+;\s*$'                  # using alias = X;
    '^\s*namespace\s+[\w\.]+\s*;?\s*$'                             # namespace X;  /  namespace X
    '^\s*[{}]\s*$'                                                 # 중괄호
    '^\s*\[[^\]]+\]\s*$'                                           # 어트리뷰트
    '^\s*(public|internal|protected|private)?\s*(sealed\s+)?partial\s+class\s+\w+(\s*:\s*[\w\.<>,\s]+)?\s*$'
    '^\s*:\s*[\w\.<>,\s]+\s*$'                                     # 다음 줄로 내려간 기반 타입
    '^\s*(public|internal|protected|private)\s+\w+\s*\(\s*\)\s*$'  # 매개변수 없는 ctor
    '^\s*InitializeComponent\(\)\s*;\s*$'
)

# @() 로 감싼다. 결과가 0개면 파이프라인이 $null 을 내고, StrictMode 아래서
# $null.Count 는 예외가 된다 — 위반이 없을 때 게이트가 실패하는 형태가 된다.
$codeBehind = @(Get-ChildItem -Path (Join-Path $root 'src') -Recurse -Filter '*.xaml.cs' -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' })

foreach ($file in $codeBehind) {
    $rel = [IO.Path]::GetRelativePath($root, $file.FullName)
    $lineNo = 0
    foreach ($line in Get-Content -LiteralPath $file.FullName) {
        $lineNo++
        $ok = $false
        foreach ($pattern in $allowed) {
            if ($line -match $pattern) { $ok = $true; break }
        }
        if (-not $ok) {
            $violations.Add("A. 코드비하인드에 로직: ${rel}:${lineNo}: $($line.Trim())")
        }
    }
}

# ── B. 참조 방향 ───────────────────────────────────────────────────
$appProj = Join-Path $root 'src\FlexDir.App\FlexDir.App.csproj'
if (Test-Path -LiteralPath $appProj) {
    if ((Get-ProjectBody $appProj) -match '<ProjectReference[^>]*FlexDir\.Shell') {
        $violations.Add('B. FlexDir.App 이 FlexDir.Shell 을 참조한다. 포트 인터페이스만 알아야 하고 구현체는 FlexDir.Host 가 주입한다.')
    }
}

# ── C. Core 순수성 ────────────────────────────────────────────────
$coreProj = Join-Path $root 'src\FlexDir.Core\FlexDir.Core.csproj'
if (Test-Path -LiteralPath $coreProj) {
    $core = Get-ProjectBody $coreProj
    if ($core -match '<ProjectReference') {
        $violations.Add('C. FlexDir.Core 가 다른 프로젝트를 참조한다. Core 는 참조 그래프의 바닥이다.')
    }
    if ($core -match '<TargetFramework>[^<]*-windows') {
        $violations.Add('C. FlexDir.Core 의 TargetFramework 가 Windows 타겟이다. Core 는 순수 .NET 이다.')
    }
    if ($core -match '<Use(WPF|WindowsForms)>\s*true') {
        $violations.Add('C. FlexDir.Core 가 WPF/WinForms 를 켰다. Core 는 UI 를 모른다.')
    }
}

# ── 결과 ──────────────────────────────────────────────────────────
if ($violations.Count -gt 0) {
    Write-Host "구조 게이트 위반 $($violations.Count)건 (ADR-006):" -ForegroundColor Red
    foreach ($v in $violations) { Write-Host "  - $v" }
    exit 1
}

Write-Host "구조 게이트 통과 — 코드비하인드 $($codeBehind.Count)개 검사."
exit 0
