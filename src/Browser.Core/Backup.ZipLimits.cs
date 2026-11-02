using System.Buffers.Binary;

namespace Browser.Core;

public static partial class Backup
{
    // ZipArchive charge son catalogue entier avant d'exposer Entries.Count. Le borner avant cette allocation.
    // Les archives multi-disques et ZIP64 sont inutiles avec nos plafonds (< 4 Go et < 65 535 entrées).
    private static bool BoundedZipDirectory(byte[] data)
    {
        var end = -1;
        for (var index = data.Length - 22; index >= Math.Max(0, data.Length - 65557); index--)
            if (UInt32(data, index) == 0x06054b50 && index + 22 + UInt16(data, index + 20) == data.Length)
            { end = index; break; }
        if (end < 0 || UInt16(data, end + 4) != 0 || UInt16(data, end + 6) != 0) return false;
        var count = UInt16(data, end + 10);
        if (count > MaxEntries || count != UInt16(data, end + 8)) return false;
        var size = UInt32(data, end + 12);
        var offset = UInt32(data, end + 16);
        if ((long)offset + size != end) return false;
        var cursor = (long)offset;
        var actual = 0;
        while (cursor < end)
        {
            if (++actual > MaxEntries || cursor + 46 > end || UInt32(data, (int)cursor) != 0x02014b50) return false;
            cursor += 46L + UInt16(data, (int)cursor + 28) + UInt16(data, (int)cursor + 30) + UInt16(data, (int)cursor + 32);
        }
        return cursor == end && actual == count;
    }

    private static ushort UInt16(byte[] data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
    private static uint UInt32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
}
