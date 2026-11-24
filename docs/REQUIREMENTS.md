# SQL Server Backup Tool – Requirements (revised)

## Original request
> Create a Windows application that can back up the selected DB from the listed
> backups available. The connection string will be stored in the config file. Use C#.

## Issues found in the original request
| # | Issue | Resolution |
|---|-------|------------|
| 1 | "back up the selected db **from the listed backups**" is ambiguous – backing up *from* a backup is a restore. | Interpreted as: list the **databases** on the server, user selects one or more, app backs them up. Existing backups (history) are listed per database for reference. Restore is out of scope (see suggestions). |
| 2 | Database engine not stated. | Microsoft SQL Server (2016+, incl. Express and SQL on Linux). |
| 3 | Backup destination not stated. | Configurable folder. **The path is on the SQL Server machine** – SQL Server's service account writes the file, not this app. |
| 4 | Backup type not stated. | Full backup, `COPY_ONLY` by default so the tool never breaks an existing log-backup/differential chain. |
| 5 | No success/failure feedback defined. | Progress bar, live log, log file, optional verification. |
| 6 | Security of the connection string not mentioned. | Windows Authentication recommended; SQL auth passwords in plain-text config are a risk (see suggestions). |

## Functional requirements
- **FR1** Read the connection string from `appsettings.json` (`ConnectionStrings:SqlServer`). The app connects to `master`.
- **FR2** On start and on *Refresh*, show server name, version and edition.
- **FR3** List databases: name, state, recovery model, size (MB), last full backup date. System databases hidden unless `IncludeSystemDatabases = true`. `tempdb` is never listed.
- **FR4** User selects one or more databases (checkboxes).
- **FR5** Selecting a database shows its last 50 backups from `msdb` (date, type, size, copy-only, file).
- **FR6** Backup options, defaults from config, editable in UI: destination folder, Copy-only, Compression (auto-disabled on Express), Checksum, Verify after backup.
- **FR7** File name from pattern `{database}_{timestamp}.bak`; invalid file-name characters replaced.
- **FR8** Backups run sequentially, asynchronously (UI stays responsive) with per-database % progress and a Cancel button.
- **FR9** If Verify is on, run `RESTORE VERIFYONLY` on the produced file.
- **FR10** Each run is written to a daily log file under `logs\` next to the exe; a summary (succeeded/failed) is shown at the end.
- **FR11** Offline / restoring databases cannot be backed up and are reported, not attempted silently.

## Non-functional requirements
- C# / .NET 10 (LTS), Windows Forms, `Microsoft.Data.SqlClient`.
- All T-SQL parameterised (database name and path passed as parameters) – no SQL injection via names.
- No command timeout for backups (large DBs).
- Required SQL permission: `db_backupoperator` on each DB (or `sysadmin`), plus read on `msdb` for history.

## v2 additions
- **FR12 Multiple servers**: `Servers[]` in config, chosen from a drop-down, managed in Settings. Each server can have its own backup folder.
- **FR13 Encrypted connection strings**: Windows DPAPI (user or machine scope), set from Settings or with `--protect-config`.
- **FR14 Retention**: after a successful backup, delete this tool's old backups (keep the newest N; delete the rest once older than D days; the newest is always kept). Backups the tool did not make are never touched.
- **FR15 Command line**: `--backup/--list/--protect-config/--help` with exit codes. The UI can copy the command line and create a Task Scheduler task.
- **FR16 Restore**: full backups from the history or from a file, either over the original database (with confirmation) or as a renamed copy with its files relocated. System databases are blocked.
- **FR17 App icon**.

## Out of scope
Differential/log backups, cloud (URL) targets, point-in-time restore.
