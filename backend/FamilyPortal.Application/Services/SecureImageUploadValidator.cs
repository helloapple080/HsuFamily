using System.Buffers.Binary;
using FamilyPortal.Application.Models;

namespace FamilyPortal.Application.Services;

/// <summary>以有界緩衝與檔頭結構解析驗證不可信任的影像上傳。</summary>
internal static class SecureImageUploadValidator
{
    private const long MaxUploadBytes = 10 * 1024 * 1024;
    private const int CopyBufferSize = 81920;

    private static readonly byte[] PngSignature =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    internal static async Task<ValidatedMediaUpload?> ValidateAsync(
        MediaUpload upload,
        CancellationToken cancellationToken)
    {
        if (upload.Content is null ||
            !upload.Content.CanRead ||
            upload.Length is <= 0 or > MaxUploadBytes ||
            string.IsNullOrWhiteSpace(upload.DeclaredContentType))
        {
            return null;
        }

        if (upload.Content.CanSeek)
        {
            upload.Content.Position = 0;
        }

        var buffered = new MemoryStream((int)upload.Length);
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
            if (total > MaxUploadBytes)
            {
                buffered.Dispose();
                return null;
            }

            await buffered.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        if (total != upload.Length)
        {
            buffered.Dispose();
            return null;
        }

        if (!TryInspectImage(buffered.GetBuffer().AsSpan(0, (int)total), out var image) ||
            !string.Equals(
                upload.DeclaredContentType.Trim(),
                image.ContentType,
                StringComparison.OrdinalIgnoreCase))
        {
            buffered.Dispose();
            return null;
        }

        buffered.Position = 0;
        var fileName = NormalizeFileName(upload.FileName);
        return new ValidatedMediaUpload(
            new MediaUpload(buffered, fileName, image.ContentType, total),
            image.ContentType,
            total,
            image.Width,
            image.Height);
    }

    private static bool TryInspectImage(ReadOnlySpan<byte> content, out ImageInfo image)
    {
        if (content.StartsWith(PngSignature))
        {
            return TryInspectPng(content, out image);
        }

        if (content.Length >= 2 && content[0] == 0xFF && content[1] == 0xD8)
        {
            return TryInspectJpeg(content, out image);
        }

        if (content.Length >= 12 &&
            content[..4].SequenceEqual("RIFF"u8) &&
            content.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return TryInspectWebP(content, out image);
        }

        image = default;
        return false;
    }

    private static bool TryInspectPng(ReadOnlySpan<byte> content, out ImageInfo image)
    {
        image = default;
        if (content.Length < 57)
        {
            return false;
        }

        var offset = PngSignature.Length;
        var hasHeader = false;
        var hasImageData = false;
        var width = 0;
        var height = 0;

        while (offset <= content.Length - 12)
        {
            var chunkLength = BinaryPrimitives.ReadUInt32BigEndian(content.Slice(offset, 4));
            if (chunkLength > int.MaxValue)
            {
                return false;
            }

            var chunkEnd = (long)offset + 12 + chunkLength;
            if (chunkEnd > content.Length)
            {
                return false;
            }

            var chunkType = content.Slice(offset + 4, 4);
            var chunkData = content.Slice(offset + 8, (int)chunkLength);
            if (!hasHeader)
            {
                if (!chunkType.SequenceEqual("IHDR"u8) || chunkLength != 13)
                {
                    return false;
                }

                width = BinaryPrimitives.ReadInt32BigEndian(chunkData[..4]);
                height = BinaryPrimitives.ReadInt32BigEndian(chunkData.Slice(4, 4));
                if (width <= 0 ||
                    height <= 0 ||
                    chunkData[10] != 0 ||
                    chunkData[11] != 0 ||
                    chunkData[12] > 1)
                {
                    return false;
                }

                hasHeader = true;
            }
            else if (chunkType.SequenceEqual("IDAT"u8))
            {
                hasImageData = true;
            }
            else if (chunkType.SequenceEqual("IEND"u8))
            {
                if (chunkLength != 0 || !hasImageData || chunkEnd != content.Length)
                {
                    return false;
                }

                image = new ImageInfo("image/png", width, height);
                return true;
            }

            offset = (int)chunkEnd;
        }

        return false;
    }

    private static bool TryInspectJpeg(ReadOnlySpan<byte> content, out ImageInfo image)
    {
        image = default;
        if (content.Length < 11 || content[^2] != 0xFF || content[^1] != 0xD9)
        {
            return false;
        }

        var offset = 2;
        while (offset < content.Length - 2)
        {
            if (content[offset] != 0xFF)
            {
                return false;
            }

            while (offset < content.Length && content[offset] == 0xFF)
            {
                offset++;
            }

            if (offset >= content.Length)
            {
                return false;
            }

            var marker = content[offset++];
            if (marker == 0xD9)
            {
                break;
            }

            if (marker == 0x01 || marker is >= 0xD0 and <= 0xD8)
            {
                continue;
            }

            if (offset > content.Length - 2)
            {
                return false;
            }

            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(content.Slice(offset, 2));
            if (segmentLength < 2 || offset + segmentLength > content.Length)
            {
                return false;
            }

            if (IsStartOfFrame(marker))
            {
                if (segmentLength < 7)
                {
                    return false;
                }

                var height = BinaryPrimitives.ReadUInt16BigEndian(content.Slice(offset + 3, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(content.Slice(offset + 5, 2));
                if (width == 0 || height == 0)
                {
                    return false;
                }

                image = new ImageInfo("image/jpeg", width, height);
                return true;
            }

            if (marker == 0xDA)
            {
                return false;
            }

            offset += segmentLength;
        }

        return false;
    }

    private static bool IsStartOfFrame(byte marker) => marker is
        0xC0 or 0xC1 or 0xC2 or 0xC3 or
        0xC5 or 0xC6 or 0xC7 or
        0xC9 or 0xCA or 0xCB or
        0xCD or 0xCE or 0xCF;

    private static bool TryInspectWebP(ReadOnlySpan<byte> content, out ImageInfo image)
    {
        image = default;
        if (content.Length < 20)
        {
            return false;
        }

        var riffSize = BinaryPrimitives.ReadUInt32LittleEndian(content.Slice(4, 4));
        if ((long)riffSize + 8 != content.Length)
        {
            return false;
        }

        var offset = 12;
        while (offset <= content.Length - 8)
        {
            var chunkType = content.Slice(offset, 4);
            var chunkLength = BinaryPrimitives.ReadUInt32LittleEndian(content.Slice(offset + 4, 4));
            if (chunkLength > int.MaxValue)
            {
                return false;
            }

            var dataOffset = offset + 8;
            var paddedLength = (long)chunkLength + (chunkLength & 1);
            var chunkEnd = dataOffset + paddedLength;
            if (chunkEnd > content.Length)
            {
                return false;
            }

            var data = content.Slice(dataOffset, (int)chunkLength);
            if (chunkType.SequenceEqual("VP8X"u8) && TryReadVp8XDimensions(data, out var width, out var height) ||
                chunkType.SequenceEqual("VP8L"u8) && TryReadVp8LDimensions(data, out width, out height) ||
                chunkType.SequenceEqual("VP8 "u8) && TryReadVp8Dimensions(data, out width, out height))
            {
                image = new ImageInfo("image/webp", width, height);
                return true;
            }

            offset = (int)chunkEnd;
        }

        return false;
    }

    private static bool TryReadVp8XDimensions(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length < 10)
        {
            return false;
        }

        width = ReadUInt24LittleEndian(data.Slice(4, 3)) + 1;
        height = ReadUInt24LittleEndian(data.Slice(7, 3)) + 1;
        return width > 0 && height > 0;
    }

    private static bool TryReadVp8LDimensions(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length < 5 || data[0] != 0x2F)
        {
            return false;
        }

        width = 1 + data[1] + ((data[2] & 0x3F) << 8);
        height = 1 + (data[2] >> 6) + (data[3] << 2) + ((data[4] & 0x0F) << 10);
        return true;
    }

    private static bool TryReadVp8Dimensions(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length < 10 ||
            !data.Slice(3, 3).SequenceEqual([(byte)0x9D, (byte)0x01, (byte)0x2A]))
        {
            return false;
        }

        width = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(6, 2)) & 0x3FFF;
        height = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(8, 2)) & 0x3FFF;
        return width > 0 && height > 0;
    }

    private static int ReadUInt24LittleEndian(ReadOnlySpan<byte> data) =>
        data[0] | (data[1] << 8) | (data[2] << 16);

    private static string NormalizeFileName(string? fileName)
    {
        var normalized = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/'));
        return string.IsNullOrWhiteSpace(normalized) ? "upload" : normalized;
    }

    private readonly record struct ImageInfo(string ContentType, int Width, int Height);
}

/// <summary>經安全驗證且可交給儲存 adapter 的媒體內容。</summary>
internal sealed record ValidatedMediaUpload(
    MediaUpload Upload,
    string ContentType,
    long Length,
    int Width,
    int Height);
