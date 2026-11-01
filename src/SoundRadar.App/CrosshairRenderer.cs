using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SoundRadar.App;

/// <summary>Параметры рисования прицела — снимок настроек из <see cref="SoundRadar.Core.SoundRadarConfig"/>.</summary>
public sealed record CrosshairParams
{
    public string Style { get; init; } = "Cross";
    public double Length { get; init; } = 10;
    public double Thickness { get; init; } = 2;
    public double Gap { get; init; } = 3;
    public bool CenterDot { get; init; }
    public double DotSize { get; init; } = 2;
    /// <summary>Толщина чёрной обводки с каждой стороны; 0 — без обводки.</summary>
    public double OutlineThickness { get; init; } = 1;
    public Color Color { get; init; } = Color.FromRgb(0, 255, 0);
    public double Opacity { get; init; } = 1;
}

/// <summary>
/// Рисовка прицела в стиле Crosshair X / CS2: крест, точка, круг, диагональный X —
/// опционально с чёрной обводкой (тёмная подложка чуть толще цветной линии) и точкой в центре.
/// Один код для оверлея и предпросмотра в окне настроек.
/// </summary>
internal static class CrosshairRenderer
{
    /// <param name="pixelScale">Масштаб DIP→физические пиксели монитора
    /// (VisualTreeHelper.GetDpi().PixelsPerDip): «10 px» = 10 физических пикселей
    /// на любом масштабе монитора, как в превью.</param>
    public static void Draw(Canvas canvas, double w, double h, CrosshairParams p, double pixelScale = 1.0)
    {
        var s = pixelScale > 0 ? pixelScale : 1.0;
        var cx = Math.Round(w / 2);
        var cy = Math.Round(h / 2);

        var color = new SolidColorBrush(p.Color);
        color.Freeze();
        var black = Brushes.Black;

        var opacity = Math.Clamp(p.Opacity, 0.05, 1.0);
        var t = Math.Clamp(p.Thickness, 1, 20) / s;
        var len = Math.Clamp(p.Length, 2, 120) / s;
        var gap = Math.Clamp(p.Gap, 0, 60) / s;
        var ot = Math.Clamp(p.OutlineThickness, 0, 4) / s; // 0 — обводка выключена
        var dot = Math.Clamp(p.DotSize, 1, 40) / s;

        void Add(FrameworkElement el)
        {
            el.Opacity = opacity;
            canvas.Children.Add(el);
        }

        void Rect(double x, double y, double rw, double rh)
        {
            if (rw < 0.5 || rh < 0.5)
                return;
            if (ot > 0)
                Add(MakeRect(x - ot, y - ot, rw + ot * 2, rh + ot * 2, black));
            Add(MakeRect(x, y, rw, rh, color));
        }

        void Diag(double x1, double y1, double x2, double y2)
        {
            if (ot > 0)
                Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = black, StrokeThickness = t + ot * 2 });
            Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = color, StrokeThickness = t });
        }

        static Rectangle MakeRect(double x, double y, double rw, double rh, Brush fill)
        {
            var r = new Rectangle { Width = rw, Height = rh, Fill = fill };
            Canvas.SetLeft(r, Math.Round(x));
            Canvas.SetTop(r, Math.Round(y));
            return r;
        }

        static Ellipse MakeCircle(double cx, double cy, double r, Brush stroke, double strokeT)
        {
            var e = new Ellipse { Width = r * 2, Height = r * 2, Stroke = stroke, StrokeThickness = strokeT };
            Canvas.SetLeft(e, cx - r);
            Canvas.SetTop(e, cy - r);
            return e;
        }

        switch (p.Style)
        {
            case "Cross":
                Rect(cx - gap - len, cy - t / 2, len, t);
                Rect(cx + gap, cy - t / 2, len, t);
                Rect(cx - t / 2, cy - gap - len, t, len);
                Rect(cx - t / 2, cy + gap, t, len);
                break;

            case "X":
            {
                var d = Math.PI / 4; // 45°: направления диагоналей
                for (var sx = -1; sx <= 1; sx += 2)
                for (var sy = -1; sy <= 1; sy += 2)
                    Diag(cx + sx * gap * Math.Cos(d), cy + sy * gap * Math.Sin(d),
                         cx + sx * (gap + len) * Math.Cos(d), cy + sy * (gap + len) * Math.Sin(d));
                break;
            }

            case "Circle":
                // Радиус круга = «Длина». Обводка — чуть больший круг чёрным под цветным.
                if (ot > 0)
                    Add(MakeCircle(cx, cy, len + ot, black, t + ot * 2));
                Add(MakeCircle(cx, cy, len, color, t));
                break;
        }

        // Центральная точка: стиль «Точка» или дополнение к кресту/кругу/X.
        if (p.Style == "Dot" || p.CenterDot)
            Rect(cx - dot / 2, cy - dot / 2, dot, dot);
    }
}
