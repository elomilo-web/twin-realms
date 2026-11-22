using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TwinRealmsSetup;

/// <summary>
/// Twin Realms installer. Installs for the current user (no admin prompt) into %LOCALAPPDATA%\Programs\Twin Realms by default:
/// reads the current release from data/release.json on GitHub (the same file the online server uses), downloads the game zip, unpacks it,
/// adds Start menu and desktop shortcuts and an "Apps & features" entry, then starts the game, which opens its launcher
/// (update check, patch notes, login, Play). From then on the launcher keeps the game up to date.
///   TwinRealmsSetup.exe              install (or repair / reinstall)
///   TwinRealmsSetup.exe /uninstall   remove the game (Apps & features runs this)
/// </summary>
static class Program
{
    public const string Name = "Twin Realms", Exe = "TwinRealms.exe", RegKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\TwinRealms";
    public const string ReleaseUrl = "https://raw.githubusercontent.com/elomilo-web/twin-realms/main/data/release.json";
    public static string DefaultDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", Name);
    public static string SavesDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Godot", "app_userdata", "TwinRealms");
    public static string StartMenuLnk => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Name + ".lnk");
    public static string DesktopLnk => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Name + ".lnk");

    [STAThread]
    static void Main(string[] args)
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | (SecurityProtocolType)12288;   // TLS 1.2 and 1.3 for GitHub
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        string a0 = args.Length > 0 ? args[0].ToLowerInvariant() : "";
        if (a0 == "/uninstall") { Uninstaller.Start(); return; }
        if (a0 == "/uninstall-run" && args.Length > 1) { Uninstaller.Run(args[1]); return; }
        Application.Run(new SetupForm());
    }

    public static Stream Res(string name) => Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
    public static Icon AppIcon() { try { return new Icon(Res("icon.ico")); } catch { return SystemIcons.Application; } }
    public static string InstalledDir() { using var k = Registry.CurrentUser.OpenSubKey(RegKey); return k?.GetValue("InstallLocation") as string ?? ""; }

    /// <summary>Is the game from this folder running? (the files cannot be replaced while it is)</summary>
    public static bool GameRunning(string dir)
    {
        string exe = Path.GetFullPath(Path.Combine(dir, Exe));
        foreach (var p in Process.GetProcessesByName("TwinRealms"))
            try { if (string.Equals(Path.GetFullPath(p.MainModule.FileName), exe, StringComparison.OrdinalIgnoreCase)) return true; } catch { }
        return false;
    }

    public static void Shortcut(string lnk, string target, string dir, string description)
    {
        var t = Type.GetTypeFromProgID("WScript.Shell"); object shell = Activator.CreateInstance(t);
        object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
        var st = sc.GetType();
        st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
        st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { dir });
        st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { target + ",0" });
        st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { description });
        st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
    }
}

/// <summary>Dark themed widgets shared by the installer windows.</summary>
static class Look
{
    public static readonly Color Bg = Color.FromArgb(14, 10, 14), Gold = Color.FromArgb(227, 190, 110), GoldDim = Color.FromArgb(150, 120, 70), Text = Color.FromArgb(236, 228, 214), Dim = Color.FromArgb(160, 150, 140), Bad = Color.FromArgb(255, 160, 120);

    public static Button Btn(string text, bool primary = false)
    {
        var b = new Button { Text = text, FlatStyle = FlatStyle.Flat, ForeColor = primary ? Color.FromArgb(20, 14, 10) : Text, BackColor = primary ? Gold : Color.FromArgb(40, 30, 34), Font = new Font("Segoe UI", primary ? 11f : 9.5f, primary ? FontStyle.Bold : FontStyle.Regular), Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
        b.FlatAppearance.BorderColor = primary ? Gold : GoldDim; b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(245, 210, 135) : Color.FromArgb(70, 52, 50);
        return b;
    }
    public static Label Lbl(string text, float size = 9.5f, Color? c = null, bool bold = false) => new Label { Text = text, AutoSize = true, ForeColor = c ?? Text, BackColor = Color.Transparent, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular) };
    public static CheckBox Check(string text, bool on) => new CheckBox { Text = text, Checked = on, AutoSize = true, ForeColor = Text, BackColor = Color.Transparent, Font = new Font("Segoe UI", 9.5f) };
}

/// <summary>A gold progress bar.</summary>
class Bar : Control
{
    float _v; public float Value { get => _v; set { _v = Math.Max(0, Math.Min(1, value)); Invalidate(); } }
    public Bar() { DoubleBuffered = true; Height = 16; }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var b = new SolidBrush(Color.FromArgb(40, 30, 34))) g.FillRectangle(b, ClientRectangle);
        int w = (int)((Width - 2) * _v);
        if (w > 0) using (var gb = new LinearGradientBrush(new Rectangle(1, 1, Math.Max(1, w), Height - 2), Color.FromArgb(250, 215, 140), Color.FromArgb(176, 128, 60), LinearGradientMode.Vertical)) g.FillRectangle(gb, 1, 1, w, Height - 2);
        using (var p = new Pen(Look.GoldDim)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
    }
}

/// <summary>The install window: background art, folder choice, shortcuts, progress.</summary>
class SetupForm : Form
{
    readonly Image _bg, _logo;
    readonly TextBox _dir; readonly Button _browse, _install, _cancel; readonly CheckBox _desk, _menu, _run;
    readonly Label _status; readonly Bar _bar;
    bool _busy;

    public SetupForm()
    {
        Text = "Twin Realms Setup"; Icon = Program.AppIcon();
        FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96f, 96f);
        ClientSize = new Size(820, 500); BackColor = Look.Bg; DoubleBuffered = true;
        _bg = Image.FromStream(Program.Res("bg.jpg")); _logo = Image.FromStream(Program.Res("logo.png"));

        string installed = Program.InstalledDir();
        var title = Look.Lbl(installed != "" ? "Twin Realms is installed. Setup will repair it with the newest version." : "Welcome! Setup installs Twin Realms and its launcher on this computer.", 10.5f);
        title.Location = new Point(40, 150); Controls.Add(title);

        var fl = Look.Lbl("Install folder", 9.5f, Look.Gold, true); fl.Location = new Point(40, 200); Controls.Add(fl);
        _dir = new TextBox { Text = installed != "" ? installed : Program.DefaultDir, Location = new Point(40, 224), Width = 620, Font = new Font("Segoe UI", 10f), BackColor = Color.FromArgb(30, 24, 28), ForeColor = Look.Text, BorderStyle = BorderStyle.FixedSingle };
        Controls.Add(_dir);
        _browse = Look.Btn("Browse ..."); _browse.SetBounds(670, 222, 110, 30); Controls.Add(_browse);
        _browse.Click += (s, e) => { using var d = new FolderBrowserDialog { Description = "Choose where to install Twin Realms (a folder you can write to, so the game can update itself).", SelectedPath = _dir.Text }; if (d.ShowDialog(this) == DialogResult.OK) _dir.Text = d.SelectedPath.EndsWith(Program.Name) ? d.SelectedPath : Path.Combine(d.SelectedPath, Program.Name); };

        _menu = Look.Check("Start menu shortcut", true); _menu.Location = new Point(40, 270); Controls.Add(_menu);
        _desk = Look.Check("Desktop shortcut", true); _desk.Location = new Point(240, 270); Controls.Add(_desk);
        _run = Look.Check("Start Twin Realms when done", true); _run.Location = new Point(420, 270); Controls.Add(_run);

        var note = Look.Lbl("The game is downloaded during setup (about 500 MB). Your saved heroes are kept.", 9f, Look.Dim); note.Location = new Point(40, 306); Controls.Add(note);

        _status = Look.Lbl("Ready to install.", 9.5f); _status.Location = new Point(40, 360); _status.AutoSize = false; _status.Size = new Size(740, 22); Controls.Add(_status);
        _bar = new Bar { Location = new Point(40, 388), Width = 740 }; Controls.Add(_bar);

        _cancel = Look.Btn("Cancel"); _cancel.SetBounds(560, 430, 100, 40); Controls.Add(_cancel);
        _cancel.Click += (s, e) => Close();
        _install = Look.Btn(installed != "" ? "Repair" : "Install", true); _install.SetBounds(670, 430, 110, 40); Controls.Add(_install);
        _install.Click += async (s, e) => await Install();
        AcceptButton = _install;
        FormClosing += (s, e) => { if (_busy && MessageBox.Show(this, "Setup is still working. Stop it?", "Twin Realms Setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) e.Cancel = true; };
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        // background art, covering the window, darkened toward the bottom so the text reads
        float s = Math.Max(ClientSize.Width / (float)_bg.Width, ClientSize.Height / (float)_bg.Height);
        int w = (int)(_bg.Width * s), h = (int)(_bg.Height * s);
        g.DrawImage(_bg, (ClientSize.Width - w) / 2, (ClientSize.Height - h) / 2, w, h);
        using (var shade = new LinearGradientBrush(ClientRectangle, Color.FromArgb(150, 0, 0, 0), Color.FromArgb(235, 8, 6, 10), LinearGradientMode.Vertical)) g.FillRectangle(shade, ClientRectangle);
        float ls = 110f / _logo.Height; g.DrawImage(_logo, 36, 22, _logo.Width * ls, 110);
    }

    void Status(string text, bool bad = false) { _status.Text = text; _status.ForeColor = bad ? Look.Bad : Look.Text; }
    void Lock(bool busy) { _busy = busy; _install.Enabled = _browse.Enabled = _dir.Enabled = _desk.Enabled = _menu.Enabled = _run.Enabled = !busy; }

    async Task Install()
    {
        string dir;
        try { dir = Path.GetFullPath(_dir.Text.Trim()); } catch { Status("That folder name is not valid.", true); return; }
        if (Program.GameRunning(dir)) { Status("Twin Realms is running from this folder. Close it, then try again.", true); return; }
        Lock(true);
        string tmp = Path.Combine(Path.GetTempPath(), "TwinRealmsSetup"), zip = Path.Combine(tmp, "game.zip");
        try
        {
            Directory.CreateDirectory(dir);
            try { File.WriteAllText(Path.Combine(dir, ".write_test"), "ok"); File.Delete(Path.Combine(dir, ".write_test")); }
            catch { throw new Exception("Setup cannot write to " + dir + ". Choose a folder in your user folder (the default is best), so the game can also update itself."); }
            Directory.CreateDirectory(tmp);

            // 1. which version is current (the same file the online server reads)
            Status("Finding the newest version ..."); _bar.Value = 0;
            string json;
            using (var wc = new WebClient()) { wc.Headers[HttpRequestHeader.UserAgent] = "TwinRealms-Setup"; json = await wc.DownloadStringTaskAsync(Program.ReleaseUrl + "?t=" + DateTime.UtcNow.Ticks); }
            string url = Field(json, "zip"), ver = Field(json, "version");
            if (url == "") throw new Exception("No release has been published yet. Try again later.");

            // 2. download
            using (var wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "TwinRealms-Setup";
                var t0 = DateTime.UtcNow;
                wc.DownloadProgressChanged += (s, e) =>
                {
                    double secs = Math.Max(.1, (DateTime.UtcNow - t0).TotalSeconds), mbs = e.BytesReceived / 1048576.0 / secs;
                    _bar.Value = e.TotalBytesToReceive > 0 ? .85f * e.BytesReceived / e.TotalBytesToReceive : 0;
                    Status($"Downloading Twin Realms {ver}: {e.BytesReceived / 1048576} of {e.TotalBytesToReceive / 1048576} MB ({mbs:0.0} MB/s)");
                };
                await wc.DownloadFileTaskAsync(url, zip);
            }

            // 3. unpack (the zip holds a TwinRealms\ folder: its contents go into the install folder)
            Status("Installing files ...");
            await Task.Run(() =>
            {
                using var z = ZipFile.OpenRead(zip);
                var exeEntry = z.Entries.FirstOrDefault(e => e.Name.Equals(Program.Exe, StringComparison.OrdinalIgnoreCase));
                if (exeEntry == null) throw new Exception("The download has no " + Program.Exe + " in it.");
                string prefix = exeEntry.FullName.Substring(0, exeEntry.FullName.Length - exeEntry.Name.Length);
                string root = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
                int n = 0, all = z.Entries.Count;
                foreach (var e in z.Entries)
                {
                    n++;
                    if (!e.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    string rel = e.FullName.Substring(prefix.Length); if (rel == "") continue;
                    string dest = Path.GetFullPath(Path.Combine(dir, rel));
                    if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new Exception("Bad path in the download: " + e.FullName);
                    if (e.FullName.EndsWith("/")) { Directory.CreateDirectory(dest); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    e.ExtractToFile(dest, true);
                    int done = n; BeginInvoke((Action)(() => { _bar.Value = .85f + .13f * done / all; Status($"Installing files ({done} of {all}) ..."); }));
                }
            });

            // 4. uninstaller, shortcuts, Apps & features entry
            Status("Creating shortcuts ...");
            string self = Application.ExecutablePath, setupCopy = Path.Combine(dir, "TwinRealmsSetup.exe");
            if (!string.Equals(Path.GetFullPath(self), setupCopy, StringComparison.OrdinalIgnoreCase)) File.Copy(self, setupCopy, true);
            string exe = Path.Combine(dir, Program.Exe);
            if (_menu.Checked) Program.Shortcut(Program.StartMenuLnk, exe, dir, "Play Twin Realms"); else TryDelete(Program.StartMenuLnk);
            if (_desk.Checked) Program.Shortcut(Program.DesktopLnk, exe, dir, "Play Twin Realms"); else TryDelete(Program.DesktopLnk);
            long kb = 0; foreach (var f in new DirectoryInfo(dir).GetFiles("*", SearchOption.AllDirectories)) kb += f.Length / 1024;
            using (var k = Registry.CurrentUser.CreateSubKey(Program.RegKey))
            {
                k.SetValue("DisplayName", Program.Name); k.SetValue("DisplayIcon", exe + ",0"); k.SetValue("Publisher", "Twin Realms");
                k.SetValue("DisplayVersion", ver); k.SetValue("InstallLocation", dir);
                k.SetValue("UninstallString", "\"" + setupCopy + "\" /uninstall"); k.SetValue("QuietUninstallString", "\"" + setupCopy + "\" /uninstall");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord); k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, kb), RegistryValueKind.DWord);
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            }
            TryDelete(zip);
            _bar.Value = 1; Status($"Twin Realms {ver} is installed.");
            _busy = false;
            if (_run.Checked) { Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = dir, UseShellExecute = true }); Close(); return; }
            _install.Text = "Done"; _install.Enabled = true; _install.Click += (s, e) => Close(); _cancel.Visible = false;
        }
        catch (Exception ex)
        {
            string msg = ex is WebException we ? "Download failed: " + we.Message + " Check your internet connection." : ex.Message;
            Status(msg, true); Lock(false);
        }
    }

    static string Field(string json, string key) { var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\""); return m.Success ? Regex.Unescape(m.Groups[1].Value) : ""; }
    public static void TryDelete(string f) { try { if (File.Exists(f)) File.Delete(f); } catch { } }
}

/// <summary>Removes the game. Runs from a copy in %TEMP% so the installed folder (which holds this exe) can be deleted.</summary>
static class Uninstaller
{
    public static void Start()
    {
        string dir = Program.InstalledDir(); if (dir == "") dir = Path.GetDirectoryName(Application.ExecutablePath);
        if (MessageBox.Show($"Remove Twin Realms from this computer?\n\n{dir}", "Uninstall Twin Realms", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        string tmp = Path.Combine(Path.GetTempPath(), "TwinRealmsUninstall.exe");
        File.Copy(Application.ExecutablePath, tmp, true);
        Process.Start(new ProcessStartInfo(tmp, "/uninstall-run \"" + dir + "\"") { UseShellExecute = false });
    }

    public static void Run(string dir)
    {
        System.Threading.Thread.Sleep(800);   // let the copy that started us exit
        while (Program.GameRunning(dir))
            if (MessageBox.Show("Twin Realms is running. Close it, then press Retry.", "Uninstall Twin Realms", MessageBoxButtons.RetryCancel, MessageBoxIcon.Warning) != DialogResult.Retry) return;
        bool saves = Directory.Exists(Program.SavesDir) && MessageBox.Show("Also delete your offline heroes, settings and saved login on this computer?\n\nChoose No to keep them for a later install. Online heroes are saved on the server either way.", "Uninstall Twin Realms", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        string err = "";
        try
        {
            // only remove a folder that really is a Twin Realms install
            if (Directory.Exists(dir) && File.Exists(Path.Combine(dir, Program.Exe)))
                for (int i = 0; ; i++) { try { Directory.Delete(dir, true); break; } catch when (i < 10) { System.Threading.Thread.Sleep(500); } }
        }
        catch (Exception e) { err = e.Message; }
        SetupForm.TryDelete(Program.StartMenuLnk); SetupForm.TryDelete(Program.DesktopLnk);
        try { Registry.CurrentUser.DeleteSubKeyTree(Program.RegKey, false); } catch { }
        if (saves) try { Directory.Delete(Program.SavesDir, true); } catch { }
        MessageBox.Show(err == "" ? "Twin Realms was removed." : "Twin Realms was removed, but some files could not be deleted:\n" + err + "\n\n" + dir, "Uninstall Twin Realms", MessageBoxButtons.OK, MessageBoxIcon.Information);
        // delete this temporary copy once it has closed
        Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 >nul & del /q \"" + Application.ExecutablePath + "\"") { CreateNoWindow = true, UseShellExecute = false });
    }
}
