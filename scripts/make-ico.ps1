#Requires -Version 7
# assets\flex-dir-source.png (1024px, 흰 배경 위 둥근 사각 타일) → assets\flex-dir.ico.
#
# 이것이 아이콘의 정본 경로다. new-icon.ps1 은 원본 PNG 를 한 번 얻는 데만 쓰고,
# .ico 는 여기서 굽는다 — 저장소를 클론한 사람에게 API 키가 필요 없다.
#
# **트레이·작업표시줄·Alt+Tab 이 서로 다른 크기를 쓴다.** 그래서 한 장이 아니라 넷을 굽는다
# (16·32·48·256). 16 은 트레이, 32 는 작업표시줄/Alt+Tab, 256 은 탐색기 큰 아이콘이다.
#
# 알파를 모델에게 받지 않는다 — 어느 이미지 모델도 투명 배경을 주지 않는다. 대신 여기서
# **행마다 흰 배경의 좌우 끝을 찾아** 잘라낸다. 타일이 가로로 볼록하므로 이것이 둥근 모서리를
# 정확히 따라가고, 모서리 반지름을 추측하지 않아도 된다.

[CmdletBinding()]
param(
    [string]$Source = (Join-Path (Split-Path -Parent $PSScriptRoot) 'assets\flex-dir-source.png'),
    [string]$Out = (Join-Path (Split-Path -Parent $PSScriptRoot) 'assets\flex-dir.ico'),

    # 배경으로 볼 밝기. 원본 배경이 순백이 아니라 #FDFDFD 근처다 (실측).
    [int]$WhiteThreshold = 246,

    [int[]]$Sizes = @(16, 32, 48, 256),

    # 이 크기들은 축소하지 않고 **직접 그린다** (사용자 결정 2026-08-06).
    # 1024 를 16 으로 줄이면 패널 둘이 파란 얼룩으로 뭉개진다 — 트레이가 쓰는 크기가 그것이다.
    # 비율은 원본에서 실측해 정수 픽셀에 반올림하므로 가장자리가 또렷하다.
    [int[]]$HandTuned = @(16, 32)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Add-Type -AssemblyName System.Drawing

if (-not (Test-Path -LiteralPath $Source)) {
    throw "원본이 없다: $Source  (scripts\new-icon.ps1 로 만든 뒤 한 장을 이 이름으로 둔다)"
}

# ── 1. 배경을 알파로 자른다 ────────────────────────────────────────
$src = New-Object System.Drawing.Bitmap $Source
$w = $src.Width; $h = $src.Height

$masked = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

$rect = New-Object System.Drawing.Rectangle 0, 0, $w, $h
$sd = $src.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$md = $masked.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::WriteOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

$buf = New-Object byte[] ($sd.Stride * $h)
[System.Runtime.InteropServices.Marshal]::Copy($sd.Scan0, $buf, 0, $buf.Length)

# 타일 가장자리 색을 모은다. 투명 픽셀의 RGB 를 이 색으로 덮어야 축소할 때 흰 테가 안 생긴다
# (GDI+ 는 premultiplied 로 섞지 않으므로 투명 픽셀의 RGB 가 그대로 번진다).
$er = 0; $eg = 0; $eb = 0; $en = 0
$minX = $w; $maxX = -1; $minY = $h; $maxY = -1

for ($y = 0; $y -lt $h; $y++) {
    $row = $y * $sd.Stride

    # 이 행에서 배경이 아닌 첫/마지막 x. 타일이 가로로 볼록하므로 그 바깥은 전부 배경이다.
    $L = -1; $R = -1
    for ($x = 0; $x -lt $w; $x++) {
        $o = $row + $x * 4
        if ($buf[$o] -lt $WhiteThreshold -or $buf[$o + 1] -lt $WhiteThreshold -or $buf[$o + 2] -lt $WhiteThreshold) {
            if ($L -lt 0) { $L = $x }
            $R = $x
        }
    }

    if ($L -lt 0) {
        # 통째로 배경인 행 — 전부 투명.
        for ($x = 0; $x -lt $w; $x++) { $buf[$row + $x * 4 + 3] = 0 }
        continue
    }

    if ($y -lt $minY) { $minY = $y }
    if ($y -gt $maxY) { $maxY = $y }
    if ($L -lt $minX) { $minX = $L }
    if ($R -gt $maxX) { $maxX = $R }

    for ($x = 0; $x -lt $w; $x++) {
        if ($x -lt $L -or $x -gt $R) { $buf[$row + $x * 4 + 3] = 0 }
    }

    # 가장자리 색은 경계에서 **몇 px 안쪽**을 떠야 한다. 경계 픽셀 자체는 흰 배경과 섞인
    # 안티앨리어싱 값이라, 그것을 평균 내면 연한 하늘색이 나오고 축소할 때 흰 테로 번진다.
    if ($R - $L -gt 16) {
        $lo = $row + ($L + 6) * 4
        $er += $buf[$lo + 2]; $eg += $buf[$lo + 1]; $eb += $buf[$lo]; $en++
    }
}

$er = [int]($er / $en); $eg = [int]($eg / $en); $eb = [int]($eb / $en)

# 투명 픽셀의 RGB 를 가장자리 색으로. 알파는 0 그대로다.
for ($y = 0; $y -lt $h; $y++) {
    $row = $y * $sd.Stride
    for ($x = 0; $x -lt $w; $x++) {
        $o = $row + $x * 4
        if ($buf[$o + 3] -eq 0) { $buf[$o] = $eb; $buf[$o + 1] = $eg; $buf[$o + 2] = $er }
    }
}

[System.Runtime.InteropServices.Marshal]::Copy($buf, 0, $md.Scan0, $buf.Length)
$src.UnlockBits($sd); $masked.UnlockBits($md); $src.Dispose()

Write-Host ("타일 {0}x{1} (가장자리 #{2:X2}{3:X2}{4:X2})" -f ($maxX - $minX + 1), ($maxY - $minY + 1), $er, $eg, $eb)

# ── 2. 타일만 잘라 정사각으로 ──────────────────────────────────────
# 모델이 낸 타일은 몇 px 씩 어긋난 직사각이다 (실측 806x808). 긴 변에 맞춰 가운데 정렬한다.
$side = [Math]::Max($maxX - $minX + 1, $maxY - $minY + 1)
$cx = ($minX + $maxX) / 2.0
$cy = ($minY + $maxY) / 2.0

$square = New-Object System.Drawing.Bitmap $side, $side, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($square)
$g.Clear([System.Drawing.Color]::FromArgb(0, $er, $eg, $eb))
$g.DrawImage($masked, [int](-($cx - $side / 2.0)), [int](-($cy - $side / 2.0)))
$g.Dispose(); $masked.Dispose()

# ── 3. 크기별로 줄인다 ────────────────────────────────────────────
# 1024 → 16 을 한 번에 줄이면 앨리어싱이 남는다. 절반씩 내려가며 줄인다.
function Resize-Half([System.Drawing.Bitmap]$img, [int]$target) {
    $cur = $img
    $mine = $false

    while ($cur.Width -ge $target * 2) {
        $half = [Math]::Max($target, [int]($cur.Width / 2))
        $next = New-Object System.Drawing.Bitmap $half, $half, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $gg = [System.Drawing.Graphics]::FromImage($next)
        $gg.InterpolationMode = 'HighQualityBicubic'
        $gg.PixelOffsetMode = 'HighQuality'
        $gg.DrawImage($cur, (New-Object System.Drawing.Rectangle 0, 0, $half, $half))
        $gg.Dispose()
        if ($mine) { $cur.Dispose() }
        $cur = $next; $mine = $true
    }

    if ($cur.Width -eq $target) {
        if ($mine) { return $cur }
        return $cur.Clone()
    }

    $final = New-Object System.Drawing.Bitmap $target, $target, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $gg = [System.Drawing.Graphics]::FromImage($final)
    $gg.InterpolationMode = 'HighQualityBicubic'
    $gg.PixelOffsetMode = 'HighQuality'
    $gg.DrawImage($cur, (New-Object System.Drawing.Rectangle 0, 0, $target, $target))
    $gg.Dispose()
    if ($mine) { $cur.Dispose() }
    return $final
}

# ── 3-b. 작은 프레임은 직접 그린다 ────────────────────────────────
# 축소로는 못 얻는 또렷함이 여기서 나온다. 축은 정수 픽셀이고, 안티앨리어싱은 둥근 모서리에만
# 건다 — 패널은 정수 경계의 직사각이라 끄는 쪽이 선명하다.
#
# 비율은 원본 타일에서 잰 값이다: 모서리 16.6% · 좌우 여백 13.8% · 패널 34.7% · 위 여백 20.7%.
# 16 을 기준 단위로 잡고 반올림하면 2+5+2+5+2 = 16 으로 정확히 떨어진다.
function Get-PatchColor([System.Drawing.Bitmap]$img, [int]$px, [int]$py) {
    # 가장 밝은 픽셀을 고른다 — 패널 안의 줄(타일과 같은 파랑)에 걸리지 않게.
    $best = $null; $bestSum = -1
    for ($dy = -8; $dy -le 8; $dy += 2) {
        for ($dx = -8; $dx -le 8; $dx += 2) {
            $c = $img.GetPixel($px + $dx, $py + $dy)
            $sum = $c.R + $c.G + $c.B
            if ($sum -gt $bestSum) { $bestSum = $sum; $best = $c }
        }
    }
    return $best
}

function New-RoundedPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-SmallFrame([int]$n, $tile, $leftPanel, $rightPanel) {
    $u = $n / 16.0
    $bmp = New-Object System.Drawing.Bitmap $n, $n, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $gg = [System.Drawing.Graphics]::FromImage($bmp)
    $gg.Clear([System.Drawing.Color]::Transparent)

    $gg.SmoothingMode = 'AntiAlias'
    $path = New-RoundedPath 0 0 $n $n (3 * $u)
    $brush = New-Object System.Drawing.SolidBrush $tile
    $gg.FillPath($brush, $path)
    $brush.Dispose(); $path.Dispose()

    $gg.SmoothingMode = 'None'
    $m = [int](2 * $u); $pw = [int](5 * $u); $gap = [int](2 * $u)
    $top = [int](3 * $u); $ph = $n - $top * 2

    $bl = New-Object System.Drawing.SolidBrush $leftPanel
    $br2 = New-Object System.Drawing.SolidBrush $rightPanel
    $gg.FillRectangle($bl, $m, $top, $pw, $ph)
    $gg.FillRectangle($br2, ($m + $pw + $gap), $top, $pw, $ph)
    $bl.Dispose(); $br2.Dispose()

    # 줄은 32 부터만. 16 에서는 1px 잡음이 되어 패널 둘을 오히려 흐린다.
    if ($n -ge 32) {
        $lineBrush = New-Object System.Drawing.SolidBrush $tile
        $lh = [int](1 * $u); $lw = $pw - [int](2 * $u); $lx = $m + [int](1 * $u)
        foreach ($k in 0, 1, 2) {
            $ly = $top + [int]((2 + $k * 3) * $u)
            $gg.FillRectangle($lineBrush, $lx, $ly, $lw, $lh)
            $gg.FillRectangle($lineBrush, ($lx + $pw + $gap), $ly, $lw, $lh)
        }
        $lineBrush.Dispose()
    }

    $gg.Dispose()
    return $bmp
}

$tileColor = [System.Drawing.Color]::FromArgb(255, $er, $eg, $eb)
$leftColor = Get-PatchColor $square ([int]($side * 0.30)) ([int]($side * 0.50))
$rightColor = Get-PatchColor $square ([int]($side * 0.70)) ([int]($side * 0.50))

Write-Host ("직접 그릴 프레임: {0}  (타일 #{1:X2}{2:X2}{3:X2} · 왼쪽 #{4:X2}{5:X2}{6:X2} · 오른쪽 #{7:X2}{8:X2}{9:X2})" -f `
    ($HandTuned -join '·'), $er, $eg, $eb, $leftColor.R, $leftColor.G, $leftColor.B, $rightColor.R, $rightColor.G, $rightColor.B)

# ── 4. .ico 로 굽는다 ─────────────────────────────────────────────
# 256 은 PNG 로 넣는다 (Vista 이후 규약, BMP 로 넣으면 256KB 를 먹는다).
# 나머지는 BMP 로 — 알림 영역의 오래된 경로가 PNG 를 못 읽는 경우가 있다.
function Get-BmpPayload([System.Drawing.Bitmap]$img) {
    $n = $img.Width
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter $ms

    # BITMAPINFOHEADER — 높이는 XOR + AND 두 장이라 두 배로 적는다.
    $bw.Write([int]40); $bw.Write([int]$n); $bw.Write([int]($n * 2))
    $bw.Write([int16]1); $bw.Write([int16]32); $bw.Write([int]0)
    $bw.Write([int]($n * $n * 4)); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)

    $r = New-Object System.Drawing.Rectangle 0, 0, $n, $n
    $d = $img.LockBits($r, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $px = New-Object byte[] ($d.Stride * $n)
    [System.Runtime.InteropServices.Marshal]::Copy($d.Scan0, $px, 0, $px.Length)
    $img.UnlockBits($d)

    # BMP 는 아래에서 위로 쌓는다.
    for ($y = $n - 1; $y -ge 0; $y--) { $bw.Write($px, $y * $d.Stride, $n * 4) }

    # AND 마스크. 32bpp 는 알파를 쓰므로 0 으로 채우되, 없으면 안 된다 (행은 4바이트 정렬).
    $maskRow = [int][Math]::Ceiling($n / 32.0) * 4
    $bw.Write((New-Object byte[] ($maskRow * $n)))

    $bw.Flush()
    $bytes = $ms.ToArray()
    $bw.Dispose(); $ms.Dispose()

    # 쉼표가 있어야 한다. PowerShell 은 함수 출력에서 배열을 풀어 내보내므로 그냥 반환하면
    # 호출부가 byte[] 가 아니라 Object[] 를 받고, BinaryWriter.Write 가 그것을 byte[] 로
    # 못 보고 bool 오버로드를 골라 **1바이트만 쓴다**. 길이 필드는 맞게 적히므로 .ico 는
    # 조용히 깨진 채로 나온다 (실제로 그랬다).
    return , $bytes
}

$payloads = @()
foreach ($size in ($Sizes | Sort-Object)) {
    $img = if ($HandTuned -contains $size) {
        New-SmallFrame $size $tileColor $leftColor $rightColor
    }
    else {
        Resize-Half $square $size
    }

    if ($size -ge 256) {
        $ms = New-Object System.IO.MemoryStream
        $img.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $payloads += , @{ Size = $size; Bytes = $ms.ToArray() }
        $ms.Dispose()
    }
    else {
        $payloads += , @{ Size = $size; Bytes = (Get-BmpPayload $img) }
    }

    $img.Dispose()
}

$square.Dispose()

$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter $fs

$bw.Write([int16]0); $bw.Write([int16]1); $bw.Write([int16]$payloads.Count)   # ICONDIR

$offset = 6 + 16 * $payloads.Count
foreach ($p in $payloads) {
    $dim = if ($p.Size -ge 256) { 0 } else { $p.Size }                        # 256 은 0 으로 적는다
    $bw.Write([byte]$dim); $bw.Write([byte]$dim)
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([int16]1); $bw.Write([int16]32)
    $bw.Write([int]$p.Bytes.Length); $bw.Write([int]$offset)
    $offset += $p.Bytes.Length
}

foreach ($p in $payloads) { $bw.Write([byte[]]$p.Bytes) }   # 캐스트를 지우지 마라 (위 주석)

$bw.Flush(); $bw.Dispose(); $fs.Dispose()

# 변수 이름을 $Sizes 와 겹치지 않게 둔다 — PowerShell 은 대소문자를 구분하지 않아
# $sizes 에 문자열을 넣으면 [int[]] 파라미터에 대입하려다 던진다.
$report = ($payloads | ForEach-Object { "$($_.Size)($([int]($_.Bytes.Length / 1024))KB)" }) -join ' · '
Write-Host "→ $Out  —  $report"

# ── 5. 구운 것을 다시 읽어 확인한다 ────────────────────────────────
# 길이 필드는 맞는데 내용만 안 실리는 종류의 실수가 있었다 (위 Get-BmpPayload 주석).
# 크기 필드만 보면 통과하므로, Windows 가 실제로 프레임을 꺼낼 수 있는지로 판정한다.
$bytes = [IO.File]::ReadAllBytes($Out)
$count = [BitConverter]::ToUInt16($bytes, 4)

if ($count -ne $payloads.Count) { throw "확인 실패: 항목 수가 $count (기대 $($payloads.Count))" }

for ($i = 0; $i -lt $count; $i++) {
    $o = 6 + $i * 16
    $len = [BitConverter]::ToInt32($bytes, $o + 8)
    $off = [BitConverter]::ToInt32($bytes, $o + 12)
    if ($off + $len -gt $bytes.Length) {
        throw "확인 실패: [$i] 이 파일 밖을 가리킨다 (off=$off len=$len 파일=$($bytes.Length))"
    }
}

# 픽셀까지 디코드해 본다. WPF 의 IconBitmapDecoder 로 여는 이유가 있다 —
# System.Drawing.Icon 은 **PNG 로 압축된 256 프레임을 꺼내지 못하고** 48 을 대신 준다.
# 그것은 파일의 흠이 아니라 그 클래스의 한계다 (탐색기·WPF·리소스 컴파일러는 읽는다).
# 그래서 트레이가 쓰는 작은 프레임은 System.Drawing 으로, 전체 목록은 WPF 로 확인한다.
Add-Type -AssemblyName PresentationCore

$uri = New-Object System.Uri ((Resolve-Path -LiteralPath $Out).Path)
$decoder = New-Object System.Windows.Media.Imaging.IconBitmapDecoder $uri,
    ([System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat),
    ([System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)

$decoded = ($decoder.Frames | ForEach-Object { $_.PixelWidth } | Sort-Object)
$wanted = ($payloads | ForEach-Object { $_.Size } | Sort-Object)

if (($decoded -join ',') -ne ($wanted -join ',')) {
    throw "확인 실패: 디코드된 프레임이 $($decoded -join '·') (기대 $($wanted -join '·'))"
}

# 트레이가 실제로 드는 경로. 16 은 System.Drawing 으로도 열려야 한다.
$tray = New-Object System.Drawing.Icon $Out, 16, 16
if ($tray.Width -ne 16) { throw "확인 실패: 트레이 16 프레임이 $($tray.Width) 로 왔다" }
$tray.Dispose()

Write-Host "확인: 프레임 $($decoded -join '·') 를 픽셀까지 디코드했다." -ForegroundColor Green
