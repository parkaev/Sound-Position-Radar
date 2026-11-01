using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace SoundRadar.App;

/// <summary>Короткое уведомление поверх экрана в левом верхнем углу — там же, где плашка
/// «активен» у оверлея. Показывается, когда окно оверлея ещё/уже не существует
/// (подтверждение включения и, главное, выключения). Цвета строятся по теме
/// приложения на момент показа.</summary>
internal static class ScreenToast
{
    private static Window? _toast;
    private static DispatcherTimer? _hideTimer;
    private static string _lastMessage = "";

    /// <summary>Перекрасить уже видимый тост под новую тему приложения.</summary>
    public static void RefreshTheme(bool dark)
    {
        if (_toast is { IsVisible: true })
            Show(_lastMessage, dark, _monitorIndex);
    }

    private static int _monitorIndex;

    /// <summary>Показать тост в левом верхнем углу заданного монитора (того же, где оверлей).</summary>
    public static void Show(string message, bool dark, int monitorIndex)
    {
        if (_toast is null)
        {
            _toast = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                ShowActivated = false, // не крадём фокус из игры
                IsHitTestVisible = false,
                Topmost = true,
                // Окно размером с плашку: без этого безрамочное окно растягивается на экран.
                SizeToContent = SizeToContent.WidthAndHeight,
                MaxWidth = 420,
                Content = new Border
                {
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 7, 12, 7),
                    Child = new TextBlock { FontSize = 13 },
                },
            };
            _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            _hideTimer.Tick += (_, _) => Hide();
        }

        var border = (Border)_toast.Content;
        var tb = (TextBlock)border.Child;
        _lastMessage = message;
        tb.Text = message;
        border.Background = dark
            ? new SolidColorBrush(Color.FromArgb(0xB0, 0x1E, 0x1E, 0x22))
            : new SolidColorBrush(Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF));
        border.BorderBrush = dark
            ? Brushes.Transparent
            : new SolidColorBrush(Color.FromArgb(0x50, 0x00, 0x00, 0x00));
        border.BorderThickness = new Thickness(1);
        tb.Foreground = dark
            ? Brushes.White
            : new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1F));

        _monitorIndex = monitorIndex;
        var screens = System.Windows.Forms.Screen.AllScreens;
        var b = screens[Math.Clamp(monitorIndex, 0, screens.Length - 1)].Bounds;
        // Приблизительно в DIP до показа, затем точно в физических пикселях —
        // тост появляется там же, где плашка оверлея, независимо от DPI мониторов.
        _toast.Left = (b.Left + 16) / 1.0;
        _toast.Top = (b.Top + 16) / 1.0;
        _toast.Show();
        MoveToMonitor(b);
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private static void MoveToMonitor(System.Drawing.Rectangle b)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(_toast!).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        _ = SetWindowPos(hwnd, IntPtr.Zero, b.Left + 16, b.Top + 16, 0, 0,
            SwpNoZorder | SwpNoActivate | SwpNoSize);
    }

    private const uint SwpNoZorder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoSize = 0x0001;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    private static void Hide()
    {
        _hideTimer?.Stop();
        _toast?.Hide();
    }
}
