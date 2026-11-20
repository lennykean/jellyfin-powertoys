using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed class Scope
{
    public static readonly Scope Empty = new Scope(null, new Dictionary<string, object?>(), null);

    private readonly Scope? _parent;
    private readonly Dictionary<string, object?> _names;
    private readonly List<string>? _featured;

    private Scope(Scope? parent, Dictionary<string, object?> names, List<string>? featured)
    {
        _parent = parent;
        _names = names;
        _featured = featured;
    }

    public static Scope Of(JsonObject context, List<string>? featured)
    {
        return new Scope(null, context.ToDictionary(property => property.Key, property => BindingValue.FromNode(property.Value)), featured);
    }

    public Scope With(string name, object? value)
    {
        return new Scope(this, new Dictionary<string, object?> { [name] = value }, _featured);
    }

    public Scope With(Dictionary<string, object?> values)
    {
        return new Scope(this, values, _featured);
    }

    public Dictionary<string, object?> Variables(JsonObject? template, JsonObject? slide, CancellationToken cancellationToken)
    {
        var variables = new Dictionary<string, object?>();
        var scope = With(variables);
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var definitions in new[] { template?["vars"], slide?["vars"] }.OfType<JsonObject>())
        {
            foreach (var (name, expression) in definitions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                variables[name] = PropertyReader.ReadExpression(expression, scope);
            }
        }

        return variables;
    }

    public Scope Collecting(List<string>? featured)
    {
        return new Scope(this, [], featured);
    }

    public object? Get(string name)
    {
        for (var scope = this; scope is not null; scope = scope._parent)
        {
            if (scope._names.TryGetValue(name, out var value))
            {
                return value;
            }
        }

        return null;
    }

    public void Feature(JsonObject? item)
    {
        if (_featured is not null && PropertyReader.IsString(item?["id"], out var id) && item!["kind"] is not null && !_featured.Contains(id))
        {
            _featured.Add(id);
        }
    }
}
