using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Brush   = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace PrivatUtilities;

// Строка вкладки «Спросить у ИИ» (единый узнаваемый элемент SeniorHub).
internal sealed class AiRow : INotifyPropertyChanged
{
    public string Name  { get; init; } = "";
    public string Url   { get; init; } = "";
    public string ApiId { get; init; } = "";
    public Brush  HeaderBrush { get; init; } = Brushes.Gray;

    private bool _enabled;
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }

    private string _response = "";
    public string Response { get => _response; set { if (Set(ref _response, value)) UpdateStats(); } }

    private Brush _responseBrush = Brushes.Black;
    public Brush ResponseBrush { get => _responseBrush; set => Set(ref _responseBrush, value); }

    private string _stats = "Символов: 0 | Слов: 0";
    public string Stats { get => _stats; private set => Set(ref _stats, value); }

    private void UpdateStats()
    {
        string t = _response ?? "";
        int chars = t.Length;
        int words = string.IsNullOrWhiteSpace(t)
            ? 0
            : t.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
        Stats = $"Символов: {chars:N0} | Слов: {words:N0}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
