using System.IO;
using System.Text.Json;

namespace PrivatUtilities;

// Сохраняемые настройки: адрес, область, идентификатор Privat24 и список поставщиков
public class AppSettings
{
    public string Address { get; set; } = "";
    public string Region { get; set; } = "";

    // Идентификатор домохозяйства в Privat24 (из ссылки payments/form/{"hos":"..."}).
    // Пусто → вкладка Privat24 открывает общую страницу платежей.
    public string Privat24Hos { get; set; } = "";

    public List<Provider> Providers { get; set; } = new();

    // Папка пользовательских данных приложения
    public static string DataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CommunalBills");

    private static string FilePath => Path.Combine(DataDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (s is not null) return s;
            }
        }
        catch { /* битый файл — берём значения по умолчанию */ }

        // Первый запуск: нейтральный шаблон без персональных данных
        return new AppSettings
        {
            Address = "",
            Region = "",
            Privat24Hos = "",
            Providers = Provider.ExampleDefault(),
        };
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* нет прав на запись — тихо игнорируем */ }
    }
}
