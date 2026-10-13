namespace Tizi.Core;

public enum LogCategory { Application, Proxy, Dns }
public sealed record ActivityEntry(DateTimeOffset Time, LogCategory Category, string Message);
public sealed class ActivityLog
{
    private readonly Queue<ActivityEntry> _entries = new();
    private DateTimeOffset _latest = DateTimeOffset.MinValue;
    private bool _outOfOrder;
    public LogSettings Settings { get; set; } = new();
    public IReadOnlyCollection<ActivityEntry> Entries => _entries;
    public void Add(string message, DateTimeOffset? time = null)
    {
        var category = message.Contains("[DNS]", StringComparison.OrdinalIgnoreCase) || message.Contains("dns query", StringComparison.OrdinalIgnoreCase) || message.Contains("dns resolve", StringComparison.OrdinalIgnoreCase) ? LogCategory.Dns
            : message.Contains("[TCP]", StringComparison.OrdinalIgnoreCase) || message.Contains("[UDP]", StringComparison.OrdinalIgnoreCase) || message.Contains(" --> ", StringComparison.Ordinal) ? LogCategory.Proxy : LogCategory.Application;
        if (category == LogCategory.Proxy && !Settings.Proxy || category == LogCategory.Dns && !Settings.Dns) return;
        var stamp = time ?? DateTimeOffset.Now; _outOfOrder |= stamp < _latest; if (stamp > _latest) _latest = stamp;
        _entries.Enqueue(new(stamp, category, message.Length <= 4000 ? message : message[..4000])); Trim(DateTimeOffset.Now);
    }
    public void Load(IEnumerable<ActivityEntry> entries) { foreach (var entry in entries.OrderBy(e => e.Time).TakeLast(Settings.MaxEntries)) _entries.Enqueue(entry); _outOfOrder = true; Trim(DateTimeOffset.Now); }
    public void Trim(DateTimeOffset now)
    {
        var cutoff = now.AddDays(-Settings.RetentionDays);
        if (_outOfOrder)
        {
            var ordered = _entries.OrderBy(e => e.Time).ToArray(); _entries.Clear(); foreach (var entry in ordered) _entries.Enqueue(entry); _outOfOrder = false;
        }
        while (_entries.TryPeek(out var first) && (_entries.Count > Settings.MaxEntries || Settings.AutoDelete && first.Time < cutoff)) _entries.Dequeue();
    }
    public void Clear(LogCategory? category = null)
    {
        var keep = category is null ? [] : _entries.Where(e => e.Category != category).ToArray(); _entries.Clear(); foreach (var entry in keep) _entries.Enqueue(entry);
    }
}
