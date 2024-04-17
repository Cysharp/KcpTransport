using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Runtime.Versioning;

namespace ConsoleApp1;


// https://learn.microsoft.com/ja-jp/dotnet/fundamentals/networking/quic/quic-overview
internal class QuicSandbox
{
    [SupportedOSPlatform("windows")]
    public static async Task QuicHelloServerAsync()
    {
        // First, check if QUIC is supported.
        if (!QuicListener.IsSupported)
        {
            Console.WriteLine("QUIC is not supported, check for presence of libmsquic and support of TLS 1.3.");
            return;
        }

        // Share configuration for each incoming connection.
        // This represents the minimal configuration necessary.
        var serverConnectionOptions = new QuicServerConnectionOptions
        {
            // Used to abort stream if it's not properly closed by the user.
            // See https://www.rfc-editor.org/rfc/rfc9000#section-20.2
            DefaultStreamErrorCode = 0x0A, // Protocol-dependent error code.

            // Used to close the connection if it's not done by the user.
            // See https://www.rfc-editor.org/rfc/rfc9000#section-20.2
            DefaultCloseErrorCode = 0x0B, // Protocol-dependent error code.

            // Same options as for server side SslStream.
            ServerAuthenticationOptions = new SslServerAuthenticationOptions
            {
                // List of supported application protocols, must be the same or subset of QuicListenerOptions.ApplicationProtocols.
                ApplicationProtocols = new List<SslApplicationProtocol>() { new("protocol-name") },
                // Server certificate, it can also be provided via ServerCertificateContext or ServerCertificateSelectionCallback.
                // ServerCertificate = serverCertificate
            }
        };

        // Initialize, configure the listener and start listening.
        await using var listener = await QuicListener.ListenAsync(new QuicListenerOptions
        {
            // Listening endpoint, port 0 means any port.
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            // List of all supported application protocols by this listener.
            ApplicationProtocols = new List<SslApplicationProtocol>() { new("protocol-name") },
            // Callback to provide options for the incoming connections, it gets called once per each connection.
            ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(serverConnectionOptions)
        });

        // Accept and process the connections.
        // while (isRunning)
        while (true)
        {
            // Accept will propagate any exceptions that occurred during the connection establishment,
            // including exceptions thrown from ConnectionOptionsCallback, caused by invalid QuicServerConnectionOptions or TLS handshake failures.
            var connection = await listener.AcceptConnectionAsync();

            // Process the connection...
        }

        // When finished, dispose the listener.
        // await listener.DisposeAsync();

    }

    [SupportedOSPlatform("windows")]
    public static async Task QuicHelloClientAsync()
    {
        // First, check if QUIC is supported.
        if (!QuicConnection.IsSupported)
        {
            Console.WriteLine("QUIC is not supported, check for presence of libmsquic and support of TLS 1.3.");
            return;
        }

        // This represents the minimal configuration necessary to open a connection.
        var clientConnectionOptions = new QuicClientConnectionOptions
        {
            // End point of the server to connect to.
            RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 0),

            // Used to abort stream if it's not properly closed by the user.
            // See https://www.rfc-editor.org/rfc/rfc9000#section-20.2
            DefaultStreamErrorCode = 0x0A, // Protocol-dependent error code.

            // Used to close the connection if it's not done by the user.
            // See https://www.rfc-editor.org/rfc/rfc9000#section-20.2
            DefaultCloseErrorCode = 0x0B, // Protocol-dependent error code.

            // Optionally set limits for inbound streams.
            MaxInboundUnidirectionalStreams = 10,
            MaxInboundBidirectionalStreams = 100,

            // Same options as for client side SslStream.
            ClientAuthenticationOptions = new SslClientAuthenticationOptions
            {
                // List of supported application protocols.
                ApplicationProtocols = new List<SslApplicationProtocol>() { new("protocol-name") }
            }
        };

        // new QuicConnection

        // Initialize, configure and connect to the server.
        await using var connection = await QuicConnection.ConnectAsync(clientConnectionOptions);

        Console.WriteLine($"Connected {connection.LocalEndPoint} --> {connection.RemoteEndPoint}");

        // Open a bidirectional (can both read and write) outbound stream.
        var outgoingStream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional);

        // Work with the outgoing stream ...

        // To accept any stream on a client connection, at least one of MaxInboundBidirectionalStreams or MaxInboundUnidirectionalStreams of QuicConnectionOptions must be set.
        while (true)
        {
            // Accept an inbound stream.
            var incomingStream = await connection.AcceptInboundStreamAsync();


            

            // Work with the incoming stream ...
        }

        // Close the connection with the custom code.
        // await connection.CloseAsync(0x0C);

        // Dispose the connection.
        // await connection.DisposeAsync();
    }

}
