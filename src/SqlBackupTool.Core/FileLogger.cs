namespace SqlBackupTool.Core;

/// <summary>Appends lines to logs\backup_yyyyMMdd.log. Thread-safe.</summary>
public sealed class FileLogger(string directory)
{
    private readonly object _lock = new();

    public string CurrentFile => Path.Combine(directory, $"backup_{DateTime.Now:yyyyMMdd}.log");

    public void Write(string message)
    {
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(directory);
                File.AppendAllText(CurrentFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
            }
        }
        catch (IOException) { /* logging must never break a backup */ }
        catch (UnauthorizedAccessException) { }
    }
}
