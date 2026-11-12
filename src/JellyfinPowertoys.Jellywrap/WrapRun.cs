using System;
using System.Collections.Generic;

using JellyfinPowertoys.Jellywrap.Audio;
using JellyfinPowertoys.Jellywrap.Context;
using JellyfinPowertoys.Jellywrap.Templates;

namespace JellyfinPowertoys.Jellywrap;

internal sealed record WrapRun(
    int Year,
    WrapYear WrapYear,
    AudioPlanner AudioPlanner,
    TemplateCatalog TemplateCatalog,
    IReadOnlyDictionary<Guid, string> TemplateChoices,
    string? ServerTemplateId);
