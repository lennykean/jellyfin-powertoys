using System.Collections.Generic;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal sealed class GroupNode : Node
{
    public List<Node> Children { get; } = [];

    protected override bool Translates => true;

    protected override void DrawSelf(Painter painter, double slideTime, double x, double y)
    {
        foreach (var child in Children)
        {
            child.Draw(painter, slideTime);
        }
    }
}
