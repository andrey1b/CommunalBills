using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;

namespace PrivatUtilities;

// Проверка новых версий через GitHub Releases (тихо при отсутствии сети/релизов)
public static class UpdateChecker
{
    private const string Repo = "andrey1b/CommunalBills";
    private const string LatestApi = "https://api.github.com/repos/" + Repo + "/releases/latest";

    public static async Task CheckAsync(Window owner)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("CommunalBills-UpdateCheck");
            var json = await http.GetStringAsync(LatestApi);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            var pageUrl = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";

            var latest = ParseVersion(tag);
            var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            if (latest is null || latest <= current) return;

            owner.Dispatcher.Invoke(() =>
            {
                var r = MessageBox.Show(owner,
                    $"Доступна новая версия {tag}.\nУ вас установлена {current.ToString(3)}.\n\nОткрыть страницу загрузки?",
                    "Доступно обновление", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (r == MessageBoxResult.Yes && !string.IsNullOrEmpty(pageUrl))
                {
                    try { Process.Start(new ProcessStartInfo(pageUrl) { UseShellExecute = true }); }
                    catch { /* не удалось открыть браузер */ }
                }
            });
        }
        catch { /* нет сети / нет релизов / прочее — молча игнорируем */ }
    }

    // "v1.2.3" / "1.2" → Version; иначе null
    private static Version? ParseVersion(string tag)
    {
        var m = Regex.Match(tag ?? "", @"\d+(\.\d+){1,3}");
        return m.Success && Version.TryParse(m.Value, out var v) ? v : null;
    }
}
