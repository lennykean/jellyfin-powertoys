using System.Collections.Generic;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed record LaidOutLine(string Text, double Y, IReadOnlyList<(string Word, double X)>? Words = null, double? Width = null);
