"""Builds the customer user guide PDF:  python docs/guide/build_guide.py  ->  docs/SQL_Backup_Tool_User_Guide.pdf"""
from pathlib import Path
from xml.sax.saxutils import escape

from reportlab.lib import colors
from reportlab.lib.enums import TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.units import mm
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import (BaseDocTemplate, CondPageBreak, Frame, Image, KeepTogether, ListFlowable, ListItem,
                                NextPageTemplate, PageBreak, PageTemplate, Paragraph, Spacer, Table, TableStyle,
                                XPreformatted)
from reportlab.platypus.tableofcontents import TableOfContents

HERE = Path(__file__).parent
IMG = HERE / "images"
OUT = HERE.parent / "SQL_Backup_Tool_User_Guide.pdf"

# ---------------------------------------------------------------- fonts & colours
FONTS = r"C:\Windows\Fonts"
for name, file in [("UI", "segoeui.ttf"), ("UI-Bold", "segoeuib.ttf"), ("UI-Semi", "seguisb.ttf"),
                   ("UI-Italic", "segoeuii.ttf"), ("UI-Light", "segoeuil.ttf"), ("Mono", "consola.ttf"), ("Mono-Bold", "consolab.ttf")]:
    pdfmetrics.registerFont(TTFont(name, f"{FONTS}\\{file}"))
pdfmetrics.registerFontFamily("UI", normal="UI", bold="UI-Bold", italic="UI-Italic", boldItalic="UI-Bold")
pdfmetrics.registerFontFamily("Mono", normal="Mono", bold="Mono-Bold", italic="Mono", boldItalic="Mono-Bold")

BLUE = colors.HexColor("#1E40AF")
BLUE_MID = colors.HexColor("#2563EB")
BLUE_LIGHT = colors.HexColor("#EFF4FF")
GREEN = colors.HexColor("#059669")
GREEN_LIGHT = colors.HexColor("#ECFDF5")
AMBER = colors.HexColor("#B45309")
AMBER_LIGHT = colors.HexColor("#FFF7E6")
INK = colors.HexColor("#1F2937")
MUTED = colors.HexColor("#6B7280")
RULE = colors.HexColor("#D1D5DB")
CODE_BG = colors.HexColor("#F3F4F6")

PAGE_W, PAGE_H = A4
MARGIN = 20 * mm
CONTENT_W = PAGE_W - 2 * MARGIN

# ---------------------------------------------------------------- styles
body = ParagraphStyle("body", fontName="UI", fontSize=10, leading=15, textColor=INK, spaceAfter=6, alignment=TA_LEFT)
small = ParagraphStyle("small", parent=body, fontSize=8.5, leading=12, textColor=MUTED)
cell = ParagraphStyle("cell", parent=body, fontSize=9, leading=12.5, spaceAfter=0)
cell_head = ParagraphStyle("cellhead", parent=cell, fontName="UI-Semi", textColor=BLUE)
caption = ParagraphStyle("caption", parent=small, alignment=1, spaceBefore=3, spaceAfter=12)
h1 = ParagraphStyle("h1", fontName="UI-Semi", fontSize=19, leading=24, textColor=BLUE, spaceBefore=4, spaceAfter=10)
h2 = ParagraphStyle("h2", fontName="UI-Semi", fontSize=12.5, leading=17, textColor=INK, spaceBefore=12, spaceAfter=5)
code_style = ParagraphStyle("code", fontName="Mono", fontSize=8.6, leading=12, textColor=INK)
toc_l1 = ParagraphStyle("toc1", parent=body, fontName="UI-Semi", fontSize=10.5, leading=20, leftIndent=0)
toc_l2 = ParagraphStyle("toc2", parent=body, fontSize=9.5, leading=15, leftIndent=14, textColor=MUTED)


class Guide(BaseDocTemplate):
    def __init__(self, path):
        super().__init__(str(path), pagesize=A4, leftMargin=MARGIN, rightMargin=MARGIN, topMargin=22 * mm, bottomMargin=20 * mm,
                         title="SQL Server Backup Tool — User Guide", author="SQL Server Backup Tool",
                         subject="User guide for the SQL Server Backup Tool, version 2.0")
        frame = Frame(MARGIN, 20 * mm, CONTENT_W, PAGE_H - 42 * mm, id="body", leftPadding=0, rightPadding=0, topPadding=0, bottomPadding=0)
        self.addPageTemplates([PageTemplate("cover", [frame], onPage=draw_cover),
                               PageTemplate("normal", [frame], onPage=draw_page)])

    def afterFlowable(self, f):
        if isinstance(f, Paragraph) and f.style.name in ("h1", "h2"):
            level = 0 if f.style.name == "h1" else 1
            text = f.getPlainText()
            key = f"h{id(f)}"
            self.canv.bookmarkPage(key)
            self.canv.addOutlineEntry(text, key, level=level, closed=level > 0)
            self.notify("TOCEntry", (level, text, self.page, key))


def draw_cover(c, doc):
    c.saveState()
    c.setFillColor(BLUE)
    c.rect(0, PAGE_H - 125 * mm, PAGE_W, 125 * mm, stroke=0, fill=1)
    c.setFillColor(BLUE_MID)
    c.rect(0, PAGE_H - 129 * mm, PAGE_W, 4 * mm, stroke=0, fill=1)
    c.drawImage(str(IMG / "icon.png"), MARGIN, PAGE_H - 70 * mm, 38 * mm, 38 * mm, mask="auto")
    c.setFillColor(colors.white)
    c.setFont("UI-Semi", 32)
    c.drawString(MARGIN, PAGE_H - 88 * mm, "SQL Server Backup Tool")
    c.setFont("UI-Light", 20)
    c.drawString(MARGIN, PAGE_H - 101 * mm, "User Guide")
    c.setFont("UI", 11)
    c.drawString(MARGIN, PAGE_H - 113 * mm, "Version 2.0   ·   November 2026")

    c.setFillColor(INK)
    c.setFont("UI-Semi", 12)
    y = PAGE_H - 150 * mm
    c.drawString(MARGIN, y, "In this guide")
    c.setFont("UI", 10.5)
    items = ["Back up one or many SQL Server databases with a few clicks",
             "Restore a backup over the original database or as a safe copy",
             "Manage several SQL Servers, with encrypted connection details",
             "Delete old backups automatically",
             "Schedule unattended backups with Windows Task Scheduler",
             "Command-line reference, configuration and troubleshooting"]
    for i, t in enumerate(items):
        yy = y - 9 * mm - i * 7.5 * mm
        c.setFillColor(GREEN)
        c.circle(MARGIN + 2 * mm, yy + 1.3 * mm, 1.3 * mm, stroke=0, fill=1)
        c.setFillColor(INK)
        c.drawString(MARGIN + 7 * mm, yy, t)
    c.setStrokeColor(RULE)
    c.line(MARGIN, 30 * mm, PAGE_W - MARGIN, 30 * mm)
    c.setFillColor(MUTED)
    c.setFont("UI", 8.5)
    c.drawString(MARGIN, 24 * mm, "Screenshots use sample servers and databases. Your server names, folders and databases will differ.")
    c.restoreState()


def draw_page(c, doc):
    c.saveState()
    c.setStrokeColor(RULE)
    c.setLineWidth(0.6)
    c.line(MARGIN, PAGE_H - 14 * mm, PAGE_W - MARGIN, PAGE_H - 14 * mm)
    c.setFont("UI", 8)
    c.setFillColor(MUTED)
    c.drawString(MARGIN, PAGE_H - 12 * mm, "SQL Server Backup Tool — User Guide")
    c.drawRightString(PAGE_W - MARGIN, PAGE_H - 12 * mm, "Version 2.0")
    c.line(MARGIN, 14 * mm, PAGE_W - MARGIN, 14 * mm)
    c.drawRightString(PAGE_W - MARGIN, 9.5 * mm, f"Page {doc.page}")
    c.restoreState()


# ---------------------------------------------------------------- building blocks
story = []
fig_no = [0]


def P(text, style=body):
    story.append(Paragraph(text, style))


def H1(text, need=60):
    story.append(CondPageBreak(need * mm))
    story.append(Paragraph(text, h1))


def H2(text):
    story.append(CondPageBreak(35 * mm))
    story.append(Paragraph(text, h2))


def bullets(items, numbered=False):
    flow = ListFlowable([ListItem(Paragraph(t, body), leftIndent=14, value=None) for t in items],
                        bulletType="1" if numbered else "bullet", start="1" if numbered else "•",
                        leftIndent=14, bulletFontName="UI-Semi" if numbered else "UI", bulletFontSize=10,
                        bulletColor=BLUE, spaceBefore=0, spaceAfter=4)
    story.append(flow)


def steps(items):
    bullets(items, numbered=True)


def callout(kind, text):
    palette = {"Note": (BLUE_MID, BLUE_LIGHT), "Important": (AMBER, AMBER_LIGHT), "Tip": (GREEN, GREEN_LIGHT)}
    edge, fill = palette[kind]
    para = Paragraph(f'<font name="UI-Semi" color="{edge.hexval()}">{kind}.</font>  {text}', cell)
    t = Table([[para]], colWidths=[CONTENT_W])
    t.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, -1), fill),
        ("LINEBEFORE", (0, 0), (0, -1), 3, edge),
        ("LEFTPADDING", (0, 0), (-1, -1), 10), ("RIGHTPADDING", (0, 0), (-1, -1), 10),
        ("TOPPADDING", (0, 0), (-1, -1), 7), ("BOTTOMPADDING", (0, 0), (-1, -1), 7),
    ]))
    story.append(Spacer(1, 3))
    story.append(KeepTogether(t))
    story.append(Spacer(1, 8))


def table(header, rows, widths, mono_cols=()):
    def mk(text, col, head=False):
        if head:
            return Paragraph(text, cell_head)
        if col in mono_cols:
            return Paragraph(f'<font name="Mono" size="8.4">{text}</font>', cell)
        return Paragraph(text, cell)

    data = [[mk(h, i, True) for i, h in enumerate(header)]] + [[mk(c, i) for i, c in enumerate(r)] for r in rows]
    t = Table(data, colWidths=[w * CONTENT_W for w in widths], repeatRows=1)
    t.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, 0), BLUE_LIGHT),
        ("LINEBELOW", (0, 0), (-1, 0), 0.8, BLUE_MID),
        ("LINEBELOW", (0, 1), (-1, -1), 0.4, RULE),
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("LEFTPADDING", (0, 0), (-1, -1), 6), ("RIGHTPADDING", (0, 0), (-1, -1), 6),
        ("TOPPADDING", (0, 0), (-1, -1), 5), ("BOTTOMPADDING", (0, 0), (-1, -1), 5),
    ]))
    story.append(t)
    story.append(Spacer(1, 10))


def code(text):
    pre = XPreformatted(escape(text), code_style)
    t = Table([[pre]], colWidths=[CONTENT_W])
    t.setStyle(TableStyle([("BACKGROUND", (0, 0), (-1, -1), CODE_BG), ("LEFTPADDING", (0, 0), (-1, -1), 9),
                           ("RIGHTPADDING", (0, 0), (-1, -1), 9), ("TOPPADDING", (0, 0), (-1, -1), 7),
                           ("BOTTOMPADDING", (0, 0), (-1, -1), 7), ("BOX", (0, 0), (-1, -1), 0.4, RULE)]))
    story.append(t)
    story.append(Spacer(1, 9))


def figure(file, text, width=1.0):
    fig_no[0] += 1
    img = Image(str(IMG / file))
    w = CONTENT_W * width
    img.drawHeight = img.drawHeight * w / img.drawWidth
    img.drawWidth = w
    story.append(KeepTogether([Spacer(1, 4), img, Paragraph(f"Figure {fig_no[0]} — {text}", caption)]))


def ui(text):
    """A UI element name, e.g. a button."""
    return f'<font name="UI-Semi">{text}</font>'


def m(text):
    """Inline monospace."""
    return f'<font name="Mono" size="9">{escape(text)}</font>'


# ================================================================== CONTENT
story.append(NextPageTemplate("normal"))
story.append(PageBreak())

story.append(Paragraph("Contents", ParagraphStyle("toctitle", parent=h1)))
toc = TableOfContents()
toc.levelStyles = [toc_l1, toc_l2]
toc.dotsMinLevel = 0
story.append(toc)
story.append(PageBreak())

# ---------------------------------------------------------------- 1
H1("1. Introduction")
P("The <b>SQL Server Backup Tool</b> is a Windows application for backing up and restoring Microsoft SQL Server databases. "
  "It lists the databases on your server so you can tick the ones you need and back them up in one step. "
  "It can also restore a backup, delete old backup files automatically, and run backups on a schedule without anyone at the keyboard.")
P("What you can do with it:")
bullets([
    "<b>Back up</b> one or many databases at once, with progress for each database and a summary at the end.",
    "<b>See the backup history</b> of any database: when it was backed up, how big the backup was and where the file is.",
    "<b>Restore</b> a backup, either over the original database or as a separate copy under a new name.",
    "<b>Work with several servers</b>, such as head office, branch and test servers, from one place.",
    "<b>Keep connection details safe</b>: passwords can be stored encrypted.",
    "<b>Delete old backups automatically</b>, so the backup disk doesn't fill up.",
    "<b>Schedule backups</b> to run daily or weekly through Windows Task Scheduler.",
])
callout("Note", "Backups made by this tool are standard SQL Server backup files (<b>.bak</b>). They can also be restored with "
                "SQL Server Management Studio or any other SQL Server tool, so you're never locked in to this application.")

# ---------------------------------------------------------------- 2
H1("2. Before you start")
H2("System requirements")
table(["Item", "Requirement"], [
    ["Computer", "Windows 10 or 11, or Windows Server 2016 or later (64-bit)"],
    [".NET runtime", "Microsoft .NET 10 Desktop Runtime (x64). This is not needed if you received the self-contained package."],
    ["SQL Server", "SQL Server 2016 or later. All editions work, including Express (backup compression is not available on Express). SQL Server on Linux is also supported."],
    ["Network", "The computer running the tool must be able to reach the SQL Server (TCP port 1433 by default)."],
], [0.22, 0.78])

H2("Permissions")
P("The account used to connect to SQL Server needs these permissions:")
table(["Task", "Permission needed"], [
    ["Back up a database", f"{m('db_backupoperator')} on that database (or {m('sysadmin')})"],
    ["See backup history", f"Read access to the {m('msdb')} system database (granted by default)"],
    ["Restore as a new database", f"{m('dbcreator')} server role (or {m('sysadmin')})"],
    ["Restore over an existing database", f"{m('db_owner')} of that database, or {m('sysadmin')}"],
    ["Delete old backups on a remote server", f"{m('sysadmin')} (on a server running on the same PC, the files are deleted directly)"],
], [0.36, 0.64])

callout("Important", "<b>Backup files are written by the SQL Server service, not by your PC.</b> The backup folder is a folder "
                     "<i>on the SQL Server machine</i>, and the SQL Server service account must be allowed to write to it. "
                     f"For example, {m('D:\\SQLBackups')} means drive D: of the server. To store backups on a network share, "
                     f"use a UNC path such as {m('\\\\backupnas\\sql')} that the SQL Server service account can write to.")

# ---------------------------------------------------------------- 3
H1("3. Installation and first start")
steps([
    f"Copy the application folder to the PC, for example to {m('C:\\Apps\\SqlBackupTool')}. Choose a folder your Windows user can "
    f"write to, so that settings and log files can be saved. Avoid {m('C:\\Program Files')}.",
    f"Double-click {m('SqlBackupTool.exe')}.",
    f"If no server has been set up yet, the {ui('Settings')} window opens. Add your SQL Server as described in section 5.",
    "The main window opens, connects to the default server and lists its databases.",
])
P("The folder contains:")
table(["File / folder", "Purpose"], [
    ["SqlBackupTool.exe", "The application (also used for the command line and scheduled tasks)"],
    ["appsettings.json", "Your settings: servers, default backup folder and options. Normally edited through the Settings window."],
    ["logs\\", "A log file for each day, e.g. backup_20261124.log, recording every backup and restore"],
], [0.3, 0.7], mono_cols=(0,))

# ---------------------------------------------------------------- 4
H1("4. The main window", need=150)
figure("main.png", "The main window after backing up three databases")
table(["Area", "What it does"], [
    [ui("Server") + " bar (top)", f"Choose which SQL Server to work with. {ui('Refresh')} reloads the list. {ui('Settings...')} manages servers and defaults. "
                                  "The server's name, version and edition are shown on the right."],
    [ui("Databases") + " (left)", f"Every database on the server, with its state, recovery model, size and the date of its last full backup. "
                                  f"Tick the databases to back up. {ui('Select all')} and {ui('Select none')} help with long lists. Offline databases are greyed out and can't be ticked."],
    [ui("Backup history") + " (right)", "Click a database name to see its 50 most recent backups, newest first: backups made by this tool and by any other tool or job."],
    ["Restore buttons", f"{ui('Restore selected backup...')} restores the full backup selected in the history. {ui('Restore from file...')} restores any .bak file."],
    [ui("Backup options"), "Where the backup files go and how they are made (see section 6), and whether old backups are deleted automatically (section 9)."],
    ["Action bar", f"{ui('Back up selected')} starts the backup. {ui('Cancel')} stops it. A progress bar and status text show how far it has got. "
                   f"{ui('Copy command line')} and {ui('Schedule...')} are for unattended backups (sections 10 and 11). {ui('Open log')} opens today's log file."],
    ["Log (bottom)", "A running record of everything the tool does, including the messages from SQL Server. The same text is saved in the log file."],
], [0.24, 0.76])

# ---------------------------------------------------------------- 5
H1("5. Connecting to your SQL Servers")
P(f"Servers are managed in {ui('Settings...')} on the {ui('Servers')} tab. You can add as many servers as you need and choose one as the "
  "default, which is selected when the application starts. To switch servers, use the drop-down at the top of the main window.")
figure("settings_servers.png", "Settings, Servers tab. The lock marks servers whose connection details are encrypted.", width=0.78)

H2("Adding or editing a server")
steps([
    f"Open {ui('Settings...')} and click {ui('Add...')} (or select a server and click {ui('Edit...')}).",
    "Fill in the fields described in the table below.",
    f"Click {ui('Test connection')}. A green tick with the server version means the details work.",
    f"Click {ui('OK')}, then {ui('Save')} in the Settings window.",
])
figure("server_edit.png", "Adding a server that uses a SQL Server login, with encryption switched on", width=0.78)
table(["Field", "Meaning"], [
    ["Name", "A friendly name shown in the drop-down, e.g. “Head Office”."],
    ["Server / instance", f"The SQL Server name, e.g. {m('SQLSRV01')}, {m('SQLSRV01\\SQLEXPRESS')} or {m('10.0.0.5,1433')}."],
    ["Authentication", f"{ui('Windows Authentication')} uses your Windows account (recommended). {ui('SQL Server Authentication')} uses a SQL login and password."],
    ["Trust server certificate", "Leave this ticked for servers that use the default self-signed certificate (most internal servers). Untick it only if the server has a certificate from a trusted authority."],
    ["Backup folder", "Optional. A backup folder used only for this server, overriding the default folder."],
    ["Encrypt the connection string", "Stores the connection details encrypted (see below). Strongly recommended when you use a SQL login."],
    ["Any Windows user on this PC may decrypt it", "Tick this if a scheduled task will run under a <i>different</i> Windows account (see section 10)."],
], [0.3, 0.7])

H2("How encryption protects your password")
P("With encryption switched on, the connection details are stored in appsettings.json as scrambled text starting with "
  f"{m('dpapi:')}. They are encrypted with Windows Data Protection (DPAPI). This means:")
bullets([
    "<b>Default (user)</b>: only <i>your</i> Windows account on <i>this</i> PC can read them. Copying the file to another PC or "
    "opening it as another user doesn't reveal the password.",
    "<b>Machine scope</b>: any Windows account on this PC can read them, but they're still unreadable on any other PC.",
])
callout("Note", "Because the encryption is tied to the PC (and to the user, by default), an encrypted entry can't be used on a "
                "different computer. After moving the application to a new PC, open Settings, edit the server and enter the password again.")

# ---------------------------------------------------------------- 6
H1("6. Backing up databases")
steps([
    "Choose the server at the top of the window.",
    f"Tick the databases you want to back up in the {ui('Databases')} list.",
    "Check the backup folder and options (explained below).",
    f"Click {ui('Back up selected')}.",
    "Watch the progress bar and the log. The databases are backed up one after another.",
    "When all are finished, a summary shows how many succeeded and the reason for any failure.",
])
H2("Backup options")
table(["Option", "What it does", "Default"], [
    ["Backup folder (on server)", "The folder on the SQL Server machine where backup files are created. It must already exist.", "Server's default backup folder"],
    ["Copy-only", "Makes a backup that does not interfere with your regular backup plan (differential and transaction-log backups carry on as normal). "
                  "Recommended for ad-hoc and tool-made backups.", "On"],
    ["Compression", "Makes the backup file much smaller (typically 60–80% smaller) and often faster to create. Not available on SQL Server Express.", "On"],
    ["Checksum", "SQL Server checks every page while backing up, so damaged data is detected immediately.", "On"],
    ["Verify after backup", "Reads the finished backup file back to confirm it is complete and can be restored.", "On"],
    ["Delete old backups made by this tool", "Applies the clean-up rule after each successful backup (see section 9).", "Off"],
], [0.24, 0.58, 0.18])

H2("Backup file names")
P(f"Each backup is saved as a new file named after the database and the date and time, for example "
  f"{m('SalesDB_20261124_020041.bak')}, so existing backups are never overwritten. The naming pattern can be changed in "
  f"{ui('Settings → Backup defaults')}.")
callout("Tip", f"The options you set in the main window apply to the next backup only. To change the defaults for every session, use "
               f"{ui('Settings → Backup defaults')}.")

# ---------------------------------------------------------------- 7
H1("7. Viewing backup history")
P(f"Click a database name in the {ui('Databases')} list. The {ui('Backup history')} panel shows its 50 most recent backups, newest "
  "first, as recorded by SQL Server. This includes backups made by maintenance plans, SQL Server Agent jobs and other tools.")
table(["Column", "Meaning"], [
    ["Finished", "When the backup completed"],
    ["Type", "Full, Differential or Log. Only Full backups can be restored with this tool."],
    ["Size / Compressed", "Size of the data backed up and the actual size of the file on disk"],
    ["Copy-only", "“Yes” if the backup was made as copy-only"],
    ["File", "The full path of the backup file on the server"],
], [0.25, 0.75])
callout("Note", "The history comes from SQL Server's own records. If a backup file has since been moved or deleted, it still appears "
                "in the history, but restoring it will fail with “cannot open backup device”.")

# ---------------------------------------------------------------- 8
H1("8. Restoring a database")
P("A restore puts a database back to the state it was in when the backup was taken. There are two ways to restore:")
table(["Restore mode", "When to use it", "What happens"], [
    ["<b>As a copy</b> (new name)", "To look at old data, recover individual records, or test a backup without touching the live database.",
     "A new database is created. Its files are renamed automatically, so the original database is not affected."],
    ["<b>Over the original</b> (same name)", "To undo damage or data loss in the live database.",
     "The existing database is <b>replaced</b> by the backup. All changes made after the backup was taken are lost."],
], [0.22, 0.39, 0.39])
H2("Step by step")
steps([
    f"Select the database and then the full backup in the history, and click {ui('Restore selected backup...')} "
    f"(or double-click the backup). To restore a .bak file that isn't in the history, click {ui('Restore from file...')} instead.",
    "The Restore window reads the backup file and shows the backup set and the files it contains.",
    f"In {ui('Restore as database')}, keep the original name to restore over it, or type a <b>new name</b> to restore as a copy.",
    f"Check the {ui('Restore as')} paths. The tool suggests suitable paths, and you can edit any path by clicking it.",
    f"Leave {ui('Close existing connections')} ticked when restoring over a database that people may be using.",
    f"Click {ui('Restore')} and confirm. When restoring over an existing database you will see a clear warning first.",
])
figure("restore.png", "Restoring a backup of SalesDB as a new copy named SalesDB_Copy", width=0.9)
callout("Important", "Restoring over an existing database <b>cannot be undone</b>, and users are disconnected. If you're not "
                     "sure, restore as a copy first and check the data. System databases (master, model, msdb) cannot be restored with this tool.")

# ---------------------------------------------------------------- 9
H1("9. Deleting old backups automatically")
P("Backups take up disk space. The tool can delete its own old backups after each successful backup, following a simple rule "
  f"that you set in {ui('Settings → Backup defaults')}:")
figure("settings_backup.png", "Settings, Backup defaults tab, with the clean-up (retention) rule", width=0.64)
table(["Setting", "Meaning"], [
    ["Always keep newest", "The newest N backups of each database are never deleted. 0 means no count rule."],
    ["Delete only when older than", "Backups newer than this many days are never deleted. 0 means no age rule."],
    ["Apply retention after each successful backup", "Whether the main window's clean-up checkbox starts ticked"],
], [0.38, 0.62])
P("A backup is deleted only when <b>both</b> rules allow it. Example with daily backups and the default rule "
  "(keep newest 7, delete when older than 30 days):")
table(["Backups", "Result"], [
    ["The newest 7 backups", "Kept (count rule)"],
    ["Backups 8 to 30 (less than 30 days old)", "Kept (age rule)"],
    ["Backups older than 30 days", "Deleted"],
], [0.55, 0.45])
callout("Note", "<b>Built-in safety:</b> only backups <i>created by this tool</i> are ever deleted. Backups made by other tools, jobs or "
                "people are never touched, and the newest backup of each database is always kept. Every deleted file is recorded in the log.")

# ---------------------------------------------------------------- 10
H1("10. Scheduling automatic backups")
P("Backups can run on their own every day or every week, with nobody at the keyboard, using Windows Task Scheduler.")
steps([
    "In the main window, choose the server, tick the databases and set the options exactly as you want them for the scheduled backup.",
    f"Click {ui('Schedule...')}.",
    "Enter a task name and choose <b>Daily</b> or <b>Weekly</b> (and the day), and the time.",
    f"Click {ui('Create task')}. The task appears in Windows Task Scheduler under the <b>SqlBackupTool</b> folder.",
])
figure("schedule.png", "Creating a daily backup task", width=0.78)
P("The scheduled backup uses the databases and options that were selected when you clicked Schedule. To change them, create the task "
  "again with the same name; it will be replaced.")
callout("Important", "By default the task runs <b>only while you are logged on to Windows</b>. To run it while you're logged off "
                     "(recommended for servers): open <b>Task Scheduler → Task Scheduler Library → SqlBackupTool</b>, open the task, choose "
                     "<b>Run whether user is logged on or not</b>, and enter your Windows password. If the task runs under a different Windows "
                     "account, save the server with the <i>Any Windows user on this PC may decrypt it</i> option (section 5).")
callout("Tip", f"Check the result of each scheduled run in the {m('logs')} folder, or in Task Scheduler's “Last Run Result” "
               f"column (0x0 means success).")

# ---------------------------------------------------------------- 11
H1("11. Command-line reference")
P(f"Everything the scheduled task does can also be run from a command prompt or a script. {ui('Copy command line')} in the main window "
  "copies the exact command for the current selection.")
code('SqlBackupTool.exe --backup --server "Head Office" --dbs SalesDB,HR_Payroll --folder D:\\SQLBackups\n'
     'SqlBackupTool.exe --backup --all --retention\n'
     'SqlBackupTool.exe --list --server "Branch Office"\n'
     'SqlBackupTool.exe --protect-config')
table(["Option", "Meaning"], [
    ["--backup", "Back up databases without opening the window"],
    ["--list", "List the databases on the server"],
    ["--protect-config [--machine]", "Encrypt every unencrypted connection string in appsettings.json (machine scope with --machine)"],
    ["--help", "Show the list of options"],
    ["--server NAME", "Server name as set up in Settings (default: the default server)"],
    ["--db NAME", "A database to back up (can be repeated)"],
    ["--dbs A,B,C", "Several databases, separated by commas"],
    ["--all", "All online user databases"],
    ["--folder PATH", "Backup folder on the server (overrides the defaults)"],
    ["--copy-only / --no-copy-only", "Override the copy-only default"],
    ["--compression / --no-compression", "Override the compression default"],
    ["--checksum / --no-checksum", "Override the checksum default"],
    ["--verify / --no-verify", "Override the verify default"],
    ["--retention / --no-retention", "Apply (or skip) the old-backup clean-up rule"],
], [0.36, 0.64], mono_cols=(0,))
H2("Exit codes")
P("Scripts and monitoring tools can check the result through the exit code:")
table(["Code", "Meaning"], [
    ["0", "All backups succeeded"],
    ["1", "At least one backup failed, or a database was not found or was offline"],
    ["2", "Invalid command-line options or configuration"],
    ["3", "Could not connect to the SQL Server"],
], [0.12, 0.88], mono_cols=(0,))
P(f"Output is written to the console and to the daily log file. In PowerShell, use "
  f"{m('Start-Process SqlBackupTool.exe -ArgumentList ... -Wait -PassThru')} and read the {m('ExitCode')} property.")

# ---------------------------------------------------------------- 12
H1("12. Configuration file reference", need=120)
P(f"All settings are stored in {m('appsettings.json')} next to the application. The Settings window is the easiest and safest way to "
  "change them, but the file can also be edited in a text editor while the application is closed.")
code('''{
  "Servers": [
    { "Name": "Head Office",
      "ConnectionString": "dpapi:user:AQAAANCMnd8B...",
      "BackupFolder": null },
    { "Name": "Branch Office",
      "ConnectionString": "Server=BRANCH-SQL\\\\SQLEXPRESS;Integrated Security=True;",
      "BackupFolder": "E:\\\\SQLBackups" }
  ],
  "DefaultServer": "Head Office",
  "Backup": {
    "DefaultFolder": "D:\\\\SQLBackups",
    "FileNamePattern": "{database}_{timestamp}.bak",
    "TimestampFormat": "yyyyMMdd_HHmmss",
    "CopyOnly": true, "Compression": true, "Checksum": true, "Verify": true,
    "IncludeSystemDatabases": false,
    "Retention": { "Enabled": false, "KeepLast": 7, "OlderThanDays": 30 }
  }
}''')
table(["Setting", "Meaning"], [
    ["Servers", "List of servers: Name, ConnectionString (plain or encrypted) and an optional BackupFolder for that server"],
    ["DefaultServer", "Name of the server selected at start-up"],
    ["Backup.DefaultFolder", "Default backup folder on the server. Leave empty to use SQL Server's own default backup folder (SQL Server 2019 and later)."],
    ["Backup.FileNamePattern", "Backup file name. Must contain {database} and {timestamp}."],
    ["Backup.TimestampFormat", "Date/time format used for {timestamp}"],
    ["Backup.CopyOnly / Compression / Checksum / Verify", "Default values for the backup options"],
    ["Backup.IncludeSystemDatabases", "Also list master, model and msdb"],
    ["Backup.Retention", "Clean-up rule: Enabled (default state), KeepLast, OlderThanDays"],
], [0.4, 0.6], mono_cols=(0,))
callout("Note", "In JSON, a backslash must be written twice, e.g. D:\\\\SQLBackups. The Settings window takes care of this automatically.")

# ---------------------------------------------------------------- 13
H1("13. Troubleshooting")
table(["Problem", "Likely cause and solution"], [
    ["<b>Could not connect to SQL Server</b>",
     "Check the server name and instance in Settings and use <i>Test connection</i>. Make sure the SQL Server service is running, remote "
     "connections are enabled and the firewall allows port 1433. For a certificate error, tick <i>Trust server certificate</i>."],
    ["<b>Operating system error 5 (Access is denied)</b>",
     "The SQL Server <i>service account</i> can't write to the backup folder. Give that account Modify permission on the folder, or choose another folder."],
    ["<b>Cannot open backup device / path not found</b>",
     "The folder doesn't exist <i>on the SQL Server machine</i>. Remember that the path is resolved on the server, not on your PC."],
    ["<b>Compression is greyed out</b>", "The server is SQL Server Express, which doesn't support backup compression. Backups still work without it."],
    ["<b>The encrypted connection string cannot be decrypted</b>",
     "The settings were encrypted by another Windows user or on another PC. Open Settings, edit the server and enter the password again."],
    ["<b>Retention could not delete a file</b>",
     "On a remote server, deleting files requires the sysadmin role. Otherwise check that the file isn't in use. The reason is shown in the log."],
    ["<b>The scheduled task didn't run</b>",
     "By default tasks run only while you are logged on. See section 10 to run them while logged off. Check “Last Run Result” in Task Scheduler and the log file."],
    ["<b>A database can't be ticked</b>", "It is offline, restoring or otherwise unavailable (shown greyed out). Bring it online in SQL Server first."],
    ["<b>Settings could not be saved</b>",
     f"The application folder is read-only (for example under {m('C:\\Program Files')}). Move the application to a folder your user can write to."],
    ["<b>No backup folder is filled in</b>",
     "On SQL Server 2016/2017 the server's default backup folder can't be detected. Set a default folder in Settings or a folder per server."],
], [0.33, 0.67])
P(f"If a problem continues, open the log file ({ui('Open log')} in the main window, or the {m('logs')} folder). It contains the "
  "exact messages from SQL Server, which make it much easier to find the cause.")

# ---------------------------------------------------------------- 14
H1("14. Quick reference", need=115)
table(["I want to…", "Do this"], [
    ["Back up some databases now", f"Tick them, then click {ui('Back up selected')}"],
    ["Back up everything", f"{ui('Select all')}, then {ui('Back up selected')}"],
    ["See when a database was last backed up", f"Look at the {ui('Last full backup')} column, or click the database to see its history"],
    ["Get a copy of last night's data", f"Select the backup in the history, then {ui('Restore selected backup...')} with a new database name"],
    ["Undo damage to a database", "Restore the last good backup over the original database (section 8)"],
    ["Free up disk space automatically", f"Set the rule in {ui('Settings → Backup defaults')} and tick {ui('Delete old backups...')}"],
    ["Back up every night", f"Select databases and options, then {ui('Schedule...')}"],
    ["Add another server", f"{ui('Settings...')} → {ui('Add...')}"],
    ["Protect a stored password", f"Edit the server and tick {ui('Encrypt the connection string')}"],
], [0.4, 0.6])

# ---------------------------------------------------------------- build
doc = Guide(OUT)
doc.multiBuild(story)
print(f"Wrote {OUT}")
