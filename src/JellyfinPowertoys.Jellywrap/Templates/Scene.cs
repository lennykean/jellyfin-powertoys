using System;
using System.Collections.Generic;
using System.Linq;

using SkiaSharp;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed class Scene : IDisposable
{
    public const int Width = 1920;
    public const int Height = 1080;

    public required IReadOnlyList<Slide> Slides { get; init; }

    public required TextShaper Shaper { get; init; }

    public required ImageStore Images { get; init; }
    public required IReadOnlyList<SKPath> ShapePaths { get; init; }
    public List<Node> Back { get; } = [];
    public List<Node> Front { get; } = [];

    public SKColor Background { get; init; }

    public Transition? Open { get; set; }

    public Transition? Close { get; set; }

    public required string CardSlide { get; init; }

    public required double CardTime { get; init; }

    public double Duration => Slides.Count == 0 ? 0 : Slides[^1].Start + Slides[^1].Duration;
    public double CardAt => (Slides.FirstOrDefault(slide => slide.Id == CardSlide)?.Start ?? 0) + CardTime;
    public void Draw(Painter painter, double videoTime)
    {
        videoTime = Math.Clamp(videoTime, 0, Duration);
        painter.Canvas.Clear(Background);
        painter.Alpha = 1;
        TransitionDrawer.DrawEdge(
            painter,
            Open,
            videoTime,
            Duration,
            opening: true,
            () => TransitionDrawer.DrawEdge(painter, Close, videoTime, Duration, opening: false, () => DrawContent(painter, videoTime)));
    }

    public void Dispose()
    {
        Shaper.Dispose();
        Images.Dispose();
        foreach (var path in ShapePaths)
        {
            path.Dispose();
        }
    }

    private void DrawContent(Painter painter, double videoTime)
    {
        foreach (var node in Back)
        {
            node.Draw(painter, videoTime);
        }

        if (Slides.Count > 0)
        {
            var index = Slides.Count - 1;
            while (index > 0 && videoTime < Slides[index].Start)
            {
                index--;
            }

            var slide = Slides[index];
            var slideTime = videoTime - slide.Start;
            var next = index + 1 < Slides.Count ? Slides[index + 1] : null;
            if (next?.TransitionIn is { } transitionOut && slideTime > slide.Duration - transitionOut.LeadTime)
            {
                TransitionDrawer.DrawBetween(painter, transitionOut, slide, slideTime, next);
            }
            else
            {
                slide.Draw(painter, slideTime);
                if (slide.TransitionIn is { Type: Transition.LayerType } layer && slideTime < layer.Duration - layer.Cut)
                {
                    layer.DrawLayer(painter, slideTime + layer.Cut);
                }
            }
        }

        foreach (var node in Front)
        {
            node.Draw(painter, videoTime);
        }
    }
}
