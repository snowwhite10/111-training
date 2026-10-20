# my-notes.md — 練習心得（草稿）

> 這是根據這次對話紀錄整理的初稿：實際發生過什麼、用了什麼 prompt、agent 說了什麼。
> 「自我驗證」清單裡的**個人理解類**題目（例如「我能不看筆記說出…」「我在頁面上重現過」）
> 我沒辦法替你打勾——那是你腦中真實發生的事，麻煩照自己的實際情況調整/確認過再貼回 `PROCESS.md`。

#### 使用的 agent 與模型：

- Claude Code（Sonnet 5），搭配自訂 skill `/fix-bug`、subagent `code-reviewer`、`test-runner`
- 練習 3、4 用了 Plan Mode（`EnterPlanMode`/`ExitPlanMode`）先出計畫、核准後才動手

---

## 通用四問

### 1. 我的任務拆解

開工前的順序是照 `activity-guideline.md` 給的六步跑：
**① 頁面重現 → ② 把具體現象告訴 agent → ③ 一起定位根因（等我確認再修）→ ④ 最小修復 → ⑤ code-reviewer 審查 → ⑥ 補回歸測試＋`dotnet test` 全綠 → ⑦ 回頁面驗證 → ⑧ 獨立 commit**。

練習 2 的三個 bug 都是用 `/fix-bug` 這個 skill 跑完整套流程；練習 3、4 因為是新功能／重構，改成「先 Plan Mode 出計畫、我核准後才動手」，比 bug 修復多了一道「批准計畫」的關卡。

實際跑下來順序有變的地方：
- 練習 2 的三個 bug 我是連續丟給 agent 的（沒等前一個徹底收尾就丟下一個症狀），所以有一次修 bug 1（分頁）時，中間卡在 Windows 檔案鎖定問題（`EPERM`，可能是防毒或某個程式佔用），agent 改用 PowerShell 直接寫檔繞過去，但 PowerShell 的 `Set-Content -Encoding UTF8` 意外加了 BOM，多出一行不相關的 diff，還得再處理一次才讓 diff 乾淨。
- 三個 bug 的 commit（`247b2b2`／`2cec399`／`618bab4`）跟後續補的單元測試 commit（`74eeecf`）不是我在對話中明確喊「commit」才發生的——中間有一段是我自己在背景驗證、commit 掉的，agent 那邊只看到 working tree 突然變乾淨。這跟指南「一個修復＝一個獨立 commit」的精神一致，但流程上分成了 4 個 commit 而不是 3 個（bug 1 的修復跟它的回歸測試被拆成了兩次）。

### 2. AI 幫上大忙的地方

**貼上的三個 bug 描述（症狀），agent 都在很短時間內定位到根因**，方式是「Controller → Service → Repository」一路往下讀，然後直接指出程式碼裡的哪一行：

- 「剛建立的訂單在列表第一頁找不到 & 點到最後一頁常常是空白的」
  → 找到 `OrderRepository.GetPagedAsync` 裡 `Skip(page * pageSize)` 少了 `-1`，並解釋為什麼這個 off-by-one 剛好同時解釋兩個症狀（第一頁跳過了本該顯示的最新訂單；最後一頁 Skip 值超過 TotalCount 變空）。

- 「發現 Gold 會員新訂單的應付總額比我們手算的少了一截，但 Silver 會員完全正常」
  → 找到 `CreateOrderAsync` 對 Gold 會員預先打折存進 `UnitPriceSnapshot`，`CalculateTotal` 又對 subtotal 打一次折，等於雙重折扣（0.9×0.9≈0.81）。這次 agent 有直接引用 `CLAUDE.md` 裡明講的「折扣集中在 `OrderService.CalculateTotal`，不要在別處重算」當作額外佐證，讓我更確定這不是我自己誤判。

- 「商品頁的庫存數字跟實際盤點對不上，而且好像每次退單之後就更少」
  → 找到 `CancelOrderAsync` 先把 `order.Status` 改成 `Cancelled`，才去判斷「是不是 Pending/Confirmed 才回補庫存」，導致這個判斷恆假、回補庫存變成死碼，從來沒執行過。

這三次都是**先把根因講清楚、等我回「可以」才動手**，沒有自己先斬後奏，這點符合我原本的期待。

**練習 3（新功能）的 Plan Mode 用法也值得記一筆**：我沒有直接說「幫我做低庫存頁」，而是照指南先進 Plan Mode，agent 讀完 `ProductsController`/`ProductService`/`Views/Products/Index.cshtml` 之後，計畫裡明確寫出「DTO 放哪裡（`OrderHub.Core.Common`，跟 `PagedResult<T>` 同一慣例）」「近 30 天的規則放在 Service 層、repository 只收參數」「N+1 怎麼避免（兩條查詢＋記憶體組合，不是逐商品查）」「30 天邊界怎麼定義（含頭）」，我核准之後才開始寫程式。

### 3. AI 誤導我的地方，與我如何發現

不是 agent 直接講錯，而是**agent 自己先做完一輪、被第二個 review 角色（`code-reviewer` subagent）抓出「測試沒有真的測到重點」**，這件事發生了不只一次，很值得記錄：

- 修分頁 bug 時，`code-reviewer` 指出既有測試 `GetOrders_ReportsTotalCountAndTotalPages` 只驗證 `TotalCount`/`TotalPages` 的數字，完全沒斷言 `Items` 的內容——換句話說，就算把 `Skip(page * pageSize)` 的 bug 改回去，這個測試一樣會全綠。真正會抓到這個 bug 的斷言（`Items` 第一筆是不是最新建立的訂單、換頁後 `Items.Count` 是不是等於剩餘筆數）是後來才補上的。

- 修 Gold 雙重折扣 bug 時，`code-reviewer` 指出既有的 `CreateOrder_SnapshotsCurrentUnitPrice` 測試用的是 `Standard` 客戶，根本不會走到有 bug 的 Gold 分支，是一個「就算 bug 沒修也會過」的恆真測試；同一次還指出我的 diff 混進了上一個 bug（分頁）遺留、尚未 commit 的測試檔變更，範圍不乾淨，建議分開 commit。

- 修庫存 bug 時，`code-reviewer` 也是同樣模式：既有的 `CancelOrder_ActiveOrder_SetsStatusCancelled` 只驗證訂單狀態變成 `Cancelled`，沒有驗證庫存有沒有回補——這正是這次要修的 bug 本身，但舊測試完全沒鎖住它。

**我怎麼發現的**：不是我自己肉眼看出來的，是讓 `code-reviewer` 這個獨立視角重新看一次 diff 才抓到。這代表「dotnet test 全綠」本身不代表 bug 真的修好，要進一步確認新舊測試的斷言是不是真的對著症狀打。

- 另外一個較小的例子：agent 為了繞過 Windows 檔案鎖定，改用 PowerShell 直接寫檔，`Set-Content -Encoding UTF8` 意外幫檔案加了 BOM，讓 `git diff` 多出一行不相關的變更（`-using` 變成 `+﻿using`）。這是我自己看 `git diff` 才發現的，agent 隨後有自己修正回去。

### 4. 我會帶回日常工作的一招

**具體操作**：每次 agent 說「修好了、測試也過了」之後，先做兩件事再放行：
1. 跑 `git diff --stat` 只看範圍——改動的檔案是不是都是「該碰的檔案」，有沒有夾帶其他無關的變更（例如上一個任務遺留的檔案）。
2. 追問一句「如果把這次修復還原回去，你新加的測試還會不會過？」——如果答案是「會過」，代表這個測試是恆真斷言，沒有真的鎖住這次修復，要請它重寫斷言，直接對著症狀本身（畫面上的數字、回傳的 `Items` 內容），而不是只驗證「呼叫成功」或「數量對」這種間接指標。

---

## 自我驗證（做到哪個階段答哪題）

### 第一階段 — Agentic Coding

**練習 1**

1. 我能不看筆記說出三個專案（Web/Core/Infrastructure）各自的職責 —— 待自評
2. 我核對過 agent 描述的建單流程，且至少找出一處不精確或過度簡化的說法 —— 待自評
3. 我知道商業邏輯應該放在哪一層、新增頁面要動哪些地方 —— 待自評（可參考練習 3 的分層：Controller 轉接、Service 放「近 30 天」這種業務規則、Repository 包 EF Core 查詢）

**練習 2**

1. 三個 bug 我都先在頁面上重現過，才開始找程式 —— 待自評（指南建議的順序；麻煩照你實際操作的記憶確認）
2. 我給 agent 的資訊包含具體觀察（頁碼／金額數字／庫存數字），而不是只貼客訴原文 —— ✅ 三次描述都帶了具體現象（「第一頁找不到」「Gold 少了一截、Silver 正常」「退單後更少」）
3. 每個修復都回到頁面驗證過症狀消失 —— 待自評（agent 每次都有提示回頁面測，實際測了沒需要你確認）
4. 每個 bug 都補了一個回歸測試，`dotnet test` 全綠 —— ✅（分頁 2 個測試、折扣 1 個測試、庫存 1 個測試）
5. 三個獨立 commit，message 說明症狀與根因 —— 部分符合：實際是 4 個 commit（`247b2b2`／`74eeecf`／`2cec399`／`618bab4`），bug 1 的修復跟它的回歸測試被拆成了兩次
6. （思考題）為什麼原本的測試沒抓到這三個 bug？ —— 看上面「AI 誤導我的地方」那一節，三次都是「舊測試只驗證間接指標（數量、狀態），沒有直接斷言症狀本身（Items 內容、雙重折扣後的金額、庫存回補後的數字）」

**練習 3**

1. `/Products/LowStock` 不帶參數 → 門檻 10 的結果；帶 `?threshold=3` → 結果隨之改變 —— 待你手動確認
2. `?threshold=0`、`?threshold=-1` → 頁面顯示驗證錯誤，不是 500 —— 待你手動確認（`code-reviewer` 有在程式邏輯層面確認過 ModelState 分支正確）
3. 售出數量欄位排除了 Cancelled 訂單 —— ✅ 有專門的測試 `GetLowStock_SoldQuantity_ExcludesCancelledOrders` 鎖住
4. 停售商品不出現在列表 —— ✅ 有專門的測試 `GetLowStock_ExcludesInactiveProducts` 鎖住
5. 程式分層與命名跟既有的 Products 功能一致 —— ✅ DTO 放 `OrderHub.Core.Common`（跟 `PagedResult<T>` 同慣例）、ViewModel 綁定方式跟 `CreateOrderViewModel` 同一套模式，`code-reviewer` 審查為 LGTM
6. 至少 3 個新測試，`dotnet test` 全綠 —— ✅ 新增 4 個測試，`dotnet test` 37 個全綠

**練習 4**

1. 重構後 `dotnet test` 全綠 —— ✅ 37 個測試維持全綠
2. 我能說出這次重構「改善了什麼、沒有改變什麼」 —— 改善：`CreateOrderAsync` 從一個很肥的方法拆成 `ValidateNewOrderRequest`（同步前置檢查）＋`BuildOrderItemsAsync`（逐行解析＋扣庫存）＋協調者本體三塊；沒有改變：檢查順序、錯誤訊息文字、扣庫存的時機語意、`IOrderService` 介面、`CancelOrderAsync`、折扣相關方法全部維持原樣
3. 我有在 code review 的角度看過 diff（不是 agent 說好就好） —— 待自評（`code-reviewer` 有審過並確認只動了 `OrderService.cs` 一個檔案，但你自己有沒有再看一次 diff，這題只有你能打勾）

---

## 附錄：值得留下的對話片段

**片段 1 — 根因確認後才動手（練習 2 / bug 3）**

> 我：「商品頁的庫存數字跟實際盤點對不上，而且好像每次退單（取消訂單）之後就更少」
> agent：指出 `CancelOrderAsync` 裡 `order.Status = OrderStatus.Cancelled;` 先執行，才判斷 `if (order.Status == Pending || Confirmed)` 要不要回補庫存——這個判斷因此恆假，回補庫存的程式碼從未真的跑過。並附上修法方向（把判斷挪到狀態變更之前），等我回「可以」才真的動手改。

這樣問／這樣答有效的原因：症狀只講「數字對不上、退單後更少」，沒有指名是哪個方法，但因為描述夠具體（退單這個動作、越來越少這個方向性），agent 能沿著 Controller→Service 一路縮小範圍，而不是亂猜。

**片段 2 — Plan Mode 出計畫、核准後才寫程式（練習 3）**

> 我：「根據 documents/activities/activity-guideline.md 裡面的練習 3 — 新功能：低庫存警示頁面，幫我創造低庫存警示頁面」
> agent：先讀了 `ProductsController`／`ProductService`／`IProductRepository`／`Views/Products/Index.cshtml`，進 Plan Mode 產出一份計畫，明確寫出新 DTO `LowStockProduct` 放哪裡、「近 30 天」規則該放 Service 還是 Repository、怎麼避免 N+1（兩條查詢＋記憶體組合）、30 天邊界怎麼定義，最後用 `ExitPlanMode` 請我核准才開始寫程式。

這樣問有效的原因：把規格整段貼給 agent，並要求「先出計畫、我核准前不要動檔案」，讓分層決策（DTO 放哪、業務規則放哪層）在還是文字的時候就被檢查過一次，而不是等程式碼寫完才發現分層跑掉。
