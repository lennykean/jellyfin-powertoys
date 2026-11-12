using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed class Scope
{
    public static readonly Scope Empty = new(null, new Dictionary<string, object?>(), null);

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

    public Scope With(IReadOnlyDictionary<string, object?> values)
    {
        return new Scope(this, values.ToDictionary(), _featured);
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
        if (_featured is not null && item?["id"]?.GetValue<string>() is { } id && item["kind"] is not null && !_featured.Contains(id))
        {
            _featured.Add(id);
        }
    }
}
