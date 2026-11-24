using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using SqlBackupTool.Core;

namespace SqlBackupTool.App;

/// <summary>Creates a Windows Task Scheduler task that runs the backup from the command line.</summary>
public sealed class ScheduleForm : Form
{
    private readonly string _arguments;
    private readonly TextBox _txtName = new();
    private readonly ComboBox _cboFrequency = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _cboDay = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DateTimePicker _time = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Width = 80 };
    private readonly TextBox _txtCommand = new() { Multiline = true, ReadOnly = true, Height = 80, ScrollBars = ScrollBars.Vertical };

    public string ResultMessage { get; private set; } = "";

    public ScheduleForm(string serverName, string arguments, bool userScopedEncryption)
    {
        _arguments = arguments;
        Text = "Schedule backup";
        Ui.ApplyDialogDefaults(this, new Size(720, 450));
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;

        _txtName.Text = $"SQL Backup - {serverName}";
        _cboFrequency.Items.AddRange(["Daily", "Weekly"]);
        _cboFrequency.SelectedIndex = 0;
        foreach (var d in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday })
            _cboDay.Items.Add(d);
        _cboDay.SelectedItem = DayOfWeek.Sunday;
        _cboDay.Enabled = false;
        _time.Value = DateTime.Today.AddHours(2);
        _cboFrequency.SelectedIndexChanged += (_, _) => _cboDay.Enabled = _cboFrequency.SelectedIndex == 1;
        _txtCommand.Text = CliOptions.Quote(Environment.ProcessPath!) + " " + arguments;

        var note = new Label
        {
            AutoSize = true, MaximumSize = new Size(660, 0), ForeColor = SystemColors.GrayText,
            Text = "The task runs as your Windows account while you are logged on. To run it when logged off, open Task Scheduler → " +
                   "Task Scheduler Library → SqlBackupTool, and choose \"Run whether user is logged on or not\"." +
                   (userScopedEncryption ? "\n\nThis server's connection string is encrypted for your user only — keep the task under your account, " +
                                           "or re-save the server with machine-scope encryption." : "") +
                   "\n\nResults go to the logs folder next to SqlBackupTool.exe.",
        };

        var grid = Ui.Form(
            ("Task name:", _txtName),
            ("Run:", Ui.Flow(_cboFrequency, Ui.Label("on"), _cboDay, Ui.Label("at"), _time)),
            ("Command:", _txtCommand),
            ("", note));

        var ok = new Button { Text = "Create task", AutoSize = true };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) => Create();
        AcceptButton = ok;
        CancelButton = cancel;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(8) };
        buttons.Controls.AddRange([cancel, ok]);

        var root = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        root.Controls.Add(grid);
        Controls.Add(root);
        Controls.Add(buttons);
    }

    private void Create()
    {
        var name = string.Concat(_txtName.Text.Trim().Select(c => "\\/:*?\"<>|".Contains(c) ? '_' : c));
        if (name.Length == 0) { Ui.Info(this, "Enter a task name."); return; }

        var exe = Environment.ProcessPath!;
        var first = DateTime.Today.Add(_time.Value.TimeOfDay);
        if (first <= DateTime.Now) first = first.AddDays(1);
        DayOfWeek? weekly = _cboFrequency.SelectedIndex == 1 ? (DayOfWeek)_cboDay.SelectedItem! : null;
        var xml = ScheduledTaskXml.Build(exe, _arguments, Path.GetDirectoryName(exe)!, WindowsIdentity.GetCurrent().Name,
            first, weekly, "SQL Server Backup Tool – unattended backup");

        var xmlPath = Path.Combine(Path.GetTempPath(), $"SqlBackupTool_task_{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(xmlPath, xml, Encoding.Unicode);
            var psi = new ProcessStartInfo("schtasks.exe")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (var a in new[] { "/Create", "/F", "/TN", $"SqlBackupTool\\{name}", "/XML", xmlPath }) psi.ArgumentList.Add(a);

            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0) { Ui.Error(this, "Task Scheduler reported an error:\n\n" + output.Trim()); return; }

            var when = weekly is { } d ? $"every {d} at {first:HH:mm}" : $"daily at {first:HH:mm}";
            ResultMessage = $"Scheduled task 'SqlBackupTool\\{name}' created ({when}): {_txtCommand.Text}";
            Ui.Info(this, $"Scheduled task 'SqlBackupTool\\{name}' was created ({when}).");
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            Ui.Error(this, "Could not create the scheduled task:\n\n" + ex.Message);
        }
        finally
        {
            try { File.Delete(xmlPath); } catch (IOException) { }
        }
    }
}
