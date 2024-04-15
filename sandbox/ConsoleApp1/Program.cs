#pragma warning disable CS8500
#pragma warning disable CS8981


using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using static KcpTransport.LowLevel.KcpMethods;
using ikcpcb = KcpTransport.LowLevel.IKCPCB;
using ConsoleApp1;


//var server = QuicSandbox.QuicHelloServerAsync();

//var client = QuicSandbox.QuicHelloClientAsync();

//await Task.WhenAny(server, client);

//await client;

var server = Task.Run(() =>
{
    KcpSandbox.KcpHelloServer();
});


Thread.Sleep(100);
var client1 = Task.Run(() =>
{
    KcpSandbox.KcpHelloClient();
});
//var client2 = Task.Run(() =>
//{
//    UdpHelloClient();
//});
await await Task.WhenAny(client1, server);


// unsafe
{
    //var memory = new MemorySocketOperation();
    //using var stream = new KcpStream(0, memory);

    //var input = Enumerable.Range(0, 100).Select(x => (byte)x).ToArray();
    //stream.Write(input, 0, 100);
    //stream.Flush();

    //var buffer = memory.SendData[0].ToArray();
    //stream.Read(buffer, 0, buffer.Length);




    //var user = new object();
    //var millisec = 100u;

    //// Initialize the kcp object, conv is an integer that represents the session number, 
    //// same as the conv of tcp, both communication sides shall ensure the same conv, 
    //// so that mutual data packets can be recognized, user is a pointer which will be 
    //// passed to the callback function.
    //var kcp = ikcp_create(0, null!);
    //kcp->output = udp_output;

    //// Call ikcp_update at a certain frequency to update the kcp state, and pass in 
    //// the current clock (in milliseconds). If the call is executed every 10ms, or 
    //// ikcp_check is used to determine time of the next call for update, no need to 
    //// call every time;
    //ikcp_update(kcp, millisec);



    //// Need to call when a lower layer data packet (such as UDP packet)is received:
    //// ikcp_input(kcp, received_udp_packet, received_udp_size);


    //var buffer = new byte[100];
    //fixed (byte* ptr = buffer)
    //{

    //    var len = ikcp_send(kcp, ptr, buffer.Length);

    //}



    //ikcp_flush(kcp);


    //// KCP lower layer protocol output function, which will be called by KCP when it 
    //// needs to send data, buf/len represents the buffer and data length. 
    //// user refers to the incoming value at the time the kcp object is created to 
    //// distinguish between multiple KCP objects
    //static int udp_output(byte* buf, int len, ikcpcb* kcp, object user)
    //{
    //    var span = new Span<byte>(buf, len);

    //    return 0;
    //}
}
