using System;

using SkiaSharp;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal static class TransitionDrawer
{
    private const double CenterX = Scene.Width / 2.0;
    private const double CenterY = Scene.Height / 2.0;
    private const double SwitchLineFrom = 0.15;
    private const double SwitchLineSpan = 0.25;
    private const double SwitchPictureFrom = 0.4;
    private const double SwitchPictureSpan = 0.6;

    private const double SwitchDotRadius = 80;
    private const float SwitchDotCore = 0.25f;
    private const byte SwitchDotCoreAlpha = 179;
    private const double SwitchLineStartWidth = 30;
    private const double SwitchLineGrowth = 1890;
    private const double SwitchLineHeight = 10;
    private const double SwitchGlowHeight = 80;
    private const byte SwitchGlowAlpha = 115;
    private const double SwitchMinPictureScale = 0.006;
    private const double SwitchWashPower = 0.6;

    private const double PageShadowWidth = 180;
    private const double PageShadowMinAlpha = 0.05;
    private const double PageShadeAlphaRange = 0.32;
    private const double PageShadowLead = 1.2;
    private const double PageFoldMinWidth = 120;
    private const double PageFoldGrowth = 260;
    private const double PageFoldMinAlpha = 0.18;
    private const double PageEdgeWidth = 3;
    private const double PageEdgeAlpha = 0.8;

    private static readonly SKColor _switchGlow = new(0xFF, 0xFA, 0xEE);
    private static readonly SKColor _pageShade = new(0x4B, 0x1D, 0x5E);
    private static readonly SKColor _pageEdge = new(0xFF, 0xFD, 0xF8);

    public static void DrawBetween(Painter painter, Transition transition, Slide outgoing, double slideTime, Slide incoming)
    {
        var canvas = painter.Canvas;
        var props = transition.Props;
        var start = outgoing.Duration - transition.LeadTime;
        var progress = Math.Clamp((slideTime - start) / transition.Duration, 0, 1);
        var eased = transition.Ease(progress);
        var incomingTime = transition.Running ? slideTime - outgoing.Duration : 0;
        switch (transition.Type)
        {
            case Transition.LayerType:
                outgoing.Draw(painter, slideTime);
                transition.DrawLayer(painter, slideTime - start);
                break;
            case "crossfade":
                DrawFaded(painter, props.Number("fadeOut", slideTime) != 0 ? 1 - eased : 1, 0, () => outgoing.Draw(painter, slideTime));
                DrawFaded(painter, eased, props.Number("rise", slideTime) * (1 - eased), () => incoming.Draw(painter, incomingTime));
                break;
            case "push":
                {
                    var (directionX, directionY) = Direction(props.Text("direction"));
                    canvas.Save();
                    canvas.Translate((float)(-directionX * Scene.Width * eased), (float)(-directionY * Scene.Height * eased));
                    outgoing.Draw(painter, slideTime);
                    canvas.Restore();
                    canvas.Save();
                    canvas.Translate((float)(directionX * Scene.Width * (1 - eased)), (float)(directionY * Scene.Height * (1 - eased)));
                    incoming.Draw(painter, incomingTime);
                    canvas.Restore();
                    break;
                }

            case "wipe":
                {
                    outgoing.Draw(painter, slideTime);
                    var (directionX, directionY) = Direction(props.Text("direction"));
                    var revealed = directionX != 0
                        ? SKRect.Create((float)(directionX > 0 ? Scene.Width * (1 - eased) : 0), 0, (float)(Scene.Width * eased), Scene.Height)
                        : SKRect.Create(0, (float)(directionY > 0 ? Scene.Height * (1 - eased) : 0), Scene.Width, (float)(Scene.Height * eased));
                    canvas.Save();
                    canvas.ClipRect(revealed, SKClipOperation.Intersect, antialias: true);
                    incoming.Draw(painter, incomingTime);
                    canvas.Restore();
                    break;
                }

            case "iris":
                outgoing.Draw(painter, slideTime);
                DrawIris(painter, transition, slideTime, eased, () => incoming.Draw(painter, incomingTime));
                break;
            case "switch":
                DrawSwitch(painter, transition, slideTime, Math.Clamp(1 - (2 * progress), 0, 1), () => outgoing.Draw(painter, slideTime));
                DrawSwitch(painter, transition, slideTime, Math.Clamp((2 * progress) - 1, 0, 1), () => incoming.Draw(painter, incomingTime));
                break;
            case "page-turn":
                incoming.Draw(painter, incomingTime);
                DrawPageTurn(painter, transition, slideTime, eased, () => outgoing.Draw(painter, slideTime, clip: false));
                break;
            default:
                outgoing.Draw(painter, slideTime);
                break;
        }
    }

    public static void DrawEdge(
        Painter painter,
        Transition? transition,
        double videoTime,
        double videoDuration,
        bool opening,
        Action drawContent)
    {
        if (transition is null)
        {
            drawContent();
            return;
        }

        var start = opening ? 0 : videoDuration - transition.End - transition.Duration;
        var elapsed = Math.Clamp((videoTime - start) / transition.Duration, 0, 1);
        var progress = opening ? transition.Ease(elapsed) : 1 - transition.Ease(elapsed);
        if (progress >= 1)
        {
            drawContent();
            return;
        }

        switch (transition.Type)
        {
            case "iris":
                DrawIris(painter, transition, videoTime, progress, drawContent);
                break;
            case "switch":
                DrawSwitch(painter, transition, videoTime, progress, drawContent);
                break;
            default:
                drawContent();
                var color = transition.Props.Paint("color", videoTime)?.Color ?? SKColors.Black;
                var cover = (byte)Rounding.HalfUp(byte.MaxValue * (1 - progress));
                painter.Canvas.DrawRect(Frame(), painter.Fill(new Paint(color.WithAlpha((byte)(cover * color.Alpha / byte.MaxValue)))));
                break;
        }
    }

    private static SKRect Frame()
    {
        return SKRect.Create(Scene.Width, Scene.Height);
    }

    private static (double X, double Y) Direction(string? direction)
    {
        return direction switch
        {
            "right" => (-1, 0),
            "up" => (0, 1),
            "down" => (0, -1),
            _ => (1, 0),
        };
    }

    private static void DrawFaded(Painter painter, double alpha, double offsetY, Action drawSlide)
    {
        if (alpha <= 0)
        {
            return;
        }

        var canvas = painter.Canvas;
        using var layer = new SKPaint { ColorF = new SKColorF(0, 0, 0, (float)Math.Min(1, alpha)) };
        canvas.SaveLayer(layer);
        canvas.Translate(0, (float)offsetY);
        drawSlide();
        canvas.Restore();
    }

    private static void DrawIris(Painter painter, Transition transition, double time, double progress, Action drawContent)
    {
        var props = transition.Props;
        var radius = props.Number("radius", time, Math.Sqrt((Scene.Width * Scene.Width) + (Scene.Height * Scene.Height)) / 2) * progress;
        if (radius <= 0)
        {
            return;
        }

        using var circle = new SKPath();
        circle.Circle(props.Number("x", time, CenterX), props.Number("y", time, CenterY), radius);
        painter.Canvas.Save();
        painter.Canvas.ClipPath(circle, SKClipOperation.Intersect, antialias: true);
        drawContent();
        painter.Canvas.Restore();
    }

    private static void DrawSwitch(Painter painter, Transition transition, double time, double progress, Action drawContent)
    {
        if (progress <= 0)
        {
            return;
        }

        var canvas = painter.Canvas;
        var glow = transition.Props.Paint("color", time)?.Color ?? _switchGlow;
        if (progress >= 1)
        {
            drawContent();
        }
        else if (progress > SwitchPictureFrom)
        {
            var opened = (progress - SwitchPictureFrom) / SwitchPictureSpan;
            var scaleY = opened < 0.5 ? 4 * opened * opened * opened : 1 - (Math.Pow((-2 * opened) + 2, 3) / 2);
            canvas.Save();
            canvas.Translate(0, (float)CenterY);
            canvas.Scale(1, (float)Math.Max(SwitchMinPictureScale, scaleY));
            canvas.Translate(0, (float)-CenterY);
            canvas.ClipRect(Frame());
            drawContent();
            var wash = (byte)Math.Round(Math.Round(Math.Pow(1 - opened, SwitchWashPower), 3) * byte.MaxValue);
            canvas.DrawRect(Frame(), painter.Fill(new Paint(glow.WithAlpha(wash))));
            canvas.Restore();
        }
        else if (progress > SwitchLineFrom)
        {
            var widened = (progress - SwitchLineFrom) / SwitchLineSpan;
            var width = SwitchLineStartWidth + (SwitchLineGrowth * (1 - Math.Pow(1 - widened, 3)));
            var left = (float)(CenterX - (width / 2));
            var glowTop = (float)(CenterY - (SwitchGlowHeight / 2));
            var glowBottom = (float)(CenterY + (SwitchGlowHeight / 2));
            var stops = new (float, SKColor)[] { (0, glow.WithAlpha(0)), (0.5f, glow.WithAlpha(SwitchGlowAlpha)), (1, glow.WithAlpha(0)) };
            var band = new Gradient(false, [0, glowTop], [0, glowBottom], stops, 1, null);
            canvas.DrawRect(SKRect.Create(left, glowTop, (float)width, (float)SwitchGlowHeight), painter.Fill(new Paint(SKColors.Black, band)));
            var lineColor = transition.Props.Paint("lineColor", time)?.Color ?? _switchGlow;
            canvas.DrawRect(
                SKRect.Create(left, (float)(CenterY - (SwitchLineHeight / 2)), (float)width, (float)SwitchLineHeight),
                painter.Fill(new Paint(lineColor)));
        }
        else
        {
            var radius = SwitchDotRadius * progress / SwitchLineFrom;
            var stops = new (float, SKColor)[] { (0, glow), (SwitchDotCore, glow.WithAlpha(SwitchDotCoreAlpha)), (1, glow.WithAlpha(0)) };
            var dot = new Gradient(true, [(float)CenterX, (float)CenterY, 0], [(float)CenterX, (float)CenterY, (float)radius], stops, 1, null);
            using var disc = new SKPath();
            disc.Circle(CenterX, CenterY, radius);
            canvas.DrawPath(disc, painter.Fill(new Paint(SKColors.Black, dot)));
        }
    }

    private static void DrawPageTurn(Painter painter, Transition transition, double time, double eased, Action drawOutgoing)
    {
        var canvas = painter.Canvas;
        var shade = transition.Props.Paint("color", time)?.Color ?? _pageShade;
        var edge = transition.Props.Paint("edgeColor", time)?.Color ?? _pageEdge;
        var pageWidth = Scene.Width * (1 - eased);
        var shadowAlpha = (PageShadeAlphaRange * Math.Sin(Math.PI * Math.Min(1, PageShadowLead * eased))) + PageShadowMinAlpha;
        var shadow = new Gradient(
            false,
            [(float)pageWidth, 0],
            [(float)(pageWidth + PageShadowWidth), 0],
            [(0, shade.WithAlpha(AlphaByte(shadowAlpha))), (1, shade.WithAlpha(0))],
            1,
            null);
        canvas.DrawRect(SKRect.Create((float)pageWidth, 0, (float)PageShadowWidth, Scene.Height), painter.Fill(new Paint(SKColors.Black, shadow)));
        if (pageWidth < 1)
        {
            return;
        }

        canvas.Save();
        canvas.Scale((float)(1 - eased), 1);
        drawOutgoing();
        canvas.Restore();
        var foldWidth = Math.Min(pageWidth, PageFoldMinWidth + (PageFoldGrowth * eased));
        var foldAlpha = PageFoldMinAlpha + (PageShadeAlphaRange * Math.Sin(Math.PI * eased));
        var fold = new Gradient(
            false,
            [(float)(pageWidth - foldWidth), 0],
            [(float)pageWidth, 0],
            [(0, shade.WithAlpha(0)), (1, shade.WithAlpha(AlphaByte(foldAlpha)))],
            1,
            null);
        canvas.DrawRect(SKRect.Create((float)(pageWidth - foldWidth), 0, (float)foldWidth, Scene.Height), painter.Fill(new Paint(SKColors.Black, fold)));
        canvas.DrawRect(
            SKRect.Create((float)(pageWidth - PageEdgeWidth), 0, (float)PageEdgeWidth, Scene.Height),
            painter.Fill(new Paint(edge), PageEdgeAlpha));
    }

    private static byte AlphaByte(double alpha)
    {
        return (byte)Math.Round(Math.Clamp(alpha, 0, 1) * byte.MaxValue);
    }
}
