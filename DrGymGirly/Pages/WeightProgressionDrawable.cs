namespace DrGymGirly;

// Hand-drawn rather than a charting NuGet package - this project doesn't have
// one, and avoids betting on a third-party package's compatibility with this
// project's very recent net10.0 TFMs. One point per day (the day's heaviest
// set), oldest to newest left-to-right, with a dashed reference line at the
// first entry - that's "the default" everything else is judged against.
public sealed class WeightProgressionDrawable : IDrawable
{
    public List<(DateTime Date, double Weight)> Points { get; set; } = [];
    public Color LineColor { get; set; } = Colors.Orange;
    public Color BaselineColor { get; set; } = Colors.Gray;
    public Color TextColor { get; set; } = Colors.Black;

    private const float PaddingLeft = 46;
    private const float PaddingRight = 16;
    private const float PaddingTop = 28;
    private const float PaddingBottom = 24;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (Points.Count == 0) return;

        var plotWidth = dirtyRect.Width - PaddingLeft - PaddingRight;
        var plotHeight = dirtyRect.Height - PaddingTop - PaddingBottom;
        if (plotWidth <= 0 || plotHeight <= 0) return;

        var minWeight = Points.Min(p => p.Weight);
        var maxWeight = Points.Max(p => p.Weight);
        var range = Math.Max(maxWeight - minWeight, 1);
        var paddedMin = minWeight - range * 0.15;
        var paddedMax = maxWeight + range * 0.15;
        var paddedRange = paddedMax - paddedMin;

        float XFor(int index) => Points.Count == 1
            ? PaddingLeft + plotWidth / 2
            : PaddingLeft + (float)index / (Points.Count - 1) * plotWidth;

        float YFor(double weight) => PaddingTop + (float)((paddedMax - weight) / paddedRange) * plotHeight;

        // Baseline reference line at the first entry's weight.
        var baselineWeight = Points[0].Weight;
        var baselineY = YFor(baselineWeight);
        canvas.StrokeColor = BaselineColor;
        canvas.StrokeSize = 1;
        canvas.StrokeDashPattern = [4, 4];
        canvas.DrawLine(PaddingLeft, baselineY, dirtyRect.Width - PaddingRight, baselineY);
        canvas.StrokeDashPattern = null;

        canvas.FontSize = 10;
        canvas.FontColor = TextColor;
        canvas.DrawString($"Start: {baselineWeight:0.#}kg", PaddingLeft, baselineY - 16, plotWidth, 14,
            HorizontalAlignment.Left, VerticalAlignment.Bottom);

        // Y-axis min/max labels.
        canvas.DrawString($"{maxWeight:0.#}", 0, PaddingTop - 7, PaddingLeft - 6, 14, HorizontalAlignment.Right, VerticalAlignment.Center);
        canvas.DrawString($"{minWeight:0.#}", 0, PaddingTop + plotHeight - 7, PaddingLeft - 6, 14, HorizontalAlignment.Right, VerticalAlignment.Center);

        // The progression line itself.
        canvas.StrokeColor = LineColor;
        canvas.StrokeSize = 3;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeLineJoin = LineJoin.Round;

        var path = new PathF();
        for (var i = 0; i < Points.Count; i++)
        {
            var x = XFor(i);
            var y = YFor(Points[i].Weight);
            if (i == 0) path.MoveTo(x, y);
            else path.LineTo(x, y);
        }
        canvas.DrawPath(path);

        // Point markers - first (baseline) and last (current) stand out.
        for (var i = 0; i < Points.Count; i++)
        {
            var x = XFor(i);
            var y = YFor(Points[i].Weight);
            var isEndpoint = i == 0 || i == Points.Count - 1;

            canvas.FillColor = isEndpoint ? LineColor : Colors.White;
            canvas.FillCircle(x, y, isEndpoint ? 5 : 3.5f);
            canvas.StrokeColor = LineColor;
            canvas.StrokeSize = 1.5f;
            canvas.DrawCircle(x, y, isEndpoint ? 5 : 3.5f);
        }

        // Current value, called out above the last point.
        var last = Points[^1];
        var lastX = XFor(Points.Count - 1);
        var lastY = YFor(last.Weight);
        canvas.FontColor = LineColor;
        canvas.FontSize = 12;
        canvas.DrawString($"{last.Weight:0.#}kg", Math.Clamp(lastX - 40, 0, dirtyRect.Width - 80), lastY - 22, 80, 16,
            HorizontalAlignment.Center, VerticalAlignment.Bottom);

        // X-axis date labels for the first and last points.
        canvas.FontSize = 9;
        canvas.FontColor = TextColor;
        canvas.DrawString(Points[0].Date.ToString("d MMM"), PaddingLeft - 20, dirtyRect.Height - PaddingBottom + 6, 60, 14,
            HorizontalAlignment.Left, VerticalAlignment.Top);
        canvas.DrawString(last.Date.ToString("d MMM"), dirtyRect.Width - PaddingRight - 60, dirtyRect.Height - PaddingBottom + 6, 60, 14,
            HorizontalAlignment.Right, VerticalAlignment.Top);
    }
}
