#Requires -Version 7
# ADR-007 의 도그푸딩 게이트. usage.log 의 서로 다른 날짜 수를 센다.
#
# **이 스크립트는 차단하지 않는다 — 세어서 보고할 뿐이고 exit 0 이다** (사용자 결정 2026-08-06).
# "새 기능인가 버그 수정인가" 는 스크립트가 판정할 수 없기 때문이다. 게이트 4종
# (build · test · check-structure · Release build)에 넣지 않는 이유도 그것이다 — 넣으면
# 버그 수정마저 막히고 휴가·주말에 자기 저장소가 잠긴다.
#
# 켜는 방법: 세션 시작에 이것을 돌려 판정을 눈에 보이게 한다. BLOCK 이 뜨면 새 기능
# 작업을 멈추고 버그 수정만 한다 — 강제는 사람이 한다.
#
# 파일의 모양이 계약이다 (src/FlexDir.Shell/Usage/FileUsageLog.cs):
# 한 줄에 하루, 앞 10자가 yyyy-MM-dd. 그래서 여기서 파싱이 한 줄이다.

[CmdletBinding()]
param(
    # 기본값은 실물 로그다. 합성 로그로 이 스크립트 자체를 확인할 때만 바꾼다.
    [string]$LogPath = (Join-Path $env:LOCALAPPDATA 'flex-dir\usage.log')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# check-structure.ps1 과 같은 이유 — 콘솔 코드페이지(949)로 나가면 한글이 깨진다.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# ADR-007: "지난 7일 중 5일 미만이면 새 기능 작업을 차단하고 버그 수정만 허용한다."
$WindowDays = 7
$RequiredDays = 5

$today = [datetime]::Today
$since = $today.AddDays(-($WindowDays - 1))   # 오늘을 포함해 7일

if (-not (Test-Path -LiteralPath $LogPath)) {
    Write-Host "도그푸딩 게이트 (ADR-007) — usage.log 가 없다: $LogPath" -ForegroundColor Yellow
    Write-Host "  앱을 한 번도 실행하지 않았거나 다른 기계다. 판정을 내리지 않는다."
    exit 0
}

# 앞 10자만 본다. 그 뒤(오프셋)는 사람이 보라고 남긴 것이지 게이트가 쓰는 값이 아니다.
$days = @(Get-Content -LiteralPath $LogPath |
    ForEach-Object { $_.Trim() } |
    Where-Object { $_.Length -ge 10 } |
    ForEach-Object {
        [datetime]$parsed = [datetime]::MinValue
        if ([datetime]::TryParseExact($_.Substring(0, 10), 'yyyy-MM-dd',
                [cultureinfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$parsed)) {
            $parsed
        }
    } |
    Where-Object { $_ -ge $since -and $_ -le $today } |
    Sort-Object -Unique)

$count = $days.Count
$used = if ($count -gt 0) { ($days | ForEach-Object { $_.ToString('MM-dd') }) -join ' ' } else { '없음' }

if ($count -ge $RequiredDays) {
    Write-Host "도그푸딩 게이트 (ADR-007) — 지난 ${WindowDays}일 중 ${count}일  ✅ PASS" -ForegroundColor Green
    Write-Host "  사용: $used"
}
else {
    Write-Host "도그푸딩 게이트 (ADR-007) — 지난 ${WindowDays}일 중 ${count}일 (기준 ${RequiredDays}일)  ⛔ BLOCK" -ForegroundColor Red
    Write-Host "  사용: $used"
    Write-Host "  → 새 기능 작업을 멈추고 버그 수정만. 전작이 실패한 1원인이 '매일 안 씀' 이다."
}

# 항상 0. 판정은 위 출력이고 강제는 사람이 한다.
exit 0
