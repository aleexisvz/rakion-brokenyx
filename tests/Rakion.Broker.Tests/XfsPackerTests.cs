using System.IO.Compression;
using System.Text;
using Rakion.Core.Utilities;

namespace Rakion.Broker.Tests;

[TestClass]
public sealed class XfsPackerTests
{
    [TestMethod]
    public void CreateNyxLauncherEnc_GeneratesValidXfsArchive()
    {
        // Arrange
        const string ip = "127.0.0.1";
        const int port = 40706;
        const string fetchUrl = "http://127.0.0.1:8080/fetch/fetch.php";

        // Act
        byte[] xfsBytes = XfsPacker.CreateNyxLauncherEnc(ip, port, fetchUrl);

        // Assert
        Assert.IsNotNull(xfsBytes);
        Assert.IsGreaterThan(50, xfsBytes.Length);

        // Verify Stream 1 decompresses to INI
        int stream1CompressedLen = (int)xfsBytes[7] + 1;
        using (var ms1 = new MemoryStream(xfsBytes, 12, stream1CompressedLen))
        using (var zs1 = new ZLibStream(ms1, CompressionMode.Decompress))
        using (var reader = new StreamReader(zs1, Encoding.ASCII))
        {
            string ini = reader.ReadToEnd();
            StringAssert.Contains(ini, "Url_Fetch=http://127.0.0.1:8080/fetch/fetch.php");
            StringAssert.Contains(ini, "Ip=127.0.0.1");
            StringAssert.Contains(ini, "Port=40706");
        }

        // Verify Stream 2 decompresses to XFS2 header
        int stream2Offset = (int)xfsBytes[6];
        using (var ms2 = new MemoryStream(xfsBytes, stream2Offset, xfsBytes.Length - stream2Offset))
        using (var zs2 = new ZLibStream(ms2, CompressionMode.Decompress))
        {
            byte[] header = new byte[64];
            int read = zs2.Read(header, 0, 64);
            Assert.AreEqual(64, read);
            Assert.AreEqual("XFS2", Encoding.ASCII.GetString(header, 0, 4));
            StringAssert.Contains(Encoding.ASCII.GetString(header), "The Xenesis2 file system version 2.0.0");
        }

        // Verify Stream 3 decompresses to dual file entries
        int s2Len = xfsBytes[stream2Offset - 1];
        int s3Offset = stream2Offset + s2Len + 3; // skip 3-byte s3 length
        using (var ms3 = new MemoryStream(xfsBytes, s3Offset, xfsBytes.Length - s3Offset))
        using (var zs3 = new ZLibStream(ms3, CompressionMode.Decompress))
        {
            byte[] table = new byte[256];
            int read = zs3.Read(table, 0, 256);
            Assert.AreEqual(256, read);
            StringAssert.StartsWith(Encoding.ASCII.GetString(table, 0, 32), "NyxLauncher.ini");
            StringAssert.StartsWith(Encoding.ASCII.GetString(table, 128, 32), "__NyxLauncher.ini");
        }
    }
}
