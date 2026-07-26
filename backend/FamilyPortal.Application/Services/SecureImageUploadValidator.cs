using FamilyPortal.Application.Common;
using FamilyPortal.Application.Interfaces;
using FamilyPortal.Application.Models;

namespace FamilyPortal.Application.Services;

/// <summary>以有界緩衝驗證不可信任的影像上傳。</summary>
internal static class SecureImageUploadValidator
{
    private const int CopyBufferSize = 81920;

    internal static async Task<ValidatedMediaUpload?> ValidateAsync(
        MediaUpload upload,
        IImageInspector imageInspector,
        CancellationToken cancellationToken)
    {
        if (upload.Content is null || !upload.Content.CanRead ||
            upload.Length is <= 0 or > ValidationLimits.MaxMediaBytes ||
            string.IsNullOrWhiteSpace(upload.DeclaredContentType))
        {
            return null;
        }

        if (upload.Content.CanSeek)
        {
            upload.Content.Position = 0;
        }

        using var buffered = new MemoryStream((int)upload.Length);
        var buffer = new byte[CopyBufferSize];
        long total = 0;
        while (true)
        {
            var read = await upload.Content.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > ValidationLimits.MaxMediaBytes)
            {
                return null;
            }

            await buffered.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        if (total != upload.Length ||
            !ImageContainerValidator.TryInspect(
                buffered.GetBuffer().AsSpan(0, (int)total),
                out var image) ||
            !string.Equals(
                upload.DeclaredContentType.Trim(),
                image.ContentType,
                StringComparison.OrdinalIgnoreCase) ||
            !DimensionsAreAllowed(image.Width, image.Height))
        {
            return null;
        }

        buffered.Position = 0;
        var decoded = await imageInspector.InspectAsync(buffered, cancellationToken);
        if (decoded is null ||
            !string.Equals(decoded.ContentType, image.ContentType, StringComparison.Ordinal) ||
            decoded.Width != image.Width || decoded.Height != image.Height ||
            !DimensionsAreAllowed(decoded.Width, decoded.Height))
        {
            return null;
        }

        var ownedContent = new MemoryStream(buffered.ToArray(), writable: false);
        return new ValidatedMediaUpload(
            new MediaUpload(
                ownedContent,
                NormalizeFileName(upload.FileName),
                image.ContentType,
                total),
            image.ContentType,
            total,
            image.Width,
            image.Height);
    }

    private static bool DimensionsAreAllowed(int width, int height) =>
        width is > 0 and <= ValidationLimits.MaxMediaWidth &&
        height is > 0 and <= ValidationLimits.MaxMediaHeight &&
        (long)width * height <= ValidationLimits.MaxMediaPixels;

    private static string NormalizeFileName(string? fileName)
    {
        var normalized = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/'));
        return string.IsNullOrWhiteSpace(normalized) ? "upload" : normalized;
    }
}

internal static class ImageContainerValidator
{
    internal static bool TryInspect(ReadOnlySpan<byte> content, out ImageInfo image)
    {
        if (PngImageValidator.HasSignature(content))
        {
            return PngImageValidator.TryInspect(content, out image);
        }

        if (JpegImageValidator.HasSignature(content))
        {
            return JpegImageValidator.TryInspect(content, out image);
        }

        if (WebPImageValidator.HasSignature(content))
        {
            return WebPImageValidator.TryInspect(content, out image);
        }

        image = default;
        return false;
    }
}

internal readonly record struct ImageInfo(string ContentType, int Width, int Height);

internal sealed record ValidatedMediaUpload(
    MediaUpload Upload,
    string ContentType,
    long Length,
    int Width,
    int Height);
