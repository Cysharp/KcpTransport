using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleApp1
{
    internal class UdpSandbox
    {
        // check use zero-allocation methods in .NET 8
        //public int SendTo(ReadOnlySpan<byte> buffer, SocketFlags socketFlags, SocketAddress socketAddress);
        //public int ReceiveFrom(Span<byte> buffer, SocketFlags socketFlags, SocketAddress receivedAddress);
        //public ValueTask<int> SendToAsync(ReadOnlyMemory<byte> buffer, SocketFlags socketFlags, SocketAddress socketAddress, CancellationToken cancellationToken = default);
        //public ValueTask<int> ReceiveFromAsync(Memory<byte> buffer, SocketFlags socketFlags, SocketAddress receivedAddress, CancellationToken cancellationToken = default);

        public static void UdpHelloServer()
        {
            const int listenPort = 11000;
            const int bufferSize = 1024;

            using Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Bind(new IPEndPoint(IPAddress.Any, listenPort));

            Console.WriteLine("UDP server started. Waiting for a message...");

            EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);

            Socket newbindSocket = null!;

            while (true)
            {
                byte[] data = new byte[bufferSize];
                int receivedBytes = socket.ReceiveFrom(data, ref remoteEP);

                string message = Encoding.ASCII.GetString(data, 0, receivedBytes);

                if (newbindSocket == null)
                {
                    newbindSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    newbindSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    newbindSocket.Bind(new IPEndPoint(IPAddress.Any, listenPort));
                    newbindSocket.Connect(remoteEP);
                }

                Console.WriteLine($"Received message from {remoteEP}: {message}");

                string response = $"Server received your message: {message}";
                byte[] responseData = Encoding.ASCII.GetBytes(response);

                newbindSocket.Send(responseData);
            }
        }

        // use SocketAddress overload
        public static void UdpHelloServer2()
        {
            const int listenPort = 11000;
            const int bufferSize = 1024;

            using Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Any, listenPort));

            Console.WriteLine("UDP server started. Waiting for a message...");

            var remoteAddress = new SocketAddress(AddressFamily.InterNetwork);

            while (true)
            {
                byte[] data = new byte[bufferSize];
                int receivedBytes = socket.ReceiveFrom(data, SocketFlags.None, remoteAddress);

                string message = Encoding.ASCII.GetString(data, 0, receivedBytes);

                Console.WriteLine($"Received message from {remoteAddress}: {message}");

                string response = $"Server received your message: {message}";
                byte[] responseData = Encoding.ASCII.GetBytes(response);
                socket.SendTo(responseData, SocketFlags.None, remoteAddress);
            }
        }


        public static void UdpHelloServer3()
        {
            const int listenPort = 11000;
            const int bufferSize = 1024;

            using Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Bind(new IPEndPoint(IPAddress.Any, listenPort));

            Console.WriteLine("UDP server started. Waiting for a message...");

            var remoteAddress = new SocketAddress(AddressFamily.InterNetwork);

            Socket? clientSocket = null;

            while (true)
            {
                byte[] data = new byte[bufferSize];
                int receivedBytes = socket.ReceiveFrom(data, SocketFlags.None, remoteAddress);

                if (clientSocket == null)
                {
                    clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    clientSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    clientSocket.Bind(new IPEndPoint(IPAddress.Any, listenPort));

                    IPEndPoint endpoint = new IPEndPoint(0, 0);
                    IPEndPoint clonedIPEndPoint = (IPEndPoint)endpoint.Create(remoteAddress);
                    clientSocket.Connect(clonedIPEndPoint);
                }

                string message = Encoding.ASCII.GetString(data, 0, receivedBytes);

                Console.WriteLine($"Received message from {remoteAddress}: {message}");

                string response = $"Server received your message: {message}";
                byte[] responseData = Encoding.ASCII.GetBytes(response);
                //clientSocket.Send(responseData, SocketFlags.None);

                clientSocket.SendTo(responseData, SocketFlags.None, remoteAddress);
            }
        }










        public static void UdpHelloClient()
        {
            const string serverIP = "127.0.0.1";
            const int serverPort = 11000;
            const int bufferSize = 1024;

            using Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            while (true)
            {
                Console.Write("Enter a message to send to the server: ");
                string? message = Console.ReadLine();

                byte[] data = Encoding.ASCII.GetBytes(message!);
                socket.SendTo(data, new IPEndPoint(IPAddress.Parse(serverIP), serverPort));

                Console.WriteLine("Message sent to the server.");

                byte[] receivedData = new byte[bufferSize];
                EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
                int receivedBytes = socket.ReceiveFrom(receivedData, ref remoteEP);
                string response = Encoding.ASCII.GetString(receivedData, 0, receivedBytes);

                Console.WriteLine($"Received response from {remoteEP}: {response}");
            }
        }

        // use connect
        public static void UdpHelloClient2()
        {
            const string serverIP = "127.0.0.1";
            const int serverPort = 11000;
            const int bufferSize = 1024;

            using Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(new IPEndPoint(IPAddress.Parse(serverIP), serverPort));

            while (true)
            {
                Console.Write("Enter a message to send to the server: ");
                string? message = Console.ReadLine();

                byte[] data = Encoding.ASCII.GetBytes(message!);
                socket.Send(data);

                Console.WriteLine("Message sent to the server.");

                byte[] receivedData = new byte[bufferSize];
                EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
                int receivedBytes = socket.Receive(receivedData);
                string response = Encoding.ASCII.GetString(receivedData, 0, receivedBytes);

                Console.WriteLine($"Received response from {remoteEP}: {response}");
            }
        }

        //public static void UdpHelloServer3()
        //{
        //    using Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        //    socket.Bind(new IPEndPoint(IPAddress.Any, 12345));

        //    var remoteAddress = new SocketAddress(AddressFamily.InterNetwork);

        //    ReadOnlyMemory<byte> message1 = Encoding.ASCII.GetBytes("hello");
        //    ReadOnlyMemory<byte> message2 = Encoding.ASCII.GetBytes("world");

        //    Span<int> lengths = stackalloc int[2];
        //    // var len = socket.SendTo([message1, message2], lengths, SocketFlags.None, remoteAddress);
        //}
    }
}

namespace System.Net.Sockets
{
    // for proposal sendmmsg, recvmmsg support: https://github.com/dotnet/runtime/issues/101036
    public class Socket2
    {
        public int Send(ReadOnlySpan<ReadOnlyMemory<byte>> buffers, Span<int> lengths, SocketFlags socketFlags) => throw new NotImplementedException();
        public int Receive(ReadOnlySpan<Memory<byte>> buffers, Span<int> lengths, SocketFlags socketFlags) => throw new NotImplementedException();
        public ValueTask<SocketSendMultiResult> SendAsync(ReadOnlyMemory<byte> buffers, SocketFlags socketFlags, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public ValueTask<SocketReceiveMultiResult> ReceiveAsync(ReadOnlyMemory<Memory<byte>> buffers, SocketFlags socketFlags, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public int SendTo(ReadOnlySpan<ReadOnlyMemory<byte>> buffers, Span<int> lengths, SocketFlags socketFlags, SocketAddress socketAddress) => throw new NotImplementedException();
        public int ReceiveFrom(ReadOnlySpan<Memory<byte>> buffers, Span<int> lengths, SocketFlags socketFlags, SocketAddress socketAddress) => throw new NotImplementedException();
        public ValueTask<SocketSendMultiResult> SendToAsync(ReadOnlyMemory<byte> buffers, SocketFlags socketFlags, SocketAddress socketAddress, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public ValueTask<SocketReceiveMultiResult> ReceiveFromAsync(ReadOnlyMemory<Memory<byte>> buffers, SocketFlags socketFlags, SocketAddress receivedAddress, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    public struct SocketSendMultiResult
    {
        public int Length;
        public Memory<int> SentBytes;
    }

    public struct SocketReceiveMultiResult
    {
        public int Length;
        public Memory<int> ReceivedBytes;
    }
}
