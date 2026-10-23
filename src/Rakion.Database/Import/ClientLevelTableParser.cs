using System.Buffers.Binary;
using System.IO.Compression;
using Rakion.Database.Entities;

namespace Rakion.Database.Import;

/// <summary>
/// Reads the experience table from the client's <c>DataSetup.xfs</c>, so the server levels characters up at the same
/// totals the client shows (the legacy dump's ClassLevelInfo belongs to another game version: 70 / 188 against the
/// client's 47 / 133). The file stays on the operator's machine.
/// Evidence (Derived from the v258 DataSetup.xfs): one zlib stream of the archive holds 5 class rows of 99 u32, each
/// the total needed to leave levels 1..99, the last one 9999999. The row is found by shape, not by entry name.
/// </summary>
public static class ClientLevelTableParser
{
    public const int ClassCount = 5;
    public const int LevelsPerClass = 99;
    private const int RowSize = LevelsPerClass * 4;
    private const int MaxStreamSize = 16 * 1024 * 1024;

    public static IReadOnlyList<ClassLevelEntity>? Parse(byte[] archive)
    {
        for (int i = 0; i + 1 < archive.Length; i++)
        {
            if (archive[i] != 0x78 || archive[i + 1] is not (0x01 or 0x5E or 0x9C or 0xDA))
            {
                continue;
            }

            byte[]? stream = TryInflate(archive, i);
            if (stream is not null && FindTable(stream) is { } table)
            {
                return table;
            }
        }

        return null;
    }

    private static List<ClassLevelEntity>? FindTable(byte[] data)
    {
        for (int start = 0; start + (ClassCount * RowSize) <= data.Length; start++)
        {
            if (!IsTable(data, start))
            {
                continue;
            }

            // A misaligned read (the first values shifted by a zero byte) also looks increasing; the real start is
            // the last one of up to four consecutive matches.
            for (int shift = 0; shift < 3 && start + 1 + (ClassCount * RowSize) <= data.Length && IsTable(data, start + 1); shift++)
            {
                start++;
            }

            var rows = new List<ClassLevelEntity>(ClassCount * (LevelsPerClass + 1));
            for (byte c = 0; c < ClassCount; c++)
            {
                rows.Add(new ClassLevelEntity(c, 0, 0));
                for (int level = 1; level <= LevelsPerClass; level++)
                {
                    int exp = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(start + (c * RowSize) + ((level - 1) * 4)));
                    rows.Add(new ClassLevelEntity(c, (byte)level, exp));
                }
            }

            return rows;
        }

        return null;
    }

    private static bool IsTable(byte[] data, int start)
    {
        for (int c = 0; c < ClassCount; c++)
        {
            if (!IsRow(data, start + (c * RowSize)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsRow(byte[] data, int offset)
    {
        int previous = 0;
        for (int level = 0; level < LevelsPerClass; level++)
        {
            int value = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + (level * 4)));
            if (value <= previous || value > 100_000_000)
            {
                return false;
            }

            previous = value;
        }

        return true;
    }

    private static byte[]? TryInflate(byte[] archive, int offset)
    {
        try
        {
            using var input = new MemoryStream(archive, offset, archive.Length - offset, writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = zlib.Read(buffer)) > 0)
            {
                output.Write(buffer, 0, read);
                if (output.Length > MaxStreamSize)
                {
                    return null;
                }
            }

            return output.Length >= ClassCount * RowSize ? output.ToArray() : null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }
}
