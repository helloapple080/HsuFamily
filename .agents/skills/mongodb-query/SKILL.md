---
name: mongodb-query
description: >-
  MongoDB 查詢最佳化與常見模式。包含索引策略、游標分頁、聚合管道、文字搜尋。
  Use when writing MongoDB queries, optimizing performance, or adding new indexes.
---

# MongoDB 查詢技能

## C# MongoDB Driver 查詢模式

### 基本 CRUD
```csharp
// 新增
await _collection.InsertOneAsync(entity);

// 查詢（強型別）
await _collection.Find(x => x.Id == id).FirstOrDefaultAsync();

// 更新（整筆替換）
await _collection.ReplaceOneAsync(x => x.Id == id, entity);

// 部分更新（僅更新指定欄位）
var update = Builders<Entity>.Update
    .Set(e => e.Name, "新名稱")
    .Set(e => e.UpdatedAt, DateTime.UtcNow);
await _collection.UpdateOneAsync(x => x.Id == id, update);

// 刪除
await _collection.DeleteOneAsync(x => x.Id == id);
```

### 複合 Filter
```csharp
var filter = Builders<Event>.Filter.And(
    Builders<Event>.Filter.Gte(e => e.StartDate, start),
    Builders<Event>.Filter.Lte(e => e.StartDate, end),
    Builders<Event>.Filter.Eq(e => e.EventType, EventType.Festival)
);
var results = await _collection.Find(filter).ToListAsync();
```

### 陣列查詢
```csharp
// 陣列包含某值
var filter = Builders<Event>.Filter.AnyEq(e => e.Tags, "節日");

// 嵌套物件陣列
var filter = Builders<Itinerary>.Filter
    .ElemMatch(i => i.Participants, p => p.MemberId == memberId);
```

## 游標式分頁（Cursor-Based Pagination）

### 為什麼不用 Skip/Limit
Skip 大量文件時效能急速下降（O(n) 複雜度），游標分頁始終是 O(log n)。

### 實作模式
```csharp
public async Task<List<T>> GetPagedAsync(
    DateTime? cursorDate, string? cursorId, int pageSize = 20)
{
    var filterBuilder = Builders<T>.Filter;
    var filter = filterBuilder.Empty;

    if (cursorDate.HasValue && cursorId != null)
    {
        filter = filterBuilder.Or(
            filterBuilder.Lt("date", cursorDate.Value),
            filterBuilder.And(
                filterBuilder.Eq("date", cursorDate.Value),
                filterBuilder.Lt("_id", ObjectId.Parse(cursorId))
            )
        );
    }

    return await _collection
        .Find(filter)
        .SortByDescending(x => x.Date)
        .ThenByDescending(x => x.Id)
        .Limit(pageSize)
        .ToListAsync();
}
```

## 索引策略

### 建立原則
1. **ESR 規則**: Equality → Sort → Range（索引欄位排列順序）
2. 複合索引最多 3-4 個欄位
3. 每個 Collection 索引數不超過 10 個
4. 觀察 `explain()` 確認索引使用

### 常用索引模式
```csharp
// 唯一索引
new CreateIndexModel<User>(
    Builders<User>.IndexKeys.Ascending(u => u.Username),
    new CreateIndexOptions { Unique = true }
);

// 複合索引
new CreateIndexModel<Event>(
    Builders<Event>.IndexKeys
        .Ascending(e => e.StartDate)
        .Ascending(e => e.EventType)
);

// 稀疏索引（只索引有值的文件）
new CreateIndexModel<User>(
    Builders<User>.IndexKeys.Ascending("refreshToken.token"),
    new CreateIndexOptions { Sparse = true }
);

// 文字索引
new CreateIndexModel<Itinerary>(
    Builders<Itinerary>.IndexKeys
        .Text(i => i.Title)
        .Text(i => i.Description)
);
```

## 效能檢查清單
- [ ] 查詢有使用到索引（IXSCAN 而非 COLLSCAN）
- [ ] 大量查詢使用投影（Projection）
- [ ] 分頁使用游標而非 Skip/Limit
- [ ] 寫入操作不在迴圈中（使用 BulkWrite）
- [ ] DateTime 使用 UTC 格式儲存
