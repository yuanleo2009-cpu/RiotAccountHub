# 產生「給朋友的下載包」：一個 zip，裡面有程式和安裝說明
#
#   RiotAccountHub-v1.0.1.zip
#     └─ Riot Account Hub\
#          ├─ RiotAccountHub.exe   （單一檔案，不需要另外安裝 .NET）
#          └─ 安裝說明.txt
#
# 用法（在專案根目錄執行）：powershell -ExecutionPolicy Bypass -File tools\make-package.ps1
# 產生的檔案放在 dist\ 資料夾（不會上傳到 git）

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

# 版本號從專案檔讀（RiotAccountHub.csproj 裡的 <Version>）
[xml]$csproj = Get-Content (Join-Path $root 'src\RiotAccountHub\RiotAccountHub.csproj') -Encoding UTF8
$version = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw '在 RiotAccountHub.csproj 找不到 <Version>' }

# 1. 打包成單一 .exe
Write-Host "[1/3] 打包 v$version ..." -ForegroundColor Cyan
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
dotnet publish (Join-Path $root 'src\RiotAccountHub') -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw '打包失敗' }
$exe = Join-Path $root 'src\RiotAccountHub\bin\Release\net10.0-windows\win-x64\publish\RiotAccountHub.exe'

# 2. 準備資料夾：程式 + 安裝說明
Write-Host '[2/3] 準備檔案' -ForegroundColor Cyan
$dist = Join-Path $root 'dist'
$staging = Join-Path $dist 'staging'
$folder = Join-Path $staging 'Riot Account Hub'
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Force $folder | Out-Null
Copy-Item $exe $folder
Copy-Item (Join-Path $root 'docs\安裝說明.txt') $folder

# 3. 壓縮成 zip
$zip = Join-Path $dist "RiotAccountHub-v$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Write-Host '[3/3] 壓縮' -ForegroundColor Cyan
Compress-Archive -Path $folder -DestinationPath $zip -CompressionLevel Optimal
Remove-Item $staging -Recurse -Force

$size = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host ''
Write-Host "完成：$zip（$size MB）" -ForegroundColor Green
