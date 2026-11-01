using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace SoundRadar.App;

/// <summary>
/// Темы оформления: тёмная, светлая и «как в системе» (читает реестр Windows и следит
/// за сменой через WM_SETTINGCHANGE). Строит словарь кистей, который окно подключает
/// первым в MergedDictionaries — все шаблоны ссылаются на кисти через DynamicResource,
/// поэтому смена темы обновляет интерфейс на лету.
/// </summary>
public static class ThemeManager
{
    public const string System = "System";
    public const string Dark = "Dark";
    public const string Light = "Light";

    private const int DwmUseImmersiveDarkMode = 20;

    /// <summary>Тема системы: тёмная?</summary>
    public static bool SystemIsDark()
    {
        try
        {
            var v = Registry.CurrentUser
                .OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")
                ?.GetValue("AppsUseLightTheme");
            return v is int i ? i == 0 : true; // нет ключа — считаем тёмной
        }
        catch
        {
            return true;
        }
    }

    public static bool IsDark(string theme) => theme switch
    {
        Light => false,
        Dark => true,
        _ => SystemIsDark(),
    };

    public static ResourceDictionary BuildDictionary(bool dark)
    {
        var rd = new ResourceDictionary();

        void B(string key, string hex)
        {
            rd[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }

        if (dark)
        {
            B("Brush.Window", "#1B1B1F");
            B("Brush.Card", "#202024");
            B("Brush.CardBorder", "#2E2E33");
            B("Brush.Text", "#DDDDDD");
            B("Brush.Muted", "#8A8A90");
            B("Brush.Accent", "#9CDCFE");
            B("Brush.InputBg", "#252528");
            B("Brush.InputBorder", "#55555C");
            B("Brush.InputBorderFocus", "#4FC3F7");
            B("Brush.BtnBg", "#2D2D31");
            B("Brush.BtnBorder", "#3F3F46");
            B("Brush.BtnBorderHover", "#6A6A72");
            B("Brush.CheckBg", "#252528");
            B("Brush.CheckBorder", "#55555C");
            B("Brush.CheckMark", "#7BD88F");
            B("Brush.BulletDot", "#4FC3F7");
            B("Brush.SliderFill", "#4FC3F7");
            B("Brush.SliderTrack", "#3F3F46");
            B("Brush.ScrollThumb", "#55555C");
            B("Brush.ScrollTrack", "#26262B");
            B("Brush.ItemHover", "#2F2F37");
            B("Brush.MeterTrack", "#2A2A2E");
            B("Brush.RadarLabelHidden", "#E57373");
            B("Brush.ListBg", "#141418");
            B("Brush.RadarBg", "#141418");
            B("Brush.RadarRing", "#4A4A52");
            B("Brush.RadarLabel", "#9A9AA0");
            B("Brush.RearShade", "#40000000");
            B("Brush.TooltipBg", "#2D2D31");
            B("Brush.TooltipBorder", "#55555A");
            B("Brush.Footer", "#202024");
            B("Brush.PopupBg", "#252528");
            B("Brush.PopupBorder", "#3F3F46");
        }
        else
        {
            B("Brush.Window", "#F2F2F4");
            B("Brush.Card", "#FFFFFF");
            B("Brush.CardBorder", "#D9D9DE");
            B("Brush.Text", "#1B1B1F");
            B("Brush.Muted", "#6E6E76");
            B("Brush.Accent", "#0B63C5");
            B("Brush.InputBg", "#FFFFFF");
            B("Brush.InputBorder", "#C4C4CB");
            B("Brush.InputBorderFocus", "#0B63C5");
            B("Brush.BtnBg", "#ECECEE");
            B("Brush.BtnBorder", "#C9C9CF");
            B("Brush.BtnBorderHover", "#9A9AA2");
            B("Brush.CheckBg", "#FFFFFF");
            B("Brush.CheckBorder", "#B0B0B8");
            B("Brush.CheckMark", "#0B63C5");
            B("Brush.BulletDot", "#0B63C5");
            B("Brush.SliderFill", "#0B63C5");
            B("Brush.SliderTrack", "#D5D5DA");
            B("Brush.ScrollThumb", "#B0B0B8");
            B("Brush.ScrollTrack", "#E7E7EA");
            B("Brush.ItemHover", "#E2E7EF");
            B("Brush.MeterTrack", "#E3E3E8");
            B("Brush.RadarLabelHidden", "#B3403E");
            B("Brush.ListBg", "#FFFFFF");
            B("Brush.RadarBg", "#FBFBFC");
            B("Brush.RadarRing", "#B8B8BE");
            B("Brush.RadarLabel", "#8A8A90");
            B("Brush.RearShade", "#18000000");
            B("Brush.TooltipBg", "#FFFFFF");
            B("Brush.TooltipBorder", "#C9C9CF");
            B("Brush.Footer", "#E9E9EC");
            B("Brush.PopupBg", "#FFFFFF");
            B("Brush.PopupBorder", "#C9C9CF");
        }

        // Системные кисти — их используют дефолтные части шаблонов (дропдаун и т.п.).
        var window = (SolidColorBrush)rd["Brush.Window"];
        var text = (SolidColorBrush)rd["Brush.Text"];
        var control = (SolidColorBrush)rd["Brush.BtnBg"];
        rd[SystemColors.WindowBrushKey] = window;
        rd[SystemColors.ControlBrushKey] = control;
        rd[SystemColors.ControlTextBrushKey] = text;
        rd[SystemColors.ControlDarkBrushKey] = (SolidColorBrush)rd["Brush.CardBorder"];
        rd[SystemColors.GrayTextBrushKey] = (SolidColorBrush)rd["Brush.Muted"];
        rd[SystemColors.HighlightBrushKey] = dark
            ? new SolidColorBrush(Color.FromRgb(0x2F, 0x52, 0x3C))
            : new SolidColorBrush(Color.FromRgb(0xD6, 0xE8, 0xF7));
        rd[SystemColors.HighlightTextBrushKey] = new SolidColorBrush(Colors.White);
        rd[SystemColors.InactiveSelectionHighlightBrushKey] = dark
            ? new SolidColorBrush(Color.FromRgb(0x3A, 0x4A, 0x52))
            : new SolidColorBrush(Color.FromRgb(0xE2, 0xEE, 0xF7));
        return rd;
    }

    /// <summary>Подключить словарь темы окну и окрасить заголовок окна.
    /// Прежний словарь темы удаляется: WPF ищет ресурсы в объединённых словарях с конца,
    /// поэтому накопление словарей при повторных Apply ломало смену темы на лету.</summary>
    public static void Apply(Window window, string theme)
    {
        var dark = IsDark(theme);
        var md = window.Resources.MergedDictionaries;
        for (var i = md.Count - 1; i >= 0; i--)
            if (md[i].Contains("Brush.Window"))
                md.RemoveAt(i);
        md.Add(BuildDictionary(dark));
        SetTitleBarMode(window, dark);
    }

    /// <summary>Тёмный/светлый заголовок окна (DWM).</summary>
    public static void SetTitleBarMode(Window window, bool dark)
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                return;
            var value = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref value, sizeof(int));
        }
        catch
        {
            // Старые Windows без DWM-атрибута — остаётся системный заголовок.
        }
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern void DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
