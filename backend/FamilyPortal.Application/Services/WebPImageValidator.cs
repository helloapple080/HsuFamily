using System.Buffers.Binary;

namespace FamilyPortal.Application.Services;

/// <summary>驗證 WebP RIFF chunk 邊界、padding 與實際 VP8/VP8L 影像 payload。</summary>
internal static class WebPImageValidator
{
    private const byte IccProfileFlag = 0x20;
    private const byte AlphaFlag = 0x10;
    private const byte ExifFlag = 0x08;
    private const byte XmpFlag = 0x04;
    private const byte AnimationFlag = 0x02;
    private const byte ReservedFlagMask = 0xC1;

    internal static bool HasSignature(ReadOnlySpan<byte> content) =>
        content.Length >= 12 && content[..4].SequenceEqual("RIFF"u8) &&
        content.Slice(8, 4).SequenceEqual("WEBP"u8);

    internal static bool TryInspect(ReadOnlySpan<byte> content, out ImageInfo image)
    {
        image = default;
        var riffSize = BinaryPrimitives.ReadUInt32LittleEndian(content.Slice(4, 4));
        if ((long)riffSize + 8 != content.Length)
        {
            return false;
        }

        var offset = 12;
        (int Width, int Height)? canvas = null;
        (int Width, int Height)? payload = null;
        byte extendedFlags = 0;
        var payloadIsLossless = false;
        var sawIccProfile = false;
        var sawAlpha = false;
        var sawExif = false;
        var sawXmp = false;
        var sawAnimationHeader = false;
        var sawAnimationFrame = false;
        while (offset <= content.Length - 8)
        {
            var type = content.Slice(offset, 4);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(content.Slice(offset + 4, 4));
            if (length > int.MaxValue)
            {
                return false;
            }

            var dataOffset = offset + 8;
            var paddedLength = (long)length + (length & 1);
            var end = dataOffset + paddedLength;
            if (end > content.Length)
            {
                return false;
            }

            if ((length & 1) != 0 && content[dataOffset + (int)length] != 0)
            {
                return false;
            }

            var data = content.Slice(dataOffset, (int)length);
            if (type.SequenceEqual("VP8X"u8))
            {
                if (offset != 12 || canvas is not null || payload is not null ||
                    !TryReadVp8X(data, out var width, out var height, out extendedFlags))
                {
                    return false;
                }

                canvas = (width, height);
            }
            else if (type.SequenceEqual("ICCP"u8))
            {
                if (canvas is null || sawIccProfile || sawAlpha || payload is not null ||
                    sawAnimationHeader || sawAnimationFrame || length == 0)
                {
                    return false;
                }

                sawIccProfile = true;
            }
            else if (type.SequenceEqual("ALPH"u8))
            {
                if (canvas is null || sawAlpha || payload is not null ||
                    sawAnimationHeader || sawAnimationFrame || length <= 1)
                {
                    return false;
                }

                sawAlpha = true;
            }
            else if (type.SequenceEqual("ANIM"u8))
            {
                if (canvas is null || sawAnimationHeader || sawAnimationFrame ||
                    payload is not null || sawAlpha || length != 6)
                {
                    return false;
                }

                sawAnimationHeader = true;
            }
            else if (type.SequenceEqual("ANMF"u8))
            {
                if (canvas is null || !sawAnimationHeader || payload is not null || length < 16)
                {
                    return false;
                }

                sawAnimationFrame = true;
            }
            else if (type.SequenceEqual("VP8 "u8))
            {
                if (sawAnimationHeader || sawAnimationFrame || payload is not null ||
                    !TryReadVp8(data, out var width, out var height))
                {
                    return false;
                }

                payload = (width, height);
            }
            else if (type.SequenceEqual("VP8L"u8))
            {
                if (sawAnimationHeader || sawAnimationFrame || sawAlpha || payload is not null ||
                    !TryReadVp8L(data, out var width, out var height))
                {
                    return false;
                }

                payload = (width, height);
                payloadIsLossless = true;
            }
            else if (type.SequenceEqual("EXIF"u8))
            {
                if (canvas is null || sawExif || length == 0)
                {
                    return false;
                }

                sawExif = true;
            }
            else if (type.SequenceEqual("XMP "u8))
            {
                if (canvas is null || sawXmp || length == 0)
                {
                    return false;
                }

                sawXmp = true;
            }

            offset = (int)end;
        }

        if (offset != content.Length)
        {
            return false;
        }

        if (canvas is null)
        {
            if (payload is null)
            {
                return false;
            }

            image = new ImageInfo("image/webp", payload.Value.Width, payload.Value.Height);
            return true;
        }

        var isAnimated = HasFlag(extendedFlags, AnimationFlag);
        if (HasFlag(extendedFlags, IccProfileFlag) != sawIccProfile ||
            HasFlag(extendedFlags, ExifFlag) != sawExif ||
            HasFlag(extendedFlags, XmpFlag) != sawXmp ||
            isAnimated != (sawAnimationHeader && sawAnimationFrame) ||
            sawAnimationHeader != sawAnimationFrame ||
            HasFlag(extendedFlags, AlphaFlag) != (sawAlpha || payloadIsLossless && HasFlag(extendedFlags, AlphaFlag)))
        {
            return false;
        }

        if (isAnimated)
        {
            if (payload is not null)
            {
                return false;
            }

            image = new ImageInfo("image/webp", canvas.Value.Width, canvas.Value.Height);
            return true;
        }

        if (payload is null || canvas.Value != payload.Value || sawAnimationHeader || sawAnimationFrame)
        {
            return false;
        }

        image = new ImageInfo("image/webp", payload.Value.Width, payload.Value.Height);
        return true;
    }

    private static bool TryReadVp8X(
        ReadOnlySpan<byte> data,
        out int width,
        out int height,
        out byte flags)
    {
        width = 0;
        height = 0;
        flags = 0;
        if (data.Length != 10 || (data[0] & ReservedFlagMask) != 0 ||
            data[1] != 0 || data[2] != 0 || data[3] != 0)
        {
            return false;
        }

        flags = data[0];
        width = ReadUInt24(data.Slice(4, 3)) + 1;
        height = ReadUInt24(data.Slice(7, 3)) + 1;
        return width > 0 && height > 0;
    }

    private static bool TryReadVp8(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length <= 10)
        {
            return false;
        }

        var frameTag = ReadUInt24(data[..3]);
        var firstPartitionLength = frameTag >> 5;
        if ((frameTag & 1) != 0 || firstPartitionLength <= 0 ||
            10L + firstPartitionLength > data.Length ||
            !data.Slice(3, 3).SequenceEqual([(byte)0x9D, (byte)0x01, (byte)0x2A]))
        {
            return false;
        }

        width = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(6, 2)) & 0x3FFF;
        height = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(8, 2)) & 0x3FFF;
        return width > 0 && height > 0;
    }

    private static bool TryReadVp8L(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length <= 5 || data[0] != 0x2F || (data[4] >> 5) != 0)
        {
            return false;
        }

        width = 1 + data[1] + ((data[2] & 0x3F) << 8);
        height = 1 + (data[2] >> 6) + (data[3] << 2) + ((data[4] & 0x0F) << 10);
        return true;
    }

    private static int ReadUInt24(ReadOnlySpan<byte> data) =>
        data[0] | (data[1] << 8) | (data[2] << 16);

    private static bool HasFlag(byte flags, byte flag) => (flags & flag) != 0;
}
