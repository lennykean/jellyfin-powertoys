using System;
using System.Threading;
using System.Threading.Tasks;

using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;

namespace JellyfinPowertoys.CastCurator;

public class LibraryPostScanTask(ITaskManager taskManager) : ILibraryPostScanTask
{
    public Task Run(IProgress<double> progress, CancellationToken cancellationToken)
    {
        taskManager.QueueIfNotRunning<ScheduledTask>();
        return Task.CompletedTask;
    }
}
