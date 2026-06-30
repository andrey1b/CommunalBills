using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace PrivatUtilities;

// Одна строка фактического коммунального расхода из базы «Денег» (HomeAccounting).
public sealed record MoneyExpense(
    string Date, string Category, string Subcategory,
    string Account, decimal Amount, string Note);

/// <summary>
/// Чтение ФАКТИЧЕСКИХ коммунальных расходов из базы HomeAccounting («Деньги»).
/// ТОЛЬКО ЧТЕНИЕ. По правилу офиса расходы ведутся исключительно в «Деньгах»;
/// «Коммуналка» их лишь показывает. База: %LocalAppData%\HomeAccounting\homeaccounting.db.
/// </summary>
public static class HomeAccountingReader
{
    private static string DbPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HomeAccounting", "homeaccounting.db");

    // Категория «Денег» с коммунальными расходами.
    private static readonly string[] UtilityCategories = { "Коммунальные услуги" };

    public static bool IsAvailable => File.Exists(DbPath);

    public static IReadOnlyList<MoneyExpense> GetUtilityExpenses(int? year = null)
    {
        var result = new List<MoneyExpense>();
        if (!IsAvailable) return result;
        try
        {
            // Mode=ReadOnly — гарантия, что «Коммуналка» ничего не изменит в чужой базе.
            using var conn = new SqliteConnection($"Data Source={DbPath};Mode=ReadOnly;Cache=Shared");
            conn.Open();

            var inCats = string.Join(",", UtilityCategories.Select((_, i) => "$c" + i));
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT e.date,
                       COALESCE(c.name,'') AS cat,
                       COALESCE(s.name,'') AS sub,
                       COALESCE(a.name,'') AS acc,
                       e.amount * (1 - e.discount/100.0) AS amt,
                       COALESCE(e.note,'') AS note
                FROM expenses e
                LEFT JOIN categories    c ON c.id = e.category_id
                LEFT JOIN subcategories s ON s.id = e.subcategory_id
                LEFT JOIN accounts      a ON a.id = e.account_id
                WHERE c.name IN ({inCats}) AND c.type = 'expense'
                  AND ($year IS NULL OR substr(e.date,1,4) = $year)
                ORDER BY e.date DESC, e.id DESC";
            for (int i = 0; i < UtilityCategories.Length; i++)
                cmd.Parameters.AddWithValue("$c" + i, UtilityCategories[i]);
            cmd.Parameters.AddWithValue("$year", (object?)year?.ToString() ?? DBNull.Value);

            using var r = cmd.ExecuteReader();
            while (r.Read())
                result.Add(new MoneyExpense(
                    r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                    Convert.ToDecimal(r.GetDouble(4)), r.GetString(5)));
        }
        catch { /* база недоступна/иная схема — вернём что есть */ }
        return result;
    }

    public static decimal Total(IEnumerable<MoneyExpense> items)
    {
        decimal t = 0;
        foreach (var i in items) t += i.Amount;
        return t;
    }

    // Открыть «Деньги» (HomeAccounting), если установлена.
    public static void OpenHomeAccounting()
    {
        try
        {
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "HomeAccounting", "HomeAccounting.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "HomeAccounting", "HomeAccounting.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "HomeAccounting", "HomeAccounting.exe"),
            };
            var exe = Array.Find(candidates, File.Exists);
            if (exe is not null)
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        }
        catch { }
    }
}
