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

    public static void Show(string message, bool dark)
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
                Left = 16,
                Top = 16,
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

        _toast.Show();
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private static void Hide()
    {
        _hideTimer?.Stop();
        _toast?.Hide();
    }
}
