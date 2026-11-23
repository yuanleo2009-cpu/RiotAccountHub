# 產生程式圖示 app.ico 與 logo.png
#
# 設計概念：「對向雙箭」（配合介面風格「夜戰紅線」）
#   - 兩支方向相反、稜角分明的斜體箭頭：紅色往右、白色往左 → 代表「切換帳號」
#   - 箭頭整體往右傾斜，有速度感；紅色就是介面裡唯一的強調色 #FF4655
#   - 背景：接近純黑的圓角方塊，右上角一點紅色光暈（跟主畫面的背景一樣）
# 全部是自己畫的幾何圖形，沒有使用任何 Riot / VALORANT 的商標或圖示。
#
# 用法（在專案根目錄執行）：powershell -STA -ExecutionPolicy Bypass -File tools\make-icon.ps1
# 加上 -PreviewPng 路徑 可以另外輸出一張 512x512 的預覽圖

param([string]$PreviewPng)

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$out = Join-Path $PSScriptRoot "..\src\RiotAccountHub\Assets\app.ico"
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null

function New-Color([string]$hex) { [System.Windows.Media.ColorConverter]::ConvertFromString($hex) }
function New-Brush([string]$hex) { $b = New-Object System.Windows.Media.SolidColorBrush (New-Color $hex); $b.Freeze(); $b }
function Pt([double]$x, [double]$y) { New-Object System.Windows.Point $x, $y }

# 把一串座標連成一個封閉的多邊形
function New-Polygon([double[]]$xy) {
    $g = New-Object System.Windows.Media.StreamGeometry
    $ctx = $g.Open()
    $ctx.BeginFigure((Pt $xy[0] $xy[1]), $true, $true)
    for ($i = 2; $i -lt $xy.Length; $i += 2) { $ctx.LineTo((Pt $xy[$i] $xy[$i + 1]), $true, $false) }
    $ctx.Close(); $g.Freeze(); $g
}

# 用 256 x 256 的座標畫，再縮放成各種尺寸
function Draw-Icon($dc, [int]$size) {
    $s = $size / 256.0
    $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform $s, $s))
    $small = $size -le 24   # 很小的尺寸：箭頭放大一點、省略光暈，才看得清楚

    # ---- 背景：接近純黑的圓角方塊 ----
    $rect = New-Object System.Windows.Rect 4, 4, 248, 248
    $bg = New-Object System.Windows.Media.LinearGradientBrush (New-Color '#FF1A1F2A'), (New-Color '#FF090B0F'), (Pt 0 0), (Pt 1 1)
    $dc.DrawRoundedRectangle($bg, $null, $rect, 56, 56)

    if (-not $small) {
        # 右上角的紅色光暈
        $glow = New-Object System.Windows.Media.RadialGradientBrush
        $glow.Center = Pt 0.85 0.1; $glow.GradientOrigin = Pt 0.85 0.1; $glow.RadiusX = 0.75; $glow.RadiusY = 0.75
        $glow.GradientStops.Add((New-Object System.Windows.Media.GradientStop (New-Color '#55FF4655'), 0))
        $glow.GradientStops.Add((New-Object System.Windows.Media.GradientStop (New-Color '#00FF4655'), 1))
        $dc.DrawRoundedRectangle($glow, $null, $rect, 56, 56)
    }

    # 細細的外框，讓圖示在深色工作列上也有邊界
    $edge = New-Object System.Windows.Media.Pen (New-Brush '#30FFFFFF'), 3
    $dc.DrawRoundedRectangle($null, $edge, (New-Object System.Windows.Rect 5.5, 5.5, 245, 245), 55, 55)

    # ---- 兩支箭頭：以中心放大（小尺寸更大），再整體往右傾斜 12 度 ----
    $zoom = if ($small) { 1.16 } else { 1.0 }
    $group = New-Object System.Windows.Media.TransformGroup
    $group.Children.Add((New-Object System.Windows.Media.ScaleTransform $zoom, $zoom, 128, 128))
    $group.Children.Add((New-Object System.Windows.Media.SkewTransform -12, 0, 128, 128))
    $dc.PushTransform($group)

    # 上面：紅色箭頭，往右
    $red = New-Object System.Windows.Media.LinearGradientBrush (New-Color '#FFFF6470'), (New-Color '#FFE23A49'), (Pt 0 0), (Pt 1 1)
    $dc.DrawGeometry($red, $null, (New-Polygon @(54,82, 148,82, 148,62, 206,97, 148,132, 148,112, 54,112)))

    # 下面：白色箭頭，往左
    $white = New-Brush '#FFF3F5F8'
    $dc.DrawGeometry($white, $null, (New-Polygon @(202,144, 108,144, 108,124, 50,159, 108,194, 108,174, 202,174)))

    $dc.Pop()
    $dc.Pop()
}

function Render-Png([int]$size) {
    $dv = New-Object System.Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    Draw-Icon $dc $size
    $dc.Close()
    $bmp = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($dv)
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $ms = New-Object System.IO.MemoryStream
    $enc.Save($ms)
    return ,$ms.ToArray()
}

# 把多個尺寸的 PNG 包成一個 .ico 檔
$sizes = 16, 24, 32, 48, 64, 128, 256
$images = @(); foreach ($sz in $sizes) { $images += ,(Render-Png $sz) }

$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $len = $images[$i].Length
    $dim = if ($sz -ge 256) { 0 } else { $sz }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$len); $bw.Write([UInt32]$offset)
    $offset += $len
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Close()
Write-Host "已產生圖示：$out"

# 程式畫面上方標題用的高解析度 PNG（.ico 在畫面裡常被挑到小尺寸，細節會不見）
$logo = Join-Path (Split-Path $out) "logo.png"
[System.IO.File]::WriteAllBytes($logo, (Render-Png 256))
Write-Host "已產生標題圖：$logo"

if ($PreviewPng) {
    [System.IO.File]::WriteAllBytes($PreviewPng, (Render-Png 512))
    Write-Host "已產生預覽圖：$PreviewPng"
}
