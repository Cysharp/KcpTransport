using KcpTransport;
using System.Text;

var server = RunEchoServer();
var client = RunEchoClient();

await await Task.WhenAny(server, client);

static async Task RunEchoServer()
{
    // Create KCP Server
    var listener = await KcpListener.ListenAsync("127.0.0.1", 11000);

    // Accept client connection loop
    while (true)
    {
        var connection = await listener.AcceptConnectionAsync();
        ConsumeClient(connection);
    }

    static async void ConsumeClient(KcpConnection connection)
    {
        using (connection)
        using (var stream = await connection.OpenOutboundStreamAsync())
        {
            try
            {
                var buffer = new byte[1024];
                while (true)
                {
                    // Wait incoming data
                    var len = await stream.ReadAsync(buffer);

                    var str = Encoding.UTF8.GetString(buffer, 0, len);
                    Console.WriteLine("Server Request  Received: " + str);

                    // Send to Client(KCP, Reliable)
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(str));

                    // Send to Client(Unreliable)
                    //await stream.WriteUnreliableAsync(Encoding.UTF8.GetBytes(str));
                }
            }
            catch (KcpDisconnectedException)
            {
                // when client has been disconnected, KcpDisconnectedException was thrown
                Console.WriteLine($"Disconnected, Id:{connection.ConnectionId}");
            }
        }
    }
}

static async Task RunEchoClient()
{
    // Create KCP Client
    var connection = await KcpConnection.ConnectAsync("127.0.0.1", 11000);

    using (var stream = await connection.OpenOutboundStreamAsync())
    {
        var buffer = new byte[1024];
        while (true)
        {
            Console.Write("Input Text:");
            var inputText = Console.ReadLine();

            // Send to Server(KCP, Reliable), or use WriteUnreliableAsync
            await stream.WriteAsync(Encoding.UTF8.GetBytes(inputText!));

            // Wait server response
            var len = await stream.ReadAsync(buffer);

            var str = Encoding.UTF8.GetString(buffer, 0, len);

            Console.WriteLine($"Client Response Received: " + str);
        }
    }
}
