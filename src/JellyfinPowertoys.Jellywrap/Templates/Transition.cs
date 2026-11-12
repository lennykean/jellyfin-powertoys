using System;
using System.Collections.Generic;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed class Transition
{
    public const string LayerType = "layer";

    public required string Type { get; init; }

    public double Duration { get; init; }

    public double Cut { get; init; }

    public required Func<double, double> Ease { get; init; }
    public bool Running { get; init; }
    public double End { get; init; }

    public required Props Props { get; init; }

    public List<Node> Layer { get; } = [];
    public double LeadTime => Type == LayerType ? Cut : Duration;

    public void DrawLayer(Painter painter, double layerTime)
    {
        foreach (var node in Layer)
        {
            node.Draw(painter, layerTime);
        }
    }
}
