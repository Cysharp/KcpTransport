#pragma warning disable CS1998

using DFrame;
using DFrame.Controller.EventMessage;
using DFrameBenchmark.Workloads;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZLogger;

var builder = DFrameApp.CreateBuilder(7312, 7313);

builder.ConfigureController(x =>
{
    x.DisableRestApi = true;
});

builder.ConfigureWorker(x =>
{
    x.VirtualProcess = 16;
    x.WorkloadAssemblies = [typeof(UdpWorkload).Assembly];
});

builder.ConfigureServices(x =>
{
    x.AddMessagePipe();
    x.AddHostedService<ServerRunner>();
});

builder.ConfigureLogging(x =>
{
    x.ClearProviders();
    x.SetMinimumLevel(LogLevel.Information);
    x.AddZLoggerConsole();
});

builder.Run(); // WebUI:7312, WorkerListen:7313

class Empty : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

internal class ServerRunner(ILogger<ServerRunner> logger, ISubscriber<ControllerEventMessage> subscriber) : IHostedService
{
    IDisposable? eventSubscription;
    Task? serverTask = null;
    CancellationTokenSource? linkedTokenSource;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        //serverTask = TcpServer.RunEchoAsync(cancellationToken);
        //Thread.Sleep(1000);
        eventSubscription = subscriber.Subscribe(x =>
        {
            if (x.MessageType == ControllerEventMessageType.WorkflowStarted)
            {
                linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                if (x.ExecutionSummary.Workload.StartsWith("Udp"))
                {
                    logger.ZLogInformation($"Udp Server Starting.");
                    serverTask = UdpServer.RunEchoAsync(linkedTokenSource.Token);
                }
                else if (x.ExecutionSummary.Workload.StartsWith("Tcp"))
                {
                    logger.ZLogInformation($"Tcp Server Starting.");
                    serverTask = TcpServer.RunEchoAsync(linkedTokenSource.Token);
                }
                else if (x.ExecutionSummary.Workload.StartsWith("Kcp"))
                {
                    logger.ZLogInformation($"Kcp Server Starting.");
                    serverTask = KcpServer.RunEchoAsync(linkedTokenSource.Token);
                }
            }
            else if (x.MessageType == ControllerEventMessageType.WorkflowCompleted)
            {
                linkedTokenSource?.Cancel();
                try
                {
                    serverTask?.Wait();
                }
                catch
                {
                    logger.ZLogInformation($"Server Stopped.");
                }
            }
        });
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        eventSubscription?.Dispose();
        var t = serverTask;
        if (t != null)
        {
            await t;
        }
    }
}
