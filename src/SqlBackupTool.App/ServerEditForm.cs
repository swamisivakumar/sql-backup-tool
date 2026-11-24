using Microsoft.Data.SqlClient;
using SqlBackupTool.Core;

namespace SqlBackupTool.App;

/// <summary>Add/edit one server profile. The connection string can be stored DPAPI-encrypted.</summary>
public sealed class ServerEditForm : Form
{
    private readonly HashSet<string> _otherNames;
    private readonly TextBox _txtName = new();
    private readonly TextBox _txtServer = new();
    private readonly ComboBox _cboAuth = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _txtUser = new();
    private readonly TextBox _txtPassword = new() { UseSystemPasswordChar = true };
    private readonly CheckBox _chkTrust = new() { Text = "Trust server certificate (needed for most self-signed / local servers)", AutoSize = true };
    private readonly TextBox _txtFolder = new() { PlaceholderText = "optional — overrides the default backup folder" };
    private readonly CheckBox _chkEncrypt = new() { Text = "Encrypt the connection string in appsettings.json (Windows DPAPI)", AutoSize = true };
    private readonly CheckBox _chkMachine = new() { Text = "Any Windows user on this PC may decrypt it (for tasks run by another account)", AutoSize = true };
    private readonly Button _btnTest = new() { Text = "&Test connection", AutoSize = true };
    private readonly Label _lblTest = new() { AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
    private readonly SqlConnectionStringBuilder _builder;

    public ServerProfile Result { get; private set; } = new();

    public ServerEditForm(ServerProfile? existing, IEnumerable<string> otherNames)
    {
        _otherNames = new HashSet<string>(otherNames, StringComparer.OrdinalIgnoreCase);
        Text = existing is null ? "Add server" : $"Edit server — {existing.Name}";
        Ui.ApplyDialogDefaults(this, new Size(720, 470));
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;

        _cboAuth.Items.AddRange(["Windows Authentication", "SQL Server Authentication"]);

        var plain = "";
        if (existing is not null)
        {
            try { plain = existing.GetConnectionString(); }
            catch (InvalidOperationException ex) { Shown += (_, _) => Ui.Error(this, ex.Message + "\n\nEnter the connection details again."); }
        }
        try { _builder = new SqlConnectionStringBuilder(plain); }
        catch (ArgumentException) { _builder = new SqlConnectionStringBuilder(); }

        _txtName.Text = existing?.Name ?? "";
        _txtServer.Text = _builder.DataSource is { Length: > 0 } ds ? ds : "localhost";
        _cboAuth.SelectedIndex = existing is null || _builder.IntegratedSecurity ? 0 : 1;
        _txtUser.Text = _builder.UserID;
        _txtPassword.Text = _builder.Password;
        _chkTrust.Checked = existing is null || _builder.TrustServerCertificate;
        _txtFolder.Text = existing?.BackupFolder ?? "";
        _chkEncrypt.Checked = existing?.IsProtected ?? false;
        _chkMachine.Checked = SecretProtector.IsMachineScope(existing?.ConnectionString);

        _cboAuth.SelectedIndexChanged += (_, _) => UpdateEnabled();
        _chkEncrypt.CheckedChanged += (_, _) => UpdateEnabled();
        _btnTest.Click += async (_, _) => await TestAsync();
        UpdateEnabled();

        var grid = Ui.Form(
            ("Name:", _txtName),
            ("Server / instance:", _txtServer),
            ("Authentication:", _cboAuth),
            ("User name:", _txtUser),
            ("Password:", _txtPassword),
            ("", _chkTrust),
            ("Backup folder:", _txtFolder),
            ("", _chkEncrypt),
            ("", _chkMachine),
            ("", Ui.Flow(_btnTest, _lblTest)));

        var ok = new Button { Text = "OK", AutoSize = true };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) => Accept();
        AcceptButton = ok;
        CancelButton = cancel;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(8) };
        buttons.Controls.AddRange([cancel, ok]);

        var root = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        root.Controls.Add(grid);
        Controls.Add(root);
        Controls.Add(buttons);
    }

    private void UpdateEnabled()
    {
        var sql = _cboAuth.SelectedIndex == 1;
        _txtUser.Enabled = _txtPassword.Enabled = sql;
        _chkMachine.Enabled = _chkEncrypt.Checked;
    }

    private string BuildConnectionString()
    {
        var b = new SqlConnectionStringBuilder(_builder.ConnectionString)
        {
            DataSource = _txtServer.Text.Trim(),
            InitialCatalog = "master",
            TrustServerCertificate = _chkTrust.Checked,
        };
        if (_cboAuth.SelectedIndex == 0)
        {
            b.IntegratedSecurity = true;
            b.Remove("User ID");
            b.Remove("Password");
        }
        else
        {
            b.IntegratedSecurity = false;
            b.UserID = _txtUser.Text.Trim();
            b.Password = _txtPassword.Text;
        }
        return b.ConnectionString;
    }

    private async Task TestAsync()
    {
        _btnTest.Enabled = false;
        _lblTest.ForeColor = SystemColors.ControlText;
        _lblTest.Text = "Connecting...";
        try
        {
            var info = await new BackupService(BuildConnectionString()).GetServerInfoAsync();
            _lblTest.ForeColor = Color.ForestGreen;
            _lblTest.Text = $"✔ {info.ServerName} — SQL Server {info.Version} ({info.Edition})";
        }
        catch (Exception ex)
        {
            _lblTest.ForeColor = Color.Firebrick;
            _lblTest.Text = "✖ " + ex.Message.Split('\n')[0];
        }
        finally { _btnTest.Enabled = true; }
    }

    private void Accept()
    {
        var name = _txtName.Text.Trim();
        if (name.Length == 0) { Ui.Info(this, "Enter a name for this server."); _txtName.Focus(); return; }
        if (_otherNames.Contains(name)) { Ui.Info(this, $"A server named '{name}' already exists."); _txtName.Focus(); return; }
        if (_txtServer.Text.Trim().Length == 0) { Ui.Info(this, "Enter the server name."); _txtServer.Focus(); return; }
        if (_cboAuth.SelectedIndex == 1 && _txtUser.Text.Trim().Length == 0) { Ui.Info(this, "Enter the SQL login name."); _txtUser.Focus(); return; }

        var cs = BuildConnectionString();
        if (!_chkEncrypt.Checked && _cboAuth.SelectedIndex == 1 &&
            MessageBox.Show(this, "The password will be stored in plain text in appsettings.json. Continue without encryption?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.No)
            return;

        Result = new ServerProfile
        {
            Name = name,
            ConnectionString = _chkEncrypt.Checked ? SecretProtector.Protect(cs, _chkMachine.Checked) : cs,
            BackupFolder = string.IsNullOrWhiteSpace(_txtFolder.Text) ? null : _txtFolder.Text.Trim(),
        };
        DialogResult = DialogResult.OK;
    }
}
