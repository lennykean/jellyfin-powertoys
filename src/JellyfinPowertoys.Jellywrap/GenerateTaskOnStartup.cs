using System;
using System.Threading;
using System.Threading.Tasks;

using MediaBrowser.Controller;
using MediaBrowser.Model.Tasks;

using Microsoft.Extensions.Hosting;

namespace JellyfinPowertoys.Jellywrap;

public class GenerateTaskOnStartup(IServerApplicationHost host, ITaskManager taskManager) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!host.CoreStartupHasCompleted)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }

        taskManager.QueueIfNotRunning<GenerateTask>();
    }
}
