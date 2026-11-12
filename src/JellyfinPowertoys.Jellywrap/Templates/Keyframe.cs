using System;

using SkiaSharp;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal readonly record struct Keyframe(double Time, double Value, SKColor Color, Func<double, double> Ease);
