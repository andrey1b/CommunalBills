using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using WMedia = System.Windows.Media;

namespace PrivatUtilities;

// Вкладка «Спросить у ИИ» — единый узнаваемый элемент SeniorHub (RU, светлая тема).
public partial class AskAiView : UserControl
{
    private static readonly (string Name, string Url, string ApiId)[] AiList =
    {
        ("ChatGPT",    "https://chat.openai.com",         ""),
        ("Claude",     "https://claude.ai",                "claude"),
        ("Gemini",     "https://gemini.google.com",        "gemini"),
        ("Copilot",    "https://copilot.microsoft.com",    ""),
        ("Perplexity", "https://www.perplexity.ai",        "perplexity"),
        ("DeepSeek",   "https://chat.deepseek.com",        "deepseek"),
    };

    private static readonly (byte r, byte g, byte b)[] AiColors =
    {
        (16,  163, 127), (190,  90,  40), (66,  133, 244),
        (0,   120, 212), (20,  100, 180), (50,   80, 200),
    };

    private readonly ObservableCollection<AiRow> aiRows = new();

    private string _claudeApiKey = "", _geminiApiKey = "", _deepSeekApiKey = "", _perplexityApiKey = "";

    private static readonly WMedia.Brush BrushAnswer = Frozen(34, 34, 34);
    private static readonly WMedia.Brush BrushError  = Frozen(200, 40, 40);
    private static readonly WMedia.Brush BrushDim    = Frozen(110, 110, 110);
    private static readonly WMedia.Brush BrushLight  = Frozen(230, 230, 230);

    private static WMedia.Brush Frozen(byte r, byte g, byte b)
    {
        var br = new WMedia.SolidColorBrush(WMedia.Color.FromRgb(r, g, b));
        br.Freeze();
        return br;
    }

    private static string AiDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CommunalBills");

    public AskAiView()
    {
        InitializeComponent();

        for (int i = 0; i < AiList.Length; i++)
        {
            var (name, url, apiId) = AiList[i];
            aiRows.Add(new AiRow
            {
                Name = name, Url = url, ApiId = apiId,
                HeaderBrush = Frozen(AiColors[i].r, AiColors[i].g, AiColors[i].b)
            });
        }
        icAiRows.ItemsSource = aiRows;

        TbAiQuestionLbl.Text = "Вопрос:";
        btnAiAsk.Content     = "▶  Спросить";
        btnAiSaveAll.Content = "Сохранить все";
        btnAiClear.Content   = "Очистить";
        btnAiApiKeys.Content = "⚙ API ключи";
        TbAiQuickLbl.Text    = "Быстрые вопросы:";
        btnAiQuick1.Content  = "Как оплатить";
        btnAiQuick2.Content  = "Снизить расходы";
        btnAiQuick3.Content  = "Понять квитанцию";

        btnAiAsk.Click     += async (_, _) => await AskAllAisAsync();
        btnAiSaveAll.Click += (_, _) => SaveAllResponses();
        btnAiClear.Click   += (_, _) => ClearAllResponses();
        btnAiApiKeys.Click += (_, _) => ShowApiKeyDialog();
        txAiQuestion.KeyDown += async (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter) { e.Handled = true; await AskAllAisAsync(); }
        };
        btnAiQuick1.Click += (_, _) => txAiQuestion.Text = "Как удобнее оплачивать коммунальные счета? Подскажи надёжные способы и сервисы.";
        btnAiQuick2.Click += (_, _) => txAiQuestion.Text = "Как законно снизить расходы на коммунальные услуги (ЖКХ)? Дай практичные советы.";
        btnAiQuick3.Click += (_, _) => txAiQuestion.Text = "Помоги разобраться, из чего складывается сумма в коммунальной квитанции.";

        LoadAiSettings();
    }

    private void AiOpenButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AiRow r)
            Process.Start(new ProcessStartInfo { FileName = r.Url, UseShellExecute = true });
    }

    private void AiSaveButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AiRow r) SaveSingleResponse(r);
    }

    private void AiCopyButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AiRow r) return;
        string txt = r.Response.Trim();
        if (string.IsNullOrEmpty(txt))
        { MessageBox.Show("Нет ответа для копирования.", "Пусто", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        Clipboard.SetText(txt);
        lblAiStatus.Text = "📋 Скопировано в буфер (" + r.Name + ")";
    }

    private void ClearAllResponses()
    {
        foreach (var r in aiRows) { r.Response = ""; r.ResponseBrush = BrushAnswer; }
        lblAiStatus.Text = "";
    }

    private async Task AskAllAisAsync()
    {
        string question = txAiQuestion.Text.Trim();
        if (string.IsNullOrEmpty(question))
        { MessageBox.Show("Введите вопрос.", "Вопрос пуст", MessageBoxButton.OK, MessageBoxImage.Information); return; }

        Clipboard.SetText(question);

        var tasks = new List<Task>();
        var browserOpened = new List<string>();

        for (int i = 0; i < aiRows.Count; i++)
        {
            var r = aiRows[i];
            if (!r.Enabled) continue;

            int idx = i;
            string? apiKey = ApiKeyFor(r.ApiId);
            bool hasKey = !string.IsNullOrEmpty(apiKey);

            if (r.ApiId == "claude" && hasKey)
            { r.ResponseBrush = BrushDim; r.Response = $"⌛ Запрос к {r.Name}…"; tasks.Add(AskClaudeAsync(idx, question, apiKey!)); }
            else if (r.ApiId == "gemini" && hasKey)
            { r.ResponseBrush = BrushDim; r.Response = $"⌛ Запрос к {r.Name}…"; tasks.Add(AskGeminiAsync(idx, question, apiKey!)); }
            else if (r.ApiId == "deepseek" && hasKey)
            { r.ResponseBrush = BrushDim; r.Response = $"⌛ Запрос к {r.Name}…"; tasks.Add(AskDeepSeekAsync(idx, question, apiKey!)); }
            else if (r.ApiId == "perplexity" && hasKey)
            { r.ResponseBrush = BrushDim; r.Response = $"⌛ Запрос к {r.Name}…"; tasks.Add(AskPerplexityAsync(idx, question, apiKey!)); }
            else
            {
                string openUrl = BuildBrowserUrl(r.Name, r.Url, question);
                Process.Start(new ProcessStartInfo { FileName = openUrl, UseShellExecute = true });
                r.ResponseBrush = BrushDim;
                r.Response = "🌐 Вопрос открыт в браузере.\nВопрос скопирован в буфер — вставьте (Ctrl+V) в чат.\nСкопируйте ответ сюда после получения.";
                browserOpened.Add(r.Name);
            }
        }

        if (tasks.Count > 0)
        {
            lblAiStatus.Text = "⌛ Жду ответы от ИИ…";
            await Task.WhenAll(tasks);
            lblAiStatus.Text = "✓ Готово!";
        }
        else
        {
            lblAiStatus.Text = browserOpened.Count > 0
                ? "🌐 Открыт(ы) в браузере: " + string.Join(", ", browserOpened)
                : "Нет выбранных ИИ.";
        }
    }

    private string? ApiKeyFor(string apiId) => apiId switch
    {
        "claude"     => string.IsNullOrEmpty(_claudeApiKey)     ? null : _claudeApiKey,
        "gemini"     => string.IsNullOrEmpty(_geminiApiKey)     ? null : _geminiApiKey,
        "deepseek"   => string.IsNullOrEmpty(_deepSeekApiKey)   ? null : _deepSeekApiKey,
        "perplexity" => string.IsNullOrEmpty(_perplexityApiKey) ? null : _perplexityApiKey,
        _            => null
    };

    private static string BuildBrowserUrl(string name, string url, string question)
    {
        string q = Uri.EscapeDataString(question);
        return name switch
        {
            "Perplexity" => $"https://www.perplexity.ai/search?q={q}",
            "Copilot"    => $"https://www.bing.com/search?q={q}&showconv=1",
            _            => url
        };
    }

    private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

    private async Task AskClaudeAsync(int idx, string question, string key)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            req.Headers.Add("x-api-key", key);
            req.Headers.Add("anthropic-version", "2023-06-01");
            req.Content = new StringContent(JsonSerializer.Serialize(new
            {
                model = "claude-sonnet-4-6", max_tokens = 1024,
                messages = new[] { new { role = "user", content = question } }
            }), Encoding.UTF8, "application/json");
            var resp = await _httpClient.SendAsync(req);
            string json = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) { SetResponse(idx, $"❌ Ошибка Claude ({(int)resp.StatusCode}): {json}", BrushError); return; }
            using var doc = JsonDocument.Parse(json);
            SetResponse(idx, doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? "", BrushAnswer);
        }
        catch (Exception ex) { SetResponse(idx, $"❌ Ошибка: {ex.Message}", BrushError); }
    }

    private async Task AskGeminiAsync(int idx, string question, string key)
    {
        try
        {
            string url  = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key={key}";
            string body = $"{{\"contents\":[{{\"parts\":[{{\"text\":{JsonSerializer.Serialize(question)}}}]}}]}}";
            var resp = await _httpClient.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"));
            string json = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) { SetResponse(idx, $"❌ Ошибка Gemini ({(int)resp.StatusCode}): {json}", BrushError); return; }
            using var doc = JsonDocument.Parse(json);
            SetResponse(idx, doc.RootElement.GetProperty("candidates")[0].GetProperty("content")
                .GetProperty("parts")[0].GetProperty("text").GetString() ?? "", BrushAnswer);
        }
        catch (Exception ex) { SetResponse(idx, $"❌ Ошибка: {ex.Message}", BrushError); }
    }

    private async Task AskDeepSeekAsync(int idx, string question, string key)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "https://api.deepseek.com/chat/completions")
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    model = "deepseek-chat",
                    messages = new[] { new { role = "user", content = question } },
                    stream = false
                }), Encoding.UTF8, "application/json")
            };
            req.Headers.Add("Authorization", $"Bearer {key}");
            var resp = await _httpClient.SendAsync(req);
            string json = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) { SetResponse(idx, $"❌ Ошибка DeepSeek ({(int)resp.StatusCode}): {json}", BrushError); return; }
            using var doc = JsonDocument.Parse(json);
            SetResponse(idx, doc.RootElement.GetProperty("choices")[0].GetProperty("message")
                .GetProperty("content").GetString() ?? "", BrushAnswer);
        }
        catch (Exception ex) { SetResponse(idx, $"❌ Ошибка: {ex.Message}", BrushError); }
    }

    private async Task AskPerplexityAsync(int idx, string question, string key)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "https://api.perplexity.ai/chat/completions")
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    model = "llama-3.1-sonar-small-128k-online",
                    messages = new[] { new { role = "user", content = question } }
                }), Encoding.UTF8, "application/json")
            };
            req.Headers.Add("Authorization", $"Bearer {key}");
            var resp = await _httpClient.SendAsync(req);
            string json = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) { SetResponse(idx, $"❌ Ошибка Perplexity ({(int)resp.StatusCode}): {json}", BrushError); return; }
            using var doc = JsonDocument.Parse(json);
            SetResponse(idx, doc.RootElement.GetProperty("choices")[0].GetProperty("message")
                .GetProperty("content").GetString() ?? "", BrushAnswer);
        }
        catch (Exception ex) { SetResponse(idx, $"❌ Ошибка: {ex.Message}", BrushError); }
    }

    private void SetResponse(int idx, string text, WMedia.Brush brush)
    {
        Dispatcher.Invoke(() => { aiRows[idx].ResponseBrush = brush; aiRows[idx].Response = text; });
    }

    private void SaveAllResponses()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Ответы ИИ — {DateTime.Now:dd.MM.yyyy HH:mm}");
        sb.AppendLine(new string('═', 60));
        sb.AppendLine($"Вопрос: {txAiQuestion.Text.Trim()}");
        sb.AppendLine();
        bool hasAny = false;
        foreach (var r in aiRows)
        {
            string txt = r.Response.Trim();
            if (string.IsNullOrEmpty(txt)) continue;
            sb.AppendLine(new string('─', 60));
            sb.AppendLine($"■ {r.Name}");
            sb.AppendLine();
            sb.AppendLine(txt);
            sb.AppendLine();
            hasAny = true;
        }
        if (!hasAny) { MessageBox.Show("Нет ответов для сохранения.", "Пусто", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        SaveToFile(sb.ToString(), $"AI_{DateTime.Now:yyyyMMdd_HHmm}.txt");
    }

    private void SaveSingleResponse(AiRow r)
    {
        string txt = r.Response.Trim();
        if (string.IsNullOrEmpty(txt)) { MessageBox.Show("Нет ответа для сохранения.", "Пусто", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var sb = new StringBuilder();
        sb.AppendLine($"{r.Name} — {DateTime.Now:dd.MM.yyyy HH:mm}");
        sb.AppendLine(new string('═', 60));
        sb.AppendLine($"Вопрос: {txAiQuestion.Text.Trim()}");
        sb.AppendLine();
        sb.AppendLine(txt);
        SaveToFile(sb.ToString(), $"{r.Name}_{DateTime.Now:yyyyMMdd_HHmm}.txt");
    }

    private static void SaveToFile(string content, string fileName)
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CommunalBills");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, fileName);
        File.WriteAllText(path, content, Encoding.UTF8);
        Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
    }

    private void ShowApiKeyDialog()
    {
        var dlg = new Window
        {
            Title = "API ключи для ИИ", Width = 580, Height = 430,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Window.GetWindow(this),
            ResizeMode = ResizeMode.NoResize, Background = Frozen(30, 38, 48)
        };
        var panel = new StackPanel { Margin = new Thickness(16) };

        TextBox Row(string label, string linkText, string linkUrl, string value)
        {
            panel.Children.Add(new TextBlock
            {
                Text = label, FontWeight = FontWeights.Bold, FontSize = 13,
                Foreground = BrushLight, Margin = new Thickness(0, 8, 0, 2)
            });
            var tx = new TextBox { Text = value, FontSize = 13, Height = 30, Padding = new Thickness(4, 3, 4, 3) };
            panel.Children.Add(tx);
            var link = new TextBlock { Margin = new Thickness(0, 3, 0, 4), FontSize = 11 };
            var hl = new Hyperlink(new Run(linkText)) { NavigateUri = new Uri(linkUrl) };
            hl.RequestNavigate += (_, e) => Process.Start(new ProcessStartInfo { FileName = e.Uri.AbsoluteUri, UseShellExecute = true });
            link.Inlines.Add(hl);
            panel.Children.Add(link);
            return tx;
        }

        var tc = Row("Claude (Anthropic) API:", "Получить на console.anthropic.com", "https://console.anthropic.com/settings/keys", _claudeApiKey);
        var tg = Row("Gemini API:",     "Бесплатно на aistudio.google.com", "https://aistudio.google.com/apikey",     _geminiApiKey);
        var td = Row("DeepSeek API:",   "Получить на platform.deepseek.com", "https://platform.deepseek.com/api_keys", _deepSeekApiKey);
        var tp = Row("Perplexity API:", "Получить на perplexity.ai/settings/api", "https://www.perplexity.ai/settings/api", _perplexityApiKey);

        var btnOk = new Button
        {
            Content = "Сохранить", Width = 140, Height = 36,
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0),
            FontWeight = FontWeights.Bold, IsDefault = true
        };
        btnOk.Click += (_, _) =>
        {
            _claudeApiKey = tc.Text.Trim(); _geminiApiKey = tg.Text.Trim();
            _deepSeekApiKey = td.Text.Trim(); _perplexityApiKey = tp.Text.Trim();
            SaveAiSettings();
            dlg.DialogResult = true;
        };
        panel.Children.Add(btnOk);
        dlg.Content = panel;
        dlg.ShowDialog();
    }

    private void LoadAiSettings()
    {
        string path = Path.Combine(AiDataDir, "ai_settings.json");
        if (!File.Exists(path)) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            if (root.TryGetProperty("ClaudeKey",     out var c)) _claudeApiKey     = c.GetString() ?? "";
            if (root.TryGetProperty("GeminiKey",     out var g)) _geminiApiKey     = g.GetString() ?? "";
            if (root.TryGetProperty("DeepSeekKey",   out var d)) _deepSeekApiKey   = d.GetString() ?? "";
            if (root.TryGetProperty("PerplexityKey", out var p)) _perplexityApiKey = p.GetString() ?? "";
        }
        catch { }
    }

    private void SaveAiSettings()
    {
        Directory.CreateDirectory(AiDataDir);
        string path = Path.Combine(AiDataDir, "ai_settings.json");
        var obj = new
        {
            ClaudeKey = _claudeApiKey, GeminiKey = _geminiApiKey,
            DeepSeekKey = _deepSeekApiKey, PerplexityKey = _perplexityApiKey
        };
        File.WriteAllText(path, JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }));
    }
}
