using System;
using System.Collections.Generic;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal static class Effects
{
    private static readonly Dictionary<string, Func<EffectParameters, string, Props, Node>> _all = new()
    {
        ["curtains"] = (parameters, id, props) => new CurtainsNode(parameters) { Id = id, Props = props },
        ["bulbs"] = (parameters, id, props) => new BulbsNode(parameters) { Id = id, Props = props },
        ["wood"] = (parameters, id, props) => new WoodNode(parameters) { Id = id, Props = props },
        ["starfield"] = (parameters, id, props) => new StarfieldNode(parameters) { Id = id, Props = props },
    };

    public static bool Exists(string name)
    {
        return _all.ContainsKey(name);
    }

    public static Node Build(string name, EffectParameters parameters, string id, Props props)
    {
        return _all.TryGetValue(name, out var build) ? build(parameters, id, props) : throw new InvalidOperationException($"There is no effect \"{name}\".");
    }
}
