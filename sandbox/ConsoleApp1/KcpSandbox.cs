using System.Net.Sockets;
using System.Net;
using System.Text;
using static KcpTransport.LowLevel.KcpMethods;
using KcpTransport.LowLevel;
using KcpTransport;

namespace ConsoleApp1;

internal class KcpSandbox
{
    const int listenPort = 11000;

    public static async Task KcpEchoServer()
    {
        var listener = await KcpListener.ListenAsync(listenPort);
        while (true)
        {
            var conn = await listener.AcceptConnectionAsync();
            ConsumeClient(await conn.OpenOutboundStreamAsync());
        }

        static async void ConsumeClient(KcpStream stream)
        {
            var buffer = new byte[1024];
            while (true)
            {
                var len = await stream.ReadAsync(buffer);

                var str = Encoding.UTF8.GetString(buffer, 0, len);
                Console.WriteLine("Server Received: " + str);

                await stream.WriteAsync(Encoding.UTF8.GetBytes(str));
            }
        }
    }

    public static async Task KcpEchoClient()
    {
        // TODO: issue conversation id from client to server.
        var conversationId = unchecked((uint)Random.Shared.Next(0, int.MaxValue));

        var connection = new KcpConnection(conversationId, new IPEndPoint(IPAddress.Parse("127.0.0.1"), listenPort));

        var buffer = new byte[1024];
        var stream = await connection.OpenOutboundStreamAsync();
        while (true)
        {
            var inputText = Console.ReadLine();
            await stream.WriteAsync(Encoding.UTF8.GetBytes(inputText!));

            var len = await stream.ReadAsync(buffer);

            var str = Encoding.UTF8.GetString(buffer, 0, len);
            Console.WriteLine("Client Received: " + str);
        }
    }
}
