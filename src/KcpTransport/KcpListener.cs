using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Quic;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace KcpTransport;

public sealed class KcpListener
{
    Socket socket;

    public static ValueTask<KcpListener> ListenAsync(int listenPort)
    {
        Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            socket.Bind(new IPEndPoint(IPAddress.Any, listenPort));
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        var listener = new KcpListener();
        listener.socket = socket;
        listener.StartListenLoop(new SocketAddress(AddressFamily.InterNetwork));
        return new ValueTask<KcpListener>(listener);
    }

    public async ValueTask<KcpConnection> AcceptConnectionAsync(CancellationToken cancellationToken = default)
    {
        



        throw new NotImplementedException();
    }

    async void StartListenLoop(SocketAddress remoteAddress)
    {
        while (true)
        {
        }
    }
}
