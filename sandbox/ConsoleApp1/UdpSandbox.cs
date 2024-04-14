using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleApp1;

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
        socket.Bind(new IPEndPoint(IPAddress.Any, listenPort));

        Console.WriteLine("UDP server started. Waiting for a message...");

        EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);

        while (true)
        {
            byte[] data = new byte[bufferSize];
            int receivedBytes = socket.ReceiveFrom(data, ref remoteEP);

            string message = Encoding.ASCII.GetString(data, 0, receivedBytes);

            Console.WriteLine($"Received message from {remoteEP}: {message}");

            string response = $"Server received your message: {message}";
            byte[] responseData = Encoding.ASCII.GetBytes(response);
            socket.SendTo(responseData, remoteEP);
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
}
