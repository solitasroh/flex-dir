#Requires -Version 7
# 배포 패키지를 굽는다 — 인스톨러 + 자동 업데이트 피드 항목 (docs/PRD-v2.md §9).
#
# 흐름은 셋이다:
#   dotnet publish  →  vpk pack  →  gh release create/upload
#
# **버전의 정본은 Directory.Build.props 의 <Version> 이다.** 여기서 인자로 덮어쓸 수 있지만
# 기본값은 그 파일을 읽는다 — 두 곳에 적으면 릴리스 태그와 앱이 보고하는 버전이 갈린다.
#
# **자동 업데이트는 "설치된 버전 < 피드의 최신 버전" 하나로 돈다.** 그래서 올리지 않고
# 릴리스하면 아무도 갱신되지 않고, 내렸다가 낮은 버전을 올리면 다운그레이드가 된다
# (VelopackUpdateSource 가 그것을 '새 버전' 으로 그리지 않는다).
#
# 상주 프로세스를 먼저 죽인다 — 자기가 띄워진 산출물을 잠근다 (.harness/HANDOFF.md §현재 상태).

[CmdletBinding()]
param(
    [string]$Version,

    # 릴리스가 올라갈 곳. AppComposition.UpdateFeed 와 같아야 한다 — 다르면 인스톨러는
    # 만들어지는데 아무도 갱신되지 않는다.
    [string]$Repo = 'solitasroh/flex-dir',

    # 패키지만 굽고 올리지 않는다. 처음 굽는 것을 눈으로 보고 싶을 때.
    [switch]$NoUpload,

    # 코드서명 인증서의 지문 (scripts/new-signing-cert.ps1 이 만들어 낸다).
    #
    # **선택이다.** 주지 않으면 서명 없이 굽는다 — vpk 가 "N file(s) will not be signed" 로
    # 경고하고 그대로 진행한다. 인증서는 이 기계의 CurrentUser\My 에만 있으므로, 없는
    # 기계에서 필수로 만들면 굽는 길이 통째로 막힌다.
    #
    # 지문으로 가리키고 .pfx 경로+비밀번호를 받지 않는 이유: 명령줄 인자는 프로세스 목록과
    # 셸 히스토리에 남는다.
    [string]$SignThumbprint,

    # 타임스탬프 서버. 서명 시각을 제3자가 보증하므로 **인증서가 만료돼도 이미 서명한
    # 것은 계속 유효하다**. 자체 서명이어도 타임스탬프 자체는 공개 TSA 를 쓴다.
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if (-not $Version) {
    $props = Get-Content (Join-Path $root 'Directory.Build.props') -Raw
    if ($props -notmatch '<Version>([^<]+)</Version>') {
        throw 'Directory.Build.props 에서 <Version> 을 못 찾았다.'
    }
    $Version = $Matches[1].Trim()
}

Write-Host "flex-dir $Version → $Repo" -ForegroundColor Cyan

# vpk 는 전역 도구다. 없으면 여기서 멈추고 설치 방법을 말한다 — 뒤에서 알 수 없는 오류로
# 깨지는 것보다 낫다.
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    throw 'vpk 가 없다. `dotnet tool install -g vpk` 로 설치한다.'
}

Get-Process FlexDir.Host -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

$publish = Join-Path $root 'artifacts\publish'
$packages = Join-Path $root 'artifacts\packages'

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

# 출력 폴더도 비운다. 남아 있으면 vpk 가 "같거나 높은 버전이 이미 있다" 며 멈춘다 —
# 같은 버전을 다시 구울 때(고쳐서 다시 패키징) 그것이 정상 흐름이다.
#
# 대가: 델타 패키지를 만들지 않는다. vpk 는 이전 릴리스가 이 폴더에 있어야 차분을 굽는데,
# 정본 피드는 GitHub 이고 여기는 출력일 뿐이다. 전체 패키지가 74MB 이고 받는 사람이
# 자기 자신이므로 지금은 값이 맞다.
#
# **켜고 싶으면 내려받을 필요가 없다** (2026-08-11 에 정정). 이 줄이 도는 시점에는 언제나
# 직전 릴리스의 .nupkg 가 이 폴더에 남아 있다 — 지난 실행이 여기에 구웠기 때문이다.
# 그것만 살려 두고 나머지를 지우면 델타가 켜진다. 그러므로 델타 켜기는 어느 릴리스에서든
# 난이도가 같고, "이번 릴리스를 놓치면 다음에 더 어렵다" 는 성질은 없다.
if (Test-Path $packages) { Remove-Item $packages -Recurse -Force }

# self-contained 다. .NET 9 런타임이 깔려 있는지 사용자가 신경 쓰지 않아야 하고,
# 이 앱의 사용자는 자기 자신이다 (ADR-007 의 도그푸딩).
& dotnet publish (Join-Path $root 'src\FlexDir.Host\FlexDir.Host.csproj') `
    -c Release -r win-x64 --self-contained true `
    -p:Version=$Version `
    -o $publish
if ($LASTEXITCODE -ne 0) { throw "publish 실패 ($LASTEXITCODE)" }

# 서명은 있으면 하고 없으면 건너뛴다. vpk 가 --signParams 를 signtool.exe 에 그대로 넘긴다.
# /sha1 은 저장소에서 지문으로 인증서를 고른다 — 파일도 비밀번호도 명령줄에 오르지 않는다.
$signArgs = @()

if ($SignThumbprint) {
    $found = Get-ChildItem Cert:\CurrentUser\My |
        Where-Object { $_.Thumbprint -eq $SignThumbprint }

    if (-not $found) {
        throw "지문 $SignThumbprint 인 인증서가 CurrentUser\My 에 없다. scripts/new-signing-cert.ps1 참조."
    }

    if ($found.NotAfter -lt (Get-Date)) {
        throw "그 인증서는 $($found.NotAfter.ToString('yyyy-MM-dd')) 에 만료됐다."
    }

    Write-Host "서명: $($found.Subject)" -ForegroundColor Cyan
    $signArgs = @('--signParams', "/sha1 $SignThumbprint /fd sha256 /tr $TimestampUrl /td sha256")
}
else {
    Write-Host '서명하지 않는다 (-SignThumbprint 없음).' -ForegroundColor Yellow
}

# --packTitle 은 시작 메뉴·제어판에 뜨는 이름이다. packId 는 바뀌면 안 된다 —
# Velopack 이 그것으로 "같은 앱인가" 를 판정하고, 바꾸면 기존 설치가 갱신 대상에서 빠진다.
& vpk pack `
    --packId flex-dir `
    --packVersion $Version `
    --packDir $publish `
    --mainExe FlexDir.Host.exe `
    --packTitle 'flex-dir' `
    --icon (Join-Path $root 'assets\flex-dir.ico') `
    --outputDir $packages `
    @signArgs
if ($LASTEXITCODE -ne 0) { throw "vpk pack 실패 ($LASTEXITCODE)" }

Write-Host "`n패키지:" -ForegroundColor Green
Get-ChildItem $packages | Select-Object Name, @{n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table -AutoSize

if ($NoUpload) {
    Write-Host '올리지 않았다 (-NoUpload).' -ForegroundColor Yellow
    return
}

# gh 가 태그를 만들고 자산을 올린다. RELEASES 파일까지 함께 올라가야 클라이언트가 피드를
# 읽는다 — vpk 가 outputDir 에 넣은 것을 통째로 올리는 이유다.
& gh release create "v$Version" (Get-ChildItem $packages | ForEach-Object FullName) `
    --repo $Repo --title "v$Version" --notes "flex-dir $Version"
if ($LASTEXITCODE -ne 0) { throw "릴리스 생성 실패 ($LASTEXITCODE)" }

Write-Host "https://github.com/$Repo/releases/tag/v$Version" -ForegroundColor Green
