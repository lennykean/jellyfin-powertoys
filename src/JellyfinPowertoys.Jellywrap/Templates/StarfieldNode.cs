using System;

using SkiaSharp;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed class StarfieldNode(EffectParameters parameters) : Node
{
    public const int MaxCount = 10000;

    private const double Seed = 2026;
    private const uint RandomMultiplier = 1664525;
    private const uint RandomIncrement = 1013904223;
    private const double RandomRange = 4294967296.0;
    private const double HeightShare = 0.94;
    private const double WarmShare = 0.12;
    private const double Count = 160;
    private const double MinRadius = 1;
    private const double RadiusRange = 2.4;
    private const double PhaseRange = 6.283;
    private const double MinSpeed = 0.6;
    private const double SpeedRange = 1.6;
    private const double MinAlpha = 0.2;
    private const double AlphaRange = 0.8;

    private readonly Paint _cool = new Paint(parameters.Color("color", "#EEF0FF"));
    private readonly Paint _warm = new Paint(parameters.Color("warm", "#FFC857"));

    protected override void DrawSelf(Painter painter, double slideTime, double x, double y)
    {
        var seed = (uint)Props.Number("seed", slideTime, Seed);
        double NextRandom()
        {
            seed = unchecked((seed * RandomMultiplier) + RandomIncrement);
            return seed / RandomRange;
        }

        var width = Props.Number("width", slideTime, Scene.Width);
        var height = Props.Number("height", slideTime, Scene.Height) * Props.Number("heightShare", slideTime, HeightShare);
        var warmShare = Props.Number("warmShare", slideTime, WarmShare);
        var videoTime = slideTime + Props.SlideStart;
        var count = (int)Math.Min(Props.Number("count", slideTime, Count), MaxCount);
        for (var i = 0; i < count; i++)
        {
            var starX = NextRandom() * width;
            var starY = NextRandom() * height;
            var radius = MinRadius + (NextRandom() * RadiusRange);
            var phase = NextRandom() * PhaseRange;
            var speed = MinSpeed + (NextRandom() * SpeedRange);
            var isWarm = NextRandom() < warmShare;
            var twinkle = 0.5 + (0.5 * Math.Sin((videoTime * speed) + phase));
            using var star = new SKPath();
            star.Circle(starX, starY, radius);
            painter.Canvas.DrawPath(star, painter.Fill(isWarm ? _warm : _cool, MinAlpha + (AlphaRange * twinkle * twinkle)));
        }
    }
}
