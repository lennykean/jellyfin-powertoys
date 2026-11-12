using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

using SkiaSharp;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed class ElementBuilder(
    JsonNode? fallbacks,
    FontStore fonts,
    ImageStore images,
    Palette palette,
    PropsBuilder propsBuilder,
    TrackBuilder trackBuilder,
    List<SKPath> shapePaths)
{
    private const string DefaultType = "group";
    private const string DefaultFont = "body";
    private const double DefaultWordStagger = 0.1;

    private static readonly string[] _boxProperties = ["x", "y", "width", "height"];

    private static readonly Dictionary<string, Func<ElementBuilder, JsonObject, Placement, string, Node>> _builders = new()
    {
        ["text"] = (builder, definition, placement, id) => builder.BuildText(definition, placement, id),
        ["image"] = (builder, definition, placement, id) => builder.BuildImage(definition, placement, id),
        ["shape"] = (builder, definition, placement, id) => builder.BuildShape(definition, placement, id, new Props()),
        ["group"] = (builder, definition, placement, id) => builder.BuildGroup(definition, placement, id),
        ["repeat"] = (builder, definition, placement, id) => builder.BuildRepeat(definition, placement, id),
        ["pie"] = (builder, definition, placement, id) => builder.BuildPie(definition, placement, id),
        ["odometer"] = (builder, definition, placement, id) => builder.BuildOdometer(definition, placement, id),
        ["effect"] = (builder, definition, placement, id) => builder.BuildEffect(definition, placement, id),
    };
    public static bool HasType(string type)
    {
        return _builders.ContainsKey(type);
    }

    public List<Node> Build(IEnumerable<JsonNode?> definitions, Placement placement)
    {
        var siblings = definitions.OfType<JsonObject>().ToList();
        var measure = new SiblingMeasure(siblings, fonts, BuildElement);
        var outerMeasure = placement.Scope.Get("measure") as Func<string, object?>;
        Scope scope = null!;
        object? MeasureById(string id)
        {
            return measure.Has(id) ? measure.Measure(id, placement with { Scope = scope }) : outerMeasure?.Invoke(id);
        }

        scope = placement.Scope.With("measure", (Func<string, object?>)MeasureById);
        var inner = placement with { Scope = scope };
        return [.. siblings.Select(definition => BuildElement(definition, inner)).OfType<Node>()];
    }

    private Node? BuildElement(JsonObject definition, Placement placement)
    {
        if (!PropertyReader.ReadCondition(definition["when"], placement.Scope))
        {
            return null;
        }

        var type = definition["type"]?.GetValue<string>() ?? DefaultType;
        var id = definition["id"]?.GetValue<string>() ?? string.Empty;
        return _builders.TryGetValue(type, out var build)
            ? build(this, definition, placement, id)
            : throw new InvalidOperationException($"There is no element type \"{type}\".");
    }

    private GroupNode BuildGroup(JsonObject definition, Placement placement, string id)
    {
        var group = new GroupNode { Id = id, Props = propsBuilder.Build(definition, placement) };
        group.Children.AddRange(Build(definition["children"] as JsonArray ?? [], placement));
        group.Clip = BuildClip(definition["clip"], placement, null);
        if (group.Props.Text("layout") is "row")
        {
            RowLayout.Place(group, fonts);
        }

        return group;
    }

    private PieNode BuildPie(JsonObject definition, Placement placement, string id)
    {
        var scope = placement.Scope;
        return new PieNode
        {
            Id = id,
            Parts = [.. (definition["parts"] as JsonArray ?? [])
                .OfType<JsonObject>()
                .Select(part => (
                    Value: PropertyReader.ReadNumber(part["value"], scope) ?? 0,
                    Fill: palette.Paint(part["fill"], scope) ?? new Paint(SKColors.Transparent)))
                .Where(part => part.Value > 0)],
            Props = propsBuilder.Build(definition, placement),
        };
    }

    private OdometerNode BuildOdometer(JsonObject definition, Placement placement, string id)
    {
        return new OdometerNode
        {
            Id = id,
            Font = FontName(definition["font"] ?? throw new InvalidOperationException("An odometer needs a \"font\"."), placement.Scope),
            Value = PropertyReader.ReadNumber(definition["value"], placement.Scope) ?? 0,
            Props = propsBuilder.Build(definition, placement),
        };
    }

    private Node BuildEffect(JsonObject definition, Placement placement, string id)
    {
        var name = definition["effect"]?.GetValue<string>() ?? throw new InvalidOperationException("An effect element needs an \"effect\".");
        var parameters = new EffectParameters(PropertyReader.ResolveBindings(definition, placement.Scope), palette);
        return Effects.Build(name, parameters, id, propsBuilder.Build(definition, placement));
    }

    private ShapeNode BuildShape(JsonObject definition, Placement placement, string id, Props props)
    {
        var scope = placement.Scope;
        var path = definition["d"] is { } pathData ? SKPath.ParseSvgPathData(PropertyReader.ReadText(pathData, scope)) : null;
        if (path is not null)
        {
            shapePaths.Add(path);
        }

        propsBuilder.Fill(props, definition, placement);
        if (definition["points"] is JsonArray points)
        {
            props.Lists["points"] = PropertyReader.ReadNumbers(points, scope);
        }
        else if (PropertyReader.ReadNumber(definition["points"], scope) is { } pointCount)
        {
            props.Numbers["points"] = pointCount;
        }

        return new ShapeNode { Id = id, Kind = definition["shape"]?.GetValue<string>() ?? "rect", SvgPath = path, Props = props };
    }

    private TextNode BuildText(JsonObject definition, Placement placement, string id)
    {
        var scope = placement.Scope;
        var written = definition["text"]?.GetValue<string>() ?? string.Empty;
        var template = TextTemplate.HasHoles(written) ? TextTemplate.Parse(written) : null;
        var value = PropertyReader.ReadNumber(definition["value"], scope) ?? 0;
        var caseMode = definition["case"]?.GetValue<string>();
        var shown = TextCase.Apply(template?.Render(scope.With("value", value)) ?? written, caseMode ?? string.Empty);
        var props = propsBuilder.Build(definition, placement);
        var originY = props.Numbers.TryGetValue("y", out var y) && !double.IsNaN(y) ? y : 0;
        var text = new TextNode
        {
            Id = id,
            Props = props,
            Font = definition["font"] is { } font ? FontName(font, scope) : DefaultFont,
            Text = shown,
            Template = definition["value"] is not null ? template : null,
            Scope = scope,
            Value = value,
            FitOnFinal = definition["fitOn"]?.GetValue<string>() == "final",
            Case = caseMode,
            Lead = definition["lead"] is JsonObject lead && palette.Paint(lead["color"], scope) is { } leadPaint ? (lead["text"]!.GetValue<string>(), leadPaint) : null,
            Fit = TextFit.FromProps(props, definition["single"] as JsonObject, scope, originY),
        };
        text.LaidOut = text.Layout(fonts, text.Text);
        if (definition["words"] is JsonObject words)
        {
            text.Words = BuildWords(text, words, placement);
        }

        return text;
    }

    private string FontName(JsonNode font, Scope scope)
    {
        var name = PropertyReader.ReadText(font, scope);
        return fonts.Has(name) ? name : throw new InvalidOperationException($"There is no font \"{name}\".");
    }

    private List<Props> BuildWords(TextNode text, JsonObject definition, Placement placement)
    {
        var stagger = PropertyReader.ReadNumber(definition["stagger"], placement.Scope) ?? DefaultWordStagger;
        var start = placement.Shift + text.Props.Number("delay", 0);
        var count = text.LaidOut!.Lines.Sum(line => line.Words?.Count ?? line.Text.Split(' ').Length);
        var words = new List<Props>();
        for (var i = 0; i < count; i++)
        {
            var word = new Props { SlideStart = placement.Start };
            if (PropertyReader.ReadNumber(definition["pivotY"], placement.Scope) is { } pivotY)
            {
                word.Numbers["pivotY"] = pivotY;
            }

            trackBuilder.AddTracks(word, definition["in"], placement, start + (i * stagger), fromEnd: false);
            words.Add(word);
        }

        return words;
    }

    private ImageNode BuildImage(JsonObject definition, Placement placement, string id)
    {
        var scope = placement.Scope;
        var key = PropertyReader.ReadValue(definition["src"], scope) switch
        {
            JsonObject image => image["path"]?.GetValue<string>(),
            string path => path,
            _ => null,
        };
        var isSvg = key is not null && key.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
        var decoded = isSvg ? null : images.Get(key);
        var art = isSvg ? images.Svg(key!, svg => TextTemplate.HasHoles(svg) ? TextTemplate.Parse(svg).Render(scope) : svg) : null;
        var props = propsBuilder.Build(definition, placement);
        var box = new JsonObject { ["width"] = props.Number("width", 0), ["height"] = props.Number("height", 0) };
        GroupNode? fallback = null;
        if (decoded is null && art is null && definition["fallback"] is { } fallbackDefinition)
        {
            var elements = fallbackDefinition is JsonValue name ? fallbacks?[name.GetValue<string>()] as JsonArray : fallbackDefinition as JsonArray;
            var title = definition["title"] is { } titleDefinition ? PropertyReader.ReadExpression(titleDefinition, scope) : null;
            fallback = new GroupNode { Id = $"{id}-fallback" };
            fallback.Children.AddRange(Build(elements ?? [], placement with { Scope = scope.With("box", box).With("title", title) }));
        }

        var radius = props.Number("radius", 0);
        var clip = definition["clip"] as JsonObject ?? (radius > 0 ? new JsonObject { ["shape"] = "roundRect", ["radius"] = radius } : null);
        return new ImageNode { Id = id, Props = props, Image = decoded, Art = art, Fallback = fallback, Shape = BuildClip(clip, placement, props) };
    }

    private ShapeNode? BuildClip(JsonNode? node, Placement placement, Props? box)
    {
        if (node is not JsonObject definition)
        {
            return null;
        }

        var props = new Props();
        if (box is not null)
        {
            foreach (var property in _boxProperties)
            {
                props.Numbers[property] = box.Numbers.GetValueOrDefault(property);
            }

            if (box.Text("anchor") is { } anchor)
            {
                props.Texts["anchor"] = anchor;
            }

            foreach (var property in _boxProperties.Where(box.Tracks.ContainsKey))
            {
                props.Tracks[property] = box.Tracks[property];
            }
        }

        return BuildShape(definition, placement, "clip", props);
    }

    private GroupNode BuildRepeat(JsonObject definition, Placement placement, string id)
    {
        var props = propsBuilder.Build(definition, placement);
        var items = RepeatItems(definition, props, placement.Scope);
        var group = new GroupNode { Id = id, Props = props };
        var slots = RepeatLayout.Place(props, items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            var scope = placement.Scope.With("item", items[i]).With("index", (double)i).With("count", (double)items.Count);
            var variables = new Dictionary<string, object?>();
            foreach (var (name, expression) in definition["vars"] as JsonObject ?? [])
            {
                variables[name] = PropertyReader.ReadExpression(expression, scope.With(variables));
            }

            scope = scope.With(variables);
            var stagger = PropertyReader.ReadNumber(definition["stagger"], scope) ?? 0;
            var cell = new GroupNode { Id = $"{id}[{i}]" };
            cell.Props.SlideStart = placement.Start;
            cell.Props.Numbers["x"] = slots[i].X;
            cell.Props.Numbers["y"] = slots[i].Y;
            cell.Props.Numbers["scale"] = slots[i].Scale;
            cell.Children.AddRange(Build(definition["template"] as JsonArray ?? [], placement with { Scope = scope, Shift = placement.Shift + (i * stagger) }));
            group.Children.Add(cell);
        }

        return group;
    }

    private static List<object?> RepeatItems(JsonObject definition, Props props, Scope scope)
    {
        List<object?> items;
        if (definition["items"] is JsonArray literal)
        {
            items = [.. literal.OfType<JsonObject>().Select(item => (object?)PropertyReader.ReadObject(item, scope))];
        }
        else if (definition["items"] is { } binding)
        {
            items = PropertyReader.ReadExpression(binding, scope) as List<object?> ?? [];
        }
        else
        {
            var count = Math.Max(0, (int)(PropertyReader.ReadNumber(definition["count"], scope) ?? 0));
            items = [.. Enumerable.Range(0, count).Select(index => (object?)(double)index)];
        }

        if (definition["where"] is { } where)
        {
            items = [.. items.Where((item, index) => PropertyReader.ReadCondition(where, scope.With("item", item).With("index", (double)index)))];
        }

        return props.Numbers.TryGetValue("max", out var max) ? [.. items.Take((int)max)] : items;
    }
}
