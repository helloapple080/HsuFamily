using FamilyPortal.Application.Models;

namespace FamilyPortal.Application.Interfaces;

/// <summary>以供應商無關方式完整解碼並檢查影像。</summary>
public interface IImageInspector
{
    /// <summary>完整解碼影像；格式不支援或內容損毀時回傳 null，且不得 dispose 由呼叫端擁有的 stream。</summary>
    Task<ImageInspectionResult?> InspectAsync(
        Stream content,
        CancellationToken cancellationToken);
}
