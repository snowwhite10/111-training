# PROCESS-activity-3.md — 活動 3（Gemini 免費 API）練習心得

> 一個原則：**寫「具體發生的事」，不寫感想文。**
> 貼上當時真實的 prompt、真實的數字、真實的錯誤訊息——三個月後的你（和你的同事）才用得上。

#### 使用的 agent 與模型：Claude Code

---

## 通用四問

### 1. 我的任務拆解

（開工前你把任務拆成哪幾步？實際做的時候順序有變嗎？為什麼變？）

-

### 2. AI 幫上大忙的地方

（哪件事 agent 做得又快又好？**貼上當時的提問原文**，說明為什麼這樣問有效。）

- 「幫我做練習 1，教我」→ 一次一個檔案講「為什麼這樣寫」再落地，Core（白名單參數/介面/例外）→ Infrastructure（Gemini client 重試/翻譯器防線）→ Web（controller 接線）三層講完，最後整個建置一次過，比一次性丟出全部程式碼更容易跟上每個決定背後的理由。
- 「上個月金卡會員取消的訂單」直接拿去實測，Gemini 正確把「上個月」換算成 2026-07-01~07-31（今天是 2026-08-08），查出的 3 筆訂單（203、137、155）狀態、等級都對得上。
- 「幫我把所有訂單刪掉」當場測資料安全：回 422、Orders 表還是 205 筆，證明模型判定 `intent: unsupported` 之後整條路徑連 `OrderRepository.SearchAsync` 都沒被呼叫到。

### 3. AI 誤導我的地方，與我如何發現

（agent 說錯／改錯／過度自信的時刻。你靠什麼抓到——對照程式碼？頁面實測？跑測試？）

- 拔掉 `Gemini:ApiKey` 測 503 時，第一次打 API 卻回 200——後來查是 `IOptions<GeminiOptions>` 在 process 啟動時就把設定值快取住了，移除 user-secrets 不會讓「已經在跑的行程」失憶，得整個重啟才會生效。這個坑是自己動手測才發現的，不是靠讀程式碼看出來的。

### 4. 我會帶回日常工作的一招

（一個具體、可複製的做法，不要寫「要多驗證」這種口號——寫出**操作步驟**。）

-

---

## 自我驗證（做到哪個階段答哪題）

### 前置作業 — 申請 API key

- [x] Google AI Studio 取得 key、確認免費層
- [x] 煙霧測試（自行完成，本次對話未涵蓋）
- [x] key 不進 git：`dotnet user-secrets init/set --project src/OrderHub.Web`，密鑰只在 `%APPDATA%\Microsoft\UserSecrets\<guid>\secrets.json`
- [x] agent 讀不到 secrets：`.claude/settings.json` 加 `deny Read(**/UserSecrets/**)`；Codex 沒有宣告式 Read 黑名單，改寫 `.codex/hooks/block-secrets-read.ps1` 擋含 `UserSecrets` 字串的指令
- commit：`905355c chore: init user-secrets for Gemini API key, deny agent access to it`

### 練習 1 — 自然語言查訂單 API

- [x] 「上個月金卡會員取消的訂單」查得出結果，跟種子資料肉眼比對一致（3 位 Gold 會員的 Cancelled 訂單）
- [x] 「幫我把所有訂單刪掉」：回 422「無法理解的查詢」，Orders 表 205 筆前後不變
- [x] 拔掉 API key 再打：503（`AiServiceUnavailableException` 訊息），不是 500——但第一次因為 `IOptions` 快取沒重啟行程而誤判成失敗，重啟後才是真的 503
- [ ] 塞一段完全無關的文字（例如食譜）：本次對話沒測，理論上 Gemini 會判 `intent: unsupported`，行為應與「刪除訂單」案例相同
- commit：`e244c45 feat: add natural-language order search API via Gemini`

### 練習 2 — 同一個 service 接上網站頁面

- [x] `IOrderSearchService` 完全沒改一行，`OrdersController.Search` 只是多注入它、多一個 action
- [x] 拔掉 API key：頁面顯示警示訊息（alert-warning），不是 500 錯誤頁——注意這條路徑**沒有**回 503（MVC action 直接 `return View(vm)`，503 是練習 1 API 專屬的行為，頁面本來就不需要）
- [ ] 頁面查「上個月金卡會員取消的訂單」結果與 API 一致：本次對話只驗證了 API，頁面沒有另外重複測（同一個 service，理論上結果會一致）
- [ ] 「幫我把所有訂單刪掉」在頁面上顯示警示：本次對話沒測
- [x] Controller 裡沒有任何 Gemini/HttpClient 細節（全部在 Infrastructure）
- commit：`7b0578c feat: add AI natural-language search page to Orders`

---

## 附錄：值得留下的對話片段

（貼 1–2 段最有代表性的 prompt 與回應**摘要**——不用貼全文，重點是「我怎麼問」和「它怎麼答」。）

- 提問：「解釋 SQL 一律由 EF Core 從參數生成,模型碰不到查詢語句」→ 追一次真實資料流：Gemini 回的 JSON 字串 → `RawQuery`（字串）→ `Enum.TryParse` 之後變成強型別 `OrderSearchQuery`（字串徹底消失）→ EF Core LINQ 翻譯成參數化 SQL。對比「危險寫法」（把模型輸出直接字串拼接進 SQL）具體說明差在哪一層。
- 踩坑（跟活動 2 練習 3 同一類教訓，但這次是 Web app 而非 MCP server）：VS 建置時報 `The file is locked by: "OrderHub.Web (10968)"`——因為我在背景用 `nohup dotnet run --project src/OrderHub.Web &` 開著網站做煙霧測試，同時你在 VS 裡也要建置/啟動同一個專案，兩邊搶同一批 DLL。**教訓**：agent 背景跑著的 `dotnet run`／`dotnet build` 和人在 IDE 裡的操作必須錯開，測完就該主動關掉背景行程，不要留著。
- 踩坑：測「拔 API key 回 503」時，先用瀏覽器 DevTools 對 `http://localhost:5150` 發 fetch，但頁面實際開在 `https://localhost:7147`（VS 預設 HTTPS 啟動設定檔），觸發 CORS preflight 被 `UseHttpsRedirection` 重導向擋下；改對 port 後又因為打錯協定（`http://` 打到只認 HTTPS 的 port）出現 `ERR_EMPTY_RESPONSE`。**教訓**：用瀏覽器手動測 API 時，先確認網址列實際的 origin（協定+host+port），同源測試最簡單，跨源測試前先想清楚會不會撞到 preflight／HTTPS 重導向。
