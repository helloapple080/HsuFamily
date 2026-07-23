---
description: 後端建置、測試、與驗證專案完整性
---

# 後端建置驗證

## 步驟

// turbo-all

1. 還原套件並建置
```bash
cd backend && export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec" && dotnet build 2>&1
```

2. 驗證建置結果
確認輸出包含「建置 成功」且沒有 errors。

3. 格式化程式碼（可選）
```bash
cd backend && export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec" && dotnet format
```
