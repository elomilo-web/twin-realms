using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Godot;

namespace TwinRealms;

/// <summary>
/// Launch check and auto update state. The launcher (below) asks the online server (OnlineCfg.Server, Railway) which build players must
/// run: the server answers from data/release.json (written and committed by tools/run.ps1 publish, so every published release redeploys
/// the server with the matching code) with the build id, the download link of the release zip and the patch notes.
/// - Exported game with a different build id: downloads the zip, unpacks it into user://update/, and starts a small script that waits for
///   the game to close, copies the new files over the install folder and starts the launcher again. Saves live in %APPDATA%, untouched.
/// - Same build (or, running from source, the same network protocol): online login and Play Online unlock.
/// - Server unreachable, download failed, or a mismatch that updating cannot fix: online stays locked; offline play and friend hosting work.
/// </summary>
public static class Updater
{
    static bool _done;   // the launcher shows once per launch ("Quit to title" reloads the scene)
    /// <summary>True once the launch check confirmed this game matches the online server.</summary>
    public static bool OnlineOk;
    public static string OnlineWhy = "the update check has not finished.";

    public static bool Wanted()
    {
        if (_done) return false; _done = true;
        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--no-update") >= 0) { OnlineWhy = "the update check was skipped (--no-update)."; return false; }
        return true;
    }

    /// <summary>The build id baked into this export by tools/run.ps1 release (empty when running from source).</summary>
    public static string LocalBuild() => Field("res://data/build.json", "build");
    public static string LocalNotes() => Field("res://data/build.json", "notes");
    public static bool Exported => OS.HasFeature("template") && OS.GetName() == "Windows" && LocalBuild() != "";

    public static string Field(string path, string key)
    {
        if (!FileAccess.FileExists(path)) return "";
        try { using var d = JsonDocument.Parse(FileAccess.GetFileAsString(path)); return d.RootElement.TryGetProperty(key, out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() ?? "" : ""; }
        catch { return ""; }
    }

    public static string Dir => ProjectSettings.GlobalizePath("user://update");

    /// <summary>Patch notes (the small markdown used in tools/release_notes.md) as BBCode for a RichTextLabel.</summary>
    public static string NotesToBb(string md)
    {
        var sb = new StringBuilder();
        foreach (var raw in md.Replace("\r", "").Split('\n'))
        {
            string l = raw.TrimEnd();
            if (l.StartsWith("### ")) sb.Append("\n[font_size=19][b][color=#ffd36a]").Append(Inline(l[4..])).Append("[/color][/b][/font_size]\n");
            else if (l.StartsWith("## ")) sb.Append("[font_size=24][b]").Append(Inline(l[3..])).Append("[/b][/font_size]\n");
            else if (l.StartsWith("# ")) sb.Append("[font_size=26][b]").Append(Inline(l[2..])).Append("[/b][/font_size]\n");
            else if (l.StartsWith("- ") || l.StartsWith("* ")) sb.Append("[indent]•  ").Append(Inline(l[2..])).Append("[/indent]\n");
            else if (l == "") sb.Append('\n');
            else sb.Append(Inline(l)).Append('\n');
        }
        return sb.ToString().Trim();
        static string Inline(string s)
        {
            s = s.Replace("[", "[lb]");
            s = Regex.Replace(s, @"\*\*(.+?)\*\*", "[b]$1[/b]");
            s = Regex.Replace(s, @"`(.+?)`", "[code]$1[/code]");
            return s;
        }
    }
}

/// <summary>What the server tells players to run (data/release.json, committed by tools/run.ps1 publish).</summary>
public record Release(string Version, string Build, string Zip, string Notes)
{
    public static Release Load()
    {
        const string p = "res://data/release.json";
        return new Release(Updater.Field(p, "version"), Updater.Field(p, "build"), Updater.Field(p, "zip"), Updater.Field(p, "notes"));
    }
}

/// <summary>
/// The launcher: the first screen of every launch (its own window size). Checks the server for the current version and updates the game
/// (progress bars), shows the patch notes, logs in to the online server (Remember me, Log in automatically), and unlocks Play once ready.
/// Play Online hands the login to the game (a login token, so the password is never kept) and goes straight to the online hero select;
/// Play Offline opens the usual title screen (offline heroes, Multiplayer).
/// </summary>
public partial class Ui
{
    static bool _lnOn;                                   // the launcher owns the screen
    string _lnStage = "";                                // check, download, extract, restart, ready
    Label? _lnStatus; ProgressBar? _lnBar; Button? _lnOnline, _lnOffline, _lnRetry; RichTextLabel? _lnNotes; VBoxContainer? _lnAcct;
    HttpRequest? _upHttp; ulong _upT0; string _upNewVer = "", _upBuild = "";
    volatile int _exDone, _exTotal; volatile string _exResult = "";   // unpacking runs on a worker thread

    // account (launcher.dat, encrypted with a key tied to this computer)
    static string _lnEmail = "", _lnToken = ""; static bool _lnRemember = true, _lnAuto = true, _lnLogged, _lnTriedAuto;
    string _lnOp = "", _lnMsg = "", _lnForgetTok = ""; bool _lnBusy, _lnForget;
    const string SavedPath = "user://launcher.dat";
    static string SavedKey => "TwinRealms|" + OS.GetUniqueId();

    // ================================================================== screen
    public void ShowLauncher()
    {
        _lnOn = true; InGame = false; Input.MouseMode = Input.MouseModeEnum.Visible;
        if (DisplayServer.GetName() != "headless")
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            var usable = DisplayServer.ScreenGetUsableRect();
            var size = new Vector2I(Math.Min(1280, usable.Size.X - 40), Math.Min(760, usable.Size.Y - 40));
            DisplayServer.WindowSetSize(size); DisplayServer.WindowSetPosition(usable.Position + (usable.Size - size) / 2);
            DisplayServer.WindowSetTitle("Twin Realms Launcher");
        }
        LoadSaved();
        BuildLauncher();
        StartCheck();
    }

    void BuildLauncher()
    {
        CloseNoUnpause(); _panelName = "launcher";
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Stop, Theme = MakeTheme() }; root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var art = GD.Load<Texture2D>("res://assets/ui/art/title_shadow.jpg");
        if (art != null) { var bg = new TextureRect { Texture = art, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, MouseFilter = Control.MouseFilterEnum.Ignore }; bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); root.AddChild(bg); }
        var shade = new ColorRect { Color = new Color(0, 0, 0, .55f), MouseFilter = Control.MouseFilterEnum.Ignore }; shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); root.AddChild(shade);
        var mc = new MarginContainer(); mc.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        foreach (var s in new[] { "left", "right", "top", "bottom" }) mc.AddThemeConstantOverride("margin_" + s, 26);
        root.AddChild(mc);
        var main = new VBoxContainer(); main.AddThemeConstantOverride("separation", 14); mc.AddChild(main);

        // top: logo, version, quit
        var top = new HBoxContainer(); top.AddThemeConstantOverride("separation", 12); main.AddChild(top);
        var logo = GD.Load<Texture2D>("res://assets/ui/art/logo.png");
        if (logo != null) top.AddChild(new TextureRect { Texture = logo, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspect, CustomMinimumSize = new Vector2(380, 110) });
        else { var t = Lbl("TWIN REALMS", 44, UiTheme.Title, top); t.AddThemeConstantOverride("outline_size", 8); }
        top.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var vv = new VBoxContainer(); top.AddChild(vv);
        Lbl("Version " + Game.Version, 15, UiTheme.Dim, vv).HorizontalAlignment = HorizontalAlignment.Right;
        var q = Btn("Quit", () => GetTree().Quit(), true); q.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd; vv.AddChild(q);

        // middle: patch notes | account
        var mid = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; mid.AddThemeConstantOverride("separation", 16); main.AddChild(mid);
        var np = Card(mid); np.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; np.SizeFlagsStretchRatio = 1.7f;
        var nv = new VBoxContainer(); nv.AddThemeConstantOverride("separation", 8); np.AddChild(nv);
        Lbl("PATCH NOTES", 18, UiTheme.Accent, nv);
        _lnNotes = new RichTextLabel { BbcodeEnabled = true, ScrollActive = true, SizeFlagsVertical = Control.SizeFlags.ExpandFill, SelectionEnabled = true };
        _lnNotes.AddThemeFontSizeOverride("normal_font_size", 15); _lnNotes.AddThemeFontSizeOverride("bold_font_size", 15);
        nv.AddChild(_lnNotes); ShowNotes();
        var ap = Card(mid); ap.CustomMinimumSize = new Vector2(380, 0);
        _lnAcct = new VBoxContainer(); _lnAcct.AddThemeConstantOverride("separation", 8); ap.AddChild(_lnAcct); BuildAccount();

        // bottom: status, progress, play
        var bp = Card(main);
        var bh = new HBoxContainer(); bh.AddThemeConstantOverride("separation", 14); bp.AddChild(bh);
        var sv = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center }; sv.AddThemeConstantOverride("separation", 6); bh.AddChild(sv);
        _lnStatus = Lbl("", 15, Colors.White, sv); _lnStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _lnBar = new ProgressBar { MinValue = 0, MaxValue = 100, CustomMinimumSize = new Vector2(0, 20), ShowPercentage = true }; sv.AddChild(_lnBar);
        _lnRetry = Btn("Try again", StartCheck); _lnRetry.Visible = false; bh.AddChild(_lnRetry);
        _lnOffline = Btn("PLAY OFFLINE", PlayOffline); _lnOffline.CustomMinimumSize = new Vector2(190, 60); _lnOffline.TooltipText = "Your heroes on this computer, and Multiplayer (host or join friends)."; bh.AddChild(_lnOffline);
        _lnOnline = Btn("PLAY ONLINE", PlayOnline); _lnOnline.CustomMinimumSize = new Vector2(230, 60); _lnOnline.AddThemeFontSizeOverride("font_size", 20); bh.AddChild(_lnOnline);

        AddChild(root); _panel = root;
        RefreshLauncher();
    }

    PanelContainer Card(Node parent)
    {
        var p = new PanelContainer(); p.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(.02f, .03f, .05f, .82f), UiTheme.AccentSoft, 1, 18, 14)); parent.AddChild(p); return p;
    }

    void ShowNotes()
    {
        if (_lnNotes == null) return;
        string md = Net.ServerNotes != "" ? Net.ServerNotes : Updater.LocalNotes();
        _lnNotes.Text = md == "" ? "[color=#7fa6c8]Patch notes appear here once the launcher has reached the server.[/color]" : Updater.NotesToBb(md);
    }

    /// <summary>Status line, progress bar and which buttons are usable, from the current stage.</summary>
    void RefreshLauncher()
    {
        if (_lnStatus == null || _lnBar == null || _lnOnline == null || _lnOffline == null || _lnRetry == null) return;
        bool ready = _lnStage == "ready";
        _lnOffline.Disabled = !ready;
        _lnOnline.Disabled = !(ready && Updater.OnlineOk && _lnLogged && !_lnBusy);
        _lnOnline.TooltipText = !ready ? "Wait for the update check." : !Updater.OnlineOk ? "Online play is locked: " + Updater.OnlineWhy : !_lnLogged ? "Log in on the right first." : "Enter the shared world as " + _lnEmail + ".";
        _lnRetry.Visible = ready && !Updater.OnlineOk && _lnStage != "check";
        _lnBar.Indeterminate = _lnStage == "check" || _lnStage == "restart";
        if (ready) { _lnBar.Indeterminate = false; _lnBar.Value = 100; }
    }

    void Status(string text, Color? c = null) { if (_lnStatus == null) return; _lnStatus.Text = text; _lnStatus.AddThemeColorOverride("font_color", c ?? Colors.White); }

    // ================================================================== version check and update
    void StartCheck()
    {
        _lnStage = "check"; Updater.OnlineOk = false; Updater.OnlineWhy = "the update check has not finished.";
        Status("Connecting to the Twin Realms server to check for updates ..."); RefreshLauncher();
        var net = Net.I!;
        net.VersionFailed = why => { if (_lnStage == "check") CheckFailed("Could not reach the Twin Realms server. " + why); };
        net.AskVersion(OnServerVersion);
        // the server may take a while to answer (waking up, busy): give up after 30 s
        GetTree().CreateTimer(30f).Timeout += () => { if (_lnStage == "check") { net.VersionReply = null; net.Logout(); CheckFailed("The Twin Realms server did not answer."); } };
    }

    void OnServerVersion(int protocol, string version, string build, string zip)
    {
        if (_lnStage != "check" || !_lnOn) return;
        _upNewVer = version; _upBuild = build; string local = Updater.LocalBuild();
        ShowNotes();
        GD.Print($"[UPDATE] server: protocol {protocol}, version {version}, build {build}; this game: protocol {Net.Protocol}, build {(local == "" ? "source" : local)}");
        string last = System.IO.Path.Combine(Updater.Dir, "last_build.txt");
        if (Updater.Exported && build != "" && build != local)
        {
            if (zip == "") { UpdateFailed("The server names a newer version but has no download link."); return; }
            System.IO.Directory.CreateDirectory(Updater.Dir);
            // never loop: this build was installed already and the game still is not it (the install folder may be read-only)
            if (System.IO.File.Exists(last) && System.IO.File.ReadAllText(last).Trim() == build) { System.IO.File.Delete(last); UpdateFailed("The update was installed but did not take effect. Reinstall with TwinRealmsSetup.exe, or install to a folder you can write to."); return; }
            _lnStage = "download"; _upT0 = Time.GetTicksMsec(); RefreshLauncher();
            Status($"Downloading the update to version {(version == "" ? "?" : version)} ..."); _lnBar!.Value = 0;
            _upHttp?.QueueFree();
            _upHttp = new HttpRequest { DownloadFile = System.IO.Path.Combine(Updater.Dir, "update.zip"), MaxRedirects = 10, UseThreads = true, DownloadChunkSize = 1 << 20 }; AddChild(_upHttp);
            _upHttp.RequestCompleted += OnZipDone;
            var err = _upHttp.Request(zip, new[] { "User-Agent: TwinRealms-Launcher" });
            if (err != Error.Ok) UpdateFailed($"Could not start the download ({err}).");
            return;
        }
        if (build != "" && build == local) try { System.IO.File.Delete(last); } catch { }
        if (protocol == Net.Protocol) { Updater.OnlineOk = true; Updater.OnlineWhy = ""; }
        else Updater.OnlineWhy = Updater.Exported ? $"this game does not match the server (server version {version})."
                                                  : $"this copy runs from source with network version {Net.Protocol}, the server runs {protocol}. Pull the newest code.";
        _lnStage = "ready";
        if (Updater.OnlineOk) Status($"Twin Realms is up to date (version {Game.Version}). Ready to play.", new Color(.6f, 1f, .7f));
        else Status("Online play is locked: " + Updater.OnlineWhy + " You can still play offline.", new Color(1f, .7f, .5f));
        RefreshLauncher(); BuildAccount();
        // Remember me + Log in automatically: log straight in with the saved login
        if (Updater.OnlineOk && !_lnLogged && !_lnTriedAuto && _lnAuto && _lnToken != "" && _lnEmail != "") { _lnTriedAuto = true; Login("resume"); }
    }

    void CheckFailed(string why)
    {
        GD.Print("[UPDATE] " + why);
        _lnStage = "ready"; Updater.OnlineOk = false; Updater.OnlineWhy = why;
        Status(why + " Online play is locked; you can still play offline.", new Color(1f, .7f, .5f));
        RefreshLauncher(); BuildAccount();
    }

    void UpdateFailed(string why)
    {
        _upHttp?.QueueFree(); _upHttp = null;
        CheckFailed(why);
    }

    void OnZipDone(long result, long code, string[] headers, byte[] body)
    {
        if (_lnStage != "download") return;
        _upHttp?.QueueFree(); _upHttp = null;
        if (result != (long)HttpRequest.Result.Success || code < 200 || code >= 300) { UpdateFailed($"The download failed ({(HttpRequest.Result)result}, {code})."); return; }
        // unpack on a worker thread so the window keeps drawing the progress bar
        _lnStage = "extract"; _exDone = 0; _exTotal = 0; _exResult = ""; RefreshLauncher();
        Status("Installing the update: unpacking files ..."); _lnBar!.Value = 0;
        string dir = Updater.Dir, zip = System.IO.Path.Combine(dir, "update.zip"), fresh = System.IO.Path.Combine(dir, "new");
        Task.Run(() =>
        {
            try
            {
                if (System.IO.Directory.Exists(fresh)) System.IO.Directory.Delete(fresh, true);
                string root = System.IO.Path.GetFullPath(fresh) + System.IO.Path.DirectorySeparatorChar;
                using var z = System.IO.Compression.ZipFile.OpenRead(zip);
                _exTotal = z.Entries.Count;
                foreach (var e in z.Entries)
                {
                    string dest = System.IO.Path.GetFullPath(System.IO.Path.Combine(fresh, e.FullName));
                    if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new Exception("bad path in the zip: " + e.FullName);
                    if (e.FullName.EndsWith("/")) System.IO.Directory.CreateDirectory(dest);
                    else { System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest)!); System.IO.Compression.ZipFileExtensions.ExtractToFile(e, dest, true); }
                    _exDone++;
                }
                _exResult = "ok";
            }
            catch (Exception ex) { _exResult = "error: " + ex.Message; }
        });
    }

    void UpdateTick()
    {
        if (!_lnOn || _lnBar == null) return;
        if (_lnStage == "download" && _upHttp != null)
        {
            long got = _upHttp.GetDownloadedBytes(), all = _upHttp.GetBodySize();
            float s = Math.Max(.1f, (Time.GetTicksMsec() - _upT0) / 1000f), mbs = got / 1048576f / s;
            if (all > 0)
            {
                _lnBar.Value = 100.0 * got / all; int left = mbs > 0 ? (int)((all - got) / 1048576f / mbs) : 0;
                Status($"Downloading the update to version {_upNewVer}: {got / 1048576} of {all / 1048576} MB  ({mbs:0.0} MB/s, about {left / 60}:{left % 60:00} left)");
            }
            else Status($"Downloading the update to version {_upNewVer}: {got / 1048576} MB ...");
        }
        else if (_lnStage == "extract")
        {
            if (_exTotal > 0) { _lnBar.Value = 100.0 * _exDone / _exTotal; Status($"Installing the update: unpacking files ({_exDone} of {_exTotal}) ..."); }
            if (_exResult == "ok") { _exResult = ""; Install(); }
            else if (_exResult.StartsWith("error")) { string r = _exResult; _exResult = ""; UpdateFailed("The update could not be unpacked: " + r[6..].Trim()); }
        }
    }

    /// <summary>Hands over to a script that replaces the game files once this process has closed, then starts the launcher again.</summary>
    void Install()
    {
        string dir = Updater.Dir, zip = System.IO.Path.Combine(dir, "update.zip"), fresh = System.IO.Path.Combine(dir, "new");
        string exeName = System.IO.Path.GetFileName(OS.GetExecutablePath()), src = "";
        foreach (var f in System.IO.Directory.GetFiles(fresh, exeName, System.IO.SearchOption.AllDirectories)) { src = System.IO.Path.GetDirectoryName(f)!; break; }
        if (src == "") { UpdateFailed("The downloaded update has no " + exeName + " in it."); return; }
        string dst = System.IO.Path.GetDirectoryName(OS.GetExecutablePath())!, exe = System.IO.Path.Combine(dst, exeName);
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "last_build.txt"), _upBuild);
        int pid = OS.GetProcessId();
        string cmd = System.IO.Path.Combine(dir, "apply_update.cmd");
        System.IO.File.WriteAllText(cmd, string.Join("\r\n",
            "@echo off",
            ":wait",
            $"tasklist /FI \"PID eq {pid}\" 2>nul | find \"{pid}\" >nul && (timeout /t 1 /nobreak >nul & goto wait)",
            $"robocopy \"{src}\" \"{dst}\" /E /R:10 /W:1 /NFL /NDL /NJH /NJS /NP >nul",
            $"start \"\" \"{exe}\"",
            $"del /q \"{zip}\"",
            $"rmdir /s /q \"{fresh}\"",
            ""));
        _lnStage = "restart"; RefreshLauncher(); Status("Update installed. Restarting the launcher ...", new Color(.6f, 1f, .7f));
        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c \"{cmd}\"") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = dir };
        System.Diagnostics.Process.Start(psi);
        GD.Print($"[UPDATE] installing {_upNewVer} into {dst}, restarting");
        GetTree().CreateTimer(.6f).Timeout += () => GetTree().Quit();
    }

    // ================================================================== account
    void BuildAccount()
    {
        if (_lnAcct == null) return;
        foreach (var c in _lnAcct.GetChildren()) c.QueueFree();
        var v = _lnAcct;
        if (_lnLogged)
        {
            Lbl("ONLINE ACCOUNT", 18, UiTheme.Accent, v);
            Row(v, "Logged in as", UiTheme.Dim, 14);
            Row(v, _lnEmail, Colors.White, 18);
            Row(v, _lnRemember ? (_lnAuto ? "Remembered on this computer. You are logged in automatically." : "Remembered on this computer.") : "Not remembered: you log in again next time.", UiTheme.Dim, 13);
            v.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
            Row(v, "Press PLAY ONLINE to choose your hero and enter the shared world.", UiTheme.Highlight, 14);
            var lo = Btn("Log out", LogoutLauncher); lo.Disabled = _lnBusy; v.AddChild(lo);
            if (_lnMsg != "") Row(v, _lnMsg, new Color(1f, .7f, .5f), 13);
            return;
        }
        Lbl("LOG IN TO PLAY ONLINE", 18, UiTheme.Accent, v);
        bool can = _lnStage == "ready" && Updater.OnlineOk && !_lnBusy;
        if (_lnStage != "ready") Row(v, "Online login unlocks after the update check.", UiTheme.Dim, 13);
        else if (!Updater.OnlineOk) Row(v, "Online play is locked: " + Updater.OnlineWhy, new Color(1f, .7f, .5f), 13);
        Lbl("EMAIL", 13, UiTheme.Dim, v);
        var email = new LineEdit { Text = _lnEmail, PlaceholderText = "you@example.com", Editable = can, CustomMinimumSize = new Vector2(0, 36) }; v.AddChild(email);
        email.TextChanged += s => _lnEmail = s.Trim();
        bool saved = _lnToken != "";
        Lbl("PASSWORD", 13, UiTheme.Dim, v);
        var pw = new LineEdit { Secret = true, PlaceholderText = saved ? "saved login: leave empty" : $"at least {OnlineCfg.MinPassword} characters", Editable = can, CustomMinimumSize = new Vector2(0, 36) }; v.AddChild(pw);
        var rem = new CheckBox { Text = "Remember me", ButtonPressed = _lnRemember, Disabled = !can, FocusMode = Control.FocusModeEnum.None }; v.AddChild(rem);
        var auto = new CheckBox { Text = "Log in automatically", ButtonPressed = _lnAuto && _lnRemember, Disabled = !can || !_lnRemember, FocusMode = Control.FocusModeEnum.None }; v.AddChild(auto);
        rem.Toggled += on => { _lnRemember = on; if (!on) { _lnAuto = false; auto.ButtonPressed = false; } auto.Disabled = !on; };
        auto.Toggled += on => _lnAuto = on && _lnRemember;
        void Go(string op) { string p = pw.Text; pw.Text = ""; if (op == "login" && p == "" && saved) Login("resume"); else Login(op, p); }
        pw.TextSubmitted += _ => Go("login");
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); v.AddChild(row);
        var li = Btn("Log in", () => Go("login")); li.Disabled = !can; li.CustomMinimumSize = new Vector2(140, 42); row.AddChild(li);
        var rg = Btn("Create account", () => Go("register")); rg.Disabled = !can; rg.CustomMinimumSize = new Vector2(170, 42); row.AddChild(rg);
        string msg = _lnBusy ? Net.AuthMsg : _lnMsg;
        if (msg != "") Row(v, msg, _lnBusy ? UiTheme.Highlight : new Color(1f, .7f, .5f), 14).CustomMinimumSize = new Vector2(340, 0);
        Row(v, "New here? Type your email and a password, then Create account.", UiTheme.Dim, 12).CustomMinimumSize = new Vector2(340, 0);
    }

    /// <summary>op: login / register (with the password) or resume (with the saved login token).</summary>
    void Login(string op, string password = "")
    {
        if (!Updater.OnlineOk || _lnBusy) return;
        if (_lnEmail == "") { _lnMsg = "Type your email first."; BuildAccount(); return; }
        _lnOp = op; _lnBusy = true; _lnMsg = "";
        Net.I!.Connect(OnlineCfg.Server, op, _lnEmail, op == "resume" ? _lnToken : password);
        BuildAccount(); RefreshLauncher();
    }

    void LogoutLauncher()
    {
        // tell the server to forget the saved login too: log in with it once more, forget it, close (OnLauncherOnline)
        string tok = _lnToken != "" ? _lnToken : Net.SessionToken;
        _lnLogged = false; _lnMsg = ""; Net.SessionToken = "";
        if (tok != "" && Updater.OnlineOk) { _lnForget = true; _lnBusy = true; _lnForgetTok = tok; Net.I!.Connect(OnlineCfg.Server, "resume", _lnEmail, tok); }
        _lnToken = ""; SaveSaved();
        BuildAccount(); RefreshLauncher();
    }

    /// <summary>Login state changed while the launcher is up (UiOnline.OnOnlineChanged hands it here).</summary>
    void OnLauncherOnline()
    {
        if (!_lnBusy) return;
        if (Net.OnlineState == "lobby" && Game.Online)
        {
            if (_lnForget) { _lnForget = false; _lnBusy = false; Net.I!.ForgetToken(_lnForgetTok); _lnForgetTok = ""; Net.I.Logout(); BuildAccount(); RefreshLauncher(); return; }
            // logged in: keep the token (on disk only with Remember me), close the connection; PLAY ONLINE logs in again with it
            _lnBusy = false; _lnLogged = true; _lnEmail = Net.SessionEmail; _lnToken = Net.SessionToken; _lnMsg = "";
            SaveSaved();
            Net.I!.Logout();
            BuildAccount(); RefreshLauncher();
            return;
        }
        if (Net.OnlineState == "")
        {
            _lnBusy = false; bool forgetting = _lnForget; _lnForget = false;
            if (!forgetting)
            {
                _lnMsg = Net.AuthMsg != "" ? Net.AuthMsg : "Could not log in.";
                if (_lnOp == "resume") { _lnToken = ""; SaveSaved(); }   // the saved login no longer works
            }
            BuildAccount(); RefreshLauncher();
            return;
        }
        BuildAccount();   // connecting / checking: show the progress message
    }

    void LoadSaved()
    {
        try
        {
            if (!FileAccess.FileExists(SavedPath)) return;
            using var f = FileAccess.OpenEncryptedWithPass(SavedPath, FileAccess.ModeFlags.Read, SavedKey); if (f == null) return;
            using var d = JsonDocument.Parse(f.GetAsText()); var r = d.RootElement;
            _lnEmail = r.TryGetProperty("email", out var e) ? e.GetString() ?? "" : "";
            _lnToken = r.TryGetProperty("token", out var t) ? t.GetString() ?? "" : "";
            _lnRemember = !r.TryGetProperty("remember", out var rm) || rm.GetBoolean();
            _lnAuto = r.TryGetProperty("auto", out var au) && au.GetBoolean();
        }
        catch (Exception ex) { GD.Print("[LAUNCHER] saved login unreadable: " + ex.Message); }
    }

    void SaveSaved()
    {
        try
        {
            var o = new Dictionary<string, object> { ["email"] = _lnEmail, ["token"] = _lnRemember ? _lnToken : "", ["remember"] = _lnRemember, ["auto"] = _lnRemember && _lnAuto };
            using var f = FileAccess.OpenEncryptedWithPass(SavedPath, FileAccess.ModeFlags.Write, SavedKey);
            f?.StoreString(JsonSerializer.Serialize(o));
        }
        catch (Exception ex) { GD.Print("[LAUNCHER] could not store the login: " + ex.Message); }
    }

    // ================================================================== play
    void LeaveLauncher()
    {
        _lnOn = false; _lnStage = ""; CloseNoUnpause();
        if (DisplayServer.GetName() != "headless")
        {
            DisplayServer.WindowSetTitle("Twin Realms");
            Game.ApplyDisplay();
            if (Game.Set.WindowMode == 0) DisplayServer.WindowSetMode(DisplayServer.WindowMode.Maximized);
        }
    }

    void PlayOffline() { LeaveLauncher(); ShowTitle(); }

    /// <summary>Hands the login to the game: logs in with the token and opens the online hero select (UiOnline.OnOnlineChanged).</summary>
    void PlayOnline()
    {
        if (!_lnLogged || _lnToken == "") return;
        string email = _lnEmail, tok = _lnToken;
        LeaveLauncher();
        Net.LastNote = "";
        if (!Net.I!.Connect(OnlineCfg.Server, "resume", email, tok)) ShowTitle();
        else ShowOnline();   // shows "Connecting ..." until the server answers; then the online hero select
    }
}
