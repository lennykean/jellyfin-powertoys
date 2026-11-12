using System;
using System.Linq;

using SkiaSharp;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed class CurtainsNode(EffectParameters parameters) : Node
{
    private const double MinWidth = 0.5;
    private const double Span = 960;
    private const double MaxWidth = 962;
    private const double Pleats = 8;
    private const double EdgeWidth = 8;
    private const double HemFromBottom = 40;
    private const double HemHeight = 12;
    private const double ValanceDrop = 300;
    private const double ValanceRise = 170;
    private const double ValanceHeight = 104;
    private const double ValanceTrimInset = 6;
    private const double ValanceTrimHeight = 10;
    private const int ScallopCount = 12;
    private const double ScallopWidth = 160;
    private const double ScallopDepth = 42;
    private const double ScallopStrokeWidth = 6;
    private const double TasselDrop = 24;
    private const double TasselRadius = 11;

    private readonly (float Offset, SKColor Color)[] _foldStops = FoldStops(parameters);
    private readonly SKColor _foldEnd = parameters.Color("foldEnd", "#3A0812");
    private readonly SKColor _trim = parameters.Color("trim", "#E3B04B");
    private readonly SKColor _valance = parameters.Color("valance", "#5A0F1B");

    protected override void DrawSelf(Painter painter, double slideTime, double x, double y)
    {
        var width = Props.Number("width", slideTime);
        if (width < MinWidth)
        {
            return;
        }

        var frameWidth = Props.Number("frameWidth", slideTime, Scene.Width);
        var frameHeight = Props.Number("frameHeight", slideTime, Scene.Height);
        var span = Props.Number("span", slideTime, Span);
        width = Math.Min(width, Props.Number("maxWidth", slideTime, MaxWidth));
        var pleats = (int)Props.Number("pleats", slideTime, Pleats);
        var canvas = painter.Canvas;
        var left = new Gradient(false, [(float)width, 0], [(float)(width - span), 0], _foldStops, pleats, _foldEnd);
        var right = new Gradient(false, [(float)(frameWidth - width), 0], [(float)(frameWidth - width + span), 0], _foldStops, pleats, _foldEnd);
        canvas.DrawRect(SKRect.Create(0, 0, (float)width, (float)frameHeight), painter.Fill(new Paint(SKColors.Black, left)));
        canvas.DrawRect(SKRect.Create((float)(frameWidth - width), 0, (float)width, (float)frameHeight), painter.Fill(new Paint(SKColors.Black, right)));
        painter.Rect(_trim, 0, frameHeight - HemFromBottom, width, HemHeight);
        painter.Rect(_trim, frameWidth - width, frameHeight - HemFromBottom, width, HemHeight);
        painter.Rect(_trim, width - EdgeWidth, 0, EdgeWidth, frameHeight);
        painter.Rect(_trim, frameWidth - width, 0, EdgeWidth, frameHeight);
        var closed = Math.Clamp(width / Props.Number("valanceDrop", slideTime, ValanceDrop), 0, 1);
        var valanceY = -ValanceRise * Math.Pow(1 - closed, 3);
        painter.Rect(_valance, 0, valanceY, frameWidth, ValanceHeight);
        for (var i = 0; i < ScallopCount; i++)
        {
            using var scallop = new SKPath();
            scallop.Arc((ScallopWidth / 2) + (i * ScallopWidth), valanceY + ValanceHeight, ScallopWidth / 2, ScallopDepth, 0, Degrees.FullTurn / 2);
            canvas.DrawPath(scallop, painter.Fill(new Paint(_valance)));
            canvas.DrawPath(scallop, painter.Stroke(new Paint(_trim), ScallopStrokeWidth));
        }

        painter.Rect(_trim, 0, valanceY + (ValanceHeight - ValanceTrimInset), frameWidth, ValanceTrimHeight);
        for (var i = 0; i <= ScallopCount; i++)
        {
            using var tassel = new SKPath();
            tassel.Circle(i * ScallopWidth, valanceY + (ValanceHeight + TasselDrop), TasselRadius);
            canvas.DrawPath(tassel, painter.Fill(new Paint(_trim)));
        }
    }

    private static (float Offset, SKColor Color)[] FoldStops(EffectParameters parameters)
    {
        var folds = parameters.Colors("folds", "#2B0710", "#7A1626", "#4A0B17", "#8A1C2D", "#4A0B17", "#6E1322");
        return [.. folds.Select((color, index) => ((float)index / folds.Count, color))];
    }
}
