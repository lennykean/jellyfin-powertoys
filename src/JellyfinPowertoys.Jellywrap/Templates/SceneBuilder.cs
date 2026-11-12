using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

using SkiaSharp;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed class SceneBuilder
{
    private const double DefaultSlideDuration = 8;
    private const double DefaultTransitionDuration = 0.8;
    private const string DefaultTransitionType = "crossfade";
    private const string DefaultCardSlide = "intro";
    private const double DefaultCardTime = 5;
    private const string BackgroundLayer = "background";
    private const string TransitionPreset = "preset";

    private readonly JsonObject _merged;
    private readonly FontStore _fonts;
    private readonly ImageStore _images;
    private readonly List<SKPath> _shapePaths;
    private readonly Palette _palette;
    private readonly PropsBuilder _propsBuilder;
    private readonly ElementBuilder _elementBuilder;

    private SceneBuilder(JsonObject merged, FontStore fonts, ImageStore images, List<SKPath> shapePaths)
    {
        _merged = merged;
        _fonts = fonts;
        _images = images;
        _shapePaths = shapePaths;
        _palette = new Palette(merged["palette"] as JsonObject ?? []);
        var trackBuilder = new TrackBuilder(merged["presets"] as JsonObject, _palette);
        _propsBuilder = new PropsBuilder(_palette, trackBuilder);
        _elementBuilder = new ElementBuilder(merged["fallbacks"], fonts, images, _palette, _propsBuilder, trackBuilder, shapePaths);
    }

    public static Scene Build(Template template, string kind, JsonObject context)
    {
        var merged = template.MergedFor(kind) ?? throw new InvalidOperationException($"Template {template.Id} has no {kind} video.");
        var families = (merged["fonts"] as JsonObject ?? []).ToDictionary(family => family.Key, family => FontFiles(family.Value));
        var fonts = new FontStore(families, file => template.ReadFile(file) ?? throw new InvalidOperationException($"The font file {file} is missing."));
        var images = new ImageStore(template.ReadFile);
        var shapePaths = new List<SKPath>();
        try
        {
            return new SceneBuilder(merged, fonts, images, shapePaths).Build(context);
        }
        catch
        {
            fonts.Dispose();
            images.Dispose();
            shapePaths.ForEach(path => path.Dispose());
            throw;
        }
    }

    private static IReadOnlyList<string> FontFiles(JsonNode? files)
    {
        return files is JsonArray chain ? chain.Select(file => file!.GetValue<string>()).ToList() : [files!.GetValue<string>()];
    }

    private Scene Build(JsonObject context)
    {
        var root = Scope.Of(context, null);
        var kept = new List<(string Id, JsonObject Definition, double Duration)>();
        foreach (var (id, node) in _merged["slides"] as JsonObject ?? [])
        {
            var definition = (JsonObject)node!;
            var scope = root.With("slide", SlideInfo(id, 0, 0, 0, 0));
            scope = scope.With(Variables(scope, _merged, definition));
            if (PropertyReader.ReadCondition(definition["when"], scope))
            {
                kept.Add((id, definition, PropertyReader.ReadNumber(definition["duration"], scope) ?? DefaultSlideDuration));
            }
        }

        var slides = new List<Slide>();
        var start = 0.0;
        var previousVariables = new Dictionary<string, object?>();
        for (var i = 0; i < kept.Count; i++)
        {
            var (id, definition, duration) = kept[i];
            var featured = new List<string>();

            var scope = root.With("slide", SlideInfo(id, i, kept.Count, duration, start));
            var variables = Variables(scope, _merged, null);
            scope = scope.With(variables).Collecting(featured);
            var slideVariables = Variables(scope, null, definition);
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
        video = video.With(Variables(video, _merged, null));
        var scene = new Scene
        {
            Slides = slides,
            Fonts = _fonts,
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

    private static Dictionary<string, object?> Variables(Scope scope, JsonObject? template, JsonObject? slide)
    {
        var variables = new Dictionary<string, object?>();
        foreach (var definitions in new[] { template?["vars"], slide?["vars"] }.OfType<JsonObject>())
        {
            foreach (var (name, expression) in definitions)
            {
                variables[name] = PropertyReader.ReadExpression(expression, scope.With(variables));
            }
        }

        return variables;
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
