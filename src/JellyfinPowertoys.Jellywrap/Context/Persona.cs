namespace JellyfinPowertoys.Jellywrap.Context;

internal sealed record Persona(
    string Id,
    string Name,
    string OneLiner,
    double Score,
    string? RunnerUp,
    TitleRef? Subject);
