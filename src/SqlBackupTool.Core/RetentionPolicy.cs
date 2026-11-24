namespace SqlBackupTool.Core;

public static class RetentionPolicy
{
    /// <summary>
    /// Decides which backup files to delete. Rules (all must allow deletion):
    /// <list type="bullet">
    /// <item>The newest backup is never deleted.</item>
    /// <item>KeepLast &gt; 0: the newest KeepLast backups are kept.</item>
    /// <item>OlderThanDays &gt; 0: backups newer than that are kept.</item>
    /// <item>Both rules 0: nothing is deleted.</item>
    /// <item>Only *.bak files are ever candidates.</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<RetentionCandidate> SelectForDeletion(
        IEnumerable<RetentionCandidate> candidates, RetentionSettings rules, DateTime now)
    {
        if (!rules.IsActive) return [];

        var ordered = candidates
            .Where(c => c.Path.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
            .GroupBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.MaxBy(c => c.Finished)!)
            .OrderByDescending(c => c.Finished)
            .ToList();

        var keep = Math.Max(1, rules.KeepLast);
        var result = new List<RetentionCandidate>();
        for (var i = keep; i < ordered.Count; i++)
        {
            var c = ordered[i];
            if (rules.OlderThanDays > 0 && (now - c.Finished).TotalDays <= rules.OlderThanDays) continue;
            result.Add(c);
        }
        return result;
    }
}
