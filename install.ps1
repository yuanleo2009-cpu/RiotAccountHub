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

# 捷徑用的圖示檔：檔名裡帶著圖示內容的「指紋」。
# 換了新 logo → 檔名跟著變 → Windows 一定會重新讀圖示，不會一直顯示存起來的舊圖示。
$iconSource = Join-Path $root 'src\RiotAccountHub\Assets\app.ico'
$iconHash   = (Get-FileHash -Algorithm SHA256 $iconSource).Hash.Substring(0, 8).ToLowerInvariant()
$installIcon = Join-Path $installDir "icon-$iconHash.ico"
Get-ChildItem -Path $installDir -Filter 'icon-*.ico' -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -ne $installIcon } | Remove-Item -Force -ErrorAction SilentlyContinue
Copy-Item -Path $iconSource -Destination $installIcon -Force

# ---- 3. 建立捷徑 ----
function New-AppShortcut([string]$path) {
    $shell = New-Object -ComObject WScript.Shell
    $lnk = $shell.CreateShortcut($path)
    $lnk.TargetPath = $installExe
    $lnk.WorkingDirectory = $installDir
    $lnk.IconLocation = "$installIcon,0"
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

# 已經有的桌面捷徑、工作列釘選，也一起換成新的圖示
$shell = New-Object -ComObject WScript.Shell
$existing = @(
    (Join-Path ([Environment]::GetFolderPath('Desktop')) $shortcutName),
    (Join-Path $env:APPDATA "Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar\$shortcutName")
)
foreach ($path in $existing) {
    if (-not (Test-Path $path)) { continue }
    $lnk = $shell.CreateShortcut($path)
    if ($lnk.TargetPath -ieq $installExe) {
        $lnk.IconLocation = "$installIcon,0"
        $lnk.Save()
    }
}

Write-Host ''
# 讓 Windows 重新整理圖示快取：換了新 logo 之後，工作列和捷徑才會顯示新的圖示
# （Windows 會把舊圖示存起來，沒有重新整理的話可能一直顯示舊的）
try { Start-Process -FilePath (Join-Path $env:WINDIR 'System32\ie4uinit.exe') -ArgumentList '-show' -Wait -WindowStyle Hidden } catch { }

Write-Host '安裝完成！' -ForegroundColor Green
Write-Host '現在可以按 Windows 鍵，輸入「Riot Account Hub」直接開啟。'
Write-Host '想固定在工作列：在搜尋結果上按右鍵 →「釘選到工作列」。'
