# 產生程式圖示 app.ico(紅色圓角方塊 + 白色雙向切換箭頭)
# 這是自己畫的簡單圖示,沒有使用任何 Riot / VALORANT 的商標。
# 用法(在專案根目錄執行):  powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1

Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot "..\src\RiotAccountHub\Assets\app.ico"
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null

function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    # 圓角方塊背景(紅色漸層)
    $r = [single]($size * 0.22)
    $rect = New-Object System.Drawing.RectangleF 0, 0, ($size - 1), ($size - 1)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($rect.X, $rect.Y, $r * 2, $r * 2, 180, 90)
    $path.AddArc($rect.Right - $r * 2, $rect.Y, $r * 2, $r * 2, 270, 90)
    $path.AddArc($rect.Right - $r * 2, $rect.Bottom - $r * 2, $r * 2, $r * 2, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $r * 2, $r * 2, $r * 2, 90, 90)
    $path.CloseFigure()
    $c1 = [System.Drawing.Color]::FromArgb(255, 239, 83, 80)
    $c2 = [System.Drawing.Color]::FromArgb(255, 183, 28, 28)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, $c1, $c2, 45
    $g.FillPath($brush, $path)

    # 兩個方向相反的箭頭(代表「切換」)
    $w = [single][Math]::Max(1.5, $size * 0.085)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), $w
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
    $s = [single]$size
    # 上方箭頭:往右
    $g.DrawLine($pen, $s * 0.24, $s * 0.36, $s * 0.74, $s * 0.36)
    $g.DrawLines($pen, [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF ($s * 0.60), ($s * 0.23)),
        (New-Object System.Drawing.PointF ($s * 0.74), ($s * 0.36)),
        (New-Object System.Drawing.PointF ($s * 0.60), ($s * 0.49))))
    # 下方箭頭:往左
    $g.DrawLine($pen, $s * 0.76, $s * 0.64, $s * 0.26, $s * 0.64)
    $g.DrawLines($pen, [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF ($s * 0.40), ($s * 0.51)),
        (New-Object System.Drawing.PointF ($s * 0.26), ($s * 0.64)),
        (New-Object System.Drawing.PointF ($s * 0.40), ($s * 0.77))))

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

# 把多個尺寸的 PNG 包成一個 .ico 檔
$sizes = 16, 24, 32, 48, 64, 128, 256
$images = @(); foreach ($s in $sizes) { $images += ,(New-IconPng $s) }

$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $images[$i].Length
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$len); $bw.Write([UInt32]$offset)
    $offset += $len
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Close()
Write-Host "已產生圖示:$out"
