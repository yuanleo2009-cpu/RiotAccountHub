# 產生程式圖示 app.ico
#
# 設計概念：「準星 ＋ 循環箭頭」
#   - 中間：FPS 遊戲的準星（四條短線 + 中心點）→ 代表射擊遊戲
#   - 外圈：一紅一白兩段互相追逐的箭頭 → 代表「切換帳號」
#   - 背景：深色圓角方塊 + 中央淡淡的紅光，搭配程式的深色紅色主題
# 全部是自己畫的幾何圖形，沒有使用任何 Riot / VALORANT 的商標或圖示。
#
# 用法（在專案根目錄執行）：powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
# 加上 -PreviewPng 路徑 可以另外輸出一張 512x512 的預覽圖

param([string]$PreviewPng)

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$out = Join-Path $PSScriptRoot "..\src\RiotAccountHub\Assets\app.ico"
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null

function New-Color([string]$hex) { [System.Windows.Media.ColorConverter]::ConvertFromString($hex) }
function New-Brush([string]$hex) { $b = New-Object System.Windows.Media.SolidColorBrush (New-Color $hex); $b.Freeze(); $b }
function Pt([double]$x, [double]$y) { New-Object System.Windows.Point $x, $y }

# 圓上的點（角度：0 度 = 右邊，順時針為正，因為螢幕的 y 軸向下）
function PolarPoint([double]$cx, [double]$cy, [double]$r, [double]$deg) {
    $rad = $deg * [Math]::PI / 180
    Pt ($cx + $r * [Math]::Cos($rad)) ($cy + $r * [Math]::Sin($rad))
}

# 畫一段弧線（順時針，從 a1 到 a2 度）
function New-Arc([double]$cx, [double]$cy, [double]$r, [double]$a1, [double]$a2) {
    $g = New-Object System.Windows.Media.StreamGeometry
    $ctx = $g.Open()
    $ctx.BeginFigure((PolarPoint $cx $cy $r $a1), $false, $false)
    $large = ($a2 - $a1) -gt 180
    $ctx.ArcTo((PolarPoint $cx $cy $r $a2), (New-Object System.Windows.Size $r, $r), 0, $large,
        [System.Windows.Media.SweepDirection]::Clockwise, $true, $true)
    $ctx.Close(); $g.Freeze(); $g
}

# 在弧線終點畫一個順時針方向的箭頭（三角形）
function New-ArrowHead([double]$cx, [double]$cy, [double]$r, [double]$deg, [double]$len, [double]$halfWidth) {
    $rad = $deg * [Math]::PI / 180
    $base = PolarPoint $cx $cy $r $deg
    $tx = -[Math]::Sin($rad); $ty = [Math]::Cos($rad)   # 切線方向（順時針）
    $nx = [Math]::Cos($rad);  $ny = [Math]::Sin($rad)   # 往外的方向
    # 箭頭尖端稍微往圓心彎，順著圓弧的方向，看起來才不會往外歪
    $tip = Pt ($base.X + $tx * $len - $nx * $len * 0.28) ($base.Y + $ty * $len - $ny * $len * 0.28)
    $p1 = Pt ($base.X + $nx * $halfWidth) ($base.Y + $ny * $halfWidth)
    $p2 = Pt ($base.X - $nx * $halfWidth) ($base.Y - $ny * $halfWidth)
    $g = New-Object System.Windows.Media.StreamGeometry
    $ctx = $g.Open()
    $ctx.BeginFigure($p1, $true, $true)
    $ctx.LineTo($tip, $true, $true)
    $ctx.LineTo($p2, $true, $true)
    $ctx.Close(); $g.Freeze(); $g
}

# 用 256 x 256 的座標畫，再縮放成各種尺寸
function Draw-Icon($dc, [int]$size) {
    $s = $size / 256.0
    $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform $s, $s))
    $small = $size -le 24   # 很小的尺寸：省略細節、線條加粗，才看得清楚

    # ---- 背景：深色圓角方塊 ----
    $bg = New-Object System.Windows.Media.LinearGradientBrush (New-Color '#FF262A35'), (New-Color '#FF0C0E13'), (Pt 0 0), (Pt 1 1)
    $rect = New-Object System.Windows.Rect 4, 4, 248, 248
    $dc.DrawRoundedRectangle($bg, $null, $rect, 58, 58)

    # 中央的紅色光暈
    $glow = New-Object System.Windows.Media.RadialGradientBrush
    $glow.GradientStops.Add((New-Object System.Windows.Media.GradientStop (New-Color '#66E5484D'), 0))
    $glow.GradientStops.Add((New-Object System.Windows.Media.GradientStop (New-Color '#00E5484D'), 1))
    $dc.DrawRoundedRectangle($glow, $null, $rect, 58, 58)

    # 細細的外框，讓圖示在深色工作列上也有邊界
    $edge = New-Object System.Windows.Media.Pen (New-Brush '#33FFFFFF'), 3
    $dc.DrawRoundedRectangle($null, $edge, (New-Object System.Windows.Rect 5.5, 5.5, 245, 245), 57, 57)

    $cx = 128; $cy = 128
    $r = 74
    $w = if ($small) { 24 } else { 17 }

    # ---- 外圈：兩段互相追逐的箭頭（切換） ----
    $red = New-Object System.Windows.Media.LinearGradientBrush (New-Color '#FFFF6B6F'), (New-Color '#FFD13A40'), (Pt 0 0), (Pt 1 1)
    $white = New-Brush '#FFF4F5F7'
    $capRound = [System.Windows.Media.PenLineCap]::Round

    $penRed = New-Object System.Windows.Media.Pen $red, $w
    $penRed.StartLineCap = $capRound
    $penWhite = New-Object System.Windows.Media.Pen $white, $w
    $penWhite.StartLineCap = $capRound

    # 紅色：上半圈（左上 → 右上），白色：下半圈（右下 → 左下）
    $dc.DrawGeometry($null, $penRed, (New-Arc $cx $cy $r 196 330))
    $dc.DrawGeometry($null, $penWhite, (New-Arc $cx $cy $r 16 150))
    $headLen = if ($small) { 30 } else { 27 }
    $headHalf = if ($small) { 26 } else { 20 }
    $dc.DrawGeometry($red, $null, (New-ArrowHead $cx $cy $r 329 $headLen $headHalf))
    $dc.DrawGeometry($white, $null, (New-ArrowHead $cx $cy $r 149 $headLen $headHalf))

    # ---- 中間：準星 ----
    $tick = if ($small) { 14 } else { 10 }
    $penTick = New-Object System.Windows.Media.Pen $white, $tick
    $penTick.StartLineCap = $capRound; $penTick.EndLineCap = $capRound
    if (-not $small) {
        $in = 20; $outR = 42
        $dc.DrawLine($penTick, (Pt $cx ($cy - $outR)), (Pt $cx ($cy - $in)))
        $dc.DrawLine($penTick, (Pt $cx ($cy + $in)), (Pt $cx ($cy + $outR)))
        $dc.DrawLine($penTick, (Pt ($cx - $outR) $cy), (Pt ($cx - $in) $cy))
        $dc.DrawLine($penTick, (Pt ($cx + $in) $cy), (Pt ($cx + $outR) $cy))
    }
    $dotR = if ($small) { 16 } else { 8 }
    $dc.DrawEllipse($red, $null, (Pt $cx $cy), $dotR, $dotR)

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
