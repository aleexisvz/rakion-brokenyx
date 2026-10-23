using System.Buffers.Binary;
using System.IO.Compression;
using Rakion.Database.Import;

namespace Rakion.Database.Tests.Import;

[TestClass]
public sealed class ClientLevelTableParserTests
{
    [TestMethod]
    public void Parse_FindsTheTableInsideACompressedStream()
    {
        var table = new byte[16 + (ClientLevelTableParser.ClassCount * ClientLevelTableParser.LevelsPerClass * 4)];
        for (int c = 0; c < ClientLevelTableParser.ClassCount; c++)
        {
            for (int level = 1; level <= ClientLevelTableParser.LevelsPerClass; level++)
            {
                int offset = 16 + (((c * ClientLevelTableParser.LevelsPerClass) + level - 1) * 4);
                BinaryPrimitives.WriteInt32LittleEndian(table.AsSpan(offset), level == 1 ? 47 : level * 100);
            }
        }

        using var archive = new MemoryStream();
        archive.Write([0x78, 0x00, 1, 2, 3]); // noise, including a byte that looks like a zlib header
        using (var zlib = new ZLibStream(archive, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(table);
        }

        var rows = ClientLevelTableParser.Parse(archive.ToArray());

        Assert.IsNotNull(rows);
        Assert.HasCount(ClientLevelTableParser.ClassCount * 100, rows);
        Assert.AreEqual(new Rakion.Database.Entities.ClassLevelEntity(0, 0, 0), rows[0]);
        Assert.AreEqual(47, rows[1].Exp);
        Assert.AreEqual(new Rakion.Database.Entities.ClassLevelEntity(4, 99, 9900), rows[^1]);
    }

    [TestMethod]
    public void Parse_ReturnsNullWithoutATable()
    {
        Assert.IsNull(ClientLevelTableParser.Parse([0x78, 0x9C, 0, 0, 1, 2, 3]));
    }
}
