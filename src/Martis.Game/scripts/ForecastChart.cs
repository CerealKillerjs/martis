using System;
using System.Collections.Generic;
using Godot;

namespace Martis.Game;

/// <summary>Small log-scale line chart of the laboratory forecast.</summary>
public partial class ForecastChart : Control
{
    private IReadOnlyList<double> _series = [];
    private bool _survived;

    public void Plot(IReadOnlyList<double> series, bool survived)
    {
        _series = series;
        _survived = survived;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = Size;
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0, 0, 0, 0.35f));
        for (var i = 1; i < 4; i++)
        {
            var y = size.Y * i / 4f;
            DrawLine(new Vector2(0, y), new Vector2(size.X, y), new Color(1, 1, 1, 0.06f));
        }

        if (_series.Count < 2)
        {
            DrawString(ThemeDB.FallbackFont, new Vector2(8, (size.Y / 2f) + 5f), "Sin pronóstico todavía", HorizontalAlignment.Left, -1, 13, new Color(1, 1, 1, 0.4f));
            return;
        }

        var min = double.MaxValue;
        var max = double.MinValue;
        foreach (var value in _series)
        {
            var log = Math.Log10(Math.Max(value, 1e-9));
            min = Math.Min(min, log);
            max = Math.Max(max, log);
        }

        if (max - min < 0.5)
        {
            min -= 0.25;
            max += 0.25;
        }

        var colour = _survived ? new Color(0.35f, 1f, 0.6f) : new Color(1f, 0.35f, 0.3f);
        var points = new Vector2[_series.Count];
        for (var i = 0; i < _series.Count; i++)
        {
            var log = Math.Log10(Math.Max(_series[i], 1e-9));
            points[i] = new Vector2(
                size.X * i / (_series.Count - 1),
                size.Y - 6f - (float)((log - min) / (max - min) * (size.Y - 12f)));
        }

        var fill = new Vector2[points.Length + 2];
        points.CopyTo(fill, 0);
        fill[^2] = new Vector2(size.X, size.Y);
        fill[^1] = new Vector2(0, size.Y);
        DrawColoredPolygon(fill, new Color(colour, 0.15f));
        DrawPolyline(points, colour, 2f, true);
        DrawCircle(points[^1], 4f, colour);
        var font = ThemeDB.FallbackFont;
        DrawString(font, new Vector2(6, 14), $"{Math.Pow(10, max):0.###e0} kg", HorizontalAlignment.Left, -1, 11, new Color(1, 1, 1, 0.55f));
        DrawString(font, new Vector2(6, size.Y - 4), $"{Math.Pow(10, min):0.###e0} kg", HorizontalAlignment.Left, -1, 11, new Color(1, 1, 1, 0.55f));
    }
}
