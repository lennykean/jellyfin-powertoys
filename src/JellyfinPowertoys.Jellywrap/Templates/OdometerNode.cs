using System;
using System.Globalization;
using System.Linq;

using SkiaSharp;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed class OdometerNode : Node
{
    private const int DecimalBase = 10;
    private const string DecimalPoint = ".";
    private const double WindowMarginX = 20;
    private const double WindowAscent = 0.92;
    private const double WindowHeight = 1.02;
    private const double RollPitch = 1.1;

    public required string Font { get; init; }

    public double Value { get; init; }
    public double Width(FontStore fonts)
    {
        return Widths(fonts, 0).Total;
    }

    protected override void DrawSelf(Painter painter, double slideTime, double x, double y)
    {
        var size = Props.Number("size", slideTime);
        var decimals = (int)Props.Number("decimals", slideTime);
        var places = Digits(decimals).Length;
        var (digitWidth, pointWidth, totalWidth) = Widths(painter.Fonts, slideTime);
        var left = x - (Anchors.Align(Props.Text("align")) * totalWidth);
        var color = Props.Paint("color", slideTime) ?? new Paint(SKColors.Black);
        var canvas = painter.Canvas;
        if (decimals > 0)
        {
            DrawGlyph(painter, DecimalPoint, left + totalWidth - (digitWidth * decimals) - pointWidth, y, size, color, center: false);
        }

        canvas.Save();
        canvas.ClipRect(SKRect.Create(
            (float)(left - WindowMarginX),
            (float)(y - (WindowAscent * size)),
            (float)(totalWidth + (2 * WindowMarginX)),
            (float)(WindowHeight * size)));
        var pitch = RollPitch * size;
        var finalValue = Rounding.HalfUp(Value * Math.Pow(DecimalBase, decimals));
        var current = finalValue * Math.Clamp(Props.Number("count", slideTime, 1), 0, 1);
        var roll = 0.0;
        for (var place = 0; place < places; place++)
        {
            var shifted = current / Math.Pow(DecimalBase, place);
            var digit = (int)(Math.Floor(shifted) % DecimalBase);
            roll = place == 0
                ? shifted - Math.Floor(shifted)
                : Math.Floor(current / Math.Pow(DecimalBase, place - 1)) % DecimalBase == DecimalBase - 1 ? roll : 0;
            var centerX = left + totalWidth - (digitWidth * (place + 0.5)) - (place >= decimals ? pointWidth : 0);
            DrawGlyph(painter, $"{digit}", centerX, y - (roll * pitch), size, color, center: true);
            if (roll > 0)
            {
                DrawGlyph(painter, $"{(digit + 1) % DecimalBase}", centerX, y - (roll * pitch) + pitch, size, color, center: true);
            }
        }

        canvas.Restore();
    }

    private string Digits(int decimals)
    {
        return ((long)Rounding.HalfUp(Value * Math.Pow(DecimalBase, decimals))).ToString(CultureInfo.InvariantCulture).PadLeft(decimals + 1, '0');
    }

    private (float Digit, float Point, float Total) Widths(FontStore fonts, double slideTime)
    {
        var size = Props.Number("size", slideTime);
        var decimals = (int)Props.Number("decimals", slideTime);
        var digitWidth = Enumerable.Range(0, DecimalBase).Max(digit => fonts.Shape(Font, size, 0, $"{digit}").Width);
        var pointWidth = decimals > 0 ? fonts.Shape(Font, size, 0, DecimalPoint).Width : 0;
        return (digitWidth, pointWidth, (digitWidth * Digits(decimals).Length) + pointWidth);
    }

    private void DrawGlyph(Painter painter, string glyph, double x, double y, double size, Paint color, bool center)
    {
        var shaped = painter.Fonts.Shape(Font, size, 0, glyph);
        if (shaped.Blob is not null)
        {
            painter.Canvas.DrawText(shaped.Blob, (float)(center ? x - (shaped.Width / 2) : x), (float)y, painter.Fill(color));
        }
    }
}
