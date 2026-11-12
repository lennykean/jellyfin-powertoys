using System;
using System.Linq;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal static class RowLayout
{
    public static void Place(GroupNode group, FontStore fonts)
    {
        double Width(Node child)
        {
            return child switch
            {
                TextNode text => text.LaidOut!.Lines.Select(line => text.Measure(fonts, line.Text, text.LaidOut.Size)).DefaultIfEmpty().Max(),
                OdometerNode odometer => odometer.Width(fonts),
                _ => child.Props.Number("width", 0),
            };
        }

        double RowWidth()
        {
            return group.Children.Sum(child => Width(child) + child.Props.Number("gap", 0));
        }

        var rowWidth = RowWidth();
        var maxWidth = group.Props.Number("maxWidth", 0, double.PositiveInfinity);
        if (rowWidth > maxWidth)
        {
            var shrink = maxWidth / rowWidth;
            foreach (var child in group.Children)
            {
                child.Props.Numbers["gap"] = child.Props.Number("gap", 0) * shrink;
                switch (child)
                {
                    case TextNode text:
                        text.Fit = text.Fit with { Size = Math.Floor(text.Fit.Size * shrink) };
                        text.LaidOut = text.Layout(fonts, text.Text);
                        break;
                    case OdometerNode odometer:
                        odometer.Props.Numbers["size"] = Math.Floor(odometer.Props.Number("size", 0) * shrink);
                        break;
                }
            }

            rowWidth = RowWidth();
        }

        group.Props.Numbers["width"] = rowWidth;
        var x = -(Anchors.Align(group.Props.Text("align")) * rowWidth);
        foreach (var child in group.Children)
        {
            x += child.Props.Number("gap", 0);
            child.Props.Numbers["x"] = x;
            child.Props.Texts["align"] = "left";
            x += Width(child);
        }
    }
}
