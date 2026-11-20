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
    OwnerFolders ownerFolders,
    WrapCollections collections,
    AccessGuard accessGuard,
    PluginConfigurationStore configurationStore,
    ILogger<GenerateTask> logger) : IScheduledTask
{
    private const int LastMonthThatGeneratesThePreviousYear = 6;

    public string Name => "Generate Jellywrap";

    public string Key => typeof(GenerateTask).FullName!;

    public string Description => "Generate each user's Jellywrap video and the server Jellywrap video for the year";

    public string Category => "Jellyfin Powertoys";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        try
        {
            await GenerateDueYearsAsync(progress, cancellationToken);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Jellywrap generation timed out", exception);
        }
    }

    private async Task GenerateDueYearsAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (configurationStore.Current is null)
        {
            logger.LogInformation("Jellywrap is uninstalled");
            return;
        }

        await accessGuard.ApplyAllAsync();
        var today = DateTime.Today;
        var generated = false;
        while (configurationStore.Current is { } configuration && DueYear(today, configuration) is { } year)
        {
            var forced = configuration.ForceYear is not null;
            if (forced)
            {
                configuration.ForceYear = null;
                configurationStore.Save();
            }

            try
            {
                var published = await GenerateAsync(year, forced, configuration, progress, cancellationToken);
                if (!published && !forced && !generated && year < today.Year && configurationStore.Current is { LastGeneratedYear: null } firstRun)
                {
                    logger.LogInformation("Jellywrap {Year} had no videos. Generating a {PreviewYear} preview", year, today.Year);
                    await GenerateAsync(today.Year, true, firstRun, progress, cancellationToken);
                }
            }
            finally
            {
                producer.DeleteWorkFiles();
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (today >= GenerationDate(year, configuration) && configurationStore.Current is { } latestConfiguration)
            {
                latestConfiguration.LastGeneratedYear = Math.Max(latestConfiguration.LastGeneratedYear ?? year, year);
                configurationStore.Save();
            }

            generated = true;
        }

        if (!generated)
        {
            logger.LogInformation("Jellywrap {Year} is not due", today.Year);
        }
    }

    private async Task<bool> GenerateAsync(
        int year,
        bool forced,
        PluginConfiguration configuration,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        var users = userManager.GetUsers().ToList();
        var root = await library.EnsureLibraryAsync();
        ownerFolders.AdoptFoldersOnDisk(root);
        ownerFolders.DeleteFoldersOfMissingUsers(root, [.. users.Select(u => JellywrapTags.GetUserTag(u.Id))]);
        await accessGuard.ApplyAllAsync();
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
            if (configurationStore.Current is null)
            {
                logger.LogInformation("Jellywrap was uninstalled during the generation of {Year}", year);
                return false;
            }

            var user = videoUsers[i];
            var target = user?.Username ?? "the server";
            try
            {
                var tag = user is null ? JellywrapTags.ServerTag : JellywrapTags.GetUserTag(user.Id);
                var video = forced ? null : publisher.FindVideo(root, tag, year);
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
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Jellywrap {Year} failed for {Target}", year, target);
                failed = true;
            }

            progress.Report(100d * (i + 1) / videoUsers.Count);
        }

        if (published.Count > 0)
        {
            await collections.AddAsync(year, published, publishedServerVideo?.PrimaryImagePath, cancellationToken);
            await library.ValidateAsync(cancellationToken);
            await accessGuard.ApplyAllAsync();
        }

        if (failed)
        {
            throw new InvalidOperationException($"Jellywrap {year} failed for some videos; due years retry on the next run");
        }

        return published.Count > 0;
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
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

        var year = today.Year;
        while (GenerationDate(year, configuration) > today)
        {
            year--;
        }

        return configuration.LastGeneratedYear >= year ? null : year;
    }

    private static DateTime GenerationDate(int year, PluginConfiguration configuration)
    {
        var calendarYear = year + YearsUntilGenerationDate(configuration);
        return new DateTime(
            calendarYear,
            configuration.GenerationMonth,
            Math.Min(configuration.GenerationDay, DateTime.DaysInMonth(calendarYear, configuration.GenerationMonth)));
    }

    // A generation date early in the year generates the previous year, so that the video covers a whole year.
    private static int YearsUntilGenerationDate(PluginConfiguration configuration)
    {
        return configuration.GenerationMonth <= LastMonthThatGeneratesThePreviousYear ? 1 : 0;
    }
}
