# Riot Account Hub

Windows 上的 Valorant / Riot 帳號切換器。每個帳號登入一次並存起來，之後點一下就能切換到那個帳號、直接啟動 VALORANT，不用再經過 Google / Facebook 登入頁面。

> 本專案與 Riot Games 無關，也沒有使用任何 Riot / VALORANT 的商標或圖示。

## 下載

到 [Releases](https://github.com/yuanleo2009-cpu/RiotAccountHub/releases/latest) 下載最新的 `RiotAccountHub-v*.zip`，解壓縮後照著裡面的 `安裝說明.txt` 做就好。

要自己產生下載包：`powershell -ExecutionPolicy Bypass -File tools\make-package.ps1`（輸出在 `dist\`）。

## 運作原理

跟開源專案 [TcNo Account Switcher](https://github.com/TCNOco/TcNo-Acc-Switcher) 的 Riot 切換方式相同：「登入狀態快照切換」。

1. 你在 Riot Client 登入，並勾選「保持登入」
2. 程式把 Riot Client 存的登入狀態檔案加密備份，當作這個帳號的「快照」
3. 切換時：完全關閉 Riot Client → 把目標帳號的快照放回原位 → 重新啟動 Riot Client 並直接開 VALORANT

會備份 / 切換的檔案（都在 `%LocalAppData%\Riot Games\Riot Client\`，不存在的會自動略過）：

| 路徑 | 說明 |
|---|---|
| `Data\RiotGamesPrivateSettings.yaml` | 主要的登入狀態（目前版本） |
| `Data\RiotClientPrivateSettings.yaml` | 其他版本用的檔名 |
| `Data\Cookies\` | 舊版的 cookie 資料夾 |
| `Data\Sessions\` | 工作階段 |
| `Config\RiotClientSettings.yaml` | Riot Client 設定 |

清單定義在 [`RiotLoginFiles.cs`](src/RiotAccountHub.Core/Riot/RiotLoginFiles.cs)，Riot 改檔名時只要改這裡。

## 安全設計

- **不儲存任何密碼**：只存 Riot Client 自己產生的登入狀態檔案
- **DPAPI 加密**：所有快照都用 Windows DPAPI（CurrentUser 範圍）加密；換電腦或換 Windows 使用者就解不開
- **不碰遊戲程序**：不注入、不讀寫任何程序的記憶體、不修改任何 Riot 執行檔，只在 Riot Client 完全關閉時讀寫資料檔
- **不碰 Vanguard**：`vgc` / `vgk` / `vgtray` 絕對不會被關閉
- **原本狀態備份**：第一次切換前，自動備份你原本的登入狀態，可以用「還原原本狀態」放回去
- **不會停在一半**：切換時先在暫存區準備好、先備份目前的檔案，任何一步失敗都會自動還原

### 程式會連到哪裡

| 連線對象 | 什麼時候 | 送出什麼 |
|---|---|---|
| `127.0.0.1`（你自己電腦上的 Riot Client） | 切換、抓 Riot ID / 段位、自動開始遊戲 | 不會離開你的電腦 |
| `media.valorant-api.com`（玩家社群網站） | 第一次看到某張卡面時下載圖片 | 只有卡面編號 |
| `player-preferences-usw2.pp.sgp.pvp.net`（Riot 官方） | 玩遊戲時自動記錄設定、使用「複製 / 套用 / 還原設定」時 | 你自己帳號的 token（只在記憶體中，不存檔）與遊戲設定 |

## 其他功能

- **段位與卡面**：用啟動器玩過一次後，卡片會顯示段位、排行榜名次和遊戲卡面
- **複製 / 套用遊戲設定（都在首頁）**：在帳號卡片按「複製設定」，上方會顯示「已複製 X 的遊戲設定」，再到其他帳號按「套用設定」就會切換、套用、開始遊戲。玩每個帳號時程式會自動記下它的設定，所以複製通常是瞬間完成。套用前自動備份，卡片上可以「還原設定」
- **主帳號**：卡片下方「設為主帳號」，其他帳號會出現「套用主帳號」，一鍵套用主帳號最新的設定
- **關閉遊戲**：卡片按鈕顯示「遊戲中」時再按一下，確認後只關掉 VALORANT（Riot Client 保持登入，不碰 Vanguard）
- **登入失效提醒**：切換後如果 Riot Client 停在登入畫面（例如之前在 Riot Client 或遊戲裡按過「登出」，那份登入會被 Riot 作廢），程式會提醒並帶你重新登入
- **按啟動後自動開始遊戲**：主畫面右下角的開關。Riot 用伺服器開關停用了啟動參數的自動開遊戲，開啟後程式會在 Riot Client 登入完成時幫你按「開始遊戲」

程式資料放在 `%AppData%\RiotAccountHub\`：

```
accounts.json         帳號清單（Riot ID、備註，不含任何登入資料）
settings.json         設定
snapshots\*.bin       每個帳號的加密快照
original-backup.bin   第一次使用前的原本登入狀態（加密）
logs\app.log          操作記錄（不含任何登入資料）
```

## 專案結構

```
src/RiotAccountHub.Core/      核心邏輯（沒有畫面，可以單元測試）
  Riot/                       Riot Client 操作：找路徑、關閉、啟動、本機 API
  Snapshots/                  快照的打包、DPAPI 加密、安全還原
  Storage/                    帳號清單與設定的存檔
  AccountSwitcher.cs          把上面組合成完整流程（切換、新增、還原）
src/RiotAccountHub/           WPF 介面（MVVM）
  ViewModels/                 畫面邏輯
  Views/                      畫面（XAML）
  Themes/Dark.xaml            深色主題
  Services/DialogService.cs   跳出視窗
tests/RiotAccountHub.Tests/   單元測試
tools/make-icon.ps1           產生程式圖示
```

## 安裝（讓 Windows 搜尋找得到）

在專案資料夾開 PowerShell，執行：

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

它會打包程式、複製到 `%LocalAppData%\Programs\RiotAccountHub\`，並在「開始」功能表建立捷徑。之後按 Windows 鍵輸入「Riot Account Hub」就能直接開啟（加上 `-Desktop` 會同時建立桌面捷徑）。

- 更新程式：改完程式碼後再執行一次 `install.ps1`
- 解除安裝：`powershell -ExecutionPolicy Bypass -File .\uninstall.ps1`（帳號資料會保留）

## 開發

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。

```powershell
# 執行程式（開發模式）
dotnet run --project src/RiotAccountHub

# 執行單元測試
dotnet test

# 打包成單一 .exe（不需要另外安裝 .NET 就能執行）
dotnet publish src/RiotAccountHub -c Release
```

打包好的檔案在：`src\RiotAccountHub\bin\Release\net10.0-windows\win-x64\publish\RiotAccountHub.exe`
