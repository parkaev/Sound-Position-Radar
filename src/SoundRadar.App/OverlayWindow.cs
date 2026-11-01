using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using SoundRadar.Core.Events;
namespace SoundRadar.App;

/// <summary>
/// Оверлей (M2): прозрачное окно поверх всего, без фокуса и кликов, показывает направление
/// звука у краёв экрана — как индикатор направления урона в шутерах.
/// 0° = перед (верх экрана), ±90° = бока, ±180° = зад (низ). Виден поверх игр в оконном
/// и «окно без рамки» режимах; в полноэкранном исключительном режиме оверлей не виден.
///
/// Два пресета: «Дуги» (секторы у краёв с меткой точного азимута) и «Кромка (градиент)»
/// (свечение вдоль краёв экрана, прозрачно к центру). Непрозрачность, размер, ширина
/// реакции, скорость затухания, скрытые зоны и цвет настраиваются из тест-окна.
///
/// Режим <c>--overlay-demo</c>: крутит синтетические метки по всем зонам без аудио.
/// </summary>
public sealed class OverlayWindow : Window
{
    private const int MaxEntries = 5;
    private const double TickSec = 0.033;

    private readonly EventHub? hub;
    private readonly bool demo;
    private readonly Canvas canvas = new();
    private readonly Border hint;
    private readonly DispatcherTimer timer;
    private readonly List<Entry> entries = new();
    private int demoTick;
    private HashSet<int> hiddenZoneSet = new();

    /// <summary>Перекрасить плашку «активен» под тему приложения (смена темы при открытом оверлее).</summary>
    public void SetHintTheme(bool dark)
    {
        hint.Background = dark
            ? new SolidColorBrush(Color.FromArgb(0xB0, 0x1E, 0x1E, 0x22))
            : new SolidColorBrush(Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF));
        hint.BorderBrush = dark
            ? Brushes.Transparent
            : new SolidColorBrush(Color.FromArgb(0x50, 0x00, 0x00, 0x00));
        hint.BorderThickness = new Thickness(dark ? 0 : 1);
        if (hint.Child is TextBlock tb)
            tb.Foreground = dark
                ? Brushes.White
                : new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1F));
    }

    private sealed class Entry
    {
        public Guid Id;
        public double Azimuth;              // целевой азимут (от детектора)
        public double DisplayedAzimuth;     // сглаженный азимут для отрисовки
        public bool HasDisplayed;
        public double Salience;
        public int DominantBand;            // самая громкая полоса события (0 = бас … 4 = 6к+)
        public double Opacity = 1;
        public int Age;
    }

    public OverlayWindow(EventHub? hub, bool demo, bool darkTheme)
    {
        this.hub = hub;
        this.demo = demo;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        IsHitTestVisible = false;
        Focusable = false;
        Topmost = true;
        Title = "Sound Radar Overlay";
        // Только основной монитор: в координатах WPF его левый верхний угол — (0,0),
        // а PrimaryScreenWidth/Height — его размер в DIP (масштаб DPI учтён).
        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;

        // Уведомление «активен» в левом верхнем углу экрана — в цветах темы приложения.
        hint = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 7, 12, 7),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(16),
            Child = new TextBlock
            {
                Text = "Sound Radar — оверлей активен",
                FontSize = 13,
            },
        };
        SetHintTheme(darkTheme);

        var grid = new Grid();
        grid.Children.Add(canvas);
        grid.Children.Add(hint);
        Content = grid;

        timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromSeconds(TickSec) };
        timer.Tick += (_, _) => Update();
        Loaded += (_, _) => timer.Start();
    }

    public const string ArcPreset = "Дуги";
    public const string EdgePreset = "Свечение краёв";

    /// <summary>Число зон по кругу (8/12/16/24): ширина подсвечиваемого сектора = 360/Zones.</summary>
    public int Zones { get; set; } = 16;

    /// <summary>Пресет отображения: «Дуги» (секторы с меткой азимута) или «Кромка (градиент)»
    /// (свечение вдоль краёв экрана, минимально отвлекающее).</summary>
    public string Preset { get; set; } = ArcPreset;

    /// <summary>Оверлей работает в демо-режиме (предпросмотр с синтетическими метками).</summary>
    public bool IsDemo => demo;

    /// <summary>Общая непрозрачность элементов оверлея, 0…1.</summary>
    public double OpacityScale { get; set; } = 0.85;

    /// <summary>Размер свечения от края экрана, px (пресет «Кромка»); для «Дуг» — обратный
    /// отступ дуг от края.</summary>
    public double SizeDepth { get; set; } = 130.0;

    /// <summary>Угловая полуширина реакции события, °.</summary>
    public double HalfWidthDeg { get; set; } = 16.0;

    /// <summary>Время затухания метки после конца события, с.</summary>
    public double FadeSec { get; set; } = 1.0;

    /// <summary>Скрытые зоны — индексы через запятую (0 = перед, N/2 = зад).</summary>
    public string HiddenZones
    {
        get => string.Join(",", hiddenZoneSet.OrderBy(x => x));
        set => hiddenZoneSet = new HashSet<int>(
            (value ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, out var v) ? v : -1)
                .Where(v => v >= 0));
    }

    /// <summary>Скрытые зоны приглушаются (≈35% яркости) вместо полного скрытия.</summary>
    public bool DimZones { get; set; } = true;

    /// <summary>Режим цвета: "Frequency" (по частотам: бас — тёмно-красный, верха — зелёный)
    /// или "Fixed" (постоянный оттенок).</summary>
    public string ColorMode { get; set; } = "Frequency";

    /// <summary>Оттенок для режима постоянного цвета (имя из RadarPalette.NamedColor).</summary>
    public string ColorName { get; set; } = "Красный";

    private Color EntryColor(Entry entry) => ColorMode switch
    {
        "Fixed" => RadarPalette.NamedColor(ColorName),
        _ => RadarPalette.FrequencyColor(entry.DominantBand),
    };

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Клик-сквозь и запрет активации: игра не теряет фокус, клики проходят насквозь.
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var style = GetWindowLongW(hwnd, GWL_EXSTYLE);
        _ = SetWindowLongW(hwnd, GWL_EXSTYLE, (int)((uint)style | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));
    }

    protected override void OnClosed(EventArgs e)
    {
        timer.Stop();
        base.OnClosed(e);
    }

    private void Update()
    {
        if (demo)
            DemoTick();
        else
            ReadHub();

        var fadeTau = Math.Max(0.05, FadeSec / 3.5); // до ~3 % непрозрачности за FadeSec
        var fadeFactor = Math.Exp(-TickSec / fadeTau);
        foreach (var entry in entries)
        {
            entry.Age++;
            // Сглаживание азимута: детектор уточняет угол по мере события, отрисовка не дёргается.
            if (!entry.HasDisplayed)
            {
                entry.DisplayedAzimuth = entry.Azimuth;
                entry.HasDisplayed = true;
            }
            else
            {
                var d = ShortestArc(entry.DisplayedAzimuth, entry.Azimuth);
                entry.DisplayedAzimuth = NormalizeAngle(entry.DisplayedAzimuth + 0.3 * d);
            }

            if (!IsAlive(entry))
                entry.Opacity *= fadeFactor;
        }
        entries.RemoveAll(e => e.Opacity < 0.04);

        Render();

        if (hint.Opacity > 0)
            hint.Opacity = Math.Max(0, hint.Opacity - 0.01);
    }

    private bool IsAlive(Entry entry) =>
        demo ? entry.Age < 26 : EntryActiveInHub(entry);

    private bool EntryActiveInHub(Entry entry)
    {
        var ev = hub?.LatestEvent;
        return ev is not null && ev.Id == entry.Id && ev.EndedUtc is null;
    }

    private void ReadHub()
    {
        var ev = hub?.LatestEvent;
        if (ev is null || ev.EndedUtc is not null)
            return;

        var hidden = IsHidden(ev.AzimuthDeg);
        // Скрытая зона без приглушения — событие не показываем и слот не занимаем.
        if (hidden && !DimZones)
        {
            entries.RemoveAll(x => x.Id == ev.Id);
            return;
        }

        var entry = entries.FirstOrDefault(x => x.Id == ev.Id);
        if (entry is null)
        {
            entry = new Entry { Id = ev.Id };
            entries.Add(entry);
            while (entries.Count > MaxEntries)
                entries.RemoveAt(0);
        }

        // Пока событие живо — угол уточняется и метка яркая; приглушённая зона — тусклая.
        entry.Azimuth = ev.AzimuthDeg;
        entry.Salience = ev.SalienceDb;
        entry.DominantBand = DominantBand(ev.BandSalienceDb);
        entry.Opacity = hidden ? 0.35 : 1.0;
    }

    /// <summary>Самая громкая полоса события: 0 = бас … 4 = 6к+.</summary>
    private static int DominantBand(double[] bandSalience)
    {
        var best = 0;
        for (var b = 1; b < bandSalience.Length; b++)
            if (bandSalience[b] > bandSalience[best])
                best = b;
        return best;
    }

    /// <summary>Событие попадает в скрытую пользователем зону?</summary>
    private bool IsHidden(double azimuthDeg)
    {
        if (hiddenZoneSet.Count == 0)
            return false;
        var step = 360.0 / Zones;
        var zone = ((int)Math.Round(NormalizeAngle(azimuthDeg) / step) % Zones + Zones) % Zones;
        return hiddenZoneSet.Contains(zone);
    }

    private static double NormalizeAngle(double a)
    {
        a %= 360.0;
        if (a > 180.0)
            a -= 360.0;
        if (a < -180.0)
            a += 360.0;
        return a;
    }

    private static double ShortestArc(double from, double to)
    {
        var d = (to - from) % 360.0;
        if (d > 180.0)
            d -= 360.0;
        if (d < -180.0)
            d += 360.0;
        return d;
    }

    private void DemoTick()
    {
        demoTick++;
        if (demoTick % 30 == 1)
        {
            var step = 360.0 / Zones;
            entries.Add(new Entry
            {
                Id = Guid.NewGuid(),
                Azimuth = (demoTick / 30 % Zones) * step, // все зоны по кругу
                Salience = 15 + 6 * ((demoTick / 30) % 3),
                DominantBand = demoTick / 30 % 5, // предпросмотр всех частотных цветов
            });
            while (entries.Count > MaxEntries)
                entries.RemoveAt(0);
        }
    }

    private void Render()
    {
        canvas.Children.Clear();
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 100 || h < 100)
            return;

        if (string.Equals(Preset, EdgePreset, StringComparison.Ordinal))
        {
            RenderEdgeGlow(w, h);
            return;
        }

        RenderArcs(w, h);
    }

    /// <summary>
    /// Пресет «Кромка»: свечение вдоль краёв экрана. Событие освещает сегмент кромки на своём
    /// азимуте: градиент от плотного цвета у самого края к прозрачному вглубь экрана, по краям
    /// сегмента — плавное затухание. Сегмент набирается из узких четырёхугольников с локальным
    /// линейным градиентом — гладко и на углах экрана.
    /// </summary>
    private void RenderEdgeGlow(double w, double h)
    {
        var cx = w / 2;
        var cy = h / 2;
        var bandDepth = Math.Clamp(SizeDepth, 40, 260);
        const double edgeInset = 2.0;     // отступ от физического края
        var halfWidth = Math.Clamp(HalfWidthDeg, 4.0, 45.0);
        const int steps = 44;

        var rxOut = w / 2 - edgeInset;
        var ryOut = h / 2 - edgeInset;
        var rxIn = Math.Max(40, w / 2 - bandDepth);
        var ryIn = Math.Max(40, h / 2 - bandDepth);

        foreach (var entry in entries)
        {
            if (IsHidden(entry.DisplayedAzimuth) && !DimZones)
                continue;
            var color = EntryColor(entry);
            var opacity = EntryOpacity(entry); // уже с масштабом непрозрачности и приглушением

            for (var i = 0; i < steps; i++)
            {
                var a0 = entry.DisplayedAzimuth - halfWidth + 2.0 * halfWidth * i / steps;
                var a1 = a0 + 2.0 * halfWidth / steps;
                var mid = (a0 + a1) / 2.0;
                var alongFalloff = Math.Pow(Math.Cos(Math.PI / 2.0 * (mid - entry.DisplayedAzimuth) / halfWidth), 2);
                var alpha = 0.8 * alongFalloff * opacity;
                if (alpha < 0.01)
                    continue;

                var po0 = PointOnRect(cx, cy, rxOut, ryOut, a0);
                var po1 = PointOnRect(cx, cy, rxOut, ryOut, a1);
                var pi1 = PointOnRect(cx, cy, rxIn, ryIn, a1);
                var pi0 = PointOnRect(cx, cy, rxIn, ryIn, a0);
                var midOut = PointOnRect(cx, cy, rxOut, ryOut, mid);
                var midIn = PointOnRect(cx, cy, rxIn, ryIn, mid);

                var figure = new PathFigure { StartPoint = po0, IsClosed = true };
                figure.Segments.Add(new LineSegment(po1, true));
                figure.Segments.Add(new LineSegment(pi1, true));
                figure.Segments.Add(new LineSegment(pi0, true));

                // Градиент вдоль локальной нормали: у края — плотный, к центру — прозрачный.
                var brush = new LinearGradientBrush
                {
                    MappingMode = BrushMappingMode.Absolute,
                    StartPoint = midOut,
                    EndPoint = midIn,
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb((byte)(255 * alpha), color.R, color.G, color.B), 0),
                        new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1),
                    },
                };

                canvas.Children.Add(new Path { Data = new PathGeometry(new[] { figure }), Fill = brush });
            }
        }
    }

    private static Point PointOnRect(double cx, double cy, double rx, double ry, double azimuthDeg)
    {
        var th = azimuthDeg * Math.PI / 180.0;
        return new Point(cx + rx * Math.Sin(th), cy - ry * Math.Cos(th));
    }

    /// <summary>
    /// Пресет «Дуги»: дуга зоны + тонкая метка точного азимута. Без сетки зон и подписей —
    /// прод-минимализм.
    /// </summary>
    /// <summary>Итоговая яркость маркера: приглушённые скрытые зоны — 35%.</summary>
    private double EntryOpacity(Entry entry) =>
        entry.Opacity * OpacityScale * (IsHidden(entry.DisplayedAzimuth) && DimZones ? 0.35 : 1.0);

    private void RenderArcs(double w, double h)
    {
        var cx = w / 2;
        var cy = h / 2;
        // «Размер» задаёт близость дуг к краю: больше значение — ближе к краю.
        var inset = Math.Clamp(160.0 - SizeDepth * 0.7, 30.0, 150.0);
        var rx = Math.Max(60, w / 2 - inset);
        var ry = Math.Max(60, h / 2 - inset);
        var step = 360.0 / Zones;

        foreach (var entry in entries)
        {
            if (IsHidden(entry.DisplayedAzimuth) && !DimZones)
                continue;
            var color = EntryColor(entry);
            var zoneCenter = Math.Round(entry.DisplayedAzimuth / step) * step;
            var zoneSpan = Math.Max(6.0, Math.Min(step, 2.0 * HalfWidthDeg));
            canvas.Children.Add(BuildArc(w, h, zoneCenter - zoneSpan / 2, zoneCenter + zoneSpan / 2, 22, color, 0.45 * EntryOpacity(entry)));
            canvas.Children.Add(BuildArc(w, h, entry.DisplayedAzimuth - 4, entry.DisplayedAzimuth + 4, 9, color, EntryOpacity(entry)));
        }
    }

    /// <summary>Дуга вдоль эллипса у краёв экрана от fromDeg до toDeg (0° = перед, по часовой).</summary>
    private Path BuildArc(double w, double h, double fromDeg, double toDeg, double thickness, Color color, double opacity)
    {
        var cx = w / 2;
        var cy = h / 2;
        var inset = Math.Clamp(160.0 - SizeDepth * 0.7, 30.0, 150.0);
        var rx = Math.Max(60, w / 2 - inset);
        var ry = Math.Max(60, h / 2 - inset);

        var figure = new PathFigure();
        var first = true;
        for (var a = fromDeg; a <= toDeg; a += 3)
        {
            var th = a * Math.PI / 180.0;
            var p = new Point(cx + rx * Math.Sin(th), cy - ry * Math.Cos(th));
            if (first)
            {
                figure.StartPoint = p;
                first = false;
            }
            else
            {
                figure.Segments.Add(new LineSegment(p, true));
            }
        }

        return new Path
        {
            Data = new PathGeometry(new[] { figure }),
            Stroke = new SolidColorBrush(color),
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Opacity = opacity,
        };
    }

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TRANSPARENT = 0x00000020;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const long WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLongW(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLongW(IntPtr hWnd, int nIndex, int dwNewLong);
}
