using System.Xml.Linq;

namespace SqlBackupTool.Core;

/// <summary>
/// Builds a Task Scheduler 1.2 task definition. Used with "schtasks /Create /XML", which (unlike /TR)
/// has no 261-character limit on the command.
/// </summary>
public static class ScheduledTaskXml
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    /// <param name="weeklyOn">null = daily; otherwise weekly on that day.</param>
    public static string Build(string command, string arguments, string workingDirectory, string userId,
        DateTime firstRun, DayOfWeek? weeklyOn, string description)
    {
        XElement E(string name, params object[] content) => new(Ns + name, content);

        var schedule = weeklyOn is { } day
            ? E("ScheduleByWeek", E("DaysOfWeek", E(day.ToString())), E("WeeksInterval", 1))
            : E("ScheduleByDay", E("DaysInterval", 1));

        var doc = new XDocument(
            E("Task", new XAttribute("version", "1.2"),
                E("RegistrationInfo", E("Description", description), E("Author", userId)),
                E("Triggers", E("CalendarTrigger",
                    E("StartBoundary", firstRun.ToString("yyyy-MM-ddTHH:mm:ss")),
                    E("Enabled", "true"),
                    schedule)),
                E("Principals", E("Principal", new XAttribute("id", "Author"),
                    E("UserId", userId),
                    E("LogonType", "InteractiveToken"),
                    E("RunLevel", "LeastPrivilege"))),
                E("Settings",
                    E("MultipleInstancesPolicy", "IgnoreNew"),
                    E("DisallowStartIfOnBatteries", "false"),
                    E("StopIfGoingOnBatteries", "false"),
                    E("StartWhenAvailable", "true"),
                    E("ExecutionTimeLimit", "PT12H"),
                    E("Enabled", "true")),
                E("Actions", new XAttribute("Context", "Author"),
                    E("Exec", E("Command", command), E("Arguments", arguments), E("WorkingDirectory", workingDirectory)))));
        return doc.Declaration + doc.ToString();
    }
}
