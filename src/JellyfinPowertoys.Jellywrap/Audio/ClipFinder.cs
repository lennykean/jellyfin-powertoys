using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using JellyfinPowertoys.Jellywrap.Context;

using Microsoft.Extensions.Logging;

namespace JellyfinPowertoys.Jellywrap.Audio;

public sealed class ClipFinder(ItemMarkerReader markerReader, AudioProbe probe, ILogger<ClipFinder> logger)
{
    internal const int MatchedEpisodes = 3;
    private const int TriedStretches = 4;
    private const double MaxScan = 150;
    private const double ScanLeadIn = 1;
    private const double MaxCredits = 150;
    private const double MinCreditsLength = 120;
    private const double ThemeSearch = 600;
    private const double CreditsSearch = 180;
    private const double ThemeMargin = 30;
    private const double CreditsMargin = 10;
    private const double MaxFingerprint = 600;
    private const double ChromaprintStep = 0.1238;

    private static readonly Regex _themeChapter = new(
        @"^(opening( credits| titles| theme)?|intro|op|theme( song)?|main titles?|title sequence)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex _creditsChapter = new(
        @"^((end|closing) )?credits$|^ending|^ed$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal async Task<Clip?> ThemeAsync(
        Guid titleId,
        string title,
        IReadOnlyList<ThemeSong> themeSongs,
        IReadOnlyList<MediaItem> episodes,
        CancellationToken cancellationToken)
    {
        foreach (var song in themeSongs)
        {
            var source = new ClipSource(titleId, title, ClipKind.Theme, "theme song", song.Path, song.Runtime);
            var clip = await MakeAsync(source, 0, song.Runtime, cancellationToken);
            if (clip is not null)
            {
                return clip;
            }
        }

        return await MatchedAsync(titleId, title, ClipKind.Theme, episodes, cancellationToken);
    }

    internal Task<Clip?> SeriesCreditsAsync(
        Guid titleId,
        string title,
        IReadOnlyList<MediaItem> episodes,
        CancellationToken cancellationToken)
    {
        return MatchedAsync(titleId, title, ClipKind.Credits, episodes, cancellationToken);
    }

    internal async Task<Clip?> MovieCreditsAsync(Guid titleId, string title, MediaItem movie, CancellationToken cancellationToken)
    {
        if (movie.Path is null)
        {
            return Drop(title, ClipKind.Credits, "no readable file");
        }

        var length = movie.Runtime ?? 0;
        var markers = await markerReader.ReadAsync(movie.Id);
        var starts = Chapters(markers, length).Where(c => _creditsChapter.IsMatch(c.Name)).Select(c => c.Start).ToList();
        if (markers.Segments.TryGetValue("Outro", out var outro))
        {
            starts.Add(outro.Start);
        }

        if (starts.Count == 0)
        {
            return Drop(title, ClipKind.Credits, "no Outro segment or credits chapter");
        }

        var start = starts.Min();
        if (length - start < MinCreditsLength)
        {
            return Drop(title, ClipKind.Credits, $"the marked credits start {length - start:F0} s before the end of the file (logo or end card)");
        }

        var source = new ClipSource(titleId, title, ClipKind.Credits, "credits start", movie.Path, length);
        return await MakeAsync(source, start, Math.Min(MaxCredits, length - start), cancellationToken);
    }

    internal async Task<Clip?> SongAsync(MediaItem song, CancellationToken cancellationToken)
    {
        if (song is not { Path: { } path, Runtime: { } length })
        {
            return null;
        }

        return await MakeAsync(new ClipSource(song.Id, song.Name, ClipKind.Song, "song", path, length), 0, length, cancellationToken);
    }

    private static (double Start, double End) Window(ClipKind kind, double length, (double Start, double End)? marked)
    {
        if (kind == ClipKind.Theme)
        {
            return marked is { } theme
                ? (Math.Max(0, theme.Start - ThemeMargin), Math.Min(length, theme.End + ThemeMargin))
                : (0, Math.Min(length / 2, ThemeSearch));
        }

        return marked is { } credits
            ? (Math.Max(0, credits.Start - CreditsMargin), length)
            : (Math.Max(length / 2, length - CreditsSearch), length);
    }

    private static IEnumerable<(string Name, double Start, double End)> Chapters(ItemMarkers markers, double length)
    {
        return markers.Chapters.Select((chapter, index) => (
            chapter.Name.Trim(),
            chapter.Start,
            index + 1 < markers.Chapters.Count ? markers.Chapters[index + 1].Start : length));
    }

    private async Task<Clip?> MatchedAsync(
        Guid titleId,
        string title,
        ClipKind kind,
        IReadOnlyList<MediaItem> episodes,
        CancellationToken cancellationToken)
    {
        if (episodes.Count < MatchedEpisodes)
        {
            return Drop(title, kind, $"fewer than {MatchedEpisodes} episodes in the season to match");
        }

        var searched = new List<(double Start, uint[] Fingerprint)>();
        foreach (var episode in episodes)
        {
            var window = await WindowAsync(episode, kind);
            var length = Math.Max(1, Math.Min(Math.Min(window.End - window.Start, MaxFingerprint), (episode.Runtime ?? 0) - window.Start));
            try
            {
                searched.Add((window.Start, await probe.FingerprintAsync(episode.Path!, window.Start, length, cancellationToken)));
            }
            catch (InvalidOperationException exception)
            {
                return Unreadable(title, kind, episode.Path!, exception);
            }
        }

        var first = searched[0];
        var runsPerOther = searched
            .Skip(1)
            .Select(other => Chromaprint.Runs(first.Fingerprint, other.Fingerprint, ChromaprintStep, Clip.MinLength))
            .ToList();
        var stretches = Chromaprint.Common(runsPerOther, Clip.MinLength);
        if (stretches.Count == 0)
        {
            return Drop(title, kind, $"no stretch of {Clip.MinLength} s or more repeats across {MatchedEpisodes} episodes");
        }

        // A shared stretch can be silence (a disclaimer card), so the next longest gets a try.
        var source = new ClipSource(
            titleId,
            title,
            kind,
            $"matched across {MatchedEpisodes} episodes",
            episodes[0].Path!,
            episodes[0].Runtime ?? 0);
        foreach (var (stretchStart, stretchEnd) in stretches.Take(TriedStretches))
        {
            var clip = await MakeAsync(source, first.Start + stretchStart, stretchEnd - stretchStart, cancellationToken);
            if (clip is not null)
            {
                return clip;
            }
        }

        return null;
    }

    private async Task<(double Start, double End)> WindowAsync(MediaItem episode, ClipKind kind)
    {
        var length = episode.Runtime ?? 0;
        var markers = await markerReader.ReadAsync(episode.Id);
        var chapterName = kind == ClipKind.Theme ? _themeChapter : _creditsChapter;
        (double Start, double End)? marked = markers.Segments.TryGetValue(kind == ClipKind.Theme ? "Intro" : "Outro", out var segment)
            ? segment
            : Chapters(markers, length).Where(c => chapterName.IsMatch(c.Name)).Select(c => ((double, double)?)(c.Start, c.End)).FirstOrDefault();
        return Window(kind, length, marked);
    }

    private async Task<Clip?> MakeAsync(ClipSource source, double start, double length, CancellationToken cancellationToken)
    {
        start = Math.Max(0, start);
        length = Math.Min(length, source.Runtime - start);
        IReadOnlyList<LoudnessFrame> frames;
        try
        {
            frames = await probe.LoudnessAsync(source.Path, Math.Max(0, start - ScanLeadIn), start + Math.Min(length, MaxScan), cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return Unreadable(source.Title, source.Kind, source.Path, exception);
        }

        var (musicStart, musicLength) = Loudness.TrimSilence(frames, start, length);
        if (musicLength < Clip.MinLength)
        {
            return Drop(source.Title, source.Kind, $"only {musicLength:F0} s of sound in the {source.Method} span");
        }

        var (isMusic, measured) = Loudness.MusicLike(frames, musicStart, musicLength);
        if (!isMusic)
        {
            return Drop(source.Title, source.Kind, $"not music ({measured}) in the {source.Method} span");
        }

        logger.LogDebug("{Title} {Kind}: ok: {Method}, {Length:F0} s from {Start:F0} s", source.Title, source.Kind.ToName(), source.Method, musicLength, musicStart);
        return new Clip(source.TitleId, source.Title, source.Kind, source.Path, musicStart, musicLength);
    }

    private Clip? Drop(string title, ClipKind kind, string reason)
    {
        logger.LogDebug("{Title} {Kind}: dropped: {Reason}", title, kind.ToName(), reason);
        return null;
    }

    private Clip? Unreadable(string title, ClipKind kind, string path, InvalidOperationException exception)
    {
        logger.LogWarning(exception, "{Title} {Kind}: dropped: ffmpeg cannot read {Path}", title, kind.ToName(), path);
        return null;
    }

    private sealed record ClipSource(Guid TitleId, string Title, ClipKind Kind, string Method, string Path, double Runtime);
}
