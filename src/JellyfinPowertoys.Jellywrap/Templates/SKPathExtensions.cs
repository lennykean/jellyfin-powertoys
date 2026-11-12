using System;

using SkiaSharp;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal static class SKPathExtensions
{
    private const double FullTurnThreshold = 359.999;

    public static void Arc(
        this SKPath path,
        double centerX,
        double centerY,
        double radiusX,
        double radiusY,
        double startDegrees,
        double sweepDegrees,
        double rotation = 0)
    {
        var oval = new SKRect((float)(centerX - radiusX), (float)(centerY - radiusY), (float)(centerX + radiusX), (float)(centerY + radiusY));
        if (rotation != 0)
        {
            using var arc = new SKPath();
            arc.Arc(centerX, centerY, radiusX, radiusY, startDegrees, sweepDegrees);
            arc.Transform(SKMatrix.CreateRotationDegrees((float)rotation, (float)centerX, (float)centerY));
            path.AddPath(arc, SKPathAddMode.Extend);
            return;
        }

        if (Math.Abs(sweepDegrees) >= FullTurnThreshold)
        {
            var halfTurn = Math.Sign(sweepDegrees) * (Degrees.FullTurn / 2);
            path.ArcTo(oval, (float)startDegrees, (float)halfTurn, forceMoveTo: false);
            path.ArcTo(oval, (float)(startDegrees + halfTurn), (float)halfTurn, forceMoveTo: false);
        }
        else
        {
            path.ArcTo(oval, (float)startDegrees, (float)sweepDegrees, forceMoveTo: false);
        }
    }

    public static void Circle(this SKPath path, double centerX, double centerY, double radius)
    {
        path.Arc(centerX, centerY, radius, radius, 0, Degrees.FullTurn);
    }

    public static void RoundRect(this SKPath path, double x, double y, double width, double height, double radius)
    {
        radius = Math.Min(radius, Math.Min(width / 2, height / 2));
        path.MoveTo((float)(x + radius), (float)y);
        path.ArcTo((float)(x + width), (float)y, (float)(x + width), (float)(y + height), (float)radius);
        path.ArcTo((float)(x + width), (float)(y + height), (float)x, (float)(y + height), (float)radius);
        path.ArcTo((float)x, (float)(y + height), (float)x, (float)y, (float)radius);
        path.ArcTo((float)x, (float)y, (float)(x + width), (float)y, (float)radius);
        path.Close();
    }
}
