<#
.SYNOPSIS
    툴바의 외부 도구 버튼 둘이 실제로 그려지는지 확인한다 (phase 5 step 8).

.DESCRIPTION
    저장소 Debug 빌드를 띄우고, UI Automation 으로 [VS Code 로 열기]·[터미널로 열기]
    버튼을 찾아 그 자리를 PrintWindow 캡처에서 세어 본다.

    재는 것은 "그려졌는가" 하나다. **"예쁜가·구분되는가" 는 사람이 본다** (CLAUDE.md §5) —
    그리고 PrintWindow 로는 "잘렸는가" 도 판정할 수 없다(검은 테두리가 잘린 자리를 덮는다).

    합성 마우스 입력을 쓰지 않는다 — 포그라운드를 못 잡으면 조용히 실패하고 클릭이 남의
    창으로 간다. UI Automation 조회는 포그라운드가 필요 없다.

    끝나면 반드시 FlexDir.Host 를 죽인다. 살려 두면 저장소 Debug 산출물이 잠겨
    다음 게이트가 깨진다.
#>
[CmdletBinding()]
param(
    # 16×16 안에서 배경과 다른 픽셀이 이만큼은 있어야 "그려졌다" 로 본다.
    # 빈 버튼은 0 이고 >_ 는 대략 90~110, 리본은 120 이 넘는다. 40 은 "무언가 그려졌다" 를
    # 가르는 자리이지 "제대로 그려졌다" 를 가르는 자리가 아니다.
    [int] $MinimumPixels = 40
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'src\FlexDir.Host\bin\Debug\net9.0-windows\FlexDir.Host.exe'
$artifacts = Join-Path $root 'artifacts'

if (-not (Test-Path $exe)) {
    Write-Error "Debug 빌드가 없다: $exe — 먼저 dotnet build 를 돌려라."
}

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing

# P/Invoke 만 C# 으로 낸다 — Bitmap·Graphics 를 C# 쪽에서 만지면 .NET 10 에서
# System.Drawing 이 쪼개진 어셈블리(Primitives · Private.Windows.GdiPlus)를 전부
# 참조로 줘야 컴파일된다. 그림은 PowerShell 쪽에서 만든다.
$shot = @'
using System;
using System.Runtime.InteropServices;

public static class WindowShot
{
    // PW_RENDERFULLCONTENT(2) 가 있어야 WPF 처럼 DirectComposition 으로 그리는 창이
    // 빈 비트맵으로 나오지 않는다.
    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
}
'@

if (-not ('WindowShot' -as [type])) { Add-Type -TypeDefinition $shot }

function Copy-Window {
    param([IntPtr] $Handle)

    $rect = New-Object WindowShot+RECT

    if (-not [WindowShot]::GetWindowRect($Handle, [ref] $rect)) { Write-Error 'GetWindowRect 실패' }

    $bitmap = New-Object System.Drawing.Bitmap(($rect.Right - $rect.Left), ($rect.Bottom - $rect.Top))
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()

    try { [WindowShot]::PrintWindow($Handle, $hdc, 2) | Out-Null }
    finally {
        $graphics.ReleaseHdc($hdc)
        $graphics.Dispose()
    }

    return [PSCustomObject]@{ Bitmap = $bitmap; Left = $rect.Left; Top = $rect.Top }
}

function Find-ToolButton {
    param([System.Windows.Automation.AutomationElement] $Window, [string] $Name)

    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $Name)

    return $Window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

# 한 칸에서 가장 흔한 색을 배경으로 보고, 그것과 다른 픽셀을 센다. 배경색을 박아 두지
# 않는 이유: 다크·라이트에서 값이 갈리고 호버로도 흔들린다.
function Measure-DrawnPixels {
    param([System.Drawing.Bitmap] $Bitmap, [int] $X, [int] $Y, [int] $Size = 16)

    $values = foreach ($dy in 0..($Size - 1)) {
        foreach ($dx in 0..($Size - 1)) {
            $Bitmap.GetPixel($X + $dx, $Y + $dy).ToArgb()
        }
    }

    $background = ($values | Group-Object | Sort-Object -Property Count -Descending | Select-Object -First 1).Name

    return @($values | Where-Object { "$_" -ne $background }).Count
}

# 상주 앱이라 두 번째 실행은 즉시 종료된다 — 이미 떠 있으면 먼저 죽인다.
Stop-Process -Name FlexDir.Host -Force -ErrorAction SilentlyContinue

$bitmap = $null
$failures = New-Object System.Collections.Generic.List[string]

try {
    $process = Start-Process -FilePath $exe -PassThru

    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    $hwnd = [IntPtr]::Zero

    while ([DateTime]::UtcNow -lt $deadline) {
        $process.Refresh()

        if ($process.HasExited) {
            Write-Error "FlexDir.Host 가 창을 띄우기 전에 종료됐다 (exit $($process.ExitCode))."
        }

        if ($process.MainWindowHandle -ne [IntPtr]::Zero) { $hwnd = $process.MainWindowHandle; break }

        Start-Sleep -Milliseconds 200
    }

    if ($hwnd -eq [IntPtr]::Zero) { Write-Error '20초 안에 창이 뜨지 않았다.' }

    $window = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
    $names = @('VS Code 로 열기', '터미널로 열기')
    $buttons = @{}

    # 창 핸들이 생긴 직후에는 아직 트리가 다 서지 않는다 — 시한 안에서 다시 묻는다.
    $deadline = [DateTime]::UtcNow.AddSeconds(20)

    while ([DateTime]::UtcNow -lt $deadline) {
        foreach ($name in $names) {
            if (-not $buttons.ContainsKey($name)) {
                $found = Find-ToolButton -Window $window -Name $name

                if ($null -ne $found) { $buttons[$name] = $found }
            }
        }

        if ($buttons.Count -eq $names.Count) { break }

        Start-Sleep -Milliseconds 200
    }

    foreach ($name in $names) {
        if (-not $buttons.ContainsKey($name)) { Write-Error "UI Automation 트리에 '$name' 버튼이 없다." }
    }

    # 첫 렌더가 끝날 틈을 준다. 여기서 찍은 그림이 판정의 전부다.
    Start-Sleep -Milliseconds 800

    $capture = Copy-Window -Handle $hwnd
    $bitmap = $capture.Bitmap
    $left = $capture.Left
    $top = $capture.Top

    Write-Host ("창 {0}x{1} @ ({2},{3})" -f $bitmap.Width, $bitmap.Height, $left, $top)

    foreach ($name in $names) {
        $rect = $buttons[$name].Current.BoundingRectangle

        # BoundingRectangle 은 화면 좌표라 창 원점을 뺀다. 버튼은 32×28 이고 아이콘은
        # 그 가운데 15 라 가운데 16×16 만 센다 — 옆 구분자가 섞이지 않는 폭이다.
        $x = [int][Math]::Round($rect.X - $left + ($rect.Width - 16) / 2)
        $y = [int][Math]::Round($rect.Y - $top + ($rect.Height - 16) / 2)

        Write-Host ("{0,-16} 화면 ({1},{2}) {3}x{4}" -f $name, $rect.X, $rect.Y, $rect.Width, $rect.Height)

        if ($x -lt 0 -or $y -lt 0 -or ($x + 16) -gt $bitmap.Width -or ($y + 16) -gt $bitmap.Height) {
            $failures.Add("'$name' 자리가 캡처 밖이다 ($x,$y).")

            continue
        }

        $drawn = Measure-DrawnPixels -Bitmap $bitmap -X $x -Y $y
        $verdict = if ($drawn -ge $MinimumPixels) { 'OK' } else { '비었다' }

        Write-Host ("{0,-16} 캡처 ({1},{2}) 그려진 픽셀 {3} — {4}" -f $name, $x, $y, $drawn, $verdict)

        if ($drawn -lt $MinimumPixels) {
            $failures.Add("'$name' 자리에 그려진 픽셀이 $drawn 개뿐이다 (기준 $MinimumPixels).")
        }
    }

    if (-not (Test-Path $artifacts)) { New-Item -ItemType Directory -Path $artifacts | Out-Null }

    $png = Join-Path $artifacts ("toolbar-{0}.png" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
    $bitmap.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)

    Write-Host "캡처: $png"
}
finally {
    if ($null -ne $bitmap) { $bitmap.Dispose() }

    # 살려 두면 다음 게이트가 파일 잠금으로 깨진다 (.harness/HANDOFF.md §배포 절차 ①).
    Stop-Process -Name FlexDir.Host -Force -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Host "실패: $_" }

    exit 1
}

Write-Host '두 버튼 다 그려졌다.'
exit 0
