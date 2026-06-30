using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace PrivatUtilities;

public partial class MainWindow : Window
{
    // Ссылка Privat24: если задан идентификатор домохозяйства — открываем форму адреса,
    // иначе общую страницу платежей.
    private string Privat24Url =>
        string.IsNullOrWhiteSpace(_settings.Privat24Hos)
            ? "https://next.privat24.ua/payments"
            : "https://next.privat24.ua/payments/form/" +
              Uri.EscapeDataString("{\"hos\":\"" + _settings.Privat24Hos + "\"}");

    private readonly AppSettings _settings;
    private readonly ObservableCollection<Provider> _providers;
    private bool _webReady;
    private bool _bankWebReady;
    private Task<CoreWebView2Environment>? _envTask;

    public MainWindow()
    {
        InitializeComponent();

        _settings = AppSettings.Load();
        _providers = new ObservableCollection<Provider>(_settings.Providers);
        ProvidersList.ItemsSource = _providers;

        TxtAddressBox.Text = _settings.Address;
        CmbRegion.ItemsSource = Provider.Regions;
        CmbRegion.SelectedItem = string.IsNullOrEmpty(_settings.Region) ? null : _settings.Region;
        CmbRegion.SelectionChanged += (_, _) => SaveSettings();

        CmbBank.ItemsSource = BankService.All();
        CmbBank.SelectedIndex = 0;

        CmbPeriod.ItemsSource = new[] { "Этот месяц", "Этот год", "Всё время" };
        CmbPeriod.SelectedIndex = 1;   // по умолчанию — этот год (полезнее, чем «за всё время»)

        UpdateListInfo();
        Closing += (_, _) => SaveSettings();
        Loaded += (_, _) =>
        {
            _ = UpdateChecker.CheckAsync(this);
            BuildUtilityExpenses();
        };
    }

    // ───────── Вкладка 4: расходы из «Денег» (только чтение) ─────────

    // Строка для отображения в таблице (отформатированные поля).
    private sealed record ExpenseRow(string DateText, string Name, string Account, string AmountText);

    private void BuildUtilityExpenses()
    {
        // во время инициализации окна CmbPeriod может ещё не существовать
        if (TxtExpensesTotal is null) return;

        if (!HomeAccountingReader.IsAvailable)
        {
            TxtExpensesTotal.Text = "Расходы на коммуналку (из «Денег»)";
            TxtExpensesInfo.Text  = "Программа «Деньги» не установлена — фактические расходы недоступны.";
            ExpensesList.ItemsSource = null;
            return;
        }

        // Фильтр периода: 0 — этот месяц, 1 — этот год, 2 — всё время
        int period = CmbPeriod?.SelectedIndex ?? 1;
        string monthPrefix = DateTime.Today.ToString("yyyy-MM");
        string yearPrefix  = DateTime.Today.ToString("yyyy");
        string periodLabel = period == 0 ? "за этот месяц" : period == 1 ? "за этот год" : "за всё время";

        var items = HomeAccountingReader.GetUtilityExpenses()
            .Where(e => period switch
            {
                0 => e.Date.StartsWith(monthPrefix, StringComparison.Ordinal),
                1 => e.Date.StartsWith(yearPrefix,  StringComparison.Ordinal),
                _ => true
            })
            .ToList();
        decimal total = HomeAccountingReader.Total(items);

        TxtExpensesTotal.Text = $"Потрачено на коммуналку {periodLabel}: {total:N2} ₴";

        var rows = items.Select(e =>
        {
            string name = !string.IsNullOrWhiteSpace(e.Subcategory) ? e.Subcategory : e.Category;
            if (!string.IsNullOrWhiteSpace(e.Note)) name += " — " + e.Note;
            return new ExpenseRow(FmtDate(e.Date), name, e.Account, e.Amount.ToString("N2"));
        }).ToList();

        ExpensesList.ItemsSource = rows;
        TxtExpensesInfo.Text = rows.Count == 0
            ? "Записей нет за выбранный период. Расходы ведутся в программе «Деньги» (категория «Коммунальные услуги»)."
            : $"Записей: {rows.Count} ({periodLabel}). Источник: «Деньги», только чтение.";
    }

    private void BtnRefreshExpenses_Click(object sender, RoutedEventArgs e) => BuildUtilityExpenses();

    private void CmbPeriod_SelectionChanged(object sender, SelectionChangedEventArgs e) => BuildUtilityExpenses();

    private void BtnOpenMoney_Click(object sender, RoutedEventArgs e) => HomeAccountingReader.OpenHomeAccounting();

    // «Записать платёж в «Деньги»»: услуга + сумма → черновик в общую очередь.
    // Запись делает сама «Деньги» при следующем запуске (правило офиса).
    private void BtnAddPayment_Click(object sender, RoutedEventArgs e)
    {
        var services = _providers.Select(p => p.Service)
            .Concat(new[] { "Газ", "Электроэнергия", "Вода холодная", "Отопление", "Квартплата", "Мусор" })
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim()).Distinct().OrderBy(s => s).ToList();

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = "Услуга:", Margin = new Thickness(0, 0, 0, 3) });
        var cmb = new ComboBox { IsEditable = true, ItemsSource = services, Margin = new Thickness(0, 0, 0, 10) };
        if (services.Count > 0) cmb.SelectedIndex = 0;
        stack.Children.Add(cmb);
        stack.Children.Add(new TextBlock { Text = "Сумма, ₴:", Margin = new Thickness(0, 0, 0, 3) });
        var tbAmount = new TextBox { Margin = new Thickness(0, 0, 0, 10) };
        stack.Children.Add(tbAmount);
        stack.Children.Add(new TextBlock { Text = "Дата:", Margin = new Thickness(0, 0, 0, 3) });
        var tbDate = new TextBox { Text = DateTime.Today.ToString("dd.MM.yyyy"), Margin = new Thickness(0, 0, 0, 10) };
        stack.Children.Add(tbDate);
        var err = new TextBlock { Foreground = Brushes.OrangeRed, FontSize = 11, TextWrapping = TextWrapping.Wrap,
                                  Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8) };
        stack.Children.Add(err);

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "Записать", Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Отмена", Padding = new Thickness(14, 5, 14, 5), IsCancel = true };
        row.Children.Add(ok); row.Children.Add(cancel);
        stack.Children.Add(row);

        var win = new Window
        {
            Title = "Записать платёж в «Деньги»", Owner = this, Width = 380,
            SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize, Content = stack
        };

        ok.Click += (_, _) =>
        {
            var svc = (cmb.Text ?? "").Trim();
            if (string.IsNullOrEmpty(svc)) { err.Text = "Укажите услугу."; err.Visibility = Visibility.Visible; return; }
            if (!decimal.TryParse((tbAmount.Text ?? "").Replace(',', '.'),
                    System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var amt) || amt <= 0)
            { err.Text = "Введите сумму больше нуля."; err.Visibility = Visibility.Visible; return; }
            DateTime d = DateTime.TryParse(tbDate.Text, out var dt) ? dt : DateTime.Today;
            ExpenseDraftQueue.Add(new ExpenseDraft(Guid.NewGuid().ToString("N"), "Коммуналка",
                d.ToString("yyyy-MM-dd"), "Коммунальные услуги", svc, (double)amt, $"{svc} (оплата)"));
            win.DialogResult = true;
        };
        cancel.Click += (_, _) => win.DialogResult = false;

        if (win.ShowDialog() != true) return;

        var ans = MessageBox.Show(this,
            "Платёж отправлен в «Деньги».\n\nОткрыть «Деньги» сейчас для подтверждения?",
            "Записать платёж", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ans == MessageBoxResult.Yes) HomeAccountingReader.OpenHomeAccounting();
    }

    // Дата из «Денег» хранится как yyyy-MM-dd → показываем dd.MM.yyyy
    private static string FmtDate(string iso)
        => DateTime.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture,
               System.Globalization.DateTimeStyles.None, out var dt)
           ? dt.ToString("dd.MM.yyyy") : iso;

    // Одно окружение WebView2 на оба браузера (создаётся один раз).
    private Task<CoreWebView2Environment> EnsureEnvAsync()
    {
        return _envTask ??= CreateEnvAsync();

        static async Task<CoreWebView2Environment> CreateEnvAsync()
        {
            var dataFolder = Path.Combine(AppSettings.DataDir, "WebView2");
            Directory.CreateDirectory(dataFolder);
            return await CoreWebView2Environment.CreateAsync(null, dataFolder);
        }
    }

    // Ленивая инициализация: WebView2 на скрытой вкладке создаётся только при её показе.
    private async void Web_Loaded(object sender, RoutedEventArgs e)
    {
        if (_webReady) return;
        try
        {
            SetStatus("Инициализация браузера…");
            await Web.EnsureCoreWebView2Async(await EnsureEnvAsync());
            _webReady = true;
            Web.CoreWebView2.NavigationCompleted += (_, ev) =>
                SetStatus(ev.IsSuccess ? "Страница загружена." : $"Ошибка загрузки ({ev.WebErrorStatus}).");
            SetStatus("Загрузка Privat24…");
            Web.CoreWebView2.Navigate(Privat24Url);
        }
        catch (Exception ex)
        {
            SetStatus("Ошибка браузера: " + ex.Message);
        }
    }

    private async void WebBank_Loaded(object sender, RoutedEventArgs e)
    {
        if (_bankWebReady) return;
        try
        {
            await WebBank.EnsureCoreWebView2Async(await EnsureEnvAsync());
            _bankWebReady = true;
            // Сразу открываем выбранный сервис, чтобы вкладка не была пустой.
            if (CmbBank.SelectedItem is BankService b)
            {
                TxtBankStatus.Text = "Открываю: " + b.Name;
                WebBank.CoreWebView2.Navigate(b.Url);
            }
        }
        catch (Exception ex)
        {
            TxtBankStatus.Text = "Ошибка браузера: " + ex.Message;
        }
    }

    // ───────── Вкладка 1: список ─────────

    private void UpdateListInfo()
        => TxtListInfo.Text = $"Поставщиков в списке: {_providers.Count}. " +
                              "Поля редактируются, изменения сохраняются автоматически.";

    private void Field_LostFocus(object sender, RoutedEventArgs e) => SaveSettings();

    private void BtnSuggest_Click(object sender, RoutedEventArgs e)
    {
        var region = CmbRegion.SelectedItem as string;
        if (string.IsNullOrEmpty(region))
        {
            MessageBox.Show("Сначала выберите область.", "Подсказка",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var add = MessageBox.Show(
            "Добавить типовых поставщиков для области «" + region + "»?\n\n" +
            "Существующие строки не удаляются. Названия — черновик, потом поправьте.",
            "Подставить по области", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (add != MessageBoxResult.Yes) return;

        // Добавляем только те услуги, которых ещё нет в списке (по названию услуги)
        var existing = _providers.Select(p => p.Service).ToHashSet();
        foreach (var p in Provider.SuggestForRegion(region))
            if (!existing.Contains(p.Service))
                _providers.Add(p);

        UpdateListInfo();
        SaveSettings();
    }

    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        _providers.Add(new Provider { Service = "Новая услуга", Company = "", Account = "", Url = "" });
        UpdateListInfo();
        SaveSettings();
    }

    private void DeleteProvider_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is Provider p)
        {
            var ok = MessageBox.Show($"Удалить «{p.Service}»?", "Удаление",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (ok != MessageBoxResult.Yes) return;
            _providers.Remove(p);
            UpdateListInfo();
            SaveSettings();
        }
    }

    private void CopyAccount_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is Provider p && !string.IsNullOrWhiteSpace(p.Account))
        {
            try { Clipboard.SetText(p.Account.Trim()); } catch { /* буфер занят */ }
        }
    }

    private void OpenUrl_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is Provider p && !string.IsNullOrWhiteSpace(p.Url))
            OpenInBrowser(p.Url.Trim());
    }

    private void BtnSaveAccounts_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
        MessageBox.Show("Список и номера счетов сохранены.", "Сохранено",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SaveSettings()
    {
        _settings.Address = TxtAddressBox.Text;
        _settings.Region = CmbRegion.SelectedItem as string ?? "";
        _settings.Providers = _providers.ToList();
        _settings.Save();
    }

    private static void OpenInBrowser(string url)
    {
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) url = "https://" + url;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show("Не удалось открыть ссылку:\n" + ex.Message); }
    }

    // ───────── Вкладка 2: Privat24 ─────────

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (!_webReady) return;
        SetStatus("Обновление…");
        Web.CoreWebView2.Navigate(Privat24Url);
    }

    private void BtnHome_Click(object sender, RoutedEventArgs e)
    {
        if (_webReady) Web.CoreWebView2.Navigate("https://next.privat24.ua/");
    }

    private async void BtnLogout_Click(object sender, RoutedEventArgs e)
    {
        if (!_webReady) return;
        var ok = MessageBox.Show(
            "Выйти из Privat24 и очистить сохранённую сессию?",
            "Выход", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ok != MessageBoxResult.Yes) return;
        try
        {
            await Web.CoreWebView2.Profile.ClearBrowsingDataAsync();
            SetStatus("Сессия очищена.");
            Web.CoreWebView2.Navigate(Privat24Url);
        }
        catch (Exception ex) { SetStatus("Не удалось очистить сессию: " + ex.Message); }
    }

    private async void BtnDump_Click(object sender, RoutedEventArgs e)
    {
        if (!_webReady) return;
        try
        {
            var html = await Web.CoreWebView2.ExecuteScriptAsync("document.documentElement.outerHTML");
            var decoded = JsonSerializer.Deserialize<string>(html) ?? "";
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "privat24_page.html");
            File.WriteAllText(path, decoded);
            SetStatus("HTML сохранён: " + path);
        }
        catch (Exception ex) { SetStatus("Не удалось сохранить HTML: " + ex.Message); }
    }

    // ───────── Вкладка 3: другой банк ─────────

    private void BtnOpenBank_Click(object sender, RoutedEventArgs e)
    {
        if (!_bankWebReady) { TxtBankStatus.Text = "Браузер ещё загружается…"; return; }
        if (CmbBank.SelectedItem is BankService b)
        {
            TxtBankStatus.Text = "Открываю: " + b.Name;
            WebBank.CoreWebView2.Navigate(b.Url);
        }
    }

    private void BtnRefreshBank_Click(object sender, RoutedEventArgs e)
    {
        if (_bankWebReady && WebBank.CoreWebView2 is not null) WebBank.CoreWebView2.Reload();
    }

    private void SetStatus(string text)
        => Dispatcher.BeginInvoke(() => TxtStatus.Text = text, DispatcherPriority.Background);
}
