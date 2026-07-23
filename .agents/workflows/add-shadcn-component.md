---
description: 新增 shadcn/ui 元件到前端專案
---

# 新增 shadcn/ui 元件

## 步驟

// turbo-all

1. 進入前端目錄並新增元件
```bash
cd frontend && npx shadcn@latest add <component-name>
```

元件會被安裝到 `src/components/ui/` 目錄。

2. 常用元件清單
```bash
# 基礎元件
npx shadcn@latest add button card dialog input label

# 資料展示
npx shadcn@latest add table badge avatar skeleton

# 導覽
npx shadcn@latest add dropdown-menu sheet tabs

# 表單
npx shadcn@latest add select checkbox textarea switch

# 回饋
npx shadcn@latest add alert sonner tooltip

# 行事曆
npx shadcn@latest add calendar popover
```

3. 驗證
```bash
cd frontend && pnpm build
```
