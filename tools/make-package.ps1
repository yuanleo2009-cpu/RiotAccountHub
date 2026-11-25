# 產生「下載包」和「更新包」，放在 dist\ 資料夾（不會上傳到 git）
#
#   RiotAccountHub-v<版本>.zip          完整下載包（第一次使用的人下載這個）
#     └─ Riot Account Hub\
#          ├─ RiotAccountHub.exe      （單一檔案，不需要另外安裝 .NET）
#          └─ 安裝說明.txt
#
#   RiotAccountHub-update-v<版本>.zip   更新包（已經在用舊版的人下載這個）
#     ├─ RiotAccountHub.exe
#     └─ 更新說明.txt
#
#   update.json                        最新版的說明檔：程式內的「檢查更新」會讀它
#                                      （版本、更新內容、更新包的網址、大小、SHA-256 指紋）
#
# 更新內容取自 CHANGELOG.md 裡這個版本的那一段，沒寫的話會停下來提醒。
#
# 用法（在專案根目錄執行）：powershell -ExecutionPolicy Bypass -File tools\make-package.ps1
# 發布到 GitHub 請用 tools\publish-release.ps1（它會先呼叫這個腳本）
#
# 只給測試用的參數：
#   -Version 1.0.5 -OutDir <資料夾> -DownloadBase http://127.0.0.1:8000
#   做一個假的新版本放到別的資料夾，網址指向本機，用來測試程式內更新。

param(
    [string]$Version,
    [string]$OutDir,
    [string]$DownloadBase
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$repo = 'yuanleo2009-cpu/RiotAccountHub'

# 版本號：沒指定就從專案檔讀（RiotAccountHub.csproj 裡的 <Version>）
[xml]$csproj = Get-Content (Join-Path $root 'src\RiotAccountHub\RiotAccountHub.csproj') -Encoding UTF8
$projectVersion = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $projectVersion) { throw '在 RiotAccountHub.csproj 找不到 <Version>' }
$isTest = [bool]$Version
if (-not $Version) { $Version = $projectVersion }
if (-not $OutDir) { $OutDir = Join-Path $root 'dist' }
if (-not $DownloadBase) { $DownloadBase = "https://github.com/$repo/releases/download/v$Version" }

# ---- 更新內容：CHANGELOG.md 裡 "## [版本] - 日期" 底下的 "- " 項目 ----
$changelog = Get-Content (Join-Path $root 'CHANGELOG.md') -Encoding UTF8
$notes = @()
$date = (Get-Date -Format 'yyyy-MM-dd')
$inSection = $false
foreach ($line in $changelog) {
    if ($line -match '^## \[(.+?)\](?:\s*-\s*(\S+))?') {
        if ($inSection) { break }
        if ($Matches[1] -eq $Version) { $inSection = $true; if ($Matches[2]) { $date = $Matches[2] } }
        continue
    }
    if ($inSection -and $line -match '^\s*[-*]\s+(.+)$') {
        # 拿掉 Markdown 的粗體、程式碼、連結記號，只留文字
        $text = $Matches[1] -replace '\*\*', '' -replace '`', '' -replace '\[([^\]]+)\]\([^)]*\)', '$1'
        $notes += $text.Trim()
    }
}
if ($notes.Count -eq 0) {
    if ($isTest) { $notes = @("測試用的版本 $Version") }
    else { throw "CHANGELOG.md 裡沒有 [$Version] 這一段（或底下沒有任何 - 項目）。請先寫好這一版的更新內容。" }
}

# ---- 1. 打包成單一 .exe ----
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
$publishDir = if ($isTest) { Join-Path $OutDir 'publish' } else { Join-Path $root 'src\RiotAccountHub\bin\Release\net10.0-windows\win-x64\publish' }
Write-Host "[1/4] 打包 v$Version ..." -ForegroundColor Cyan
$publishArgs = @((Join-Path $root 'src\RiotAccountHub'), '-c', 'Release', '--nologo', '-v', 'quiet')
if ($isTest) { $publishArgs += @("-p:Version=$Version", '-o', $publishDir) }
dotnet publish @publishArgs
if ($LASTEXITCODE -ne 0) { throw '打包失敗' }
$exe = Join-Path $publishDir 'RiotAccountHub.exe'

New-Item -ItemType Directory -Force $OutDir | Out-Null
$staging = Join-Path $OutDir 'staging'
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }

# ---- 2. 完整下載包：程式 + 安裝說明 ----
Write-Host '[2/4] 完整下載包' -ForegroundColor Cyan
$folder = Join-Path $staging 'full\Riot Account Hub'
New-Item -ItemType Directory -Force $folder | Out-Null
Copy-Item $exe $folder
Copy-Item (Join-Path $root 'docs\安裝說明.txt') $folder
$fullZip = Join-Path $OutDir "RiotAccountHub-v$Version.zip"
if (Test-Path $fullZip) { Remove-Item $fullZip -Force }
Compress-Archive -Path $folder -DestinationPath $fullZip -CompressionLevel Optimal

# ---- 3. 更新包：程式 + 更新說明 ----
Write-Host '[3/4] 更新包' -ForegroundColor Cyan
$updFolder = Join-Path $staging 'update'
New-Item -ItemType Directory -Force $updFolder | Out-Null
Copy-Item $exe $updFolder
Copy-Item (Join-Path $root 'docs\更新說明.txt') $updFolder
$updName = "RiotAccountHub-update-v$Version.zip"
$updZip = Join-Path $OutDir $updName
if (Test-Path $updZip) { Remove-Item $updZip -Force }
Compress-Archive -Path (Join-Path $updFolder '*') -DestinationPath $updZip -CompressionLevel Optimal
Remove-Item $staging -Recurse -Force

# ---- 4. update.json ----
Write-Host '[4/4] update.json' -ForegroundColor Cyan
$sha = (Get-FileHash -Algorithm SHA256 $updZip).Hash.ToLowerInvariant()
$size = (Get-Item $updZip).Length
$manifest = [ordered]@{
    version = $Version
    date    = $date
    notes   = @($notes)
    url     = "$DownloadBase/$updName"
    sha256  = $sha
    size    = $size
    page    = "https://github.com/$repo/releases/tag/v$Version"
}
$json = $manifest | ConvertTo-Json -Depth 3
# 不要 BOM：有些程式讀 JSON 時看到 BOM 會出錯
[IO.File]::WriteAllText((Join-Path $OutDir 'update.json'), $json, (New-Object Text.UTF8Encoding $false))

Write-Host ''
Write-Host "完成（v$Version）：" -ForegroundColor Green
Write-Host ("  完整下載包  {0}（{1} MB）" -f $fullZip, [math]::Round((Get-Item $fullZip).Length / 1MB, 1))
Write-Host ("  更新包      {0}（{1} MB）" -f $updZip, [math]::Round($size / 1MB, 1))
Write-Host ("  說明檔      {0}" -f (Join-Path $OutDir 'update.json'))
