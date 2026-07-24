namespace FamilyPortal.Application.Common;

/// <summary>不依賴 HTTP 的 Application 執行結果。</summary>
/// <typeparam name="T">成功時回傳的資料型別。</typeparam>
public sealed record ServiceResult<T>(bool IsSuccess, T? Value, ServiceError Error, string? Message)
{
    /// <summary>建立成功結果。</summary>
    /// <param name="value">成功時回傳的資料。</param>
    /// <returns>代表成功的 Application 執行結果。</returns>
    public static ServiceResult<T> Success(T value) => new(true, value, ServiceError.None, null);

    /// <summary>建立失敗結果。</summary>
    /// <param name="error">失敗類型。</param>
    /// <param name="message">供呼叫端處理的失敗訊息。</param>
    /// <returns>代表失敗的 Application 執行結果。</returns>
    public static ServiceResult<T> Failure(ServiceError error, string message) => new(false, default, error, message);
}
