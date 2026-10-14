namespace Rakion.Core.Protocol;

/// <summary>
/// Exception thrown when a packet buffer violates bounds or contains invalid format data.
/// </summary>
public sealed class PacketParseException : Exception
{
    public PacketParseException(string message) : base(message)
    {
    }

    public PacketParseException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
