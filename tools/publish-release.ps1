# 發布新版到 GitHub（每次出新版都用這個）
#
# 做的事：
#   1. 檢查：程式碼都已經 commit、在 main 分支、這個版本還沒發布過、CHANGELOG.md 寫好了
#   2. 跑全部的單元測試
#   3. 產生完整下載包、更新包、update.json（tools\make-package.ps1）
#   4. 推送程式碼，建立 GitHub Release，上傳三個檔案
#   5. 確認 GitHub 上的檔案跟本機的一模一樣
#
# 發布之後：
#   - 舊版（v1.0.4 以後）的程式會自己看到「有新版本」，按一下就能更新
#   - 個人作品集網頁會自動從 GitHub 抓到新版（完整下載包和更新包都會出現）
#
# 用法：先改 RiotAccountHub.csproj 的 <Version>、在 CHANGELOG.md 最上面寫好這一版，commit，然後
#   powershell -ExecutionPolicy Bypass -File tools\publish-release.ps1

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$repo = 'yuanleo2009-cpu/RiotAccountHub'
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')

[xml]$csproj = Get-Content (Join-Path $root 'src\RiotAccountHub\RiotAccountHub.csproj') -Encoding UTF8
$version = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
$tag = "v$version"
Write-Host "準備發布 $tag" -ForegroundColor Cyan

# ---- 1. 檢查 ----
# 只看專案裡的檔案；沒加進 git 的資料夾（例如別的工具暫存的東西）不影響打包，不擋發布
if (git status --porcelain --untracked-files=no) { throw '還有沒 commit 的修改，請先 commit。' }
if ((git rev-parse --abbrev-ref HEAD) -ne 'main') { throw '請在 main 分支發布。' }
gh release view $tag --repo $repo 2>$null | Out-Null
if ($LASTEXITCODE -eq 0) { throw "$tag 已經發布過了。要發新版請先改 RiotAccountHub.csproj 的 <Version>。" }
if (-not (Select-String -Path (Join-Path $root 'CHANGELOG.md') -Pattern "^## \[$([regex]::Escape($version))\]" -Quiet)) {
    throw "CHANGELOG.md 裡沒有 [$version] 這一段，請先寫好這一版的更新內容。"
}

# ---- 2. 測試 ----
Write-Host '跑單元測試…' -ForegroundColor Cyan
dotnet test --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw '有測試沒通過，沒有發布。' }

# ---- 3. 打包 ----
& (Join-Path $PSScriptRoot 'make-package.ps1')
$dist = Join-Path $root 'dist'
$assets = @(
    (Join-Path $dist "RiotAccountHub-v$version.zip"),
    (Join-Path $dist "RiotAccountHub-update-v$version.zip"),
    (Join-Path $dist 'update.json')
)
foreach ($a in $assets) { if (-not (Test-Path $a)) { throw "找不到 $a" } }

# ---- 4. 發布說明：下載方式 + CHANGELOG 這一段 ----
$lines = Get-Content (Join-Path $root 'CHANGELOG.md') -Encoding UTF8
$section = @()
$in = $false
foreach ($l in $lines) {
    if ($l -match '^## \[(.+?)\]') { if ($in) { break }; $in = ($Matches[1] -eq $version); continue }
    if ($in) { $section += $l }
}
$notes = @"
## Riot Account Hub $tag

VALORANT / Riot 帳號切換器（Windows 10 / 11）。每個帳號登入一次，之後按一下「啟動」就會自動切換帳號並打開遊戲。

### 下載

| 檔案 | 給誰 |
|---|---|
| **``RiotAccountHub-v$version.zip``** | 第一次使用：解壓縮後照著 ``安裝說明.txt`` 做 |
| **``RiotAccountHub-update-v$version.zip``** | 已經在用 v1.0.3 以前的舊版：解壓縮後照著 ``更新說明.txt`` 做（先打開舊版，再打開裡面的程式，按「更新」） |

**v1.0.4 以後的版本不用下載**：程式上方會出現「有新版本」，按「立即更新」就好。

> 如果跳出「Windows 已保護您的電腦」：按「其他資訊」→「仍要執行」。這是因為程式沒有付費數位簽章。

### 這一版的更新內容

$(($section | Where-Object { $_.Trim() }) -join "`n")

本程式與 Riot Games 無關，不是官方工具。
"@
$notesFile = Join-Path $dist "release-notes-$tag.md"
[IO.File]::WriteAllText($notesFile, $notes, (New-Object Text.UTF8Encoding $false))

# ---- 5. 推送並發布 ----
Write-Host '推送程式碼…' -ForegroundColor Cyan
git push origin main
if ($LASTEXITCODE -ne 0) { throw '推送失敗' }

Write-Host '建立 GitHub Release…' -ForegroundColor Cyan
gh release create $tag @assets --repo $repo --target main --title "Riot Account Hub $tag" --notes-file $notesFile
if ($LASTEXITCODE -ne 0) { throw '建立 Release 失敗' }

# ---- 6. 確認 GitHub 上的檔案跟本機一樣 ----
$remote = gh release view $tag --repo $repo --json assets | ConvertFrom-Json
$ok = $true
foreach ($a in $assets) {
    $name = Split-Path $a -Leaf
    $local = 'sha256:' + (Get-FileHash -Algorithm SHA256 $a).Hash.ToLowerInvariant()
    $r = $remote.assets | Where-Object { $_.name -eq $name }
    if ($null -eq $r -or $r.digest -ne $local) { Write-Host "  不一致：$name" -ForegroundColor Red; $ok = $false }
    else { Write-Host "  一致：$name" -ForegroundColor Green }
}
if (-not $ok) { throw 'GitHub 上的檔案跟本機不一樣，請檢查。' }

Write-Host ''
Write-Host "發布完成：https://github.com/$repo/releases/tag/$tag" -ForegroundColor Green
