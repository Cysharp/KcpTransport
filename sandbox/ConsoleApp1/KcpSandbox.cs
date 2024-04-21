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

        var listener = await KcpListener.ListenAsync(new KcpListenerOptions
        {
            ListenEndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), listenPort),
            EventLoopCount = 1
        });
        while (true)
        {
            var conn = await listener.AcceptConnectionAsync();
            ConsumeClient(await conn.OpenOutboundStreamAsync());
        }

        static async void ConsumeClient(KcpStream stream)
        {
            try
            {
                var buffer = new byte[1024];
                while (true)
                {
                    var len = await stream.ReadAsync(buffer);

                    var str = Encoding.UTF8.GetString(buffer, 0, len);
                    Console.WriteLine("Server Received: " + str);

                    await stream.WriteAsync(Encoding.UTF8.GetBytes(str));
                    //await stream.WriteUnreliableAsync(Encoding.UTF8.GetBytes(str));
                }
            }
            catch (KcpDisconnectedException)
            {
                Console.WriteLine("Disconnected");
            }
        }
    }

    public static async Task KcpEchoClient(int id)
    {
        while (true)
        {
            var connection = await KcpConnection.ConnectAsync("127.0.0.1", listenPort);

            var buffer = new byte[1024];
            var stream = await connection.OpenOutboundStreamAsync();
            //while (true)
            {
                // var inputText = Console.ReadLine();
                var inputText = id + ":" + Random.Shared.Next().ToString();

                await stream.WriteAsync(Encoding.UTF8.GetBytes(inputText!));

                var len = await stream.ReadAsync(buffer);

                var str = Encoding.UTF8.GetString(buffer, 0, len);

                if (inputText == str)
                {
                    Console.WriteLine($"Client{id} Received: " + str);
                }
                else
                {
                    Console.WriteLine("NG");
                    throw new Exception("Invalid Data Received");
                }

                //connection.Disconnect();
                // await Task.Delay(1000);
                connection.Dispose();

                Console.Read();
            }
        }
    }

    public static async Task KcpEchoClientUnreliable(int id)
    {
        var connection = await KcpConnection.ConnectAsync("127.0.0.1", listenPort);

        var buffer = new byte[1024];
        var stream = await connection.OpenOutboundStreamAsync();
        while (true)
        {
            // var inputText = Console.ReadLine();
            var inputText = Random.Shared.Next().ToString();
            await stream.WriteUnreliableAsync(Encoding.UTF8.GetBytes(inputText!));

            var len = await stream.ReadAsync(buffer);

            var str = Encoding.UTF8.GetString(buffer, 0, len);
            Console.WriteLine($"Client{id} Received: " + str);
        }
    }
}
