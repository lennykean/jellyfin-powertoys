using System.Collections.Generic;

using SkiaSharp;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed class Slide
{
    public required string Id { get; init; }

    public double Start { get; init; }

    public double Duration { get; init; }

    public required IReadOnlyList<string> Featured { get; init; }

    public required IReadOnlyList<Node> Nodes { get; init; }

    public Paint? Background { get; init; }
    public Transition? TransitionIn { get; set; }

    public void Draw(Painter painter, double slideTime, bool clip = true)
    {
        var canvas = painter.Canvas;
        canvas.Save();
        if (clip)
        {
            canvas.ClipRect(SKRect.Create(Scene.Width, Scene.Height));
        }

        if (Background is { } background)
        {
            canvas.DrawRect(SKRect.Create(Scene.Width, Scene.Height), painter.Fill(background));
        }

        foreach (var node in Nodes)
        {
            node.Draw(painter, slideTime);
        }

        canvas.Restore();
    }
}
