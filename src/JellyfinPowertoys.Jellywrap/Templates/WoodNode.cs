using System;

using SkiaSharp;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed class WoodNode(EffectParameters parameters) : Node
{
    private const double GrainSpacing = 34;
    private const double GrainSegment = 60;
    private const double GrainWaveAlong = 0.013;
    private const double GrainWaveAcross = 0.7;
    private const double GrainSway = 7;
    private const double GrainStrokeWidth = 3;
    private const double SeamWidth = 6;

    private readonly SKColor _color = parameters.Color("color", "#3A2A1F");
    private readonly Paint _grain = new(parameters.Color("grain", "#4A3628"));
    private readonly SKColor _seam = parameters.Color("seam", "#2B1F16");

    protected override void DrawSelf(Painter painter, double slideTime, double x, double y)
    {
        var width = Props.Number("width", slideTime);
        var height = Props.Number("height", slideTime);
        var plankWidth = Props.Number("planks", slideTime);
        var canvas = painter.Canvas;
        canvas.Save();
        using (var clip = new SKPath())
        {
            clip.RoundRect(x, y, width, height, Props.Number("radius", slideTime));
            canvas.ClipPath(clip, SKClipOperation.Intersect, antialias: true);
        }

        painter.Rect(_color, x, y, width, height);
        for (var grainX = x + (GrainSpacing / 2); grainX < x + width; grainX += GrainSpacing)
        {
            using var line = new SKPath();
            line.MoveTo((float)grainX, (float)y);
            for (var grainY = y; grainY <= y + height + GrainSegment; grainY += GrainSegment)
            {
                line.LineTo((float)(grainX + (Math.Sin((grainY * GrainWaveAlong) + (grainX * GrainWaveAcross)) * GrainSway)), (float)grainY);
            }

            canvas.DrawPath(line, painter.Stroke(_grain, GrainStrokeWidth));
        }

        if (plankWidth > 0)
        {
            for (var seamX = x + plankWidth; seamX < x + width; seamX += plankWidth)
            {
                painter.Rect(_seam, seamX - (SeamWidth / 2), y, SeamWidth, height);
            }
        }

        canvas.Restore();
    }
}
