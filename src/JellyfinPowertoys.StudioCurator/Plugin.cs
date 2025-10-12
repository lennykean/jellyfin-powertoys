using System;
using System.Collections.Generic;
using System.Globalization;

using JellyfinPowertoys.StudioCurator.Configuration;

using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Tasks;

namespace JellyfinPowertoys.StudioCurator;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    private readonly ITaskManager _taskManager;

    public Plugin(ITaskManager taskManager, IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) : base(applicationPaths, xmlSerializer)
    {
        _taskManager = taskManager;
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override Guid Id => new("ac5bb0cf-9de9-4c42-b72f-c59d1edbb6ae");
    public override string Name => "Studio Curator";

    public override void SaveConfiguration()
    {
        base.SaveConfiguration();
        _taskManager.QueueIfNotRunning<ScheduledTask>();
    }

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new()
        {
            Name = "Studio Curator",
            EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
        };
    }
}
