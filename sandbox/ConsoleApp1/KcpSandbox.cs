using System.Net.Sockets;
using System.Net;
using System.Text;
using static KcpTransport.LowLevel.KcpMethods;
using KcpTransport.LowLevel;

namespace ConsoleApp1
{
    internal unsafe class KcpSandbox
    {
        public Socket socket = default!;
        public SocketAddress? address;

        public static unsafe void KcpHelloServer()
        {
            const int listenPort = 11000;
            const int bufferSize = 1024;

            using Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Any, listenPort));
            var remoteAddress = new SocketAddress(AddressFamily.InterNetwork);

            var context = new KcpSandbox { socket = socket, address = remoteAddress };

            var kcp = ikcp_create(0, context);
            kcp->output = udp_output;
            ikcp_update(kcp, 100u);

            Console.WriteLine("KCP server started. Waiting for a message...");

            while (true)
            {
                byte[] data = new byte[bufferSize];
                int receivedBytes = socket.ReceiveFrom(data, SocketFlags.None, remoteAddress);

                fixed (byte* p = data)
                {
                    // success == 0, otherwise error-code
                    var succeed = ikcp_input(kcp, p, receivedBytes) == 0;
                    receivedBytes = ikcp_recv(kcp, p, data.Length);
                }

                string message = Encoding.ASCII.GetString(data, 0, receivedBytes);

                Console.WriteLine($"Received message from {remoteAddress}: {message}");

                string response = $"Server received your message: {message}";
                byte[] responseData = Encoding.ASCII.GetBytes(response);

                fixed (byte* p = responseData)
                {
                    var sentSize = ikcp_send(kcp, p, responseData.Length);
                }

                ikcp_flush(kcp);
            }
        }

        public static void KcpHelloClient()
        {
            const string serverIP = "127.0.0.1";
            const int serverPort = 11000;
            const int bufferSize = 1024;

            using Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(new IPEndPoint(IPAddress.Parse(serverIP), serverPort));


            var context = new KcpSandbox { socket = socket, address = null! };

            var kcp = ikcp_create(0, context);
            kcp->output = udp_output;
            ikcp_update(kcp, 100u);
            while (true)
            {
                Console.Write("Enter a message to send to the server: ");
                string? message = Console.ReadLine();

                byte[] data = Encoding.ASCII.GetBytes(message!);

                fixed (byte* p = data)
                {
                    ikcp_send(kcp, p, data.Length);
                    ikcp_flush(kcp);
                }

                Console.WriteLine("Message sent to the server.");

                byte[] receivedData = new byte[bufferSize];
                EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
                int receivedBytes = socket.Receive(receivedData);

                fixed (byte* p = receivedData)
                {
                    // success == 0, otherwise error-code
                    var succeed = ikcp_input(kcp, p, receivedBytes) == 0;
                    receivedBytes = ikcp_recv(kcp, p, receivedData.Length);
                }

                string response = Encoding.ASCII.GetString(receivedData, 0, receivedBytes);

                Console.WriteLine($"Received response from {remoteEP}: {response}");
            }
        }

        static int udp_output(byte* buf, int len, IKCPCB* kcp, object user)
        {
            var ctx = (KcpSandbox)user;

            var span = new Span<byte>(buf, len);
            if (ctx.address == null)
            {
                ctx.socket.Send(span, SocketFlags.None);
            }
            else
            {
                ctx.socket.SendTo(span, SocketFlags.None, ctx.address);
            }
            return 0;
        }
    }
}
