namespace KcpTransport;

// KCP first 4 byte is conversationId, If 0~99(reserved, don't issue this range) then not KCP packet.
internal enum PacketType : uint
{
    Handshake = 33,
    Unreliable = 71, // Unliable is 8byte header(71(4byte) + conversationId(4byte))

    // Reliable = 100 ~ uint.MaxValue
}
