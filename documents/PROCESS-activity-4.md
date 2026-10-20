# PROCESS-activity-4.md — 活動 4（n8n 自動化）練習心得

> 一個原則：**寫「具體發生的事」，不寫感想文。**
> 貼上當時真實的 prompt、真實的數字、真實的錯誤訊息——三個月後的你（和你的同事）才用得上。

#### 使用的 agent 與模型：Claude Code

---

## 通用四問

### 1. 我的任務拆解

（開工前你把任務拆成哪幾步？實際做的時候順序有變嗎？為什麼變？）

- 補齊 HTTP transport → 練習 1 Hello Webhook → 練習 2 退單巡檢日報 → 練習 3 MCP 合體。
- 順序中途插進一段「環境」：原本跑的 n8n 是舊版，最新版 n8n 2.43.8 要求 Node >= 24，系統是 Node 22.23.3。另外下載 Node 24.22.0 可攜版（SHA256 對過官方 SHASUMS256）放 `.tools/`，只給 n8n 用，系統 Node 不動。

### 2. AI 幫上大忙的地方

（哪件事 agent 做得又快又好？**貼上當時的提問原文**，說明為什麼這樣問有效。）

- 每次按完 Execute Workflow 只說「失敗了」，agent 直接唯讀讀 `~/.n8n/database.sqlite` 的 `execution_entity`／`execution_data`，逐節點列出輸入輸出與錯誤訊息，一次就定位到是哪個節點、什麼原因（503、Data table 未選、Issues 停用、webhook 未註冊），不用自己在 UI 一個一個節點點開。
- workflow 先由 agent 照文件產成 JSON（`training-repo/n8n/*.json`）再匯入，credential（Gemini key、GitHub token）留給自己在 UI 填，agent 沒碰金鑰。

### 3. AI 誤導我的地方，與我如何發現

（agent 說錯／改錯／過度自信的時刻。你靠什麼抓到——對照程式碼？頁面實測？跑測試？）

- agent 一開始用 `npx n8n import:workflow` 匯入：跑 5 分鐘還在下載、吃到 1.4 GB 記憶體，而且 npx 抓的是最新版，可能把正在跑的舊版 n8n 的 DB 升級壞掉——中途叫停，改成 UI 的 Import from File。
- agent 第一次安裝 n8n 失敗：在 Git Bash 設 `PATH="D:/...:$PATH"`，`D:` 的冒號被當成 PATH 分隔符，npm 實際跑的是系統 Node 22，`isolated-vm` 找不到 Node 22 的預編譯檔改走 node-gyp，沒有 Python 直接失敗。改用 PowerShell 設 PATH 重裝才成功（`added 2240 packages ... in 20m`）。
- agent 把時區寫進匯入檔的 `settings.timezone = Asia/Taipei`，但匯入後沒生效，Schedule Trigger 輸出仍是 `America/New_York (UTC-04:00)`；要在 workflow Settings 手動選 `Asia/Taipei` 才變成 `UTC+08:00`。
- agent 建議在 Gemini Chat Model 節點開 Retry On Fail，結果仍直接 503；改在 AI Agent 節點本身開才有效。
- 匯入檔預先填好 Data Table 的 `date`／`note` 對應，但在 UI 選表時 n8n 會清空欄位對應，第一筆歸檔寫進去是 `date: null, note: null`——是看執行紀錄的輸出才發現，畫面上節點是綠勾。

### 4. 我會帶回日常工作的一招

（一個具體、可複製的做法，不要寫「要多驗證」這種口號——寫出**操作步驟**。）

-

---

## 自我驗證（做到哪個階段答哪題）

### 補齊 — MCP server 加開 HTTP transport

- [x] Inspector（CLI 模式，`--transport http`，URL `http://localhost:3001`）：4 個工具 `customer_orders`、`get_order`、`low_stock`、`cancel_order`，resource `orderhub://discount-rules`，prompt `low_stock_report` 都列得出來；`get_order(id=1)` 實際查得到訂單
- [x] 不帶 `--http` 照舊走 stdio：`claude mcp list` 顯示 `orderhub ... ✔ Connected`
- [x] 獨立 commit（`50fba66 feat: add streamable HTTP transport to OrderHub MCP server`）

**踩坑**：HTTP 版開著時，`claude mcp list` 的 orderhub 回 `connection timed out after 30000ms`——`.mcp.json` 是 `dotnet run`，每次都會先 build，但 dll 被 HTTP 版鎖住。關掉 HTTP 版就連上。兩個要同時開，HTTP 版改用 `dotnet run --no-build --project src/OrderHub.Mcp -- --http`。

### 練習 1 — Hello Webhook

- [x] POST `{"text":"hello"}` 到 Test URL `http://localhost:5678/webhook-test/hello`：回應含 `body.text = hello`、`receivedAt = 2026-10-19T22:45:52.153-04:00`、`executionMode = test`
- [x] Test URL vs Production URL：Test 要在編輯器按 Execute Workflow 才監聽、120 秒、一發即停；Production（`/webhook/hello`）要 Publish（n8n 2.x 的 Activate）後才存在。沒 Publish 時練習 2 的通知節點回 `The requested webhook "POST hello" is not registered`

### 練習 2 — 退單巡檢日報

- [x] 素材：在網站新建並取消訂單 #206（郭俊傑，Gold，882 元）
- [x] true 分支：查到 1 筆 → AI 日報「本日共有 1 筆取消訂單，總金額 882 元，包含 1 筆 Gold 會員退單。」→ 開出 issue（snowwhite10/111-training #1、#2）→ 通知打到練習 1 的 Production URL（Hello Webhook 執行紀錄 `executionMode: production`，body 含 `report` 與 `issueUrl`）
- [x] 日報數字與 API 結果一致：1 筆、882 元、#206
- [x] false 分支：查詢改成「昨天取消的訂單」→ `count: 0` → AI 回「本日無退單」→ If 走 false、沒開 issue → `巡檢紀錄` 寫入 `2026-10-20 / 本日無退單`（執行 #18）
  - 第一次寫入欄位是 null（見上方第 3 問），重新填欄位對應後才正確
  - 修好後又踩一次：`note` 填成 `= 本日無退單`，`=` 後多一個空格，寫進去變成「 本日無退單」（前面有空白），表格上肉眼幾乎看不出來，是查資料才發現；刪掉空格後正確
- [x] 思考題：如果「查什麼、怎麼查」也交給 AI Agent 自由發揮，會失去什麼？

  這次就算查詢已經放在產品程式碼裡、AI 只負責把「過去 30 天」翻成日期，同一句話仍出過一次錯：n8n 那次拿到 **24 筆**（23 筆是 6、7 月建立的舊單），隨後直接打 API 三次都正確回 **1 筆**。Gemini 偶爾沒換算出 `DateFrom`，條件就退化成「所有取消的訂單」，日報數字跟著錯。要是連 SQL／查詢條件都讓 Agent 自由產生，會失去：
  - **白名單防線**：活動 3 只接受固定幾個參數，「幫我把所有訂單刪掉」回 422；自由發揮就沒有這層。
  - **可測試性**：API 的參數組合可以寫測試；Agent 每次生成的查詢都不同，沒辦法測。
  - **數字可信度**：連翻譯一個日期都會飄，自由查詢的誤差只會更大，而日報會被當成事實拿去開 issue。

**其他觀察**：
- Gemini 不穩定直接讓流程停擺：多次 `503 Service Unavailable — This model is currently experiencing high demand`，以及 `429 Too Many Requests`（免費層每個模型每天 20 次，`retry in 3h27m`）。search API 與 AI Agent 用同一把 key，每次執行至少吃 2 次額度。
- `count` 為 0 時日報內容固定是「本日無退單」，其實不需要 AI。目前流程是 AI 在 If 前面，所以 Gemini 一掛，連「沒事」的那天都無法歸檔；把 If 移到 AI Agent 前面，false 分支就不依賴外部服務。

### 練習 3 — MCP 合體

- [x] 執行紀錄裡看得到 agent 對退單呼叫了 `get_order`，日報引用了真實品項與金額（執行 #15）：
  - Gemini 第一輪回 `finishMessage: "Model generated function call(s)."`，呼叫 `MCP_Client_get_order {"id":206}`
  - MCP Client 節點回傳 #206 明細：SKU-1038 曜石 行動電源 × 1、單價 980、小計 980、`DiscountRate 0.10`、`Total 882`——和直接打 `http://localhost:3001` 的 `get_order(206)` 完全一致
  - 第二輪寫日報時中途碰到一次 503，AI Agent 的 Retry On Fail 重試後成功
  - 只勾 `get_order`，`cancel_order` 沒掛進流程；整次執行只有這一個工具被呼叫
- [x] 對照練習 2：同一批退單（只有 #206），有深挖 vs 沒深挖的日報差異

| | 練習 2（沒深挖） | 練習 3（有 `get_order`） |
|---|---|---|
| 資料來源 | search API 的 6 個欄位：`id`、`customerName`、`tier`、`status`、`total`、`createdAt` | 再加 MCP `get_order`：品項、SKU、數量、單價快照、小計、折扣率 |
| 日報內容 | 「訂單 #206：客戶 郭俊傑（Gold 會員），金額 882 元」 | 多了「訂單小計 980 元」「Gold（享有 10% 折扣）」「曜石 行動電源（SKU-1038）× 1 件，單價 980 元」 |
| Gemini 呼叫次數 | AI Agent 1 次 | AI Agent 2 次（決定呼叫工具 + 寫日報），prompt tokens 從 164 增加到 1,596 |
| issue | #1、#2 | #3 |

**心得**：日報第一次能回答「退的是什麼」，而不只是「退了多少錢」——882 是 980 打 9 折，這個推導是 `get_order` 的 `DiscountRate` 給的，不是 AI 自己算的。代價是每次執行多 1 次 Gemini 呼叫、token 約 10 倍；在免費層每天 20 次的額度下，退單一多，每筆一次 `get_order` 的深挖會很快用完額度。
- 同一個 MCP server、同一份 `OrderHubTools`，Claude Code 走 stdio、n8n 走 HTTP，工具程式碼一行都沒改。
