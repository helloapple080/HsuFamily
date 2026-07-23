---
name: error-handling
description: >-
  錯誤處理最佳實踐。包含後端例外中間件、前端錯誤邊界、React Query 錯誤處理、Loading 狀態設計。
  Use when implementing error boundaries, adding global exception handling, or designing loading/error states.
---

# 錯誤處理技能

## 後端全域例外中間件

### 建立 ExceptionMiddleware
```csharp
// 位置: FamilyPortal.Api/Middleware/ExceptionMiddleware.cs
public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "未處理的例外: {Message} on {Path}",
                ex.Message, context.Request.Path);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        context.Response.ContentType = "application/json";
        var response = ex switch
        {
            ArgumentException or InvalidOperationException =>
                (StatusCode: 400, Message: ex.Message),
            UnauthorizedAccessException =>
                (StatusCode: 401, Message: "未授權"),
            KeyNotFoundException =>
                (StatusCode: 404, Message: "資源不存在"),
            _ =>
                (StatusCode: 500, Message: "伺服器發生錯誤，請稍後重試")
        };

        context.Response.StatusCode = response.StatusCode;
        await context.Response.WriteAsJsonAsync(
            ApiResponse.Fail(response.Message));
    }
}
```

### 在 Program.cs 中啟用
```csharp
app.UseMiddleware<ExceptionMiddleware>(); // 放在最前面
```

## 前端 React Query 錯誤處理

### 全域錯誤處理
```typescript
// 在 QueryClient 設定中
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: (failureCount, error) => {
        // 401/403 不重試
        if (axios.isAxiosError(error) && [401, 403].includes(error.response?.status ?? 0)) {
          return false
        }
        return failureCount < 2
      },
      throwOnError: false,
    },
    mutations: {
      onError: (error) => {
        if (axios.isAxiosError(error)) {
          toast.error(error.response?.data?.message || '操作失敗')
        }
      },
    },
  },
})
```

### useQuery 使用模式
```typescript
const { data, isLoading, isError, error } = useQuery({
  queryKey: ['events', { start, end }],
  queryFn: () => api.get<ApiResponse<CalendarEvent[]>>('/api/events', { params: { start, end } })
    .then(res => {
      if (!res.data.success) throw new Error(res.data.message)
      return res.data.data ?? []
    }),
})

// 在 JSX 中
if (isLoading) return <CalendarSkeleton />
if (isError) return <ErrorState message="無法載入行事曆" onRetry={refetch} />
```

## Loading 狀態元件模版
```tsx
/// 骨架載入元件
function CalendarSkeleton() {
  return (
    <div className="space-y-3 animate-pulse">
      {Array.from({ length: 5 }).map((_, i) => (
        <Skeleton key={i} className="h-12 w-full rounded-lg" />
      ))}
    </div>
  )
}

/// 錯誤狀態元件
function ErrorState({ message, onRetry }: { message: string; onRetry?: () => void }) {
  return (
    <div className="flex flex-col items-center justify-center py-12 gap-4">
      <span className="text-4xl">⚠️</span>
      <p className="text-[var(--muted-foreground)] text-sm">{message}</p>
      {onRetry && (
        <Button variant="outline" size="sm" onClick={onRetry}>
          重新載入
        </Button>
      )}
    </div>
  )
}

/// 空狀態元件
function EmptyState({ message }: { message: string }) {
  return (
    <div className="flex flex-col items-center justify-center py-12 gap-3">
      <span className="text-4xl">📭</span>
      <p className="text-[var(--muted-foreground)] text-sm">{message}</p>
    </div>
  )
}
```

## 後端 Health Check
```csharp
// 在 Program.cs 中加入
builder.Services.AddHealthChecks()
    .AddMongoDb(
        mongodbConnectionString: builder.Configuration["MongoDbSettings:ConnectionString"]!,
        name: "mongodb",
        timeout: TimeSpan.FromSeconds(3)
    );

// ...
app.MapHealthChecks("/health");
```

## 通知模式（Sonner Toast）
```typescript
// 成功
toast.success('事件已建立', { description: event.title })

// 錯誤
toast.error('建立失敗', { description: error.message })

// 載入中（promise pattern）
toast.promise(createEventMutation.mutateAsync(data), {
  loading: '建立中...',
  success: '事件已建立！',
  error: '建立失敗，請重試',
})
```
