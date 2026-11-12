using System;
using System.Collections.Generic;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed record Filter(int MinArguments, int? MaxArguments, Func<object?, IReadOnlyList<object?>, object?> Apply);
