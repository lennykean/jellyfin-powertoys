using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;

using SkiaSharp;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed class SceneBuilder
{
    public const double MaxVideoSeconds = 3600;

    private const double DefaultSlideDuration = 8;
    private const double DefaultTransitionDuration = 0.8;
    private const string DefaultTransitionType = "crossfade";
    private const string DefaultCardSlide = "intro";
    private const double DefaultCardTime = 5;
    private const string BackgroundLayer = "background";
    private const string TransitionPreset = "preset";

    private static readonly TimeSpan _buildTimeout = TimeSpan.FromMinutes(1);

    private readonly CancellationToken _cancellationToken;
    private readonly JsonObject _merged;
    private readonly TextShaper _shaper;
    private readonly ImageStore _images;
    private readonly List<SKPath> _shapePaths;
    private readonly Palette _palette;
    private readonly PropsBuilder _propsBuilder;
    private readonly ElementBuilder _elementBuilder;

    private SceneBuilder(
        JsonObject merged,
        TextShaper shaper,
        ImageStore images,
        List<SKPath> shapePaths,
        CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
        _merged = merged;
        _shaper = shaper;
        _images = images;
        _shapePaths = shapePaths;
        _palette = new Palette(merged["palette"] as JsonObject ?? []);
        var trackBuilder = new TrackBuilder(merged["presets"] as JsonObject, _palette, cancellationToken);
        _propsBuilder = new PropsBuilder(_palette, trackBuilder);
        _elementBuilder = new ElementBuilder(
            merged["fallbacks"],
            shaper,
            images,
            _palette,
            _propsBuilder,
            trackBuilder,
            shapePaths,
            cancellationToken);
    }

    public static Scene Build(Template template, string kind, JsonObject context, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_buildTimeout);
        cancellationToken.ThrowIfCancellationRequested();
        var merged = template.MergedFor(kind) ?? throw new InvalidOperationException($"Template {template.Id} has no {kind} video.");
        var shaper = new TextShaper();
        var images = new ImageStore(template.ReadFile, ImagePaths(context).ToHashSet());
        var shapePaths = new List<SKPath>();
        try
        {
            foreach (var (name, files) in merged["fonts"] as JsonObject ?? [])
            {
                shaper.LoadFonts(name, FontFiles(files), template, timeoutSource.Token);
            }

            return new SceneBuilder(merged, shaper, images, shapePaths, timeoutSource.Token).Build(context);
        }
        catch (Exception exception)
        {
            shaper.Dispose();
            images.Dispose();
            shapePaths.ForEach(path => path.Dispose());
            if (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"The template {template.Id} did not build in {_buildTimeout.TotalSeconds:F0} s.", exception);
            }

            throw;
        }
    }

    private static IEnumerable<string> ImagePaths(JsonNode? context)
    {
        return context switch
        {
            JsonObject image when PropertyReader.IsString(image["path"], out var path) && image["width"] is not null && image["height"] is not null => [path],
            JsonObject item => item.SelectMany(property => ImagePaths(property.Value)),
            JsonArray list => list.SelectMany(ImagePaths),
            _ => [],
        };
    }

    private static IEnumerable<string> FontFiles(JsonNode? files)
    {
        return files is JsonArray chain ? chain.Select(file => file!.GetValue<string>()) : [files!.GetValue<string>()];
    }

    private Scene Build(JsonObject context)
    {
        var root = Scope.Of(context, null);
        var kept = new List<(string Id, JsonObject Definition, double Duration)>();
        foreach (var (id, node) in _merged["slides"] as JsonObject ?? [])
        {
            var definition = (JsonObject)node!;
            var scope = root.With("slide", SlideInfo(id, 0, 0, 0, 0));
            scope = scope.With(scope.Variables(_merged, definition, _cancellationToken));
            if (PropertyReader.ReadCondition(definition["when"], scope))
            {
                var duration = PropertyReader.ReadNumber(definition["duration"], scope) ?? DefaultSlideDuration;
                if (duration < 0)
                {
                    throw new InvalidOperationException($"The duration of the slide \"{id}\" is {duration}; a duration cannot be negative.");
                }

                kept.Add((id, definition, duration));
            }
        }

        if (kept.Sum(slide => slide.Duration) is var length and > MaxVideoSeconds)
        {
            throw new InvalidOperationException($"The video is {length} seconds long; the longest video is {MaxVideoSeconds} seconds.");
        }

        var slides = new List<Slide>();
        var start = 0.0;
        var previousVariables = new Dictionary<string, object?>();
        for (var i = 0; i < kept.Count; i++)
        {
            var (id, definition, duration) = kept[i];
            var featured = new List<string>();

            var scope = root.With("slide", SlideInfo(id, i, kept.Count, duration, start));
            var variables = scope.Variables(_merged, null, _cancellationToken);
            scope = scope.With(variables).Collecting(featured);
            var slideVariables = scope.Variables(null, definition, _cancellationToken);
            scope = scope.With(slideVariables);
            foreach (var (name, value) in slideVariables)
            {
                variables[name] = value;
            }

            var elements = EffectElements(definition, BackgroundLayer)
                .Concat(definition["elements"] as JsonArray ?? [])
                .Concat(EffectElements(definition, null));
            var slide = new Slide
            {
                Id = id,
                Start = start,
                Duration = duration,
                Featured = featured,
                Background = definition["background"] is { } background ? _palette.Paint(background, scope) : null,
                Nodes = _elementBuilder.Build(elements, new Placement(scope, duration, start, 0)),
            };
            if (i > 0 && (definition["transitionIn"] ?? _merged["transition"]) is JsonObject transition)
            {
                var between = root.With("from", ToJsonObject(previousVariables)).With("to", ToJsonObject(variables));
                slide.TransitionIn = BuildTransition(transition, between, start);
            }

            slides.Add(slide);
            previousVariables = variables;
            start += duration;
        }

        var video = root.With("slide", SlideInfo("video", 0, 1, start, 0));
        video = video.With(video.Variables(_merged, null, _cancellationToken));
        var scene = new Scene
        {
            Slides = slides,
            Shaper = _shaper,
            Images = _images,
            ShapePaths = _shapePaths,
            Background = _palette.Paint(_merged["background"], video)?.Color ?? SKColors.Black,
            CardSlide = _merged["card"]?["slide"]?.GetValue<string>() ?? DefaultCardSlide,
            CardTime = PropertyReader.ReadNumber(_merged["card"]?["time"], video) ?? DefaultCardTime,
        };
        var wholeVideo = new Placement(video, scene.Duration, 0, 0);
        scene.Back.AddRange(_elementBuilder.Build(EffectElements(_merged, BackgroundLayer).Concat(_merged["backdrop"] as JsonArray ?? []), wholeVideo));
        scene.Front.AddRange(_elementBuilder.Build(EffectElements(_merged, null), wholeVideo));
        scene.Open = _merged["open"] is JsonObject open ? BuildTransition(open, video, 0) : null;
        scene.Close = _merged["close"] is JsonObject close ? BuildTransition(close, video, scene.Duration) : null;
        return scene;
    }

    private static JsonObject SlideInfo(string id, int index, int count, double duration, double start)
    {
        return new JsonObject
        {
            ["id"] = id,
            ["index"] = index,
            ["number"] = index + 1,
            ["count"] = count,
            ["duration"] = duration,
            ["start"] = start,
        };
    }

    private static JsonObject ToJsonObject(IReadOnlyDictionary<string, object?> values)
    {
        var json = new JsonObject();
        foreach (var (name, value) in values)
        {
            json[name] = BindingValue.ToNode(value);
        }

        return json;
    }

    private static IEnumerable<JsonNode?> EffectElements(JsonObject owner, string? layer)
    {
        return (owner["effects"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Where(effect => effect["layer"]?.GetValue<string>() == layer)
            .Select(EffectElement);
    }

    private static JsonNode? EffectElement(JsonObject effect)
    {
        var element = (JsonObject)effect.DeepClone();
        element["effect"] = element["type"]!.GetValue<string>();
        element["type"] = "effect";
        element["id"] ??= $"effect-{element["effect"]}";
        return element;
    }

    private Transition BuildTransition(JsonObject definition, Scope scope, double boundary)
    {
        if (definition[TransitionPreset] is JsonValue name)
        {
            var named = _merged["transitions"]?[name.GetValue<string>()] as JsonObject ?? throw new InvalidOperationException($"There is no transition \"{name}\".");
            var preset = (JsonObject)named.DeepClone();
            foreach (var (property, value) in definition.Where(property => property.Key != TransitionPreset))
            {
                preset[property] = value?.DeepClone();
            }

            definition = preset;
        }

        var duration = PropertyReader.ReadNumber(definition["duration"], scope) ?? DefaultTransitionDuration;
        var cut = PropertyReader.ReadNumber(definition["cut"], scope) ?? duration;
        var transition = new Transition
        {
            Type = definition["type"]?.GetValue<string>() ?? DefaultTransitionType,
            Duration = duration,
            Cut = cut,
            Ease = Ease.Parse(definition["ease"]?.GetValue<string>()),
            Running = definition["incoming"]?.GetValue<string>() == "running",
            End = PropertyReader.ReadNumber(definition["end"], scope) ?? 0,
            Props = _propsBuilder.Build(definition, new Placement(scope, duration, boundary - cut, 0)),
        };
        var layerScope = scope.With("transition", new JsonObject { ["duration"] = duration, ["cut"] = cut });
        transition.Layer.AddRange(_elementBuilder.Build(definition["elements"] as JsonArray ?? [], new Placement(layerScope, duration, boundary - cut, 0)));
        return transition;
    }
}
