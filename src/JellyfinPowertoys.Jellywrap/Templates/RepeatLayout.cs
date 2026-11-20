using System;
using System.Collections.Generic;
using System.Linq;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal static class RepeatLayout
{
    private const double CeilingTolerance = 1e-9;

    public static IReadOnlyList<(double X, double Y, double Scale)> Place(Props props, int count)
    {
        var layout = props.Text("layout") ?? "none";
        var align = props.Text("align") ?? (layout == "grid" ? "center-rows" : "center");
        var pitch = props.Lists.GetValueOrDefault("pitch") ?? [props.Number("pitch", 0), props.Number("pitch", 0)];
        switch (layout)
        {
            case "row" or "column":
                {
                    var step = pitch[0];
                    var offset = align == "start" ? 0 : (count - 1) * step / 2;
                    return Enumerable.Range(0, count)
                        .Select(i => layout == "row" ? ((i * step) - offset, 0.0, 1.0) : (0.0, (i * step) - offset, 1.0))
                        .ToList();
                }

            case "grid":
                return PlaceGrid(props, count, align, pitch);
            default:
                return Enumerable.Repeat((0.0, 0.0, 1.0), count).ToList();
        }
    }

    private static IReadOnlyList<(double X, double Y, double Scale)> PlaceGrid(Props props, int count, string align, double[] pitch)
    {
        var maxColumns = Math.Max(1, (int)props.Number("columns", 0, count));
        var columns = maxColumns;
        var rows = (int)Math.Ceiling((double)count / maxColumns);
        var scale = 1.0;
        var cell = props.Lists.GetValueOrDefault("cell") ?? pitch;
        switch (props.Text("fit"))
        {
            case "area":
                {
                    var wantedRows = props.Number("rows", 0, 1);
                    if (wantedRows <= 0)
                    {
                        throw new InvalidOperationException($"A grid's \"rows\" must be above 0, not {wantedRows}.");
                    }

                    scale = Math.Min(1, Math.Sqrt(maxColumns * wantedRows / count));
                    var perRow = (int)Math.Ceiling((maxColumns / scale) - CeilingTolerance);
                    rows = (int)Math.Ceiling((double)count / perRow);
                    columns = props.Number("balance", 0) != 0 ? (int)Math.Ceiling((double)count / rows) : perRow;
                    break;
                }

            case "box":
                {
                    var box = props.Lists.GetValueOrDefault("fitBox") ?? [Scene.Width, Scene.Height];
                    scale = 0;
                    for (var candidateRows = 1; candidateRows <= Math.Min(count, props.Number("maxRows", 0, count)); candidateRows++)
                    {
                        var candidateColumns = (int)Math.Ceiling((double)count / candidateRows);
                        var candidateScale = Math.Min(
                            1,
                            Math.Min(
                                box[0] / (((candidateColumns - 1) * pitch[0]) + cell[0]),
                                box[1] / (((candidateRows - 1) * pitch[1]) + cell[1])));
                        if (candidateScale > scale)
                        {
                            (scale, columns, rows) = (candidateScale, candidateColumns, candidateRows);
                        }
                    }

                    break;
                }
        }

        var pitchX = props.Number("spread", 0) != 0 ? pitch[0] * maxColumns / columns : pitch[0] * scale;
        var pitchY = pitch[1] * scale;
        return Enumerable.Range(0, count).Select(i =>
        {
            var (row, column) = (i / columns, i % columns);
            var inRow = row < rows - 1 ? columns : count - (columns * (rows - 1));
            var x = align switch
            {
                "start" => column * pitchX,
                "center" => (column - ((columns - 1) / 2.0)) * pitchX,
                _ => (column - ((inRow - 1) / 2.0)) * pitchX,
            };
            var y = align == "start" ? row * pitchY : (row - ((rows - 1) / 2.0)) * pitchY;
            return (x, y, scale);
        }).ToList();
    }
}
