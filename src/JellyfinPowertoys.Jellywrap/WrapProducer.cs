using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Jellyfin.Database.Implementations.Entities;

using JellyfinPowertoys.Jellywrap.Audio;
using JellyfinPowertoys.Jellywrap.Configuration;
using JellyfinPowertoys.Jellywrap.Context;
using JellyfinPowertoys.Jellywrap.Templates;

using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

using Microsoft.Extensions.Logging;

namespace JellyfinPowertoys.Jellywrap;

public class WrapProducer(
    WrapSourceReader sourceReader,
    ClipFinder clipFinder,
    VideoEncoder videoEncoder,
    IApplicationPaths applicationPaths,
    ILibraryManager libraryManager,
    ILogger<WrapProducer> logger)
{
    private const int MinutesPerHour = 60;

    internal async Task<WrapRun> StartRunAsync(
        int year,
        PluginConfiguration configuration,
        CancellationToken cancellationToken)
    {
        DeleteWorkFiles();
        Directory.CreateDirectory(applicationPaths.GetJellywrapWorkPath());
        var source = await sourceReader.ReadAsync(
            year,
            DateTimeOffset.UtcNow,
            configuration.UserSettings,
            cancellationToken);
        var wrapYear = WrapYear.Build(source);
        var catalog = TemplateCatalog.Load(applicationPaths.GetJellywrapTemplatesPath(configuration.CustomTemplateFolder));
        foreach (var template in catalog.All.Where(t => t.Errors.Count > 0))
        {
            logger.LogWarning("The Jellywrap template {Id} is not used: {Errors}", template.Id, string.Join("; ", template.Errors));
        }

        var templateChoices = configuration.UserSettings
            .Where(settings => !string.IsNullOrWhiteSpace(settings.TemplateId))
            .ToDictionary(settings => settings.UserId, settings => settings.TemplateId!);
        return new WrapRun(
            year,
            wrapYear,
            new AudioPlanner(wrapYear, clipFinder, logger),
            catalog,
            templateChoices,
            string.IsNullOrWhiteSpace(configuration.ServerTemplateId) ? null : configuration.ServerTemplateId);
    }

    internal void DeleteWorkFiles()
    {
        var workPath = applicationPaths.GetJellywrapWorkPath();
        if (Directory.Exists(workPath))
        {
            Directory.Delete(workPath, true);
        }
    }

    internal async Task<ProducedWrap?> ProduceAsync(WrapRun run, User? user, CancellationToken cancellationToken)
    {
        var context = user is null
            ? run.WrapYear.Server
            : run.WrapYear.Users.FirstOrDefault(c => c.Me!.Id == user.Id);
        if (context is null)
        {
            return null;
        }

        var stopwatch = Stopwatch.StartNew();
        context = await WithLocalActorImageAsync(context, cancellationToken);
        var json = JsonSerializer.SerializeToNode(context, JsonSerializerOptions.Web)!.AsObject();
        var fileStem = user?.Id.ToString("N") ?? "server";
        var name = context.Me?.Name ?? context.Server.Name;
        var kind = user is null ? Template.ServerKind : Template.IndividualKind;
        var audience = context.Me is { IsKid: true } ? Template.KidsAudience : Template.GeneralAudience;
        var chosenTemplateId = user is null ? run.ServerTemplateId : run.TemplateChoices.GetValueOrDefault(user.Id);
        var chosen = run.TemplateCatalog.Find(chosenTemplateId, kind, audience);
        if (chosenTemplateId is not null && chosen is null)
        {
            logger.LogWarning(
                "The Jellywrap template {Id} chosen for {Name} is not a valid {Audience} template with a {Kind} video; using a random one",
                chosenTemplateId,
                name,
                audience,
                kind);
        }

        var failed = new List<Template>();
        while (true)
        {
            var template = chosen is not null && !failed.Contains(chosen)
                ? chosen
                : run.TemplateCatalog.Pick(kind, audience, failed);
            try
            {
                using var scene = SceneBuilder.Build(template, kind, json, cancellationToken);
                if (scene.Slides.Count == 0)
                {
                    throw new InvalidOperationException($"The template {template.Id} shows no slide.");
                }

                var timeline = scene.Slides.Select(slide => new TimelineSlide(slide.Start, slide.Duration, slide.Featured)).ToList();
                var clips = await run.AudioPlanner.PlanAsync(context, timeline, cancellationToken);
                var workPath = applicationPaths.GetJellywrapWorkPath();
                var videoPath = Path.Combine(workPath, $"{fileStem}.mp4");
                var imagePath = Path.Combine(workPath, $"{fileStem}.jpg");
                var hasSound = await videoEncoder.EncodeAsync(scene, clips, videoPath, cancellationToken);
                SceneRenderer.RenderTitleCard(scene, imagePath);
                logger.LogInformation(
                    "Rendered the Jellywrap {Year} video of {Name} in {Template} in {Seconds:F0} s with audio {Clips}",
                    run.Year,
                    name,
                    template.Name,
                    stopwatch.Elapsed.TotalSeconds,
                    hasSound ? string.Join("; ", clips.Select(c => $"{c.Title} {c.Kind.ToName()} {c.Duration:F1} s")) : "no sound");
                return new ProducedWrap(videoPath, imagePath, Overview(template, context));
            }
            catch (Exception exception) when (exception is not VideoEncodingException && (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested))
            {
                logger.LogError(exception, "The Jellywrap template {Id} failed for {Name}; picking another", template.Id, name);
                failed.Add(template);
            }
        }
    }

    private async Task<WrapContext> WithLocalActorImageAsync(WrapContext context, CancellationToken cancellationToken)
    {
        if (context.Me is not { TopActors: [var actor, ..] } me
            || libraryManager.GetItemById(actor.Id) is not { } person
            || person.GetImageInfo(ImageType.Primary, 0) is not { IsLocalFile: false } image)
        {
            return context;
        }

        try
        {
            var local = await libraryManager.ConvertImageToLocal(person, image, 0, false);
            var localActor = actor with { Image = new ImageRef(local.Path, local.Width, local.Height) };
            return context with { Me = me with { TopActors = [localActor, .. me.TopActors.Skip(1)] } };
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning(exception, "The image of {Name} could not be downloaded", actor.Name);
            return context;
        }
    }

    private static string Overview(Template template, WrapContext context)
    {
        var me = context.Me;
        var hours = (me?.Totals ?? context.Server.Totals).Hours;
        var minutes = Math.Max(1, (int)Math.Round(hours * MinutesPerHour));
        var rounded = Math.Round(hours, hours >= 10 ? 0 : 1);
        var top = (me?.TopSeries ?? context.Server.TopSeries).Concat(me?.TopMovies ?? context.Server.TopMovies).MaxBy(t => t.Hours);
        var parts = new[]
        {
            template.Name,
            me?.Persona.Name,
            hours switch
            {
                < 1 when minutes == 1 => "1 minute",
                < 1 => $"{minutes} minutes",
                _ when rounded == 1 => "1 hour",
                _ => $"{rounded.ToString(CultureInfo.InvariantCulture)} hours",
            },
            top is null ? null : $"#1 {top.Title}",
        };
        return string.Join(" · ", parts.OfType<string>());
    }
}
