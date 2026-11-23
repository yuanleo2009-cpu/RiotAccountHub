# ============================================================
#  Riot Account Hub 解除安裝腳本
#
#  會移除：
#    - %LocalAppData%\Programs\RiotAccountHub\（程式本體）
#    - 「開始」功能表與桌面上的捷徑
#
#  不會移除：%AppData%\RiotAccountHub\（你的帳號清單與加密快照）。
#  如果也想刪掉，請先在程式裡按「還原原本狀態」，再手動刪除那個資料夾。
#
#  用法：
#    powershell -ExecutionPolicy Bypass -File .\uninstall.ps1
# ============================================================

$ErrorActionPreference = 'Stop'

if (Get-Process -Name 'RiotAccountHub' -ErrorAction SilentlyContinue) {
    Write-Host '請先關閉 Riot Account Hub，再重新執行。' -ForegroundColor Yellow
    exit 1
}

$shortcutName = 'Riot Account Hub.lnk'
$targets = @(
    (Join-Path ([Environment]::GetFolderPath('Programs')) $shortcutName),
    (Join-Path ([Environment]::GetFolderPath('Desktop')) $shortcutName),
    (Join-Path $env:LOCALAPPDATA 'Programs\RiotAccountHub')
)

foreach ($t in $targets) {
    if (Test-Path $t) {
        Remove-Item -Path $t -Recurse -Force
        Write-Host "已移除：$t"
    }
}

Write-Host ''
Write-Host '解除安裝完成。帳號資料仍保留在：' -ForegroundColor Green
Write-Host (Join-Path $env:APPDATA 'RiotAccountHub')
