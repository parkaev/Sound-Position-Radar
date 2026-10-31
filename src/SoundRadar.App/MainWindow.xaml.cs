using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using NAudio.Wave;
using SoundRadar.Core;
using SoundRadar.Core.Audio;
using SoundRadar.Core.Dsp;
using SoundRadar.Core.Events;
using WpfPath = System.Windows.Shapes.Path;

namespace SoundRadar.App;

public partial class MainWindow : Window
{
    private static readonly int BandCount = AnalyzerOptions.BandEdgesHz.Length - 1;
    private const double MeterMaxDb = 24;

    private readonly EventHub hub = new();
    private readonly DispatcherTimer uiTimer;
    private Analyzer? analyzer;
    private IAudioSource? source;
    private JsonlEventLogger? logger;
    private WaveFileWriter? wavWriter;
    private OverlayWindow? overlay;
    private bool overlayDemo;
    private bool running;
    private bool hotkeyCapturing;
    private string? latestVersion;
    private HwndSource? hwndSource;

    private const int HotkeyId = 0x5244; // "RD"
    private const int WmHotkey = 0x0312;
    private const int WmSettingChange = 0x001A;
    private const uint ModAlt = 0x1;
    private const uint ModControl = 0x2;
    private const uint ModShift = 0x4;
    private const uint ModWin = 0x8;

    private Ellipse? ring;
    private Polygon? wedge;
    private Polygon? rearShade;
    private TextBlock? rearLabel;
    private readonly List<UIElement> tickElements = new();
    private double wedgeOpacity;
    private bool surroundDetected;

    private readonly Rectangle?[] barL = new Rectangle[BandCount];
    private readonly Rectangle?[] barR = new Rectangle[BandCount];
    private readonly TextBlock?[] barValue = new TextBlock[BandCount];

    public MainWindow()
    {
        // Тема подключается до InitializeComponent: кисти нужны шаблонам при первой отрисовке.
        ThemeManager.Apply(this, Config.Theme);
        InitializeComponent();
        BuildMeters();
        uiTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        uiTimer.Tick += OnUiTick;
        uiTimer.Start();
        hub.EventEnded += e => Dispatcher.BeginInvoke(() => AddEventToList(e));
        Loaded += async (_, _) => await OnLoadedAsync();
    }

    /// <summary>5 строк метров: подпись полосы, двухполосный бар (L сверху, R снизу), значение превышения.</summary>
    private void BuildMeters()
    {
        var gray = new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90));
        var trackBg = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2E));

        for (var b = 0; b < BandCount; b++)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var label = new TextBlock
            {
                Text = BandSplitter.BandLabel(b),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = gray,
                FontSize = 11,
            };
            Grid.SetColumn(label, 0);
            row.Children.Add(label);

            var barGrid = new Grid();
            barGrid.RowDefinitions.Add(new RowDefinition());
            barGrid.RowDefinitions.Add(new RowDefinition());
            var rectL = new Rectangle
            {
                Height = 7,
                Width = 1,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(1, 1, 0, 0),
                Fill = Brushes.Transparent,
            };
            var rectR = new Rectangle
            {
                Height = 7,
                Width = 1,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(1, 0, 0, 1),
                Fill = Brushes.Transparent,
            };
            Grid.SetRow(rectL, 0);
            Grid.SetRow(rectR, 1);
            barGrid.Children.Add(rectL);
            barGrid.Children.Add(rectR);

            var track = new Border { Background = trackBg, Height = 18, CornerRadius = new CornerRadius(2), Child = barGrid };
            Grid.SetColumn(track, 1);
            row.Children.Add(track);

            var value = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = gray,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
            };
            Grid.SetColumn(value, 2);
            row.Children.Add(value);

            metersPanel.Children.Add(row);
            barL[b] = rectL;
            barR[b] = rectR;
            barValue[b] = value;
        }
    }

    private SoundRadarConfig Config { get; } = SoundRadarConfig.Load();

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        hwndSource?.AddHook(WndProc);
        ThemeManager.SetTitleBarMode(this, ThemeManager.IsDark(Config.Theme));
        TryRegisterHotkey(Config.OverlayHotkey);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            // Глобальный хоткей работает и когда фокус в игре.
            ToggleOverlay();
            handled = true;
        }
        else if (msg == WmSettingChange)
        {
            // Смена темы Windows: в режиме «Системная» перекрашиваемся на лету.
            var setting = Marshal.PtrToStringAuto(lParam);
            if (setting == "ImmersiveColorSet")
            {
                ThemeManager.Apply(this, Config.Theme);
                RebuildRadar();
                UpdateOverlayButtons();
            }
        }
        return IntPtr.Zero;
    }

    /// <summary>Зарегистрировать хоткей. Возвращает null при успехе, иначе — причину.</summary>
    private string? TryRegisterHotkey(string? binding)
    {
        if (hwndSource is null)
            return "окно ещё не готово";
        UnregisterHotKey(hwndSource.Handle, HotkeyId);
        if (string.IsNullOrWhiteSpace(binding))
            return null;
        if (!TryParseHotkey(binding, out var mods, out var vk))
            return "сочетание должно включать Ctrl, Alt, Shift или Win";
        // Может не зарегистрироваться, если сочетание занято другой программой.
        return RegisterHotKey(hwndSource.Handle, HotkeyId, mods, vk)
            ? null
            : "сочетание занято другой программой";
    }

    private static bool TryParseHotkey(string binding, out uint mods, out uint vk)
    {
        mods = 0;
        vk = 0;
        var parts = binding.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return false;
        foreach (var p in parts[..^1])
        {
            switch (p.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= ModControl; break;
                case "alt": mods |= ModAlt; break;
                case "shift": mods |= ModShift; break;
                case "win": mods |= ModWin; break;
                default: return false;
            }
        }
        if (mods == 0 || !Enum.TryParse<Key>(parts[^1], true, out var key))
            return false;
        vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        return vk != 0;
    }

    private async Task OnLoadedAsync()
    {
        slThreshold.Value = Config.ThresholdDb;
        slMinSalience.Value = Config.MinSalienceDb;
        cbIncludeTree.IsChecked = Config.IncludeProcessTree;
        cb360.IsChecked = Config.PreferMultichannel;
        slOpacity.Value = Config.OverlayOpacity * 100.0;
        slSize.Value = Config.OverlaySizeDepth;
        slHalfWidth.Value = Config.OverlayHalfWidth;
        slFade.Value = Config.OverlayFadeSec;
        cbShowLog.IsChecked = Config.ShowEventLog;
        ApplyLogVisibility();
        RebuildHiddenZonesPanel();
        foreach (var item in cbTheme.Items.Cast<ComboBoxItem>())
        {
            var code = item.Content?.ToString() switch
            {
                "Тёмная" => ThemeManager.Dark,
                "Светлая" => ThemeManager.Light,
                _ => ThemeManager.System,
            };
            if (code == Config.Theme)
                item.IsSelected = true;
        }
        foreach (var item in cbTheme.Items.Cast<ComboBoxItem>())
            if (item.Content?.ToString() == Config.Theme)
                item.IsSelected = true;
        tbVersion.Text = "v" + UpdateChecker.CurrentVersion();
        _ = CheckUpdatesAsync(quiet: true);
        foreach (var item in cbColorMode.Items.Cast<ComboBoxItem>())
        {
            var code = item.Content?.ToString() switch
            {
                "По громкости" => "Salience",
                "Постоянный" => "Fixed",
                _ => "Proximity",
            };
            if (code == Config.OverlayColorMode)
                item.IsSelected = true;
        }
        cbTintColor.IsEnabled = Config.OverlayColorMode == "Fixed";
        foreach (var item in cbTintColor.Items.Cast<ComboBoxItem>())
            if (item.Content?.ToString() == Config.OverlayColorName)
                item.IsSelected = true;
        foreach (var item in cbOverlayPreset.Items.Cast<ComboBoxItem>())
            if (string.Equals(item.Content?.ToString(), Config.OverlayPreset, StringComparison.Ordinal))
                item.IsSelected = true;
        tbHotkey.Text = string.IsNullOrEmpty(Config.OverlayHotkey) ? "(не задано)" : Config.OverlayHotkey;
        foreach (var item in cbProfile.Items.Cast<ComboBoxItem>())
            if (string.Equals(item.Content?.ToString(), Config.GameProfile, StringComparison.OrdinalIgnoreCase))
                item.IsSelected = true;
        (Config.IsProcessSource ? rbProcess : rbDevice).IsChecked = true;
        await RefreshProcessesAsync();

        // --overlay-demo: показать оверлей с синтетическими метками (проверка поверх игры без аудио).
        overlayDemo = Environment.GetCommandLineArgs().Contains("--overlay-demo");
        foreach (var item in cbZones.Items.Cast<ComboBoxItem>())
            if (item.Content?.ToString() == Config.OverlayZones.ToString())
                item.IsSelected = true;
        if (Config.OverlayEnabled || overlayDemo)
            OpenOverlay(demo: overlayDemo);
        UpdateOverlayButtons();
    }

    // ---- запуск / остановка ----------------------------------------------------------------

    private async void OnStartStop(object sender, RoutedEventArgs e)
    {
        if (running)
            await StopAsync();
        else
            await StartAsync();
    }

    private async Task StartAsync()
    {
        var status = "Старт...";
        try
        {
            btnStart.IsEnabled = false;
            tbStatus.Text = status;

            var options = string.Equals(Config.GameProfile, "DayZ", StringComparison.OrdinalIgnoreCase)
                ? AnalyzerOptions.DayZ()
                : new AnalyzerOptions();
            options.ThresholdDb = slThreshold.Value;
            options.MinSalienceDb = slMinSalience.Value;
            analyzer = new Analyzer(hub, options);

            var logPath = Paths.NewTimestamped("logs", "events", ".jsonl");
            logger = new JsonlEventLogger(logPath, slMinSalience.Value);
            logger.Attach(hub);

            IAudioSource src;
            if (rbProcess.IsChecked == true)
            {
                if (cbProcess.SelectedItem is not AudioProcessInfo target)
                    throw new InvalidOperationException(
                        "Выберите процесс в списке (или переключитесь на захват всего устройства).");
                src = new ProcessLoopbackSource(target.Pid, cbIncludeTree.IsChecked == true,
                    cb360.IsChecked == true ? 8 : 2);
                Config.ProcessName = target.Name;
                Config.ProcessId = target.Pid;
            }
            else
            {
                src = new DeviceLoopbackSource();
            }

            src.SamplesAvailable += OnSamples;
            src.Stopped += OnSourceStopped;
            await src.StartAsync();
            source = src;

            if (cbWav.IsChecked == true && src.Format is { } format)
            {
                var wavPath = Paths.NewTimestamped("captures", "capture", ".wav");
                wavWriter = new WaveFileWriter(wavPath, format);
            }

            running = true;
            btnStart.Content = "■  Стоп";
            status = $"Работает: {src.Description}\nФормат: {src.Format}";
            if (src is ProcessLoopbackSource { FellBackToStereo: true })
                status += "\n⚠ 8 каналов недоступны — стерео, зад не различим (180°).";
        }
        catch (Exception ex)
        {
            status = $"Ошибка: {ex.Message}";
            await CleanupAsync();
        }
        finally
        {
            btnStart.IsEnabled = true;
            tbStatus.Text = status;
            SaveConfig();
        }
    }

    private async Task StopAsync()
    {
        running = false;
        btnStart.Content = "▶  Старт";
        await CleanupAsync();
        tbStatus.Text = "Остановлено.";
        SaveConfig();
    }

    private async Task CleanupAsync()
    {
        if (source is { } src)
        {
            src.SamplesAvailable -= OnSamples;
            src.Stopped -= OnSourceStopped;
            try { await src.StopAsync(); } catch { /* источник уже умер */ }
            try { await src.DisposeAsync(); } catch { }
            source = null;
        }
        wavWriter?.Dispose();
        wavWriter = null;
        if (logger is { } log)
        {
            log.Detach(hub);
            log.Dispose();
            logger = null;
        }
        analyzer = null;
        wedgeOpacity = 0;
    }

    private void OnSourceStopped(Exception? ex) => Dispatcher.BeginInvoke(async () =>
    {
        if (!running)
            return;
        tbStatus.Text = ex is null ? "Захват остановился." : $"Захват остановился с ошибкой: {ex.Message}";
        await StopAsync();
    });

    private void OnSamples(AudioChunk chunk)
    {
        // Поток захвата: обработка анализа синхронно, запись WAV — на том же потоке.
        analyzer?.ProcessSamples(chunk.Samples, chunk.Channels, chunk.SampleRate);
        wavWriter?.WriteSamples(chunk.Samples, 0, chunk.Samples.Length);
    }

    // ---- процесс-пикер ---------------------------------------------------------------------

    private async void OnRefreshProcesses(object sender, RoutedEventArgs e) => await RefreshProcessesAsync();

    private async Task RefreshProcessesAsync()
    {
        btnRefresh.IsEnabled = false;
        var candidates = await Task.Run(AudioProcessList.GetCandidates);
        cbProcess.ItemsSource = candidates;

        // Восстановить выбор из конфига: по имени, затем по PID.
        var restore = candidates.FirstOrDefault(p =>
                          string.Equals(p.Name, Config.ProcessName, StringComparison.OrdinalIgnoreCase) && p.Pid == Config.ProcessId)
                      ?? candidates.FirstOrDefault(p => string.Equals(p.Name, Config.ProcessName, StringComparison.OrdinalIgnoreCase))
                      ?? candidates.FirstOrDefault(p => p.Pid == Config.ProcessId);
        if (restore is not null)
            cbProcess.SelectedItem = restore;
        tbStatus.Text = restore is null
            ? "Процесс из конфига не найден — выберите из списка."
            : $"Восстановлен выбор: {restore}";
        btnRefresh.IsEnabled = true;
    }

    // ---- детектор: слайдеры ----------------------------------------------------------------

    private void OnDetectorSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Minimum/Maximum из XAML меняют Value ещё во время загрузки разметки, когда соседние
        // элементы не созданы — не трогаем ничего до конца InitializeComponent.
        if (!IsLoaded || tbThreshold is null || tbMinSalience is null || slThreshold is null || slMinSalience is null
            || tbThresholdValue is null || tbSalienceValue is null)
            return;
        SetIntText(tbThresholdValue, (int)slThreshold.Value);
        SetIntText(tbSalienceValue, (int)slMinSalience.Value);
        tbThreshold.Text = $"Порог над фоном: {slThreshold.Value:F1} дБ";
        tbMinSalience.Text = $"Мин. громкость события: {slMinSalience.Value:F1} дБ";
        if (analyzer is not null)
        {
            analyzer.Options.ThresholdDb = slThreshold.Value;
            analyzer.Options.MinSalienceDb = slMinSalience.Value;
        }
    }

    private void OnDetectorTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || tbThreshold is null || tbMinSalience is null || slThreshold is null || slMinSalience is null
            || tbThresholdValue is null || tbSalienceValue is null)
            return;
        ApplyIntText(tbThresholdValue, slThreshold);
        ApplyIntText(tbSalienceValue, slMinSalience);
        tbThreshold.Text = $"Порог над фоном: {slThreshold.Value:F1} дБ";
        tbMinSalience.Text = $"Мин. громкость события: {slMinSalience.Value:F1} дБ";
        if (analyzer is not null)
        {
            analyzer.Options.ThresholdDb = slThreshold.Value;
            analyzer.Options.MinSalienceDb = slMinSalience.Value;
        }
    }

    private void NumericOnly(object sender, TextCompositionEventArgs e) =>
        e.Handled = !e.Text.All(char.IsDigit);

    // ---- оверлей ---------------------------------------------------------------------------

    private void OnOverlayToggle(object sender, RoutedEventArgs e) => ToggleOverlay();

    private void OnPreviewOverlay(object sender, RoutedEventArgs e)
    {
        if (overlay is not null && overlay.IsDemo)
            CloseOverlay();
        else
        {
            CloseOverlay();
            OpenOverlay(demo: true);
        }
        UpdateOverlayButtons();
    }

    private void ToggleOverlay()
    {
        if (overlay is not null)
            CloseOverlay();
        else
            OpenOverlay(demo: overlayDemo);
        UpdateOverlayButtons();
    }

    private void OpenOverlay(bool demo)
    {
        if (overlay is not null)
            return;
        overlay = new OverlayWindow(hub, demo)
        {
            Zones = Config.OverlayZones,
            HiddenZones = Config.OverlayHiddenZones,
            Preset = Config.OverlayPreset,
            ColorMode = Config.OverlayColorMode,
            ColorName = Config.OverlayColorName,
            OpacityScale = Config.OverlayOpacity,
            SizeDepth = Config.OverlaySizeDepth,
            HalfWidthDeg = Config.OverlayHalfWidth,
            FadeSec = Config.OverlayFadeSec,
        };
        overlay.Show();
        UpdateOverlayButtons();
    }

    private void CloseOverlay()
    {
        overlay?.Close();
        overlay = null;
        UpdateOverlayButtons();
    }

    private void UpdateOverlayButtons()
    {
        var active = overlay is not null;
        btnOverlayToggle.Content = active ? "Оверлей: ВКЛ" : "Оверлей: ВЫКЛ";
        btnOverlayToggle.Background = new SolidColorBrush(active
            ? Color.FromRgb(0x2F, 0x6B, 0x4F)
            : Color.FromRgb(0x6B, 0x2F, 0x2F));
        btnPreview.Content = overlay is not null && overlay.IsDemo
            ? "■  Стоп предпросмотр"
            : "Предпросмотр оверлея";
    }

    private void OnOverlayPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || cbOverlayPreset.SelectedItem is not ComboBoxItem item)
            return;
        var preset = item.Content?.ToString() ?? OverlayWindow.ArcPreset;
        Config.OverlayPreset = preset;
        if (overlay is not null)
            overlay.Preset = preset;
        SaveConfig();
    }

    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;
        if (key == Key.Escape)
        {
            SetHotkey("");
            return;
        }
        var parts = new List<string>();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        SetHotkey(string.Join("+", parts));
    }

    private void OnHotkeyReset(object sender, RoutedEventArgs e) => SetHotkey("");

    private void SetHotkey(string binding)
    {
        var err = TryRegisterHotkey(binding);
        if (err is null)
        {
            Config.OverlayHotkey = binding;
            tbHotkey.Text = string.IsNullOrEmpty(binding) ? "наж. для установки" : binding;
            hintHotkey.Text = string.IsNullOrEmpty(binding) ? "Хоткей сброшен." : "✔ Хоткей установлен.";
            hintHotkey.Foreground = (Brush)TryFindResource("Brush.Text") ?? Brushes.Gray;
        }
        else
        {
            hintHotkey.Text = "✖ " + err;
            hintHotkey.Foreground = (Brush)TryFindResource("Brush.RadarLabelHidden") ?? Brushes.IndianRed;
        }
        hintHotkey.Visibility = Visibility.Visible;
        SaveConfig();
    }

    private void OnHotkeyGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        hotkeyCapturing = true;
        tbHotkey.Text = "нажмите сочетание…";
        hintHotkey.Text = "Нажмите сочетание с Ctrl, Alt, Shift или Win. Esc — отмена.";
        hintHotkey.Foreground = (Brush)TryFindResource("Brush.Accent") ?? Brushes.Gray;
        hintHotkey.Visibility = Visibility.Visible;
    }

    private void OnHotkeyLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        hotkeyCapturing = false;
        hintHotkey.Visibility = Visibility.Collapsed;
        if (tbHotkey.Text == "нажмите сочетание…")
            tbHotkey.Text = string.IsNullOrEmpty(Config.OverlayHotkey) ? "наж. для установки" : Config.OverlayHotkey;
    }

    private void OnColorModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || cbColorMode.SelectedItem is not ComboBoxItem item)
            return;
        var mode = item.Content?.ToString() switch
        {
            "По громкости" => "Salience",
            "Постоянный" => "Fixed",
            _ => "Proximity",
        };
        Config.OverlayColorMode = mode;
        cbTintColor.IsEnabled = mode == "Fixed";
        if (overlay is not null)
            overlay.ColorMode = mode;
        SaveConfig();
    }

    private void OnTintColorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || cbTintColor.SelectedItem is not ComboBoxItem item)
            return;
        var name = item.Content?.ToString() ?? "Красный";
        Config.OverlayColorName = name;
        if (overlay is not null)
            overlay.ColorName = name;
        SaveConfig();
    }

    private void OnOverlaySliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || tbOpacityValue is null || tbSizeValue is null || tbHalfWidthValue is null || tbFadeValue is null)
            return;
        Config.OverlayOpacity = slOpacity.Value / 100.0;
        Config.OverlaySizeDepth = slSize.Value;
        Config.OverlayHalfWidth = slHalfWidth.Value;
        Config.OverlayFadeSec = slFade.Value;
        SyncOverlayTexts();
        ApplyOverlayAppearance();
        SaveConfig();
    }

    private void SyncOverlayTexts()
    {
        SetIntText(tbOpacityValue, (int)slOpacity.Value);
        SetIntText(tbSizeValue, (int)slSize.Value);
        SetIntText(tbHalfWidthValue, (int)slHalfWidth.Value);
        SetIntText(tbFadeValue, (int)slFade.Value);
    }

    private static void SetIntText(TextBox tb, int value)
    {
        var text = value.ToString();
        if (tb.Text != text)
            tb.Text = text;
    }

    private void OnOverlayTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded)
            return;
        ApplyIntText(tbOpacityValue, slOpacity);
        ApplyIntText(tbSizeValue, slSize);
        ApplyIntText(tbHalfWidthValue, slHalfWidth);
        ApplyIntText(tbFadeValue, slFade);
        Config.OverlayOpacity = slOpacity.Value / 100.0;
        Config.OverlaySizeDepth = slSize.Value;
        Config.OverlayHalfWidth = slHalfWidth.Value;
        Config.OverlayFadeSec = slFade.Value;
        ApplyOverlayAppearance();
        SaveConfig();
    }

    private static void ApplyIntText(TextBox tb, Slider slider)
    {
        if (int.TryParse(tb.Text, out var v))
        {
            v = Math.Clamp(v, (int)slider.Minimum, (int)slider.Maximum);
            if (Math.Abs(slider.Value - v) > 0.001)
                slider.Value = v;
        }
    }

    private void ApplyOverlayAppearance()
    {
        if (overlay is null)
            return;
        overlay.OpacityScale = Config.OverlayOpacity;
        overlay.SizeDepth = Config.OverlaySizeDepth;
        overlay.HalfWidthDeg = Config.OverlayHalfWidth;
        overlay.FadeSec = Config.OverlayFadeSec;
    }

    private void RebuildHiddenZonesPanel()
    {
        hiddenZonesPanel.Children.Clear();
        var n = Config.OverlayZones;
        var step = 360.0 / n;
        var hidden = ParseHiddenZones(Config.OverlayHiddenZones);
        for (var k = 0; k < n; k++)
        {
            var center = Math.Round(k * step);
            var cb = new CheckBox
            {
                Content = center.ToString(),
                FontSize = 10,
                Margin = new Thickness(0, 0, 6, 0),
                IsChecked = hidden.Contains(k),
                ToolTip = $"Зона {k}: центр {center:F0}° (0 = перед, {Math.Round(n / 2.0 * step):F0} = зад)",
            };
            cb.Checked += (_, _) => UpdateHiddenZones();
            cb.Unchecked += (_, _) => UpdateHiddenZones();
            hiddenZonesPanel.Children.Add(cb);
        }
    }

    private static HashSet<int> ParseHiddenZones(string csv) => new(
        (csv ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var v) ? v : -1)
            .Where(v => v >= 0));

    private void UpdateHiddenZones()
    {
        var n = Config.OverlayZones;
        var list = hiddenZonesPanel.Children.OfType<CheckBox>()
            .Select((cb, i) => (isChecked: cb.IsChecked == true, i))
            .Where(x => x.isChecked)
            .Select(x => x.i);
        Config.OverlayHiddenZones = string.Join(",", list);
        if (overlay is not null)
            overlay.HiddenZones = Config.OverlayHiddenZones;
        SaveConfig();
    }

    private void OnResetOverlay(object sender, RoutedEventArgs e)
    {
        Config.OverlayZones = 16;
        Config.OverlayPreset = OverlayWindow.ArcPreset;
        Config.OverlayColorMode = "Proximity";
        Config.OverlayColorName = "Красный";
        Config.OverlayOpacity = 0.85;
        Config.OverlaySizeDepth = 130.0;
        Config.OverlayHalfWidth = 16.0;
        Config.OverlayFadeSec = 1.0;
        Config.OverlayHiddenZones = "";

        slOpacity.Value = 85;
        slSize.Value = 130;
        slHalfWidth.Value = 16;
        slFade.Value = 1;
        cbZones.SelectedItem = cbZones.Items.Cast<ComboBoxItem>().First(i => i.Content?.ToString() == "16");
        cbOverlayPreset.SelectedItem = cbOverlayPreset.Items.Cast<ComboBoxItem>().First(i => i.Content?.ToString() == OverlayWindow.ArcPreset);
        cbColorMode.SelectedItem = cbColorMode.Items.Cast<ComboBoxItem>().First();
        RebuildHiddenZonesPanel();
        ApplyOverlayAppearance();
        if (overlay is not null)
        {
            overlay.Zones = Config.OverlayZones;
            overlay.Preset = Config.OverlayPreset;
            overlay.ColorMode = Config.OverlayColorMode;
            overlay.ColorName = Config.OverlayColorName;
            overlay.HiddenZones = Config.OverlayHiddenZones;
        }
        SaveConfig();
    }

    private void OnResetDetector(object sender, RoutedEventArgs e)
    {
        slThreshold.Value = 9;
        slMinSalience.Value = 6;
        SaveConfig();
    }

    // ---- тема, справка, обновления ---------------------------------------------------------

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || cbTheme.SelectedItem is not ComboBoxItem item)
            return;
        Config.Theme = item.Content?.ToString() switch
        {
            "Тёмная" => ThemeManager.Dark,
            "Светлая" => ThemeManager.Light,
            _ => ThemeManager.System,
        };
        ThemeManager.Apply(this, Config.Theme);
        RebuildRadar();
        UpdateOverlayButtons();
        SaveConfig();
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        var help = new HelpWindow(Config.Theme) { Owner = this };
        help.Show();
    }

    private void OnVersionClick(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        _ = CheckUpdatesAsync(quiet: false);

    private void OnUpdateDownload(object sender, RoutedEventArgs e) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = UpdateChecker.ReleasesPage,
            UseShellExecute = true,
        });

    private void OnUpdateSkip(object sender, RoutedEventArgs e)
    {
        if (latestVersion is not null)
        {
            Config.SkipUpdateVersion = latestVersion;
            SaveConfig();
        }
        updateBar.Visibility = Visibility.Collapsed;
    }

    private async Task CheckUpdatesAsync(bool quiet)
    {
        var latest = await UpdateChecker.GetLatestVersionAsync();
        if (latest is null)
        {
            if (!quiet)
                ShowUpdateBar("Не удалось проверить обновления — нет доступа к GitHub.", showButtons: false);
            return;
        }
        latestVersion = latest;
        var current = UpdateChecker.CurrentVersion();
        if (UpdateChecker.IsNewer(latest, current) && latest != Config.SkipUpdateVersion)
            ShowUpdateBar($"Доступна новая версия v{latest} (у вас v{current}).", showButtons: true);
        else if (!quiet)
            ShowUpdateBar($"У вас актуальная версия v{current}.", showButtons: false, autoHide: true);
    }

    private void ShowUpdateBar(string text, bool showButtons, bool autoHide = false)
    {
        tbUpdateText.Text = text;
        btnUpdateDownload.Visibility = showButtons ? Visibility.Visible : Visibility.Collapsed;
        btnUpdateSkip.Visibility = showButtons ? Visibility.Visible : Visibility.Collapsed;
        updateBar.Visibility = Visibility.Visible;
        if (autoHide)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                updateBar.Visibility = Visibility.Collapsed;
            };
            t.Start();
        }
    }

    private void OnShowLogChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;
        Config.ShowEventLog = cbShowLog.IsChecked == true;
        ApplyLogVisibility();
        SaveConfig();
    }

    private void ApplyLogVisibility()
    {
        var show = Config.ShowEventLog;
        rightPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        colLog.MinWidth = show ? 250 : 0;
        colLog.Width = new GridLength(show ? 1 : 0, GridUnitType.Star);
    }

    private void OnProfileChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || cbProfile.SelectedItem is not ComboBoxItem item)
            return;
        Config.GameProfile = item.Content?.ToString() ?? "DayZ";
        SaveConfig();
    }

    private void OnZonesChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || cbZones.SelectedItem is not ComboBoxItem item)
            return;
        if (!int.TryParse(item.Content?.ToString(), out var zones))
            return;
        Config.OverlayZones = zones;
        if (overlay is not null)
            overlay.Zones = zones;
        RebuildHiddenZonesPanel();
        SaveConfig();
    }
    private void AddEventToList(SoundEvent e)
    {
        if (e.SalienceDb < slMinSalience.Value)
            return;
        // Короткий формат: строка должна влезать в узкую колонку без обрезки.
        lbEvents.Items.Insert(0,
            $"{e.StartedUtc:HH:mm:ss.fff}  {e.AzimuthDeg,5:F0}°  s{e.SalienceDb,3:F0}  c{e.Confidence:F1}");
        if (lbEvents.Items.Count > 200)
            lbEvents.Items.RemoveAt(lbEvents.Items.Count - 1);
    }

    // ---- радар -----------------------------------------------------------------------------

    private void OnRadarSizeChanged(object sender, SizeChangedEventArgs e) => RebuildRadar();

    private static double NormalizeAngle(double a)
    {
        a %= 360.0;
        if (a > 180.0)
            a -= 360.0;
        if (a < -180.0)
            a += 360.0;
        return a;
    }

    private void RebuildRadar()
    {
        var w = radar.ActualWidth;
        var h = radar.ActualHeight;
        if (w < 60 || h < 60)
            return;
        radar.Children.Clear();
        tickElements.Clear();

        var (center, radius) = RadarGeometry(w, h);
        var stroke = new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x52));
        var dim = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));

        ring = new Ellipse
        {
            Width = radius * 2,
            Height = radius * 2,
            Stroke = stroke,
            StrokeThickness = 2,
        };
        Canvas.SetLeft(ring, center.X - radius);
        Canvas.SetTop(ring, center.Y - radius);
        radar.Children.Add(ring);
        tickElements.Add(ring);

        // Задняя (нижняя) половина — заливка-подсказка для стерео-режима, скрывается при 360°.
        var shadePoints = new PointCollection { center };
        for (var a = 90; a <= 270; a += 10)
            shadePoints.Add(PointOnCircle(center, radius, a));
        rearShade = new Polygon
        {
            Points = shadePoints,
            Fill = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)),
        };
        radar.Children.Add(rearShade);

        rearLabel = new TextBlock
        {
            Text = "зад не различим (стерео-микс)",
            Foreground = dim,
            FontSize = 11,
        };
        Canvas.SetLeft(rearLabel, center.X - 92);
        Canvas.SetTop(rearLabel, center.Y + radius * 0.45);
        radar.Children.Add(rearLabel);

        // Границы зон по числу зон оверлея + градусные подписи центров зон
        // (совпадают с галочками «Скрытые зоны»: скрытые — красноватые).
        var zoneCount = Math.Max(4, Config.OverlayZones);
        var zoneStep = 360.0 / zoneCount;
        var hidden = ParseHiddenZones(Config.OverlayHiddenZones);
        for (var k = 0; k < zoneCount; k++)
        {
            var boundary = k * zoneStep + zoneStep / 2.0 - 180.0;
            var outer = PointOnCircle(center, radius, boundary);
            var inner = PointOnCircle(center, radius - 12, boundary);
            var line = new Line { X1 = outer.X, Y1 = outer.Y, X2 = inner.X, Y2 = inner.Y, Stroke = stroke, StrokeThickness = 1 };
            radar.Children.Add(line);
            tickElements.Add(line);
        }

        foreach (var (a, text, dx) in new[] { (-90, "−90° Л", -30), (90, "П +90°", -8), (180, "180°", -14) })
        {
            var p = PointOnCircle(center, radius - 46, a);
            var label = new TextBlock { Text = text, Foreground = dim, FontSize = 11 };
            Canvas.SetLeft(label, p.X + dx);
            Canvas.SetTop(label, p.Y - 8);
            radar.Children.Add(label);
            tickElements.Add(label);
        }

        for (var k = 0; k < zoneCount; k++)
        {
            var centerDeg = NormalizeAngle(k * zoneStep);
            var p = PointOnCircle(center, radius - 30, centerDeg);
            var isHidden = hidden.Contains(k);
            var label = new TextBlock
            {
                Text = $"{centerDeg:+0;-0;0}°",
                Foreground = isHidden
                    ? new SolidColorBrush(Color.FromRgb(0xE5, 0x73, 0x73))
                    : new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xA0)),
                FontSize = 10,
            };
            Canvas.SetLeft(label, p.X - 11);
            Canvas.SetTop(label, p.Y - 7);
            radar.Children.Add(label);
            tickElements.Add(label);
        }

        // Стрелка «перед» на верхней точке круга.
        var frontArrow = new Polygon
        {
            Points = new PointCollection
            {
                new Point(center.X, center.Y - radius - 15),
                new Point(center.X - 7, center.Y - radius - 2),
                new Point(center.X + 7, center.Y - radius - 2),
            },
            Fill = new SolidColorBrush(Color.FromRgb(0x9C, 0xDC, 0xFE)),
        };
        radar.Children.Add(frontArrow);
        tickElements.Add(frontArrow);

        var frontLabel = new TextBlock { Text = "ПЕРЕД", Foreground = dim, FontSize = 11 };
        Canvas.SetLeft(frontLabel, center.X + 14);
        Canvas.SetTop(frontLabel, center.Y - radius - 16);
        radar.Children.Add(frontLabel);
        tickElements.Add(frontLabel);

        wedge = new Polygon { Points = WedgePoints(center, radius, 0, 10), Fill = Brushes.LimeGreen, Opacity = 0 };
        radar.Children.Add(wedge);
    }

    private void OnUiTick(object? sender, EventArgs e)
    {
        UpdateMeters();
        UpdateWedge();
        UpdateSurroundMode();
    }

    private void UpdateSurroundMode()
    {
        // Режим 360° включается на сессию, как только в миксе появились задние каналы.
        var meter = hub.LatestMeter;
        if (meter is { HasRearChannels: true, RearRatioDb: > -13 })
            surroundDetected = true;

        // Затенённый задний сектор показываем только при включённом 7.1-режиме, пока
        // задние каналы не подтверждены; в стерео-режиме (7.1 выключен) задней зоны нет вовсе.
        var showShade = !surroundDetected && cb360.IsChecked == true;
        if (rearShade is not null)
            rearShade.Visibility = showShade ? Visibility.Visible : Visibility.Collapsed;
        if (rearLabel is not null)
            rearLabel.Visibility = showShade ? Visibility.Visible : Visibility.Collapsed;

        tbRadarMode.Text = meter switch
        {
            null when cb360.IsChecked != true =>
                "Захват всего устройства / стерео: показывается фронт 180°.",
            null => "Режим (180°/360°) определяется по наличию задних каналов в миксе.",
            { HasRearChannels: true } when surroundDetected =>
                $"360° активен: многоканальный микс ({meter.Channels} кан.) — перед и зад различимы.",
            { HasRearChannels: true } =>
                $"Захвачено {meter.Channels} кан., но задних каналов пока нет — показывается фронт 180°.",
            _ => $"Стерео-микс ({meter.Channels} кан.): фронт 180°, зад не различим.",
        };
    }

    private void UpdateMeters()
    {
        var meter = hub.LatestMeter;
        if (meter is null)
            return;

        var level = meter.BandBackgroundDb.Length == BandCount ? Math.Pow(10, meter.BandBackgroundDb[0] / 10.0) : 1e-30;

        for (var b = 0; b < BandCount; b++)
        {
            var exceedL = Math.Clamp(10.0 * Math.Log10(Math.Max(meter.BandEnergyL[b], 1e-30) / level), 0, MeterMaxDb);
            var exceedR = Math.Clamp(10.0 * Math.Log10(Math.Max(meter.BandEnergyR[b], 1e-30) / level), 0, MeterMaxDb);
            var exceed = meter.BandExceedDb[b];

            SetBar(barL[b], exceedL);
            SetBar(barR[b], exceedR);
            if (barValue[b] is { } tb)
                tb.Text = $"{exceed,5:F1}";
        }
    }

    private static void SetBar(Rectangle? bar, double db)
    {
        if (bar is null)
            return;
        var fraction = Math.Clamp(db / MeterMaxDb, 0, 1);
        bar.Width = Math.Max(1, fraction * BarMaxWidth);
        bar.Fill = new SolidColorBrush(BarColor(fraction));
    }

    private static double BarMaxWidth => 150;

    private static Color BarColor(double t) => Lerp(
        Color.FromRgb(0x4C, 0xAF, 0x50),
        Color.FromRgb(0xF4, 0x43, 0x36),
        Math.Clamp(t, 0, 1));

    private void UpdateWedge()
    {
        if (wedge is null)
            return;
        var w = radar.ActualWidth;
        var h = radar.ActualHeight;
        var (center, radius) = RadarGeometry(w, h);
        var ev = hub.LatestEvent;
        if (ev is null)
            return;

        if (hub.LatestMeter is { } meter && meter.EventActive)
        {
            wedgeOpacity = 0.35 + 0.65 * ev.Confidence;
            var halfWidth = 6 + 8 * (1 - ev.Confidence);
            wedge.Points = WedgePoints(center, radius, ev.AzimuthDeg, halfWidth);
            wedge.Fill = new SolidColorBrush(SalienceColor(ev.SalienceDb));
        }
        else
        {
            wedgeOpacity *= 0.88;
        }

        wedge.Opacity = wedgeOpacity < 0.03 ? 0 : wedgeOpacity;
    }

    private static Color SalienceColor(double salienceDb) => RadarPalette.SalienceColor(salienceDb);

    private static (Point center, double radius) RadarGeometry(double w, double h)
    {
        var center = new Point(w / 2, h / 2 + 4);
        var radius = Math.Max(40, Math.Min(w / 2 - 30, h / 2 - 36));
        return (center, radius);
    }

    private static PointCollection WedgePoints(Point center, double radius, double azimuth, double halfWidthDeg)
    {
        var p1 = PointOnCircle(center, radius, azimuth - halfWidthDeg);
        var p2 = PointOnCircle(center, radius, azimuth + halfWidthDeg);
        return new PointCollection { center, p1, p2 };
    }

    private static Point PointOnCircle(Point center, double radius, double azimuthDeg)
    {
        var a = azimuthDeg * Math.PI / 180.0;
        return new Point(center.X + radius * Math.Sin(a), center.Y - radius * Math.Cos(a));
    }

    private static Color Lerp(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));

    // ---- конфиг ----------------------------------------------------------------------------

    private void SaveConfig()
    {
        Config.SourceType = rbProcess.IsChecked == true ? "Process" : "Device";
        Config.ThresholdDb = slThreshold.Value;
        Config.MinSalienceDb = slMinSalience.Value;
        Config.IncludeProcessTree = cbIncludeTree.IsChecked == true;
        Config.PreferMultichannel = cb360.IsChecked == true;
        Config.OverlayEnabled = overlay is not null && !overlay.IsDemo;
        Config.OverlayHideCenter = false; // мигрировано в OverlayHiddenZones
        Config.OverlayOpacity = slOpacity.Value / 100.0;
        Config.OverlaySizeDepth = slSize.Value;
        Config.OverlayHalfWidth = slHalfWidth.Value;
        Config.OverlayFadeSec = slFade.Value;
        if (cbProfile.SelectedItem is ComboBoxItem profileItem && !string.IsNullOrEmpty(profileItem.Content?.ToString()))
            Config.GameProfile = profileItem.Content.ToString();
        try { Config.Save(); } catch { /* не критично */ }
    }

    private async void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (hwndSource is not null)
        {
            UnregisterHotKey(hwndSource.Handle, HotkeyId);
            hwndSource.RemoveHook(WndProc);
        }
        overlay?.Close();
        overlay = null;
        if (running)
            await StopAsync();
        SaveConfig();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private static class Paths
    {
        public static string NewTimestamped(string subDir, string prefix, string extension)
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SoundPositionRadar",
                subDir);
            Directory.CreateDirectory(dir);
            return System.IO.Path.Combine(dir, $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}{extension}");
        }
    }
}
