#Requires -Version 7
# 제품 아이콘 원본을 OpenAI 이미지 API 로 생성한다 (사용자 결정 2026-08-06).
#
# 이것은 빌드의 일부가 아니다 — 한 번 돌려 assets\flex-dir-source.png 를 얻고,
# 그 뒤로는 make-ico.ps1 이 그 PNG 에서 .ico 를 굽는다. 그래서 저장소를 클론한 사람에게
# API 키가 필요 없다.
#
# 투명 배경은 어느 모델도 주지 않는다 (문서: "keep the output background opaque and use a
# downstream background-removal step"). 그래서 흰 배경 위 **둥근 사각 타일**로 뽑고
# 알파는 make-ico.ps1 이 결정적으로 씌운다 — 모델이 만든 알파를 믿지 않는다.

[CmdletBinding()]
param(
    [string]$OutDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'assets'),

    # 변형을 몇 장 뽑을지. 시안을 고르려면 2 이상.
    [ValidateRange(1, 4)]
    [int]$Count = 3,

    [string]$Model = 'gpt-image-2',

    # 16px 트레이에서 읽혀야 한다 — 그것이 이 프롬프트의 모든 제약을 정한다.
    # 2분할이 이 제품의 정체성이므로 형태는 나란한 패널 둘이다.
    [string]$Prompt = @'
A flat vector app icon for a Windows 11 file manager called "flex-dir".

Subject: a single rounded-square app tile, filled with a solid strong blue (#0067C0).
Inside the tile, two simple side-by-side vertical panels in white, separated by a
narrow gap, suggesting a dual-pane file browser. The left panel is solid white; the
right panel is white at lower opacity. Each panel shows two or three short horizontal
lines suggesting a file list.

Style: flat design, clean geometric vector shapes, strong silhouette, generous negative
space, no gradients, no shadows, no bevel, no 3D, no texture, no text, no lettering,
no watermark.

Composition: one centered tile on a plain pure white background, with generous even
padding around the tile. The tile must be a rounded square with clearly visible corner
radius. Nothing else in the frame.

Constraint: the design must stay legible when scaled down to 16x16 pixels, so use very
few shapes and thick strokes.
'@
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$root = Split-Path -Parent $PSScriptRoot

# ── 키 ─────────────────────────────────────────────────────────────
# .env 는 .gitignore 가 막는다. 형식은 KEY=VALUE 한 줄 (.env.example 참조).
$envPath = Join-Path $root '.env'
$key = $env:OPENAI_API_KEY

if (-not $key -and (Test-Path -LiteralPath $envPath)) {
    foreach ($line in Get-Content -LiteralPath $envPath) {
        if ($line -match '^\s*OPENAI_API_KEY\s*=\s*(.+?)\s*$') {
            $key = $Matches[1].Trim("'", '"')
            break
        }
    }
}

if (-not $key) {
    throw "OPENAI_API_KEY 가 없다. .env 에 'OPENAI_API_KEY=sk-...' 를 넣어라 (.env.example 참조)."
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# ── 생성 ───────────────────────────────────────────────────────────
# **한 장씩 따로 보낸다.** n=3 을 quality=high 로 한 요청에 담으면 게이트웨이가 먼저
# 끊고 Cloudflare 520 이 온다 (실측: 장당 45초 이상). 재시도도 장 단위여야 값이 싸다.
$body = @{
    model   = $Model
    prompt  = $Prompt
    size    = '1024x1024'
    quality = 'high'
    n       = 1
} | ConvertTo-Json -Depth 4

Write-Host "이미지 생성 중 ($Model · ${Count}장 · 한 장씩)…"

for ($i = 1; $i -le $Count; $i++) {
    $item = $null

    foreach ($attempt in 1..3) {
        try {
            $res = Invoke-RestMethod -Method Post -Uri 'https://api.openai.com/v1/images/generations' `
                -Headers @{ Authorization = "Bearer $key" } `
                -ContentType 'application/json; charset=utf-8' `
                -Body ([Text.Encoding]::UTF8.GetBytes($body)) `
                -TimeoutSec 600
            $item = $res.data[0]
            break
        }
        catch {
            Write-Host "  ${i}번 ${attempt}차 실패: $($_.Exception.Message)" -ForegroundColor Yellow
        }
    }

    if (-not $item) {
        Write-Host "  ${i}번 포기." -ForegroundColor Red
        continue
    }

    $path = Join-Path $OutDir "flex-dir-source-$i.png"
    [IO.File]::WriteAllBytes($path, [Convert]::FromBase64String($item.b64_json))
    Write-Host "  → $path"
}

Write-Host "고른 한 장을 assets\flex-dir-source.png 로 두고 scripts\make-ico.ps1 을 돌린다."
