using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FAAH_Frontend.Models;

namespace FAAH_Frontend.Controls;

// Petit graphique sans bibliotheque supplementaire :
// une ligne = plus bas / plus haut, un rectangle = ouverture / cloture.
public class CandleChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<Candle>?> CandlesProperty =
        AvaloniaProperty.Register<CandleChart, IReadOnlyList<Candle>?>(nameof(Candles));

    static CandleChart() => AffectsRender<CandleChart>(CandlesProperty);
    public IReadOnlyList<Candle>? Candles
    {
        get => GetValue(CandlesProperty);
        set => SetValue(CandlesProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var candles = Candles;
        double width = Bounds.Width - 110, height = Bounds.Height - 55;
        if (width <= 0 || height <= 0 || candles is null || candles.Count == 0) return;

        double low = (double)candles.Min(c => c.Low);
        double high = (double)candles.Max(c => c.High);
        double padding = Math.Max((high - low) * 0.08, Math.Max(Math.Abs(high) * 0.001, 0.00000001));
        low -= padding;
        high += padding;
        double Y(double price) => 10 + (high - price) / (high - low) * height;

        // Axe des prix et lignes horizontales.
        for (int i = 0; i <= 4; i++)
        {
            double price = low + (high - low) * i / 4;
            double y = Y(price);
            context.DrawLine(new Pen(Brushes.LightGray, 1), new Point(5, y), new Point(width, y));
            Label(context, price.ToString("G8", CultureInfo.InvariantCulture), width + 8, y - 7);
        }

        double step = (width - 10) / candles.Count;
        double bodyWidth = Math.Max(1, Math.Min(14, step * 0.7));
        for (int i = 0; i < candles.Count; i++)
        {
            var candle = candles[i];
            double x = 5 + (i + 0.5) * step;
            var color = candle.Close >= candle.Open ? Brushes.SeaGreen : Brushes.IndianRed;
            context.DrawLine(new Pen(color, 1), new Point(x, Y((double)candle.High)), new Point(x, Y((double)candle.Low)));
            double top = Y((double)Math.Max(candle.Open, candle.Close));
            double bottom = Y((double)Math.Min(candle.Open, candle.Close));
            context.DrawRectangle(color, null, new Rect(x - bodyWidth / 2, top, bodyWidth, Math.Max(1, bottom - top)));
        }

        Label(context, candles[0].Timestamp.UtcDateTime.ToString("dd/MM HH:mm"), 5, height + 22);
        if (candles.Count > 1)
            Label(context, candles[^1].Timestamp.UtcDateTime.ToString("dd/MM HH:mm"), Math.Max(5, width - 95), height + 22);
    }

    private static void Label(DrawingContext context, string value, double x, double y)
    {
        var text = new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 11, Brushes.DimGray);
        context.DrawText(text, new Point(x, y));
    }
}
