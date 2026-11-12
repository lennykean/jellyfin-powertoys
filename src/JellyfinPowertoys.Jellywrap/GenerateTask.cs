using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Jellyfin.Database.Implementations.Entities;

using JellyfinPowertoys.Jellywrap.Configuration;
using JellyfinPowertoys.Jellywrap.Library;

using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;

using Microsoft.Extensions.Logging;

namespace JellyfinPowertoys.Jellywrap;

public class GenerateTask(
    IUserManager userManager,
    WrapProducer producer,
    WrapPublisher publisher,
    JellywrapLibrary library,
    AccessGuard accessGuard,
    ILogger<GenerateTask> logger) : IScheduledTask
{
    public string Name => "Generate Jellywrap";

    public string Key => typeof(GenerateTask).FullName!;

    public string Description => "Generate each user's Jellywrap video and the server Jellywrap video for the year";

    public string Category => "Jellyfin Powertoys";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var configuration = Plugin.Instance!.Configuration;
        var today = DateTime.Today;
        if (DueYear(today, configuration) is not { } year)
        {
            logger.LogInformation("Jellywrap {Year} is not due", today.Year);
            return;
        }

        var forced = configuration.ForceYear is not null;
        var users = userManager.GetUsers().ToList();
        var root = await library.EnsureLibraryAsync();
        library.DeleteFoldersOfMissingUsers(root, [.. users.Select(u => JellywrapTags.GetUserTag(u.Id))]);
        await accessGuard.ApplyAllAsync(Plugin.Instance.Configuration.IndividualVisibleToAll);
        var run = await producer.StartRunAsync(year, configuration, cancellationToken);

        var withoutVideo = configuration.UserSettings.Where(settings => !settings.Generate).Select(settings => settings.UserId).ToHashSet();
        var videoUsers = users.Where(u => !withoutVideo.Contains(u.Id)).Select(u => (User?)u).ToList();
        if (configuration.GenerateServerVideo)
        {
            videoUsers.Add(null);
        }

        var published = new List<Video>();
        Video? publishedServerVideo = null;
        var failed = false;
        for (var i = 0; i < videoUsers.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var user = videoUsers[i];
            var target = user?.Username ?? "the server";
            try
            {
                var video = forced
                    ? null
                    : library.FindVideo(root, user is null ? JellywrapTags.ServerTag : JellywrapTags.GetUserTag(user.Id), year);
                if (video is not null && File.GetLastWriteTime(video.Path) < GenerationDate(year, configuration))
                {
                    video = null;
                }

                if (video is not null)
                {
                    logger.LogInformation("Jellywrap {Year} for {Target} is already published", year, target);
                }
                else if (await producer.ProduceAsync(run, user, cancellationToken) is not { } wrap)
                {
                    logger.LogInformation("No Jellywrap {Year} for {Target}: no plays", year, target);
                }
                else if (user is null)
                {
                    video = await publisher.PublishServerWrapAsync(root, year, wrap, cancellationToken);
                }
                else
                {
                    video = await publisher.PublishUserWrapAsync(root, year, wrap, user, cancellationToken);
                }

                if (video is not null)
                {
                    published.Add(video);
                }

                if (user is null)
                {
                    publishedServerVideo = video;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Jellywrap {Year} failed for {Target}", year, target);
                failed = true;
            }

            progress.Report(100d * (i + 1) / videoUsers.Count);
        }

        if (published.Count > 0)
        {
            await library.AddToCollectionAsync(year, published, publishedServerVideo?.PrimaryImagePath, cancellationToken);
            await library.ValidateAsync(cancellationToken);
            await accessGuard.ApplyAllAsync(Plugin.Instance.Configuration.IndividualVisibleToAll);
        }

        var latestConfiguration = Plugin.Instance.Configuration;
        latestConfiguration.ForceYear = null;
        if (!failed && today >= GenerationDate(year, configuration))
        {
            latestConfiguration.LastGeneratedYear = Math.Max(latestConfiguration.LastGeneratedYear ?? year, year);
        }

        Plugin.Instance.SaveConfiguration();
        if (failed)
        {
            var retry = DueYear(today, latestConfiguration) == year ? "the next run retries" : "force the task to retry";
            throw new InvalidOperationException($"Jellywrap {year} failed for some videos; {retry}");
        }
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new()
        {
            Type = TaskTriggerInfoType.DailyTrigger,
            TimeOfDayTicks = 0,
        };
    }

    internal static int? DueYear(DateTime today, PluginConfiguration configuration)
    {
        if (configuration.ForceYear is { } forcedYear)
        {
            return forcedYear;
        }

        var generationDate = GenerationDate(today.Year, configuration);
        var year = today >= generationDate ? today.Year : today.Year - 1;
        var neverGenerated = today < generationDate && configuration.LastGeneratedYear is null;
        return configuration.LastGeneratedYear >= year || neverGenerated ? null : year;
    }

    private static DateTime GenerationDate(int year, PluginConfiguration configuration)
    {
        return new DateTime(
            year,
            configuration.GenerationMonth,
            Math.Min(configuration.GenerationDay, DateTime.DaysInMonth(year, configuration.GenerationMonth)));
    }
}
