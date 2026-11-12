using System.Collections.Generic;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed record LaidOutText(double Size, IReadOnlyList<LaidOutLine> Lines);
