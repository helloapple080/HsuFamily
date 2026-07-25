using System.Buffers.Binary;

namespace FamilyPortal.Application.Services;

/// <summary>驗證 JPEG marker 邊界、尺寸與非空掃描資料。</summary>
internal static class JpegImageValidator
{
    internal static bool HasSignature(ReadOnlySpan<byte> content) =>
        content.Length >= 2 && content[0] == 0xFF && content[1] == 0xD8;

    internal static bool TryInspect(ReadOnlySpan<byte> content, out ImageInfo image)
    {
        image = default;
        var offset = 2;
        var inScan = false;
        var sawScan = false;
        var sawScanData = false;
        var width = 0;
        var height = 0;

        while (offset < content.Length)
        {
            if (inScan && content[offset] != 0xFF)
            {
                sawScanData = true;
                offset++;
                continue;
            }

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
            if (inScan && marker == 0x00)
            {
                sawScanData = true;
                continue;
            }

            if (inScan && marker is >= 0xD0 and <= 0xD7)
            {
                continue;
            }

            inScan = false;
            if (marker == 0xD9)
            {
                if (offset != content.Length || width <= 0 || height <= 0 || !sawScan || !sawScanData)
                {
                    return false;
                }

                image = new ImageInfo("image/jpeg", width, height);
                return true;
            }

            if (marker == 0xD8 || marker == 0x00 || marker == 0x01 || marker is >= 0xD0 and <= 0xD7)
            {
                return false;
            }

            if (offset > content.Length - 2)
            {
                return false;
            }

            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(content.Slice(offset, 2));
            if (segmentLength < 2 || (long)offset + segmentLength > content.Length)
            {
                return false;
            }

            if (IsStartOfFrame(marker))
            {
                if (!TryReadFrame(content.Slice(offset, segmentLength), out width, out height))
                {
                    return false;
                }
            }
            else if (marker == 0xDA)
            {
                var components = content[offset + 2];
                if (width <= 0 || height <= 0 || components == 0 || segmentLength != 6 + (2 * components))
                {
                    return false;
                }

                sawScan = true;
                inScan = true;
            }

            offset += segmentLength;
        }

        return false;
    }

    private static bool TryReadFrame(ReadOnlySpan<byte> segment, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (segment.Length < 11)
        {
            return false;
        }

        var components = segment[7];
        if (components == 0 || segment.Length != 8 + (3 * components))
        {
            return false;
        }

        height = BinaryPrimitives.ReadUInt16BigEndian(segment.Slice(3, 2));
        width = BinaryPrimitives.ReadUInt16BigEndian(segment.Slice(5, 2));
        return width > 0 && height > 0;
    }

    private static bool IsStartOfFrame(byte marker) => marker is
        0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or
        0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF;
}
