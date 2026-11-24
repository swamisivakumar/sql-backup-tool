using SqlBackupTool.Core;

namespace SqlBackupTool.App;

/// <summary>Restores a full backup, either over the original database or as a new copy with relocated files.</summary>
public sealed class RestoreForm : Form
{
    private readonly BackupService _service;
    private readonly ServerInfo _server;
    private readonly string _browseFolder;
    private readonly Action<string> _mainLog;
    private readonly int _initialPosition;
    private readonly string? _sourceDb;
    private IReadOnlyList<BackupHeader> _headers = [];
    private IReadOnlyList<RestoreFile> _files = [];
    private bool _running;

    private readonly TextBox _txtFile = new() { Dock = DockStyle.Fill };
    private readonly Button _btnBrowse = new() { Text = "...", Width = 32 };
    private readonly Button _btnRead = new() { Text = "Read file", AutoSize = true };
    private readonly ComboBox _cboSet = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly TextBox _txtTarget = new() { Dock = DockStyle.Fill };
    private readonly Label _lblMode = new() { AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.CellSelect,
        BackgroundColor = SystemColors.Window,
    };
    private readonly CheckBox _chkClose = new() { Text = "Close existing connections to the target database", AutoSize = true, Checked = true };
    private readonly Button _btnRestore = new() { Text = "&Restore", AutoSize = true, Enabled = false };
    private readonly Button _btnClose = new() { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Fill };
    private readonly Label _lblStatus = new() { AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
    private readonly TextBox _txtLog = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas", 9f) };

    public bool Restored { get; private set; }

    public RestoreForm(BackupService service, ServerInfo server, string? backupPath, int position, string? sourceDb,
        string browseFolder, Action<string> mainLog)
    {
        _service = service;
        _server = server;
        _browseFolder = browseFolder;
        _mainLog = mainLog;
        _initialPosition = position;
        _sourceDb = sourceDb;

        Text = $"Restore database — {server.ServerName}";
        Ui.ApplyDialogDefaults(this, new Size(980, 680));
        _btnRestore.Font = Ui.Bold;

        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Logical", HeaderText = "Logical name", ReadOnly = true, FillWeight = 20 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Type", HeaderText = "Type", ReadOnly = true, FillWeight = 8 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Original", HeaderText = "Original file", ReadOnly = true, FillWeight = 36 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Target", HeaderText = "Restore as (editable)", FillWeight = 36 });

        var fileRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true };
        fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fileRow.Controls.Add(_txtFile, 0, 0);
        fileRow.Controls.Add(_btnBrowse, 1, 0);
        fileRow.Controls.Add(_btnRead, 2, 0);

        var form = Ui.Form(
            ("Backup file (on server):", fileRow),
            ("Backup set:", _cboSet),
            ("Restore as database:", _txtTarget),
            ("", _lblMode));

        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, AutoSize = true };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actions.Controls.Add(_btnRestore, 0, 0);
        actions.Controls.Add(_progress, 1, 0);
        actions.Controls.Add(_lblStatus, 2, 0);
        actions.Controls.Add(_btnClose, 3, 0);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), RowCount = 6 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        root.Controls.Add(form);
        root.Controls.Add(Ui.Label("Database files", bold: true));
        root.Controls.Add(_grid);
        root.Controls.Add(_chkClose);
        root.Controls.Add(actions);
        root.Controls.Add(_txtLog);
        Controls.Add(root);
        CancelButton = _btnClose;

        _txtFile.Text = backupPath ?? "";
        _btnBrowse.Click += (_, _) => Browse();
        _btnRead.Click += async (_, _) => await ReadFileAsync();
        _cboSet.SelectedIndexChanged += async (_, _) => await LoadFilesAsync();
        _txtTarget.TextChanged += (_, _) => Replan();
        _btnRestore.Click += async (_, _) => await RestoreAsync();
        FormClosing += (_, e) =>
        {
            if (_running) { e.Cancel = true; Ui.Info(this, "A restore is running. Please wait for it to finish."); }
        };
        Shown += async (_, _) => { if (_txtFile.Text.Length > 0) await ReadFileAsync(); else _txtFile.Focus(); };
    }

    private BackupHeader? SelectedHeader => _cboSet.SelectedIndex >= 0 ? _headers[_cboSet.SelectedIndex] : null;

    private void Browse()
    {
        if (!_server.IsLocal)
        {
            Ui.Info(this, $"SQL Server runs on another machine ({_server.MachineName}). Type the backup file path as seen from that server.");
            return;
        }
        using var dlg = new OpenFileDialog
        {
            Filter = "SQL Server backups (*.bak)|*.bak|All files (*.*)|*.*",
            InitialDirectory = Directory.Exists(_browseFolder) ? _browseFolder : "",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) _txtFile.Text = dlg.FileName;
    }

    private async Task ReadFileAsync()
    {
        var path = _txtFile.Text.Trim();
        if (path.Length == 0) return;
        SetBusy(true, "Reading backup file...");
        try
        {
            _headers = await _service.GetBackupHeadersAsync(path);
            _cboSet.Items.Clear();
            foreach (var h in _headers)
                _cboSet.Items.Add($"#{h.Position}: {h.Type} backup of [{h.DatabaseName}] finished {h.FinishDate:yyyy-MM-dd HH:mm:ss}");
            if (_headers.Count == 0) { SetBusy(false, "No backup sets in file"); return; }
            var idx = _headers.ToList().FindIndex(h => h.Position == _initialPosition);
            SetBusy(false, "");
            _cboSet.SelectedIndex = idx >= 0 ? idx : _headers.Count - 1;
        }
        catch (Exception ex)
        {
            SetBusy(false, "Could not read file");
            Log("ERROR: " + ex.Message);
        }
    }

    private async Task LoadFilesAsync()
    {
        if (SelectedHeader is not { } h) return;
        SetBusy(true, "Reading file list...");
        try
        {
            _files = await _service.GetBackupFilesAsync(_txtFile.Text.Trim(), h.Position);
            if (_txtTarget.Text.Trim().Length == 0) _txtTarget.Text = _sourceDb ?? h.DatabaseName;
            SetBusy(false, "");
            Replan();
        }
        catch (Exception ex)
        {
            _files = [];
            SetBusy(false, "Could not read file list");
            Log("ERROR: " + ex.Message);
        }
    }

    /// <summary>Same name as in the backup → keep original paths; different name → new, non-colliding paths.</summary>
    private void Replan()
    {
        _grid.Rows.Clear();
        if (SelectedHeader is not { } h || _files.Count == 0) { UpdateRestoreEnabled(); return; }

        var target = _txtTarget.Text.Trim();
        var sameDb = target.Equals(h.DatabaseName, StringComparison.OrdinalIgnoreCase);
        IReadOnlyList<FileMove> moves;
        if (sameDb)
        {
            moves = _files.Select(f => new FileMove(f.LogicalName, f.Type, f.PhysicalName)).ToList();
            _lblMode.Text = $"Restoring over [{h.DatabaseName}] — files keep their original locations. The existing database will be overwritten.";
            _lblMode.ForeColor = Color.DarkOrange;
        }
        else
        {
            string FolderOf(string type) => BackupScript.GetServerDirectory(
                (_files.FirstOrDefault(f => f.Type == type) ?? _files[0]).PhysicalName);
            var dataFolder = _server.DefaultDataPath is { Length: > 0 } d ? d : FolderOf("D");
            var logFolder = _server.DefaultLogPath is { Length: > 0 } l ? l : FolderOf("L");
            moves = target.Length == 0 ? [] : BackupScript.PlanMoves(_files, target, dataFolder, logFolder);
            _lblMode.Text = target.Length == 0 ? "" : $"Restoring as a copy named [{target}] — files are relocated so the original database is not touched.";
            _lblMode.ForeColor = Color.ForestGreen;
        }

        for (var i = 0; i < _files.Count && i < moves.Count; i++)
            _grid.Rows.Add(_files[i].LogicalName, TypeName(_files[i].Type), _files[i].PhysicalName, moves[i].TargetPath);
        UpdateRestoreEnabled();
    }

    private static string TypeName(string t) => t switch { "D" => "Data", "L" => "Log", "F" => "Full-text", "S" => "FILESTREAM", _ => t };

    private void UpdateRestoreEnabled() =>
        _btnRestore.Enabled = !_running && _grid.Rows.Count > 0 && _txtTarget.Text.Trim().Length > 0;

    private async Task RestoreAsync()
    {
        if (SelectedHeader is not { } h) return;
        if (h.Type != "Full") { Ui.Info(this, "Only full backups can be restored with this tool."); return; }
        var target = _txtTarget.Text.Trim();
        if (BackupScript.SystemDatabases.Contains(target)) { Ui.Info(this, "Restoring system databases is not supported."); return; }

        _grid.EndEdit();
        var moves = new List<FileMove>();
        for (var i = 0; i < _files.Count; i++)
        {
            var path = (_grid.Rows[i].Cells["Target"].Value as string)?.Trim();
            if (string.IsNullOrEmpty(path)) { Ui.Info(this, $"Enter a target path for '{_files[i].LogicalName}'."); return; }
            moves.Add(new FileMove(_files[i].LogicalName, _files[i].Type, path));
        }

        bool exists;
        try { exists = await _service.DatabaseExistsAsync(target); }
        catch (Exception ex) { Log("ERROR: " + ex.Message); return; }

        var confirm = exists
            ? $"Database [{target}] EXISTS and will be OVERWRITTEN with the backup from {h.FinishDate:yyyy-MM-dd HH:mm}.\n\n" +
              (_chkClose.Checked ? "All users will be disconnected.\n\n" : "") + "This cannot be undone. Continue?"
            : $"Restore backup of [{h.DatabaseName}] from {h.FinishDate:yyyy-MM-dd HH:mm} as new database [{target}]?";
        if (MessageBox.Show(this, confirm, Text, MessageBoxButtons.YesNo, exists ? MessageBoxIcon.Warning : MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        var request = new RestoreRequest(_txtFile.Text.Trim(), h.Position, target, Replace: exists, _chkClose.Checked, moves);
        _running = true;
        SetBusy(true, "Restoring... 0%");
        var pct = new Progress<int>(p => { _progress.Value = Math.Clamp(p, 0, 100); _lblStatus.Text = $"Restoring... {p}%"; });
        var result = await _service.RestoreAsync(request, pct, new Progress<string>(Log));
        _running = false;

        if (result.Success)
        {
            Restored = true;
            SetBusy(false, "Restore completed");
            Log($"==== RESTORE of [{target}] SUCCEEDED in {result.Duration:hh\\:mm\\:ss}");
            Ui.Info(this, $"Database [{target}] was restored successfully.");
        }
        else
        {
            SetBusy(false, "Restore failed");
            Log($"==== RESTORE of [{target}] FAILED — {result.Error}");
            Ui.Error(this, $"Restore failed:\n\n{result.Error}");
        }
    }

    private void SetBusy(bool busy, string status)
    {
        _lblStatus.Text = status;
        foreach (var c in new Control[] { _txtFile, _btnBrowse, _btnRead, _cboSet, _txtTarget, _grid, _chkClose })
            c.Enabled = !busy;
        _btnClose.Enabled = !_running;
        if (!busy) _progress.Value = _running ? _progress.Value : (status == "Restore completed" ? 100 : 0);
        UseWaitCursor = busy && !_running;
        UpdateRestoreEnabled();
    }

    private void Log(string message)
    {
        _mainLog(message);
        _txtLog.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");
    }
}
