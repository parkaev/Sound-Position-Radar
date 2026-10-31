using System.Text.Json;

namespace SoundRadar.App;

/// <summary>
/// Проверка обновлений: сравнивает версию приложения с последним релизом на GitHub
/// (публичный API, без авторизации).
/// </summary>
public static class UpdateChecker
{
    private const string ReleasesApi = "https://api.github.com/repos/parkaev/Sound-Position-Radar/releases/latest";
    public const string ReleasesPage = "https://github.com/parkaev/Sound-Position-Radar/releases/latest";

    public static string CurrentVersion() =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Вернуть тег последнего релиза (например "v0.2.0") или null при ошибке/отсутствии сети.</summary>
    public static async Task<string?> GetLatestVersionAsync()
    {
        try
        {
            using var http = new System.Net.Http.HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("SoundRadar-UpdateCheck");
            http.Timeout = TimeSpan.FromSeconds(10);
            var json = await http.GetStringAsync(ReleasesApi);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("tag_name", out var tag) && tag.GetString() is { } t)
                return t.TrimStart('v');
        }
        catch
        {
            // Нет сети / GitHub недоступен — считаем, что проверка не удалась.
        }
        return null;
    }

    /// <summary>latest новее current?</summary>
    public static bool IsNewer(string latest, string current)
    {
        var l = Parse(latest);
        var c = Parse(current);
        for (var i = 0; i < 3; i++)
        {
            if (l[i] > c[i])
                return true;
            if (l[i] < c[i])
                return false;
        }
        return false;
    }

    private static int[] Parse(string version)
    {
        var parts = version.TrimStart('v').Split('.');
        var result = new int[3];
        for (var i = 0; i < 3 && i < parts.Length; i++)
            _ = int.TryParse(parts[i], out result[i]);
        return result;
    }
}
