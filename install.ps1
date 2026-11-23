# ============================================================
#  Riot Account Hub 安裝腳本
#
#  做三件事：
#    1. 把程式打包成單一 .exe
#    2. 複製到 %LocalAppData%\Programs\RiotAccountHub\
#    3. 在「開始」功能表建立捷徑 → 之後在 Windows 搜尋打「Riot Account Hub」就找得到
#
#  更新程式時再執行一次就好（帳號資料存在 %AppData%\RiotAccountHub，不會被動到）。
#
#  用法（在專案資料夾的 PowerShell 裡）：
#    powershell -ExecutionPolicy Bypass -File .\install.ps1
#  加上 -Desktop 會順便在桌面建立捷徑：
#    powershell -ExecutionPolicy Bypass -File .\install.ps1 -Desktop
# ============================================================

param(
    [switch]$Desktop
)

$ErrorActionPreference = 'Stop'

$root       = $PSScriptRoot
$exeName    = 'RiotAccountHub.exe'
$publishDir = Join-Path $root 'src\RiotAccountHub\bin\Release\net10.0-windows\win-x64\publish'
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\RiotAccountHub'
$installExe = Join-Path $installDir $exeName
$shortcutName = 'Riot Account Hub.lnk'

# 確保找得到 dotnet 指令（剛安裝完 .NET 時，舊的視窗可能還沒更新 PATH）
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')

# 程式開著的話無法覆蓋 .exe
if (Get-Process -Name 'RiotAccountHub' -ErrorAction SilentlyContinue) {
    Write-Host '請先關閉 Riot Account Hub，再重新執行安裝。' -ForegroundColor Yellow
    exit 1
}

# ---- 1. 打包 ----
Write-Host '[1/3] 正在打包程式（第一次會比較久）...' -ForegroundColor Cyan
dotnet publish (Join-Path $root 'src\RiotAccountHub') -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) {
    Write-Host '打包失敗，請把上面的錯誤訊息貼給 Claude。' -ForegroundColor Red
    exit 1
}

# ---- 2. 複製到安裝位置 ----
Write-Host "[2/3] 複製到 $installDir" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Copy-Item -Path (Join-Path $publishDir $exeName) -Destination $installExe -Force

# ---- 3. 建立捷徑 ----
function New-AppShortcut([string]$path) {
    $shell = New-Object -ComObject WScript.Shell
    $lnk = $shell.CreateShortcut($path)
    $lnk.TargetPath = $installExe
    $lnk.WorkingDirectory = $installDir
    $lnk.IconLocation = "$installExe,0"
    $lnk.Description = 'Valorant / Riot 帳號切換器'
    $lnk.Save()
}

Write-Host '[3/3] 建立「開始」功能表捷徑' -ForegroundColor Cyan
$startMenuShortcut = Join-Path ([Environment]::GetFolderPath('Programs')) $shortcutName
New-AppShortcut $startMenuShortcut

if ($Desktop) {
    New-AppShortcut (Join-Path ([Environment]::GetFolderPath('Desktop')) $shortcutName)
    Write-Host '      也已在桌面建立捷徑' -ForegroundColor Cyan
}

Write-Host ''
Write-Host '安裝完成！' -ForegroundColor Green
Write-Host '現在可以按 Windows 鍵，輸入「Riot Account Hub」直接開啟。'
Write-Host '想固定在工作列：在搜尋結果上按右鍵 →「釘選到工作列」。'
