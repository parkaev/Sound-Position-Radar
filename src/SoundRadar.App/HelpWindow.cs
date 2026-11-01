using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoundRadar.App;

/// <summary>Окно справки: пояснения к настройкам детектора, оверлея и обновлений.</summary>
public sealed class HelpWindow : Window
{
    public HelpWindow(string theme)
    {
        Title = "Sound Position Radar — справка";
        Width = 620;
        Height = 640;
        MinWidth = 480;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        // Тема подключается до обращения к кистям — дальше они доступны через TryFindResource.
        ThemeManager.Apply(this, theme);
        Background = (Brush)TryFindResource("Brush.Window") ?? Brushes.White;

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(10),
        };
        var stack = new StackPanel();
        scroll.Content = stack;

        AddTitle(stack, "Детектор");
        AddItem(stack, "Порог над фоном", "Насколько громче постоянного фона игры должен быть звук, чтобы " +
            "программа создала событие. Ниже — программа замечает больше (и чаще реагирует на ветер, птиц, " +
            " distant звуки), выше — только очень явные звуки. Начните с 8–10 дБ: если оверлей мигает без " +
            "причины — поднимите на 1–2 дБ.");
        AddItem(stack, "Мин. громкость события", "Фильтр показа: события слабее этого уровня не отображаются " +
            "в оверлее и журнале. Меньше — видны тихие и дальние звуки; больше — только близкие и громкие. " +
            "Не то же самое, что порог: порог решает «обнаружить ли звук», этот фильтр — «показывать ли его».");
        AddItem(stack, "Профиль игры", "DayZ — калибровка по записям выстрелов этой игры (пан и частотный " +
            "баланс отличаются от стандарта). Универсальный — стандартная стерео-панорама constant-power.");

        AddTitle(stack, "Оверлей");
        AddItem(stack, "Пресеты вида", "«Дуги» — подсветка сектора у края экрана с меткой точного азимута. " +
            "«Свечение краёв» — плавное свечение вдоль краёв, минимально отвлекающее.");
        AddItem(stack, "Цвет", "«По близости» — зеленоватый означает, что звук дальше, жёлтый — средне, " +
            "красный и тёмно-красный — совсем рядом. «По громкости» — классическая шкала от зелёного к " +
            "красному по силе звука. «Постоянный» — один выбранный оттенок для всех событий.");
        AddItem(stack, "Скрытые зоны", "Отметьте зоны, которые не нужно показывать (0° — прямо перед вами, " +
            "±90° — по бокам, 180° — за спиной). Обычно скрывают зону 0° — собственные шаги и выстрелы " +
            "игрока звучат «из центра». Помните: скрытая зона отключает и полезные звуки из этого направления.");
        AddItem(stack, "Тонкая настройка", "Непрозрачность — общая яркость меток. Размер от края — насколько " +
            "глубоко свечение заходит в экран. Ширина реакции — угловой размер подсветки одного события. " +
            "Затухание — время угасания метки после конца звука. Все значения можно вводить вручную.");
        AddItem(stack, "Хоткей", "Кликните по полю и нажмите сочетание с Ctrl, Alt, Shift или Win — оно будет " +
            "включать и выключать оверлей глобально, даже когда вы в игре. Esc во время ввода — отмена, " +
            "кнопка «Сброс» — снять биндинг. Если сочетание занято другой программой, будет показано предупреждение.");

        AddTitle(stack, "Обновления и данные");
        AddItem(stack, "Проверка обновлений", "Кликните по номеру версии в футере — программа сравнит её с " +
            "последним релизом на GitHub и предложит открыть страницу загрузки. Кнопка «Не показывать снова» " +
            "скрывает напоминание до выхода следующей версии.");
        AddItem(stack, "Журнал JSONL и запись WAV", "Журнал пишет каждое событие в файл — пригодится для " +
            "разбора ложных срабатываний. Запись WAV сохраняет сырой захваченный звук — если что-то " +
            "детектируется странно, пришлите этот файл вместе с журналом.");
        AddItem(stack, "Ограничения", "Стерео-звук не различает «перед/зад» — для 360° нужен многоканальный " +
            "микс игры и включённый захват 8 каналов. В полноэкранном исключительном режиме оверлей не виден — " +
            "используйте «окно без рамки».");

        // Корень: контент + футер с автором (клик по нику открывает профиль GitHub).
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(scroll, 0);
        root.Children.Add(scroll);

        var footer = new Border
        {
            BorderBrush = FindThemeBrush("Brush.CardBorder"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Background = FindThemeBrush("Brush.Footer"),
            Padding = new Thickness(14, 8, 14, 8),
        };
        var author = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right };
        var link = new System.Windows.Documents.Hyperlink
        {
            NavigateUri = new Uri("https://github.com/parkaev"),
            Foreground = FindThemeBrush("Brush.Accent"),
            BaselineAlignment = System.Windows.BaselineAlignment.Center,
            ToolTip = "Открыть профиль автора на GitHub",
        };
        link.Inlines.Add("parkaev");
        link.RequestNavigate += (_, e) =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        author.Inlines.Add(link);
        footer.Child = author;
        Grid.SetRow(footer, 1);
        root.Children.Add(footer);

        Content = root;
    }

    private static void AddTitle(StackPanel stack, string text)
    {
        stack.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindThemeBrush("Brush.Accent"),
            Margin = new Thickness(0, 14, 0, 6),
        });
    }

    private static void AddItem(StackPanel stack, string title, string text)
    {
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindThemeBrush("Brush.Text"),
            Margin = new Thickness(0, 6, 0, 2),
        });
        stack.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindThemeBrush("Brush.Text"),
            Opacity = 0.85,
            Margin = new Thickness(0, 0, 8, 4),
        });
    }

    private static Brush FindThemeBrush(string key) =>
        Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
}
