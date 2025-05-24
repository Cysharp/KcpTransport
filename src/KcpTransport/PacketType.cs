namespace KcpTransport
{
    // KCP first 4 byte is conversationId, If 0~99(reserved, don't issue this range) then not KCP packet.
    internal enum PacketType : uint
    {
        HandshakeInitialRequest = 31,  // 4byte(type)
        HandshakeInitialResponse = 32, // 20byte(type + conversationId + cookie + timestamp)
        HandshakeOkRequest = 33,       // 20byte(type + conversationId + cookie + timestamp)
        HandshakeOkResponse = 34,      // 4byte(type)
        HandshakeNgResponse = 35,      // 4byte(type)

        Ping = 50, // for KeepAlive
        Pong = 51, // for KeepAlive

        Disconnect = 60,

        Unreliable = 71, // Unliable is 8byte header(type(4byte) + conversationId(4byte))

        // Reliable = 100 ~ uint.MaxValue
    }
}