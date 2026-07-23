---
description: Docker 容器化部署流程
---

# Docker 部署

## 步驟

1. 複製環境變數範本
```bash
cp .env.example .env
```
然後編輯 `.env` 填入安全的密鑰值。

// turbo
2. 建置並啟動所有容器
```bash
docker compose up -d --build
```

// turbo
3. 確認所有容器都在運行
```bash
docker compose ps
```

// turbo
4. 查看後端日誌（確認索引建立與 Admin 帳號初始化）
```bash
docker compose logs backend -f --tail 50
```

5. 驗證服務
- 後端健康檢查: `curl http://localhost:5001/health`
- 前端: 瀏覽 `http://localhost:3000`

## 常用指令
```bash
docker compose down          # 停止所有服務
docker compose down -v       # 停止並清除資料卷（⚠️ 資料庫資料也會刪除）
docker compose logs -f       # 即時查看所有日誌
docker compose restart api   # 重啟特定服務
```
