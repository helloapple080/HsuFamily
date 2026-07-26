using System.Buffers.Binary;
using FamilyPortal.Infrastructure.Media;
using Xunit;

namespace FamilyPortal.Security.Tests.Infrastructure;

/// <summary>以真實 Skia codec 驗證完整影像解碼與格式識別。</summary>
public sealed class SkiaImageInspectorTests
{
    /// <summary>支援的真實影像必須完成像素解碼並回傳 canonical MIME。</summary>
    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    public async Task InspectAsync_ValidImageReturnsDecodedMetadata(string contentType)
    {
        var inspector = new SkiaImageInspector();
        await using var content = new MemoryStream(CreateImage(contentType));

        var result = await inspector.InspectAsync(content, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(contentType, result.ContentType);
        Assert.Equal(1, result.Width);
        Assert.Equal(1, result.Height);
    }

    /// <summary>CRC 正確但 zlib payload 損毀的 PNG 必須被完整解碼器拒絕。</summary>
    [Fact]
    public async Task InspectAsync_PngWithCorruptZlibPayloadReturnsNull()
    {
        var inspector = new SkiaImageInspector();
        var bytes = CorruptPngImageData(CreatePng());
        await using var content = new MemoryStream(bytes);

        var result = await inspector.InspectAsync(content, CancellationToken.None);

        Assert.Null(result);
    }

    /// <summary>JPEG entropy stream 截斷時不得接受部分解碼結果。</summary>
    [Fact]
    public async Task InspectAsync_JpegWithTruncatedEntropyReturnsNull()
    {
        var inspector = new SkiaImageInspector();
        var genuine = CreateJpeg();
        var bytes = genuine[..^24].Concat([(byte)0xFF, (byte)0xD9]).ToArray();
        await using var content = new MemoryStream(bytes);

        var result = await inspector.InspectAsync(content, CancellationToken.None);

        Assert.Null(result);
    }

    private static byte[] CreateImage(string contentType) => contentType switch
    {
        "image/jpeg" => CreateJpeg(),
        "image/png" => CreatePng(),
        "image/webp" => Convert.FromBase64String(
            "UklGRiIAAABXRUJQVlA4IBYAAAAwAQCdASoBAAEADsD+JaQAA3AAAAAA"),
        _ => throw new ArgumentOutOfRangeException(nameof(contentType))
    };

    private static byte[] CreatePng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAHhlWElmTU0AKgAAAAgABAEaAAUAAAABAAAAPgEbAAUAAAABAAAARgEoAAMAAAABAAIAAIdpAAQAAAABAAAATgAAAAAAAABIAAAAAQAAAEgAAAABAAOgAQADAAAAAQABAACgAgAEAAAAAQAAAAGgAwAEAAAAAQAAAAEAAAAAyq2pyQAAAAlwSFlzAAALEwAACxMBAJqcGAAAAA1JREFUCB1j+HTv5H8ACPgDmWZXp3EAAAAASUVORK5CYII=");

    private static byte[] CreateJpeg()
    {
        var encoded = Convert.FromBase64String(
            "/9j/4AAQSkZJRgABAQAASABIAAD/4QCARXhpZgAATU0AKgAAAAgABAEaAAUAAAABAAAAPgEbAAUAAAABAAAARgEoAAMAAAABAAIAAIdpAAQAAAABAAAATgAAAAAAAABIAAAAAQAAAEgAAAABAAOgAQADAAAAAQABAACgAgAEAAAAAQAAAAGgAwAEAAAAAQAAAAEAAAAA/+0AOFBob3Rvc2hvcCAzLjAAOEJJTQQEAAAAAAAAOEJJTQQlAAAAAAAQ1B2M2Y8AsgTpgAmY7PhCfv/AABEIAAEAAQMBIgACEQEDEQH/xAAfAAABBQEBAQEBAQAAAAAAAAAAAQIDBAUGBwgJCgv/xAC1EAACAQMDAgQDBQUEBAAAAX0BAgMABBEFEiExQQYTUWEHInEUMoGRoQgjQrHBFVLR8CQzYnKCCQoWFxgZGiUmJygpKjQ1Njc4OTpDREVGR0hJSlNUVVZXWFlaY2RlZmdoaWpzdHV2d3h5eoOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4eLj5OXm5+jp6vHy8/T19vf4+fr/xAAfAQADAQEBAQEBAQEBAAAAAAAAAQIDBAUGBwgJCgv/xAC1EQACAQIEBAMEBwUEBAABAncAAQIDEQQFITEGEkFRB2FxEyIygQgUQpGhscEJIzNS8BVictEKFiQ04SXxFxgZGiYnKCkqNTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqCg4SFhoeIiYqSk5SVlpeYmZqio6Slpqeoqaqys7S1tre4ubrCw8TFxsfIycrS09TV1tfY2dri4+Tl5ufo6ery8/T19vf4+fr/2wBDAAICAgICAgMCAgMFAwMDBQYFBQUFBggGBgYGBggKCAgICAgICgoKCgoKCgoMDAwMDAwODg4ODg8PDw8PDw8PDw//2wBDAQICAgQEBAcEBAcQCwkLEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBD/3QAEAAH/2gAMAwEAAhEDEQA/AP2Iooor4c+wP//Z");
        var genuine = new byte[encoded.Length + 3];
        encoded.AsSpan(0, 794).CopyTo(genuine);
        genuine.AsSpan(794, 3).Fill(0x10);
        encoded.AsSpan(794).CopyTo(genuine.AsSpan(797));
        return genuine;
    }

    private static byte[] CorruptPngImageData(byte[] bytes)
    {
        var offset = 8;
        while (offset <= bytes.Length - 12)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4));
            if (bytes.AsSpan(offset + 4, 4).SequenceEqual("IDAT"u8))
            {
                bytes[offset + 8] ^= 0xFF;
                BinaryPrimitives.WriteUInt32BigEndian(
                    bytes.AsSpan(offset + 8 + length, 4),
                    ComputeCrc32(bytes.AsSpan(offset + 4, length + 4)));
                return bytes;
            }

            offset += length + 12;
        }

        throw new InvalidOperationException("測試 PNG 缺少 IDAT chunk");
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xEDB88320u;
            }
        }

        return ~crc;
    }
}
