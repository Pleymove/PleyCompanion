namespace OpenClicky.Core.Launcher;

public sealed record LocalTool(string Id, string Name, Action Execute);

public sealed class LocalToolRegistry
{
    private readonly Dictionary<string, LocalTool> tools = new(StringComparer.Ordinal);
    public void Register(LocalTool tool) => tools.Add(tool.Id, tool);
    public IReadOnlyCollection<LocalTool> Tools => tools.Values;
    public void Execute(string id) => tools[id].Execute();
}

public static class LauncherSearch
{
    public static int Score(string name, string query)
    {
        var text = name.ToUpperInvariant();
        var term = query.Trim().ToUpperInvariant();
        if (term.Length == 0) return 1;
        if (text == term) return 100;
        if (text.StartsWith(term, StringComparison.Ordinal)) return 85;
        if (text.Contains(term, StringComparison.Ordinal)) return 70;
        if (term.Length >= 3 && text.Split(' ').Any(word => OneEdit(word, term))) return 60;
        var matched = 0;
        foreach (var letter in text)
            if (letter == term[matched] && ++matched == term.Length) return 35;
        return 0;
    }

    private static bool OneEdit(string a, string b)
    {
        if (a.Length < b.Length) return OneEdit(b, a);
        if (a.Length - b.Length > 1) return false;
        var i = 0;
        while (i < b.Length && a[i] == b[i]) i++;
        if (i == b.Length) return true;
        if (a.Length != b.Length) return a.AsSpan(i + 1).SequenceEqual(b.AsSpan(i));
        return a.AsSpan(i + 1).SequenceEqual(b.AsSpan(i + 1)) ||
            (i + 1 < b.Length && a[i] == b[i + 1] && a[i + 1] == b[i] &&
             a.AsSpan(i + 2).SequenceEqual(b.AsSpan(i + 2)));
    }
}
