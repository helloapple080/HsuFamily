using FamilyPortal.Application.Common;
using FamilyPortal.Application.Interfaces;
using FamilyPortal.Application.Models;
using SkiaSharp;

namespace FamilyPortal.Infrastructure.Media;

/// <summary>使用 Skia codec 完整解碼 JPEG、PNG 與 WebP。</summary>
public sealed class SkiaImageInspector : IImageInspector
{
    /// <inheritdoc />
    public Task<ImageInspectionResult?> InspectAsync(
        Stream content,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (content is null || !content.CanRead)
        {
            return Task.FromResult<ImageInspectionResult?>(null);
        }

        try
        {
            if (content.CanSeek)
            {
                content.Position = 0;
            }

            using var managedStream = new SKManagedStream(content, disposeManagedStream: false);
            using var codec = SKCodec.Create(managedStream);
            if (codec is null || !TryMapContentType(codec.EncodedFormat, out var contentType))
            {
                return Task.FromResult<ImageInspectionResult?>(null);
            }

            var info = codec.Info;
            if (!DimensionsAreAllowed(info.Width, info.Height))
            {
                return Task.FromResult<ImageInspectionResult?>(null);
            }

            var decodeInfo = new SKImageInfo(
                info.Width,
                info.Height,
                SKColorType.Rgba8888,
                SKAlphaType.Unpremul);
            using var bitmap = new SKBitmap(decodeInfo);
            var decodeResult = codec.GetPixels(
                decodeInfo,
                bitmap.GetPixels(),
                bitmap.RowBytes,
                new SKCodecOptions());
            return Task.FromResult<ImageInspectionResult?>(
                decodeResult == SKCodecResult.Success
                    ? new ImageInspectionResult(contentType, info.Width, info.Height)
                    : null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult<ImageInspectionResult?>(null);
        }
    }

    private static bool DimensionsAreAllowed(int width, int height) =>
        width is > 0 and <= ValidationLimits.MaxMediaWidth &&
        height is > 0 and <= ValidationLimits.MaxMediaHeight &&
        (long)width * height <= ValidationLimits.MaxMediaPixels;

    private static bool TryMapContentType(SKEncodedImageFormat format, out string contentType)
    {
        contentType = format switch
        {
            SKEncodedImageFormat.Jpeg => "image/jpeg",
            SKEncodedImageFormat.Png => "image/png",
            SKEncodedImageFormat.Webp => "image/webp",
            _ => string.Empty
        };
        return contentType.Length > 0;
    }
}
