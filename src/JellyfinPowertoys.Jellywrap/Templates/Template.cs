using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace JellyfinPowertoys.Jellywrap.Templates;

public sealed class Template
{
    internal const string IndividualKind = "individual";
    internal const string ServerKind = "server";
    internal const string GeneralAudience = "general";
    internal const string KidsAudience = "kids";
    internal const string BaseAudience = "base";
    internal const string PackagePrefix = "pkg:";

    private readonly Dictionary<string, JsonObject> _merged;
    private readonly Func<string, byte[]?> _readFile;

    internal Template(
        string id,
        string name,
        string audience,
        Dictionary<string, JsonObject> merged,
        IReadOnlyDictionary<string, string> bases,
        IReadOnlyList<string> errors,
        Func<string, byte[]?> readFile)
    {
        Id = id;
        Name = name;
        Audience = audience;
        _merged = merged;
        Bases = bases;
        Errors = errors;
        _readFile = readFile;
    }

    public string Id { get; }

    public string Name { get; }

    public IReadOnlyList<string> Errors { get; }
    internal string Audience { get; }
    internal IReadOnlyDictionary<string, string> Bases { get; }

    internal bool IsValid => Errors.Count == 0 && Audience != BaseAudience;
    internal JsonObject? MergedFor(string kind)
    {
        return _merged.GetValueOrDefault(kind);
    }

    internal byte[]? ReadFile(string key)
    {
        return _readFile(key);
    }
}
