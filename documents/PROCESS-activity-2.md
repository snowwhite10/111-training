# PROCESS-activity-2.md — 活動 2（自建 MCP Server）練習心得

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

- 提問：「哪些商品庫存低於 5?」在 orderhub MCP server 連上後，agent 一次呼叫 `low_stock(threshold=5)` 就拿到正確、已套用「只列在售商品」業務規則的結果，不用自己重新推導規則。

### 3. AI 誤導我的地方，與我如何發現

（agent 說錯／改錯／過度自信的時刻。你靠什麼抓到——對照程式碼？頁面實測？跑測試？）

-

### 4. 我會帶回日常工作的一招

（一個具體、可複製的做法，不要寫「要多驗證」這種口號——寫出**操作步驟**。）

-

---

## 自我驗證（做到哪個階段答哪題）

### 練習 0 — 接一個現成的 MCP（Playwright）

- [ ] agent 能自己開瀏覽器完成操作並回傳截圖
- [ ] 對比活動 1 練習 2 人工重現 bug 的步驟，記錄差異

### 練習 1 — 建立 OrderHub MCP Server（stdio）

- [x] `dotnet build src/OrderHub.Mcp` 成功
- [x] 獨立 commit（`7225622 feat: add OrderHub MCP server (stdio) with read-only tools`）
- 三個唯讀工具：`get_order`、`low_stock`、`customer_orders`

### 練習 2 — 用 MCP Inspector 除錯

- [ ] 三個工具都列得出來，description、參數說明如所寫
- [ ] `low_stock`（threshold=10）結果與 `/Products` 頁面上的低庫存商品一致
- [ ] `get_order` 用不存在的 Id，回應是清楚的錯誤訊息而不是 exception dump

### 練習 3 — 註冊給 agent，做 before/after 對照

**設定**

- [x] Claude Code：`training-repo/.mcp.json` 註冊 `orderhub` server，進 git，獨立 commit（`8d18166 chore: register OrderHub MCP server for Claude Code (.mcp.json)`）
- [x] Codex：`~/.codex/config.toml` 加入 `[mcp_servers.orderhub]`（command = "dotnet", args = ["run", "--project", "src/OrderHub.Mcp"]）
- [x] Claude Code `/mcp` 能看到 orderhub server 與三個工具（需完整重啟 CLI，中途 `/mcp` 不會重新掃描新增的 `.mcp.json`）

**對照實驗**：問同一句「哪些商品庫存低於 5?」

| | Before（沒有 MCP） | After（有 orderhub MCP） |
|---|---|---|
| agent 做了什麼 | 讀 `Product.cs`、`ProductRepository.cs` 弄懂欄位，讀 `appsettings.json` 拿連線字串（`Server=localhost;Database=OrderHubTraining;...`），確認機器上有 `sqlcmd`，直接對 DB 開 raw SQL | 呼叫 `low_stock(threshold=5)` 一次結束 |
| 工具呼叫次數 | 0（純 bash/sqlcmd，完全繞過 MCP／service 層） | 1 |
| 踩到的坑 | 第一次輸出中文全部變亂碼（`���� �Є��Դ`），要加 `chcp 65001` + `-f 65001` 才修正——純技術性除錯，跟「庫存」問題本身無關 | 無 |
| 業務規則 | SQL 裡的 `IsActive` 篩選是自己手動加的，如果忘記加就會把停售商品也算進去 | 走 `ProductRepository.GetActiveAsync()`，「只列在售商品」是工具自帶的規則，不用重新判斷 |
| 結果 | ```SKU-1048 晨光 行動電源 2```<br>```SKU-1005 極光 筆電支架 3```<br>```SKU-1023 雲峰 27吋螢幕 3```<br>```SKU-1032 曜石 機械鍵盤 4```<br>```SKU-1014 星河 USB-C 集線器 4``` | ```SKU-1048 晨光 行動電源 2```<br>```SKU-1005 極光 筆電支架 3```<br>```SKU-1023 雲峰 27吋螢幕 3```<br>```SKU-1014 星河 USB-C 集線器 4```<br>```SKU-1032 曜石 機械鍵盤 4``` |

**結論**：兩次結果數字完全一致（差別只在同分排序時 SKU-1014/1032 誰先誰後），可見 before 那次手動查詢在資料正確性上沒問題，差的是**過程**——知識門檻（要懂連線字串、SQL、編碼參數）、風險（直接碰生產 DB、業務規則要自己補一次）、與過程長度（1 次工具呼叫 vs. 讀 3 個檔案 + 1 次編碼除錯 + 1 條手寫 SQL）。

- [x] 對照實驗完成且記錄（見上表）

### 練習 4 — 會改資料的工具：cancel_order

- [ ] MCP Inspector 中 `cancel_order` annotations 如所標，三個唯讀工具顯示 read-only（沒開 Inspector 驗證，只在程式碼標了 `[McpServerTool(ReadOnly = true)]` / `[McpServerTool(Destructive = true, Idempotent = false)]`）
- [x] 對 agent 說「幫我取消訂單205」：因為工具標了 `Destructive = true`，呼叫前 Claude Code 跳出權限確認，按允許前資料沒被動到
- [x] 取消訂單 205（原為 Pending）成功：狀態變 `Cancelled`，品項 SKU-1001（極光 無線滑鼠）庫存回補（用 sqlcmd 直查 DB 確認為 24，沒開瀏覽器看 `/Products` 頁面）
- [x] 邊界測試：對同一筆訂單 205 再取消一次 → `取消失敗:狀態為 Cancelled 的訂單不可取消`；對一筆 Shipped 訂單（Id 2）取消 → `取消失敗:狀態為 Shipped 的訂單不可取消`。兩次都是清楚的業務訊息，不是 exception dump
- [x] 獨立 commit（`dfd6f40 feat: add cancel_order MCP tool, mark read-only tools`）

**心得**：`cancel_order` 工具本身只有 4 行（呼叫 `orderService.CancelOrderAsync` 後轉譯 `ServiceResult`），狀態檢查跟庫存回補的規則完全沒重寫——這跟練習 1「金額別自己算」是同一件事的延伸：**改資料的規則更不能在工具層重複一份**，不然改一次規則要記得改兩個地方。標 `Destructive = true` 這件事在這次操作上看得到直接效果：Claude Code 真的在執行前擋下來要我確認，跟三個唯讀工具（不用確認、直接跑）行為不同——annotations 不是裝飾，是真的會改變 client 行為。

### 練習 5 — Resources 與 Prompts

- [ ] MCP Inspector：Resources／Prompts 分頁沒開來看，改用 Claude Code 直接驗證
- [x] Claude Code：`@orderhub:orderhub://discount-rules` 選取後問「Gold 會員買 1000 元商品應付多少?」，agent 從 resource 內容（Gold 9 折）直接算出 900 元，沒有讀 `OrderService.cs`
- [x] Claude Code：`/mcp__orderhub__low_stock_report 10` 一鍵展開成 prompt 範本，agent 自動呼叫 `low_stock(10)` 並產出 SKU／名稱／現有庫存／建議補貨量／理由的表格
- [x] 思考題（先答一版，之後可以自己再補）：
  - Resource vs. 讓 agent 讀 `OrderService.cs`：讀原始碼每次都要重新解析程式邏輯、量體較大（整份 service），而且商業邏輯的寫法不保證每個 agent 都能正確反推成「規則說明」；Resource 是**已經翻譯成人類/agent 都好懂的一段話**，而且团队每個人問到的是同一份說明，不會有人讀 code 讀錯、有人讀對。
  - Prompt 範本放 server vs. 每個人自己打字：範本進 git 之後，措辞、要用哪些工具、輸出格式是**團隊共用且受版控**的，改一次全隊生效；每個人各自打一段話，措辞會逐漸分裂，且沒有人知道別人是怎麼問的，無法沉澱成共同流程。
- [x] 獨立 commit（`72b16d0 feat: add discount-rules resource and low_stock_report prompt`）

**心得**：`OrderHubResources.DiscountRules()` 沒有照文件範例寫死字串，而是改成即時讀 `OrderService.GetDiscountRate()` 組出折扣說明——文件裡自己提過「resource 內容跟程式碼一樣會過期」這個地雷，剛好手上就有現成的 service 可以呼叫，沒理由留一份會過期的複本。這次寫 Resource 建構子注入 `IOrderService` 而不是像文件範例用 `static` 方法，是唯一跟文件範例不同的地方，但換來的是規則永遠跟 `OrderService` 一致。

---

## 附錄：值得留下的對話片段

（貼 1–2 段最有代表性的 prompt 與回應**摘要**——不用貼全文，重點是「我怎麼問」和「它怎麼答」。）

- 提問：「哪些商品庫存低於 5?」（MCP 開啟前）→ agent 讀 domain/repository 程式碼、找 `appsettings.json` 連線字串、用 `sqlcmd` 手動查詢，中途修了一次中文編碼亂碼，才給出結果。
- 提問：同一句（MCP 開啟後）→ agent 直接呼叫 `low_stock(threshold=5)`，一步拿到結果，數字與前者一致。
- 踩坑：改完 `OrderHubTools.cs`／新增 `OrderHubResources.cs`／`OrderHubPrompts.cs` 後 `dotnet build` 兩次都被鎖檔失敗（`OrderHub.Core.dll ... being used by another process`），因為 orderhub MCP server 是用 `dotnet run` 常駐執行，改 code 不會熱重載。第一次單純 `/mcp` 重連就解決；第二次 `/mcp` 重連回報 `Failed to reconnect to orderhub: -32000`，用 `tasklist` 查發現舊的 `OrderHub.Mcp.exe` 變成孤兒行程還在鎖檔，`taskkill /PID <pid> /F` 殺掉後才建置成功、重連正常。**教訓**：`dotnet run` 型的 MCP server 改完程式碼要重連才會生效；如果重連失敗，先去確認舊行程是不是沒真的退出。
