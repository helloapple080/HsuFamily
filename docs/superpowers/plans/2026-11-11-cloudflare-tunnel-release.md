# Cloudflare Tunnel 部署實作計畫

**目標**：用單一主機部署現有系統，使用 Cloudflare Tunnel 提供家族登入網址，並交付兩種讀者的 Markdown／HTML 手冊。

**架構**：瀏覽器經 HTTPS 連到 Cloudflare，Tunnel 將流量送進 Docker 私有網路。Caddy 以同一網域分流 Next.js、.NET API 和 SignalR。MongoDB 與資料保護金鑰沿用現有 Compose volumes。

- [x] 比較自管主機、Cloudflare Containers 與 Firebase 的成本和遷移範圍。選擇既有主機與網域下無新增託管月費的方案。
- [x] 新增 Tunnel Compose overlay 和代理規則，限制來源與上傳大小，保留現有生產設定作為獨立部署路徑。
- [x] 修正前端 Docker 安裝時缺少套件 patch 的問題。
- [x] 產生開發者與使用者手冊，檢查 HTML 內容、Compose 合併與 Caddy 設定。使用本機替身服務驗證代理路由、HTTPS 標頭與上傳限額。
- [ ] 在使用者指定的主機，以獨立測試資料和正式拓撲檢查登入、權限、WebSocket 和備份還原。
- [ ] 以已驗證的發布版本和帳戶配置正式網址，核對外部 HTTPS 及非登入存取結果。
