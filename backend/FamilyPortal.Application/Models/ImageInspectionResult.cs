namespace FamilyPortal.Application.Models;

/// <summary>完整影像解碼後的供應商無關結果。</summary>
/// <param name="ContentType">canonical MIME 類型。</param>
/// <param name="Width">像素寬度。</param>
/// <param name="Height">像素高度。</param>
public sealed record ImageInspectionResult(string ContentType, int Width, int Height);
