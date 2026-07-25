using System.Buffers.Binary;

namespace FamilyPortal.Application.Services;

/// <summary>驗證 PNG chunk 邊界、順序、CRC 與必要影像資料。</summary>
internal static class PngImageValidator
{
    private static readonly byte[] Signature =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    internal static bool HasSignature(ReadOnlySpan<byte> content) => content.StartsWith(Signature);

    internal static bool TryInspect(ReadOnlySpan<byte> content, out ImageInfo image)
    {
        image = default;
        var offset = Signature.Length;
        var sawHeader = false;
        var sawImageData = false;
        var imageDataEnded = false;
        var sawPalette = false;
        var colorType = 0;
        var width = 0;
        var height = 0;

        while (offset <= content.Length - 12)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(content.Slice(offset, 4));
            if (length > int.MaxValue)
            {
                return false;
            }

            var end = (long)offset + 12 + length;
            if (end > content.Length)
            {
                return false;
            }

            var typeAndData = content.Slice(offset + 4, 4 + (int)length);
            var expectedCrc = BinaryPrimitives.ReadUInt32BigEndian(
                content.Slice(offset + 8 + (int)length, 4));
            if (ComputeCrc32(typeAndData) != expectedCrc)
            {
                return false;
            }

            var type = typeAndData[..4];
            var data = typeAndData[4..];
            if (!sawHeader)
            {
                if (!type.SequenceEqual("IHDR"u8) || length != 13 || !TryReadHeader(data, out width, out height))
                {
                    return false;
                }

                sawHeader = true;
                colorType = data[9];
            }
            else if (type.SequenceEqual("IHDR"u8))
            {
                return false;
            }
            else if (type.SequenceEqual("PLTE"u8))
            {
                if (sawPalette || sawImageData || length is 0 or > 768 || length % 3 != 0)
                {
                    return false;
                }

                sawPalette = true;
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                if (imageDataEnded || length == 0 || colorType == 3 && !sawPalette)
                {
                    return false;
                }

                sawImageData = true;
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                if (length != 0 || !sawImageData || end != content.Length)
                {
                    return false;
                }

                image = new ImageInfo("image/png", width, height);
                return true;
            }
            else if (type[0] is >= (byte)'A' and <= (byte)'Z')
            {
                return false;
            }
            else if (sawImageData)
            {
                imageDataEnded = true;
            }

            offset = (int)end;
        }

        return false;
    }

    private static bool TryReadHeader(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = BinaryPrimitives.ReadInt32BigEndian(data[..4]);
        height = BinaryPrimitives.ReadInt32BigEndian(data.Slice(4, 4));
        var bitDepth = data[8];
        var colorType = data[9];
        var validColorDepth = colorType switch
        {
            0 => bitDepth is 1 or 2 or 4 or 8 or 16,
            2 => bitDepth is 8 or 16,
            3 => bitDepth is 1 or 2 or 4 or 8,
            4 or 6 => bitDepth is 8 or 16,
            _ => false
        };

        return width > 0 && height > 0 && validColorDepth &&
            data[10] == 0 && data[11] == 0 && data[12] <= 1;
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
