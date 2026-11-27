using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace OpenClicky.Windows.Platform;

internal sealed record LauncherFile(string Path, string Kind)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public void Open() => Process.Start(new ProcessStartInfo(Path) { UseShellExecute = true });
    public bool Reveal() => ExplorerReveal.Select(Path);
}

internal sealed class LauncherFiles(Func<IEnumerable<string?>> folders)
{
    private readonly SemaphoreSlim gate = new(1);
    private DateTime retryIndex;
    private LauncherFile[] fallback = [];
    private DateTime fallbackExpires;

    public async Task<IReadOnlyList<LauncherFile>> Search(string term, CancellationToken token)
    {
        if (term.Trim().Length < 2) return [];
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                if (DateTime.UtcNow >= retryIndex)
                {
                    try { return Indexed(term, token); }
                    catch (COMException) { token.ThrowIfCancellationRequested(); retryIndex = DateTime.UtcNow.AddMinutes(1); }
                }
                if (fallbackExpires < DateTime.UtcNow)
                {
                    var entries = new List<LauncherFile>();
                    foreach (var root in folders().Where(Directory.Exists))
                    {
                        try
                        {
                            foreach (var path in Directory.EnumerateFileSystemEntries(root!).Take(300))
                            {
                                token.ThrowIfCancellationRequested();
                                entries.Add(new(path, Directory.Exists(path) ? "Dossier" : Path.GetExtension(path)));
                            }
                        }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                    }
                    fallback = entries.ToArray();
                    fallbackExpires = DateTime.UtcNow.AddMinutes(1);
                }
                return fallback.Where(f => f.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase)).Take(12).ToList();
            }, token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    private static List<LauncherFile> Indexed(string term, CancellationToken token)
    {
        dynamic connection = Activator.CreateInstance(Type.GetTypeFromProgID("ADODB.Connection", true)!)!;
        dynamic? records = null;
        try
        {
            connection.ConnectionTimeout = 2;
            connection.CommandTimeout = 2;
            connection.Open("Provider=Search.CollatorDSO;Extended Properties='Application=Windows';");
            token.ThrowIfCancellationRequested();
            var escaped = term.Replace("'", "''").Replace("%", "[%]").Replace("_", "[_]");
            records = connection.Execute("SELECT TOP 12 System.ItemPathDisplay, System.ItemTypeText FROM SYSTEMINDEX " +
                $"WHERE System.ItemUrl LIKE 'file:%' AND System.FileName LIKE '%{escaped}%' ORDER BY System.DateModified DESC");
            var files = new List<LauncherFile>();
            while (!records.EOF)
            {
                token.ThrowIfCancellationRequested();
                if (records.Fields.Item(0).Value is string path && Path.IsPathFullyQualified(path))
                    files.Add(new(path, Convert.ToString(records.Fields.Item(1).Value) ?? ""));
                records.MoveNext();
            }
            return files;
        }
        finally
        {
            if (records is not null) { records.Close(); Marshal.FinalReleaseComObject(records); }
            try { if (connection.State != 0) connection.Close(); }
            finally { Marshal.FinalReleaseComObject(connection); }
        }
    }
}
