using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed class TemplateValidator
{
    private const int KeyframeMinParts = 2;
    private const string DefaultElementType = "group";
    private const string DefaultFont = "body";

    private static readonly string[] _trackLists = ["in", "out", "idle", "animate"];
    private static readonly string[] _propertyGroups = ["box", "border"];
    private static readonly string[] _transitionUses = ["transition", "open", "close"];
    private static readonly string[] _gridPairs = ["pitch", "cell", "fitBox"];
    private static readonly string[] _gradientPoints = ["from", "to"];
    private readonly List<string> _errors = [];
    private readonly JsonObject _template;
    private readonly JsonObject _fonts;
    private readonly JsonObject _presets;
    private readonly Palette _palette;
    private readonly Func<string, byte[]?> _readFile;

    private TemplateValidator(JsonObject template, Func<string, byte[]?> readFile)
    {
        _template = template;
        _fonts = template["fonts"] as JsonObject ?? [];
        _presets = (JsonObject)Presets.Builtin.DeepClone();
        foreach (var (name, preset) in template["presets"] as JsonObject ?? [])
        {
            _presets[name] = preset?.DeepClone();
        }

        _palette = new Palette(template["palette"] as JsonObject ?? []);
        _readFile = readFile;
    }

    public static List<string> Validate(JsonObject template, Func<string, byte[]?> readFile)
    {
        var validator = new TemplateValidator(template, readFile);
        validator.CheckFonts();
        validator.CheckBindingsAndFiles(template, "$");
        validator.CheckTemplate();
        return validator._errors;
    }

    private void CheckFonts()
    {
        foreach (var (name, files) in _fonts)
        {
            var chain = files is JsonArray array ? array.ToList() : [files];
            if (chain.Count == 0)
            {
                _errors.Add($"font \"{name}\": the font has no file");
            }

            foreach (var file in chain)
            {
                try
                {
                    var path = PropertyReader.IsString(file, out var text) ? text : throw new InvalidDataException($"font \"{name}\": a font file must be a text");
                    using var face = FontFace.FromFile(_readFile(path) ?? throw new InvalidDataException($"font \"{name}\": the file {path} is missing"));
                }
                catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
                {
                    _errors.Add(exception.Message);
                }
            }
        }
    }

    private void CheckBindingsAndFiles(JsonNode? node, string location)
    {
        switch (node)
        {
            case JsonObject definition:
                foreach (var (property, value) in definition)
                {
                    CheckBindingsAndFiles(value, $"{location}.{property}");
                }

                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    CheckBindingsAndFiles(array[i], $"{location}[{i}]");
                }

                break;
            default:
                if (!PropertyReader.IsString(node, out var text))
                {
                    break;
                }

                if (TextTemplate.HasHoles(text))
                {
                    Try(location, () => TextTemplate.Parse(text));
                }
                else if (text.StartsWith(Template.PackagePrefix, StringComparison.Ordinal))
                {
                    if (_readFile(text) is not { } bytes)
                    {
                        _errors.Add($"{location}: the file {text} is missing, or it is in a package that the template does not extend");
                    }
                    else if (text.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                    {
                        CheckSvg(SvgArt.ReadText(bytes), $"{location} ({text})");
                    }
                    else if (ImageStore.IsTooLarge(bytes))
                    {
                        _errors.Add($"{location}: the image {text} has more than {ImageStore.MaxMegapixels} megapixels");
                    }
                }

                break;
        }
    }

    private void CheckSvg(string svg, string location)
    {
        try
        {
            if (TextTemplate.HasHoles(svg))
            {
                XDocument.Parse(svg);
                TextTemplate.Parse(svg);
            }
            else if (SvgArt.Load(svg) is not { } picture)
            {
                _errors.Add($"{location}: not an <svg> with a viewBox or width and height");
            }
            else
            {
                picture.Dispose();
            }
        }
        catch (Exception exception) when (exception is FormatException or XmlException)
        {
            _errors.Add($"{location}: {exception.Message}");
        }
    }

    private void CheckTemplate()
    {
        if (_template["slides"] is not JsonObject slides || slides.Count == 0)
        {
            _errors.Add("the template has no slides");
        }

        foreach (var (name, _) in _template["palette"] as JsonObject ?? [])
        {
            Try($"$.palette.{name}", () => _palette.Color(name));
        }

        var nestedTooDeep = PresetsNestedTooDeep();
        foreach (var (name, preset) in _template["presets"] as JsonObject ?? [])
        {
            if (preset is not JsonArray)
            {
                _errors.Add($"$.presets.{name}: a preset must be a list of tracks");
            }
            else if (nestedTooDeep.Contains(name))
            {
                _errors.Add($"$.presets.{name}: the preset uses itself, or its presets are nested deeper than {TrackBuilder.MaxPresetDepth} levels");
            }

            CheckTracks(preset, $"$.presets.{name}");
        }

        if (_template["fallbacks"] is { } fallbacks and not JsonObject)
        {
            _errors.Add($"$.fallbacks: must be an object of element lists, not {fallbacks.ToJsonString()}");
        }

        if (_template["card"] is { } card and not JsonObject)
        {
            _errors.Add($"$.card: must be an object, not {card.ToJsonString()}");
        }
        else
        {
            CheckIsText(_template["card"]?["slide"], "$.card.slide");
        }

        CheckVars(_template["vars"], "$.vars");
        CheckPaint(_template["background"], "$.background");
        CheckElements(_template["backdrop"], "$.backdrop");
        CheckEffects(_template["effects"], "$.effects");
        foreach (var (name, elements) in _template["fallbacks"] as JsonObject ?? [])
        {
            CheckElements(elements, $"$.fallbacks.{name}");
        }

        foreach (var (name, transition) in _template["transitions"] as JsonObject ?? [])
        {
            CheckTransition(transition, $"$.transitions.{name}");
        }

        foreach (var use in _transitionUses)
        {
            CheckTransition(_template[use], $"$.{use}");
        }

        foreach (var (id, node) in _template["slides"] as JsonObject ?? [])
        {
            var location = $"$.slides.{id}";
            if (node is not JsonObject slide)
            {
                _errors.Add($"{location}: a slide must be an object");
                continue;
            }

            CheckCondition(slide["when"], $"{location}.when");
            CheckRange(slide["duration"], $"{location}.duration", 0, SceneBuilder.MaxVideoSeconds);
            CheckVars(slide["vars"], $"{location}.vars");
            CheckPaint(slide["background"], $"{location}.background");
            CheckElements(slide["elements"], $"{location}.elements");
            CheckEffects(slide["effects"], $"{location}.effects");
            CheckTransition(slide["transitionIn"], $"{location}.transitionIn");
        }
    }

    private HashSet<string> PresetsNestedTooDeep()
    {
        // After each round a depth is right up to one level more; a preset that uses itself never stops growing.
        var depths = _presets.ToDictionary(preset => preset.Key, _ => 1);
        for (var round = 0; round < TrackBuilder.MaxPresetDepth; round++)
        {
            depths = depths.ToDictionary(
                preset => preset.Key,
                preset => 1 + PresetNames(_presets[preset.Key]).Select(name => depths.GetValueOrDefault(name)).DefaultIfEmpty().Max());
        }

        return [.. depths.Where(preset => preset.Value > TrackBuilder.MaxPresetDepth).Select(preset => preset.Key)];
    }

    private void CheckTransition(JsonNode? node, string location)
    {
        if (node is not JsonObject definition)
        {
            return;
        }

        if (definition["preset"] is { } preset && (!PropertyReader.IsString(preset, out var name) || (_template["transitions"] as JsonObject)?[name] is not JsonObject))
        {
            _errors.Add($"{location}: unknown transition {preset.ToJsonString()}");
        }

        CheckIsText(definition["type"], $"{location}.type");
        CheckIsText(definition["incoming"], $"{location}.incoming");
        CheckEase(definition["ease"], $"{location}.ease");
        CheckProperties(definition, location);
        CheckElements(definition["elements"], $"{location}.elements");
    }

    private void CheckEffects(JsonNode? node, string location)
    {
        var effects = node as JsonArray ?? [];
        for (var i = 0; i < effects.Count; i++)
        {
            if (effects[i] is JsonObject effect)
            {
                CheckCondition(effect["when"], $"{location}[{i}].when");
                CheckIsText(effect["id"], $"{location}[{i}].id");
                CheckIsText(effect["layer"], $"{location}[{i}].layer");
                CheckProperties(effect, $"{location}[{i}]");
                CheckEffect(effect["type"], effect, $"{location}[{i}]");
            }
        }
    }

    private void CheckEffect(JsonNode? effect, JsonObject definition, string location)
    {
        if (!PropertyReader.IsString(effect, out var name) || !Effects.Exists(name))
        {
            _errors.Add($"{location}: unknown effect {effect?.ToJsonString() ?? "null"}");
        }
        else if (!HasBindings(definition))
        {
            Try(location, () => Effects.Build(name, new EffectParameters(definition, _palette), string.Empty, new Props()));
        }
    }

    private void CheckElements(JsonNode? node, string location)
    {
        var elements = node as JsonArray ?? [];
        for (var i = 0; i < elements.Count; i++)
        {
            if (elements[i] is JsonObject element)
            {
                CheckElement(element, $"{location}[{i}]");
            }
        }
    }

    private void CheckElement(JsonObject element, string location)
    {
        var type = DefaultElementType;
        if (element["type"] is { } written && (!PropertyReader.IsString(written, out type) || !ElementBuilder.HasType(type)))
        {
            _errors.Add($"{location}: unknown element type {written.ToJsonString()}");
            return;
        }

        CheckIsText(element["id"], $"{location}.id");
        CheckIsText(element["before"], $"{location}.before");
        CheckIsText(element["after"], $"{location}.after");
        if (element["remove"] is { } remove && remove.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False))
        {
            _errors.Add($"{location}.remove: must be true or false, not {remove.ToJsonString()}");
        }

        CheckCondition(element["when"], $"{location}.when");
        CheckProperties(element, location);
        switch (type)
        {
            case "text":
                CheckText(element, location);
                break;
            case "shape":
                CheckShape(element, location);
                break;
            case "image":
                CheckImage(element, location);
                break;
            case "group":
                CheckElements(element["children"], $"{location}.children");
                CheckClip(element["clip"], $"{location}.clip");
                break;
            case "repeat":
                CheckRepeat(element, location);
                CheckExpression(element["items"], $"{location}.items");
                CheckCondition(element["where"], $"{location}.where");
                CheckVars(element["vars"], $"{location}.vars");
                CheckElements(element["template"], $"{location}.template");
                break;
            case "pie":
                foreach (var (part, index) in (element["parts"] as JsonArray ?? []).Select((part, index) => (part, index)))
                {
                    CheckPaint((part as JsonObject)?["fill"], $"{location}.parts[{index}].fill");
                }

                break;
            case "odometer":
                if (element["font"] is null)
                {
                    _errors.Add($"{location}: an odometer needs a font");
                }

                CheckFont(element["font"], location);
                break;
            case "effect":
                CheckEffect(element["effect"], element, location);
                break;
        }
    }

    private void CheckText(JsonObject element, string location)
    {
        if (element["text"] is { } text && !PropertyReader.IsString(text, out _))
        {
            _errors.Add($"{location}.text: must be a text, not {text.ToJsonString()}");
        }

        if (element["font"] is null && !_fonts.ContainsKey(DefaultFont))
        {
            _errors.Add($"{location}: a text without a font needs the font \"{DefaultFont}\"");
        }

        CheckFont(element["font"], location);
        CheckIsText(element["case"], $"{location}.case");
        CheckIsText(element["fitOn"], $"{location}.fitOn");
        Try(location, () => TextFit.FromProps(NumbersOf(element), null, Scope.Empty, 0));
        if (element["single"] is JsonObject single)
        {
            Try($"{location}.single", () => TextFit.FromProps(NumbersOf(single), null, Scope.Empty, 0));
        }

        if (element["words"] is JsonObject words)
        {
            CheckTracks(words["in"], $"{location}.words.in");
        }

        if (element["lead"] is JsonObject lead)
        {
            if (!PropertyReader.IsString(lead["text"], out _))
            {
                _errors.Add($"{location}.lead: the lead needs a text");
            }

            CheckPaint(lead["color"], $"{location}.lead.color");
        }
    }

    private static Props NumbersOf(JsonObject definition)
    {
        var props = new Props();
        foreach (var (property, value) in definition)
        {
            if (value is JsonValue number && number.GetValueKind() == JsonValueKind.Number)
            {
                props.Numbers[property] = number.GetValue<double>();
            }
        }

        return props;
    }

    private void CheckShape(JsonObject shape, string location)
    {
        CheckIsText(shape["shape"], $"{location}.shape");
        CheckIsText(shape["d"], $"{location}.d");
    }

    private void CheckImage(JsonObject element, string location)
    {
        if (element["src"] is { } source)
        {
            CheckIsText(source, $"{location}.src");
            if (PropertyReader.IsString(source, out var file)
                && !TextTemplate.HasHoles(file)
                && !file.StartsWith(Template.PackagePrefix, StringComparison.Ordinal))
            {
                _errors.Add($"{location}.src: the image \"{file}\" must be a file in the \"img\" folder of the package, such as \"img/{Path.GetFileName(file)}\"");
            }
        }

        if (element["fallback"] is JsonValue fallback && !PropertyReader.IsString(fallback, out _))
        {
            _errors.Add($"{location}.fallback: must be a list of elements or the name of a fallback, not {fallback.ToJsonString()}");
        }

        CheckExpression(element["title"], $"{location}.title");
        CheckElements(element["fallback"], $"{location}.fallback");
        CheckClip(element["clip"], $"{location}.clip");
    }

    private void CheckRepeat(JsonObject element, string location)
    {
        CheckRange(element["count"], $"{location}.count", double.NegativeInfinity, ElementBuilder.MaxElements);
        foreach (var pair in _gridPairs.Where(pair => element[pair] is JsonArray { Count: < 2 }))
        {
            _errors.Add($"{location}.{pair}: must be [x, y], not {element[pair]!.ToJsonString()}");
        }

        if (element["rows"] is JsonValue rows && rows.GetValueKind() == JsonValueKind.Number && rows.GetValue<double>() <= 0)
        {
            _errors.Add($"{location}.rows: must be above 0");
        }
    }

    private void CheckFont(JsonNode? font, string location)
    {
        if (font is not null && (!PropertyReader.IsString(font, out var name) || (!TextTemplate.HasHoles(name) && !_fonts.ContainsKey(name))))
        {
            _errors.Add($"{location}: unknown font {font.ToJsonString()}");
        }
    }

    private void CheckClip(JsonNode? node, string location)
    {
        if (node is JsonObject clip)
        {
            CheckProperties(clip, location);
            CheckShape(clip, location);
        }
    }

    private void CheckProperties(JsonObject definition, string location)
    {
        foreach (var (property, value) in definition.Where(property => Props.IsPaint(property.Key)))
        {
            CheckPaint(value, $"{location}.{property}");
        }

        foreach (var group in _propertyGroups)
        {
            foreach (var (property, value) in definition[group] as JsonObject ?? [])
            {
                if (property.Length > 0 && Props.IsPaint(group + char.ToUpperInvariant(property[0]) + property[1..]))
                {
                    CheckPaint(value, $"{location}.{group}.{property}");
                }
            }
        }

        if (definition["pivot"] is { } pivot && pivot is not JsonArray { Count: >= 2 })
        {
            _errors.Add($"{location}.pivot: the pivot must be [x, y]");
        }

        foreach (var list in _trackLists)
        {
            CheckTracks(definition[list], $"{location}.{list}");
        }
    }

    private void CheckTracks(JsonNode? node, string location)
    {
        switch (node)
        {
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    CheckTracks(array[i], $"{location}[{i}]");
                }

                break;
            case JsonObject use when use["preset"] is JsonValue:
                foreach (var (argument, value) in use["args"] as JsonObject ?? [])
                {
                    CheckExpression(value, $"{location}.args.{argument}");
                }

                CheckExpression(use["period"], $"{location}.period");
                CheckPresetName(use["preset"]!, location);
                break;
            case JsonObject track:
                CheckTrack(track, location);
                break;
            case JsonValue name when name.GetValueKind() == JsonValueKind.String:
                CheckPresetName(name, location);
                break;
        }
    }

    private void CheckPresetName(JsonNode preset, string location)
    {
        if (!PropertyReader.IsString(preset, out var name) || _presets[name] is not JsonArray)
        {
            _errors.Add($"{location}: unknown preset {preset.ToJsonString()}");
        }
    }

    private void CheckTrack(JsonObject track, string location)
    {
        if (!PropertyReader.IsString(track["prop"], out var property))
        {
            _errors.Add($"{location}: a track needs a \"prop\"");
        }

        CheckIsText(track["clock"], $"{location}.clock");
        CheckEase(track["ease"], $"{location}.ease");
        CheckTime(track["start"], $"{location}.start");
        CheckTime(track["end"], $"{location}.end");
        if (track["start"]?.GetValueKind() == JsonValueKind.Number
            && track["end"]?.GetValueKind() == JsonValueKind.Number
            && track["start"]!.GetValue<double>() > track["end"]!.GetValue<double>())
        {
            _errors.Add($"{location}: the track starts after its end");
        }

        var keys = track["keys"] as JsonArray ?? [];
        for (var i = 0; i < keys.Count; i++)
        {
            CheckKeyframe(keys[i], Props.IsPaint(property), $"{location}.keys[{i}]");
        }

        if (track["wave"] is JsonObject wave)
        {
            if (wave["grow"] is JsonArray { Count: < 2 } grow)
            {
                _errors.Add($"{location}.wave.grow: must be [from, to], not {grow.ToJsonString()}");
            }

            foreach (var (time, index) in (wave["grow"] as JsonArray ?? []).Select((time, index) => (time, index)))
            {
                CheckTime(time, $"{location}.wave.grow[{index}]");
            }
        }
        else if (track["rate"] is { } rate)
        {
            if (keys.Count == 0 && rate.GetValueKind() == JsonValueKind.Number && rate.GetValue<double>() == 0)
            {
                _errors.Add($"{location}: a track with a rate of 0 does nothing");
            }
        }
        else if (keys.Count == 0)
        {
            _errors.Add($"{location}: a track needs keys, a wave or a rate");
        }
    }

    private void CheckKeyframe(JsonNode? keyframe, bool isColor, string location)
    {
        var (time, value, ease) = keyframe switch
        {
            JsonArray { Count: >= KeyframeMinParts } array => (array[0], array[1], array.Count > KeyframeMinParts ? array[KeyframeMinParts] : null),
            JsonObject parts when parts["t"] is not null && parts.ContainsKey("v") => (parts["t"], parts["v"], parts["ease"]),
            _ => (null, null, null),
        };
        if (time is null)
        {
            _errors.Add($"{location}: a keyframe must be [time, value] or {{ \"t\", \"v\" }}, with an optional ease");
            return;
        }

        CheckTime(time, location);
        CheckEase(ease, location);
        if (isColor && PropertyReader.IsString(value, out var color) && !TextTemplate.HasHoles(color))
        {
            Try(location, () => _palette.Color(color));
        }
    }

    private void CheckTime(JsonNode? node, string location)
    {
        if (PropertyReader.IsString(node, out var text) && !TextTemplate.HasHoles(text))
        {
            Try(location, () => _ = TrackBuilder.EndOffset(text) ?? double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture));
        }
    }

    private void CheckEase(JsonNode? node, string location)
    {
        if (node is null)
        {
            return;
        }

        if (!PropertyReader.IsString(node, out var name))
        {
            _errors.Add($"{location}: an easing must be a name, not {node.ToJsonString()}");
        }
        else
        {
            Try(location, () => Ease.Parse(name));
        }
    }

    private void CheckPaint(JsonNode? node, string location)
    {
        if (node is null)
        {
            return;
        }

        CheckIsText((node as JsonObject)?["type"], $"{location}.type");
        if (!HasBindings(node))
        {
            Try(location, () => _palette.Paint(node, Scope.Empty));
        }
        else if (node is JsonObject gradient)
        {
            if ((gradient["stops"] as JsonArray ?? []).Any(stop => stop is JsonArray { Count: < Palette.StopParts }))
            {
                _errors.Add($"{location}.stops: a gradient stop must be [offset, color]");
            }

            foreach (var point in _gradientPoints.Where(point => gradient[point] is not JsonArray { Count: >= Palette.PointNumbers }))
            {
                _errors.Add($"{location}: a gradient needs \"{point}\": [x, y]");
            }
        }
    }

    private void CheckCondition(JsonNode? node, string location)
    {
        if (node is not null && !PropertyReader.IsString(node, out _))
        {
            _errors.Add($"{location}: a condition must be an expression in a text, not {node.ToJsonString()}");
        }

        CheckExpression(node, location);
    }

    private void CheckVars(JsonNode? node, string location)
    {
        foreach (var (name, expression) in node as JsonObject ?? [])
        {
            CheckExpression(expression, $"{location}.{name}");
        }
    }

    private void CheckExpression(JsonNode? node, string location)
    {
        if (PropertyReader.IsString(node, out var text) && !TextTemplate.HasHoles(text))
        {
            Try(location, () => Expression.Parse(text));
        }
    }

    private void CheckIsText(JsonNode? node, string location)
    {
        if (node is not null && !PropertyReader.IsString(node, out _))
        {
            _errors.Add($"{location}: must be a text, not {node.ToJsonString()}");
        }
    }

    private void CheckRange(JsonNode? node, string location, double min, double max)
    {
        if (node is JsonValue value
            && value.GetValueKind() == JsonValueKind.Number
            && value.GetValue<double>() is var number
            && !(number >= min && number <= max))
        {
            var range = double.IsNegativeInfinity(min) ? $"at most {max}" : $"from {min} to {max}";
            _errors.Add($"{location}: must be {range}, not {number}");
        }
    }

    private void Try(string location, Action check)
    {
        try
        {
            check();
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException or ArgumentException)
        {
            _errors.Add($"{location}: {exception.Message}");
        }
    }

    private static bool HasBindings(JsonNode node)
    {
        return TextTemplate.HasHoles(node.ToJsonString());
    }

    private static IEnumerable<string> PresetNames(JsonNode? node)
    {
        return node switch
        {
            JsonValue value when value.GetValueKind() == JsonValueKind.String => [value.GetValue<string>()],
            JsonObject use when PropertyReader.IsString(use["preset"], out var preset) => [preset],
            JsonArray array => array.SelectMany(PresetNames),
            _ => [],
        };
    }
}
