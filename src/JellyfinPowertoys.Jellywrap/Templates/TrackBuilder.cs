using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

using SkiaSharp;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed class TrackBuilder
{
    private const string EndTime = "end";

    private readonly JsonObject _presets;
    private readonly Palette _palette;
    private readonly HashSet<string> _presetsInUse = [];
    public TrackBuilder(JsonObject? templatePresets, Palette palette)
    {
        _palette = palette;
        _presets = (JsonObject)Presets.Builtin.DeepClone();
        foreach (var (name, preset) in templatePresets ?? [])
        {
            _presets[name] = preset!.DeepClone();
        }
    }

    public void AddTracks(Props props, JsonNode? node, Placement placement, double shift, bool fromEnd)
    {
        switch (node)
        {
            case JsonArray array:
                foreach (var item in array)
                {
                    AddTracks(props, item, placement, shift, fromEnd);
                }

                break;
            case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                AddPreset(props, value.GetValue<string>(), null, placement, shift, fromEnd);
                break;
            case JsonObject use when use["preset"] is JsonValue name:
                var delay = PropertyReader.ReadNumber(use["at"] ?? use["delay"], placement.Scope) ?? 0;
                AddPreset(props, name.GetValue<string>(), use, placement, shift + delay, fromEnd);
                break;
            case JsonObject definition:
                var track = BuildTrack(definition, placement with { Shift = shift });
                if (!props.Tracks.TryGetValue(track.Property, out var tracks))
                {
                    tracks = [];
                    props.Tracks[track.Property] = tracks;
                }

                tracks.Add(track);
                break;
        }
    }

    public static double? EndOffset(string text)
    {
        if (!text.StartsWith(EndTime, StringComparison.Ordinal))
        {
            return null;
        }

        if (text.Length == EndTime.Length)
        {
            return 0;
        }

        return double.TryParse(text[EndTime.Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out var offset)
            ? offset
            : throw new FormatException($"\"{text}\" is not a time; an end time is \"end\", \"end-0.5\" or \"end+0.5\".");
    }

    private void AddPreset(Props props, string name, JsonObject? use, Placement placement, double shift, bool fromEnd)
    {
        var arguments = new Dictionary<string, object?>();
        foreach (var (argument, value) in use?["args"] as JsonObject ?? [])
        {
            arguments[argument] = PropertyReader.ReadExpression(value, placement.Scope);
        }

        if (use?["period"] is { } period)
        {
            arguments["period"] = PropertyReader.ReadExpression(period, placement.Scope);
        }

        var presetPlacement = placement with { Scope = placement.Scope.With(arguments) };
        var tracks = _presets[name] as JsonArray ?? throw new InvalidOperationException($"There is no preset \"{name}\".");
        if (!_presetsInUse.Add(name))
        {
            throw new InvalidOperationException($"The preset \"{name}\" uses itself.");
        }

        if (fromEnd)
        {
            var lastTime = tracks
                .OfType<JsonObject>()
                .SelectMany(track => track["keys"] as JsonArray ?? [])
                .Max(keyframe => ReadTime(KeyframeParts(keyframe).Time, presetPlacement)) ?? 0;
            shift = placement.Duration - lastTime;
        }

        AddTracks(props, tracks, presetPlacement, shift, fromEnd: false);
        _presetsInUse.Remove(name);
    }

    private static (JsonNode? Time, JsonNode? Value, JsonNode? Ease) KeyframeParts(JsonNode? keyframe)
    {
        return keyframe is JsonArray array
            ? (array[0], array[1], array.Count > 2 ? array[2] : null)
            : (keyframe?["t"], keyframe?["v"], keyframe?["ease"]);
    }

    private static double? ReadTime(JsonNode? node, Placement placement)
    {
        return node is JsonValue value && value.GetValueKind() == JsonValueKind.String && EndOffset(value.GetValue<string>()) is { } offset
            ? placement.Duration + offset
            : PropertyReader.ReadNumber(node, placement.Scope);
    }

    private Track BuildTrack(JsonObject definition, Placement placement)
    {
        var scope = placement.Scope;
        var property = definition["prop"]?.GetValue<string>() ?? throw new InvalidOperationException("A track needs a \"prop\".");
        var videoClock = definition["clock"]?.GetValue<string>() == "video";
        var start = ReadTime(definition["start"], placement) is { } startTime ? startTime + placement.Shift : double.NegativeInfinity;
        var end = ReadTime(definition["end"], placement) is { } endTime ? endTime + placement.Shift : double.PositiveInfinity;
        if (definition["wave"] is JsonObject wave)
        {
            var angularFrequency = PropertyReader.ReadNumber(wave["frequency"], scope) ?? (2 * Math.PI / (PropertyReader.ReadNumber(wave["period"], scope) ?? 1));
            return new Track
            {
                Property = property,
                Wave = (PropertyReader.ReadNumber(wave["amplitude"], scope) ?? 0, angularFrequency, PropertyReader.ReadNumber(wave["phase"], scope) ?? 0),
                Grow = wave["grow"] is JsonArray grow
                    ? ((ReadTime(grow[0], placement) ?? 0) + placement.Shift, (ReadTime(grow[1], placement) ?? 0) + placement.Shift)
                    : null,
                VideoClock = videoClock,
                Start = start,
                End = end,
            };
        }

        if (definition["rate"] is { } rate)
        {
            if (start > end)
            {
                throw new InvalidOperationException($"The \"{property}\" track starts after its end.");
            }

            return new Track { Property = property, Rate = PropertyReader.ReadNumber(rate, scope) ?? 0, VideoClock = videoClock, Start = start, End = end };
        }

        var isColor = Props.IsPaint(property);
        var (keyframes, relative) = BuildKeyframes(definition, placement, isColor, videoClock);
        if (keyframes.Count == 0)
        {
            throw new InvalidOperationException($"The \"{property}\" track needs keys, a wave or a rate.");
        }

        var (loopPeriod, loopCount) = definition["loop"] switch
        {
            JsonObject loop => (PropertyReader.ReadNumber(loop["period"], scope) ?? 0, PropertyReader.ReadNumber(loop["count"], scope) ?? double.PositiveInfinity),
            { } period => (PropertyReader.ReadNumber(period, scope) ?? 0, double.PositiveInfinity),
            null => (0, double.PositiveInfinity),
        };
        return new Track
        {
            Property = property,
            Keyframes = [.. keyframes.OrderBy(keyframe => keyframe.Time)],
            Relative = relative,
            IsColor = isColor,
            LoopPeriod = loopPeriod,
            LoopCount = loopCount,
            VideoClock = videoClock,
            Min = PropertyReader.ReadNumber(definition["min"], scope) ?? double.NegativeInfinity,
            Max = PropertyReader.ReadNumber(definition["max"], scope) ?? double.PositiveInfinity,
        };
    }

    private (List<Keyframe> Keyframes, bool Relative) BuildKeyframes(JsonObject definition, Placement placement, bool isColor, bool videoClock)
    {
        var defaultEase = definition["ease"]?.GetValue<string>();
        var relative = false;
        var keyframes = new List<Keyframe>();
        foreach (var keyframe in definition["keys"] as JsonArray ?? [])
        {
            var (timeNode, valueNode, easeNode) = KeyframeParts(keyframe);
            var time = (ReadTime(timeNode, placement) ?? 0) + (videoClock ? 0 : placement.Shift);
            var text = valueNode is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;
            var isRelative = text is not null && (text.StartsWith('+') || text.StartsWith('-'));
            if (text is not null && TextTemplate.HasHoles(text))
            {
                text = BindingValue.Print(TextTemplate.Parse(text).Evaluate(placement.Scope));
            }

            var ease = Ease.Parse(easeNode?.GetValue<string>() ?? defaultEase);
            if (isColor)
            {
                keyframes.Add(new Keyframe(time, 0, text is null ? SKColors.Black : _palette.Color(text), ease));
                continue;
            }

            relative |= isRelative;
            var number = text is null
                ? PropertyReader.ReadNumber(valueNode, placement.Scope) ?? 0
                : double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
            keyframes.Add(new Keyframe(time, number, SKColors.Black, ease));
        }

        return (keyframes, relative);
    }
}
