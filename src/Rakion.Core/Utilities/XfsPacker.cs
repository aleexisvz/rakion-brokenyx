using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Rakion.Core.Utilities;

/// <summary>
/// Utility to pack and generate valid Xenesis2 (XFS2) encrypted launcher configuration files (NyxLauncherEnc.xfs).
/// </summary>
public static class XfsPacker
{
    public static byte[] CreateNyxLauncherEnc(string brokerIp, int brokerPort, string fetchUrl)
    {
        string iniContent = $"""
            [NyxLauncher]
            Url_Fetch={fetchUrl}
            Ip={brokerIp}
            Port={brokerPort}

            [Gunbound]
            Url_Fetch={fetchUrl}

            [Rakion]
            Url_Fetch={fetchUrl}

            [Wolfteam]
            Url_Fetch={fetchUrl}

            """;

        // Normalize to CRLF as required by Windows INI parsers
        iniContent = iniContent.Replace("\r\n", "\n").Replace("\n", "\r\n");
        byte[] uncompressedIni = Encoding.ASCII.GetBytes(iniContent);

        // Stream 1: compressed INI content
        byte[] stream1 = CompressZlib(uncompressedIni);

        // Stream 2: ArchiveHeader (64 bytes)
        int stream2Seek = 12 + stream1.Length;
        int stream2ZlibOffset = stream2Seek + 1;

        byte[] uncompressedHeader = new byte[64];
        Encoding.ASCII.GetBytes("XFS2").CopyTo(uncompressedHeader, 0);
        BinaryPrimitives.WriteInt32LittleEndian(uncompressedHeader.AsSpan(8, 4), 2); // 2 file entries: retail & audit
        BinaryPrimitives.WriteInt32LittleEndian(uncompressedHeader.AsSpan(16, 4), stream2Seek);
        BinaryPrimitives.WriteInt32LittleEndian(uncompressedHeader.AsSpan(20, 4), 0x00010000);
        Encoding.ASCII.GetBytes("The Xenesis2 file system version 2.0.0").CopyTo(uncompressedHeader, 24);

        byte[] stream2 = CompressZlib(uncompressedHeader);

        // Stream 3: FileTable (2 entries * 128 bytes = 256 bytes)
        byte[] uncompressedTable = new byte[256];

        // Entry 0: NyxLauncher.ini (Official retail client)
        Encoding.ASCII.GetBytes("NyxLauncher.ini").CopyTo(uncompressedTable, 0);
        BinaryPrimitives.WriteInt32LittleEndian(uncompressedTable.AsSpan(112, 4), 4);
        BinaryPrimitives.WriteInt32LittleEndian(uncompressedTable.AsSpan(116, 4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(uncompressedTable.AsSpan(120, 4), uncompressedIni.Length);
        BinaryPrimitives.WriteInt32LittleEndian(uncompressedTable.AsSpan(124, 4), stream1.Length);

        // Entry 1: __NyxLauncher.ini (Patched audit dev client)
        Encoding.ASCII.GetBytes("__NyxLauncher.ini").CopyTo(uncompressedTable, 128);
        BinaryPrimitives.WriteInt32LittleEndian(uncompressedTable.AsSpan(128 + 112, 4), 4);
        BinaryPrimitives.WriteInt32LittleEndian(uncompressedTable.AsSpan(128 + 116, 4), 2);
        BinaryPrimitives.WriteInt32LittleEndian(uncompressedTable.AsSpan(128 + 120, 4), uncompressedIni.Length);
        BinaryPrimitives.WriteInt32LittleEndian(uncompressedTable.AsSpan(128 + 124, 4), stream1.Length);

        byte[] stream3 = CompressZlib(uncompressedTable);

        // 12-byte File Header
        byte[] fileHeader = new byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(fileHeader.AsSpan(0, 4), stream2Seek);
        BinaryPrimitives.WriteInt16LittleEndian(fileHeader.AsSpan(4, 2), (short)uncompressedIni.Length);
        fileHeader[6] = (byte)stream2ZlibOffset;
        fileHeader[7] = (byte)(stream1.Length - 1);

        using var ms = new MemoryStream();
        ms.Write(fileHeader);
        ms.Write(stream1);
        ms.WriteByte((byte)stream2.Length);
        ms.Write(stream2);
        ms.WriteByte((byte)(stream3.Length & 0xFF));
        ms.WriteByte((byte)((stream3.Length >> 8) & 0xFF));
        ms.WriteByte((byte)((stream3.Length >> 16) & 0xFF));
        ms.Write(stream3);

        return ms.ToArray();
    }

    private static byte[] CompressZlib(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var zs = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            zs.Write(data);
        }
        return ms.ToArray();
    }
}
