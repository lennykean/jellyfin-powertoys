using System;
using System.Collections.Generic;

namespace JellyfinPowertoys.Jellywrap.Context;

internal sealed record SourceUser(
    Guid Id,
    string Name,
    int? MaxParentalRating,
    bool? IsKidOverride,
    IReadOnlySet<Guid>? Visible,
    ImageRef? Avatar);
