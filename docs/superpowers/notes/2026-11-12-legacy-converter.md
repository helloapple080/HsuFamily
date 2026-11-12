# 原版 MongoDB 離線遷移與還原演練

這是**離線轉換工具**，不會連線 MongoDB、Supabase 或 Google Drive，也不會部署或搬移正式資料。請先以假資料在獨立 Supabase 專案驗證；切換前保留原版及可獨立還原的備份。

## 1. 備份與匯出

1. 保留加密的 `mongodump`、原有圖片檔與必要的還原資料；先實際演練還原。JSON 只是轉換來源，不是原始資料庫的備份。
2. 用唯讀權限分別匯出 `Members`、`Itineraries`、`Events`、`MediaAssets` 到本機私有目錄。支援 [MongoDB Extended JSON v2](https://www.mongodb.com/docs/manual/reference/mongodb-extended-json/) 的 Relaxed、Canonical 及陣列／逐行文件，每檔最多 32 MiB。空 collection 提供空檔或 `[]`。
3. 不要匯出 `Users`、密碼雜湊或登入權杖給此工具。新版帳號應重新建立、核准。原始照片網址不得當成新版公開連結。

## 2. 先查核，不寫檔

從 `frontend` 執行（示例目錄請換成本機工作區以外的私有目錄）：

```sh
node scripts/migration/convert-mongo-export.mjs \
  --members /private/export/Members.json \
  --itineraries /private/export/Itineraries.json \
  --events /private/export/Events.json \
  --media /private/export/MediaAssets.json \
  --dry-run
```

報告不列出姓名、聯絡方式、來源 ID 或照片網址，只顯示來源與預計匯入筆數、異常原因及文件筆序（從 1 開始）。結束碼 2 表示有項目要核對；若有格式驗證錯誤或重複 ID，修正隔離副本後重跑，不會跳過錯誤資料繼續產出 SQL。

成員與行程採舊站的 UTC 民用日期。活動保留開始、結束、農曆週期種類、間隔、農曆日、閏月策略、有效期間與祭拜細節，寫入普通月曆資料；不重複建立一筆簡式拜拜規則。月曆活動日期範圍為 1901–2100，單次重複事件的長度最多 366 天。超出新版規格時停在核對，不會自行裁切。以固定 UUID 對應舊 ObjectId，保留跨資料關聯。原版的建立者 ID 不移植，由目標專案已核准的管理員承接。

原版頭像、照片網址、`MediaAssets` 與匯出缺少的關聯會分開報告；不下載、不複製舊照片。日後由管理員確認照片使用權與對應紀錄，再透過入口網上傳。確認原檔和對照資料已保存後，才可用 `--allow-partial` 產生 SQL，這不表示資料已完整搬遷。

## 3. 在隔離目標演練匯入與還原

1. 保留第 2 步的來源參數，把 `--dry-run` 改成 `--backup-confirmed --admin-id <已核准的目標測試管理員UUID> --output /private/export/restore-preview.sql`。程式只會建立新的檔案，權限 0600，並拒絕在原始碼工作區內輸出。
2. 在已套用所有 schema migrations 的隔離 PostgreSQL 以資料庫管理權限執行生成 SQL。再次核對實際專案名稱和管理員 ID。使用 `psql -v ON_ERROR_STOP=1 -f /private/export/restore-preview.sql` 或 Supabase 測試專案 SQL Editor。SQL 包含私密資料，**不可**上傳 GitHub、公開展示版、共享文件或聊天。
3. 匯入是在同一筆交易中**僅新增**，不覆寫、不清空資料。遇到 ID 衝突會整筆回滾；不要改成忽略衝突後盲目重跑。從新隔離資料庫重新演練，或者先人工核對差異。
4. 對照資料筆數、停用成員是否只對管理員可見、時區、關聯、農曆跨年及閏月。再以同一份備份在第二個隔離資料庫還原，並核對管理員可寫、家人可讀、未登入與撤權帳號被拒。

使用 `pnpm test scripts/migration/convert-mongo-export.test.mjs` 可在本機以嵌入式 PostgreSQL 跑虛構資料；此測試不等同已在託管 Supabase 驗收或已遷移正式資料。託管資料庫和 Storage 圖片本體仍需分開備份。
