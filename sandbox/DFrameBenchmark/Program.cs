#pragma warning disable CS1998

using DFrame;
using DFrameBenchmark.Workloads;

//var serverTask = KcpServer.RunEchoAsync();
var serverTask = UdpServer.RunEchoAsync();
//var serverTask = UdpServer.RunEchoMultiAsync();
//var serverTask = TcpServer.RunEchoAsync();

Thread.Sleep(100);

var builder = DFrameApp.CreateBuilder(7312, 7313);

builder.ConfigureController(x =>
{
    x.DisableRestApi = true;
});

builder.ConfigureWorker(x =>
{
    x.VirtualProcess = 16;
    // x.BatchRate = 1;
});

builder.Run(); // WebUI:7312, WorkerListen:7313

await serverTask;
