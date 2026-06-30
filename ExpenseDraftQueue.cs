using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PrivatUtilities;

// ╔══════════════════════════════════════════════════════════════════════════╗
// ║  Общая очередь черновиков расходов Senior Hub (КАНОНИЧЕСКАЯ копия).         ║
// ║  «Коммуналка» кладёт сюда оплату кнопкой «Записать в «Деньги»».             ║
// ║  «Деньги» при запуске показывают список и по подтверждению создают          ║
// ║  расходы у себя — запись делает только «Деньги» (правило офиса).            ║
// ║  Файл: %LOCALAPPDATA%\SeniorHub\pending_expenses.json                       ║
// ╚══════════════════════════════════════════════════════════════════════════╝
public sealed record ExpenseDraft(
    string Id, string Source, string Date,
    string Category, string? Subcategory, double Amount, string Note);

public static class ExpenseDraftQueue
{
    private static string QueuePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SeniorHub", "pending_expenses.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static List<ExpenseDraft> ReadAll()
    {
        try
        {
            if (!File.Exists(QueuePath)) return new();
            var json = File.ReadAllText(QueuePath);
            if (string.IsNullOrWhiteSpace(json)) return new();
            return JsonSerializer.Deserialize<List<ExpenseDraft>>(json) ?? new();
        }
        catch { return new(); }
    }

    public static void AddRange(IEnumerable<ExpenseDraft> drafts)
    {
        var list = ReadAll();
        list.AddRange(drafts);
        Write(list);
    }

    public static void Add(ExpenseDraft draft) => AddRange(new[] { draft });

    public static void RemoveByIds(IEnumerable<string> ids)
    {
        var set = new HashSet<string>(ids);
        Write(ReadAll().Where(d => !set.Contains(d.Id)).ToList());
    }

    public static int Count() => ReadAll().Count;

    private static void Write(List<ExpenseDraft> list)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(QueuePath)!);
            var tmp = QueuePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(list, JsonOpts));
            if (File.Exists(QueuePath)) File.Delete(QueuePath);
            File.Move(tmp, QueuePath);
        }
        catch { }
    }
}
