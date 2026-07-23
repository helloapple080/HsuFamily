# 🏠 許氏入口網 — 後端 (.NET 10 Web API) | Hsu Family Portal Backend

## 架構概覽 (Clean Architecture)

```
FamilyPortal.Domain          ← 最核心層，零外部依賴
    ↑
FamilyPortal.Application     ← 介面層，定義 Repository / Service 介面
    ↑
FamilyPortal.Infrastructure  ← 實作層，MongoDB / JWT / Argon2id / SignalR
    ↑
FamilyPortal.Api             ← 表現層，Controllers / Middleware / 啟動配置
```

## 技術棧
- **.NET 10** — LTS, 最新 Web API 框架
- **MongoDB Driver 3.7** — 官方 C# Driver
- **Argon2id** — OWASP 推薦的密碼加密演算法
- **JWT Bearer** — Access Token (30min) + Refresh Token (7 days)
- **SignalR** — WebSocket 即時通訊
- **Rate Limiting** — 內建速率限制中間件

## 啟動方式

```bash
# 確保 MongoDB 已啟動
mongod --dbpath /data/db

# 啟動 API
dotnet run --project FamilyPortal.Api

# 開發模式 (hot reload)
dotnet watch --project FamilyPortal.Api
```

## 預設帳號
啟動時自動建立（如不存在）：
- 帳號：`admin`
- 密碼：`Admin@123456`
- 角色：`Admin` (顯示名稱：許浴宸)
- 安全性：啟動時自動於 `Users` 集合建立 (若不存在)

## API 端點

| 方法 | 端點 | 權限 | 說明 |
|------|------|------|------|
| POST | `/api/auth/login` | 公開 | 登入 |
| POST | `/api/auth/refresh` | 公開 | Token 更新 |
| POST | `/api/auth/logout` | 已登入 | 登出 |
| POST | `/api/auth/register` | Admin | 建立帳號 |
| GET | `/api/auth/me` | 已登入 | 當前使用者 |
| GET/POST/PUT/DELETE | `/api/members` | Admin (寫) / User (讀) | 家庭成員 |
| GET/POST/PUT/DELETE | `/api/events` | Admin (寫) / User (讀) | 行事曆事件 |
| GET/POST/PUT/DELETE | `/api/itineraries` | Admin (寫) / User (讀) | 歷年行程 |

## SignalR Hub
- **路由**: `/hubs/calendar`
- **事件**: `EventCreated`, `EventUpdated`, `EventDeleted`
- **群組**: `FamilyCalendar`

## 環境配置 (appsettings.json)

```json
{
  "MongoDbSettings": {
    "ConnectionString": "mongodb://localhost:27017",
    "DatabaseName": "FamilyPortal"
  },
  "JwtSettings": {
    "SecretKey": "至少 32 字元的密鑰",
    "Issuer": "FamilyPortal",
    "Audience": "FamilyPortalApp",
    "AccessTokenExpirationMinutes": 30,
    "RefreshTokenExpirationDays": 7
  }
}
```

## MongoDB 索引
啟動時自動建立 10+ 索引，包含：
- Users: `username` (unique), `email` (unique), `refreshTokens.tokenHash` (sparse)
- Members: `isActive + sortOrder`, `name`
- Events: `startDate + eventType`, `tags`, `lunarDate.*`
- Itineraries: `date + tags`, `participants.memberId + date`, text index
