namespace FamilyPortal.Application.DTOs;

/// <summary>
/// 統一 API 回應包裝 — 用於所有 API 端點的回應格式
/// </summary>
/// <typeparam name="T">回應資料型別</typeparam>
public class ApiResponse<T>
{
    /// <summary>是否成功</summary>
    public bool Success { get; set; }

    /// <summary>訊息</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>回應資料</summary>
    public T? Data { get; set; }

    /// <summary>錯誤清單（用於驗證失敗等場景）</summary>
    public List<string>? Errors { get; set; }

    /// <summary>建立成功回應</summary>
    public static ApiResponse<T> Ok(T data, string message = "操作成功")
        => new() { Success = true, Message = message, Data = data };

    /// <summary>建立失敗回應</summary>
    public static ApiResponse<T> Fail(string message, List<string>? errors = null)
        => new() { Success = false, Message = message, Errors = errors };
}

/// <summary>無資料的 API 回應（用於刪除等操作）</summary>
public class ApiResponse : ApiResponse<object>
{
    /// <summary>建立成功回應（無資料）</summary>
    public static ApiResponse Ok(string message = "操作成功")
        => new() { Success = true, Message = message };

    /// <summary>建立失敗回應（無資料）</summary>
    public new static ApiResponse Fail(string message, List<string>? errors = null)
        => new() { Success = false, Message = message, Errors = errors };
}
