using System;
using System.Collections.Generic;
using System.Globalization;

using JellyfinPowertoys.WatchHistoryJanitor.Configuration;

using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Tasks;

namespace JellyfinPowertoys.WatchHistoryJanitor;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    private readonly ITaskManager _taskManager;

    public Plugin(ITaskManager taskManager, IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) : base(applicationPaths, xmlSerializer)
    {
        _taskManager = taskManager;
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override Guid Id => new("5513dce4-a436-495c-8925-30845bf2c1dd");
    public override string Name => "Watch History Janitor";

    public override void SaveConfiguration()
    {
        base.SaveConfiguration();
        _taskManager.QueueIfNotRunning<ScheduledTask>();
    }

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new()
        {
            Name = "Watch History Janitor",
            EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
        };
    }
}
