using System.IO;
using System.Windows;

namespace SoundRadar.App;

public partial class App : Application
{
    public App()
    {
        // Необработанные исключения — в файл рядом с логами: без этого падение на старте
        // у GUI-приложения видно только как «окно закрылось».
        DispatcherUnhandledException += (_, e) =>
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SoundPositionRadar");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "crash.txt"), e.Exception.ToString());
            }
            catch
            {
                // Лог не критичен.
            }
        };
    }
}
