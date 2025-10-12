using System;
using System.Collections.Generic;
using System.Globalization;

using JellyfinPowertoys.CastCurator.Configuration;

using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Tasks;

namespace JellyfinPowertoys.CastCurator;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    private readonly ITaskManager _taskManager;

    public Plugin(ITaskManager taskManager, IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) : base(applicationPaths, xmlSerializer)
    {
        _taskManager = taskManager;
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override Guid Id => new("c797039e-7640-45bb-a83f-7976220285dc");
    public override string Name => "Cast Curator";

    public override void SaveConfiguration()
    {
        base.SaveConfiguration();
        _taskManager.QueueIfNotRunning<ScheduledTask>();
    }

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new()
        {
            Name = "Cast Curator",
            EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
        };
    }
}
