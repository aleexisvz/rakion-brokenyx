using System.Text;

namespace Rakion.Core.Cryptography;

/// <summary>
/// IPC cryptographic and checksum utilities for Broker - WorldServer communication.
/// Evidence: Observed in BrokenServer.Network.Servers (BCRC, IPCenCode, IPCdeCode).
/// </summary>
public static class IpcCrypto
{
    /// <summary>
    /// Computes the single-byte XOR checksum (BCRC) over the specified buffer.
    /// Evidence: Exact IL decompilation of BrokenServer.Network.Servers.BCRC.
    /// </summary>
    public static byte CalculateChecksum(ReadOnlySpan<byte> data)
    {
        byte crc = 0;
        for (int i = 0; i < data.Length; i++)
        {
            crc ^= data[i];
        }
        return crc;
    }

    /// <summary>
    /// Applies XOR transformation on the data buffer using the specified ASCII code/key.
    /// In XOR encryption, Encode and Decode are identical operations.
    /// </summary>
    public static void Transform(Span<byte> data, string key)
    {
        if (string.IsNullOrEmpty(key) || data.IsEmpty)
        {
            return;
        }

        byte[] keyBytes = Encoding.Latin1.GetBytes(key);
        for (int i = 0; i < data.Length; i++)
        {
            data[i] ^= keyBytes[i % keyBytes.Length];
        }
    }
}
