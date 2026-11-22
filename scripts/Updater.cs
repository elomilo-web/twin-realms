using System;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>
/// Launch check and auto update. Before the title screen the game asks the online server (OnlineCfg.Server, Railway) which version
/// players must run: the server answers from data/release.json (written and committed by tools/run.ps1 publish, so every published
/// release redeploys the server with the matching code) with the build id and the download link of the release zip.
/// - Exported game with a different build id: downloads the zip, unpacks it into user://update/, and starts a small script that waits
///   for the game to close, copies the new files over the install folder and starts the game again. Saves live in %APPDATA%, untouched.
/// - Same build (or, running from source, the same network protocol): Play Online is unlocked.
/// - Server unreachable, download failed, or a mismatch that cannot be fixed by updating: Play Online stays locked (offline play works).
/// </summary>
public static class Updater
{
    static bool _done;   // checked once per launch ("Quit to title" reloads the scene)
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
    public static bool Exported => OS.HasFeature("template") && OS.GetName() == "Windows" && LocalBuild() != "";

    public static string Field(string path, string key)
    {
        if (!FileAccess.FileExists(path)) return "";
        try { using var d = JsonDocument.Parse(FileAccess.GetFileAsString(path)); return d.RootElement.TryGetProperty(key, out var b) ? b.GetString() ?? "" : ""; }
        catch { return ""; }
    }

    public static string Dir => ProjectSettings.GlobalizePath("user://update");
}

/// <summary>What the server tells players to run (data/release.json, committed by tools/run.ps1 publish).</summary>
public record Release(string Version, string Build, string Zip)
{
    public static Release Load()
    {
        const string p = "res://data/release.json";
        return new Release(Updater.Field(p, "version"), Updater.Field(p, "build"), Updater.Field(p, "zip"));
    }
}

/// <summary>The update screen shown before the title (see Updater).</summary>
public partial class Ui
{
    HttpRequest? _upHttp; Label? _upMsg; ProgressBar? _upBar; ulong _upT0; string _upNewVer = "", _upBuild = "";
    string _upStage = "";   // server, zip, install ("" = not updating)

    public void ShowUpdate()
    {
        InGame = false; Input.MouseMode = Input.MouseModeEnum.Visible;
        Updater.OnlineOk = false; Updater.OnlineWhy = "the update check has not finished.";
        var v = Frame("title", "TWIN REALMS", new Vector2(640, 300), () => { });   // no rebuild: it would restart the check
        Lbl("Version " + Game.Version, 13, Dim, v);
        _upMsg = Row(v, "Checking the Twin Realms server for updates ...", Colors.White, 16); _upMsg.CustomMinimumSize = new Vector2(580, 0);
        _upBar = new ProgressBar { MinValue = 0, MaxValue = 100, Value = 0, CustomMinimumSize = new Vector2(0, 22), Visible = false }; v.AddChild(_upBar);
        _upStage = "server";
        var net = Net.I!; net.VersionFailed = why => { if (_upStage == "server") UpdateFailed("Could not reach the Twin Realms server. " + why); };
        net.AskVersion(OnServerVersion);
        // the server may take a while to answer (waking up, busy): give up after 30 s
        GetTree().CreateTimer(30f).Timeout += () => { if (_upStage == "server") { net.VersionReply = null; net.Logout(); UpdateFailed("The Twin Realms server did not answer."); } };
    }

    void OnServerVersion(int protocol, string version, string build, string zip)
    {
        if (_upStage != "server") return;
        _upNewVer = version; _upBuild = build; string local = Updater.LocalBuild();
        GD.Print($"[UPDATE] server: protocol {protocol}, version {version}, build {build}; this game: protocol {Net.Protocol}, build {(local == "" ? "source" : local)}");
        if (Updater.Exported && build != "" && build != local)
        {
            if (zip == "") { UpdateFailed("The server has a newer version but no download link."); return; }
            System.IO.Directory.CreateDirectory(Updater.Dir);
            // never loop: this build was installed already and the game still is not it (the install folder may be read-only)
            string last = System.IO.Path.Combine(Updater.Dir, "last_build.txt");
            if (System.IO.File.Exists(last) && System.IO.File.ReadAllText(last).Trim() == build) { System.IO.File.Delete(last); UpdateFailed("The update was installed but did not take effect. Download the newest version from the Twin Realms releases page, or move the game to a folder you can write to."); return; }
            _upStage = "zip"; _upT0 = Time.GetTicksMsec();
            if (_upMsg != null) _upMsg.Text = $"Downloading the update to {(version == "" ? "the newest version" : "version " + version)} ...";
            if (_upBar != null) _upBar.Visible = true;
            _upHttp?.QueueFree();
            _upHttp = new HttpRequest { DownloadFile = System.IO.Path.Combine(Updater.Dir, "update.zip"), MaxRedirects = 10, UseThreads = true }; AddChild(_upHttp);
            _upHttp.RequestCompleted += OnZipDone;
            var err = _upHttp.Request(zip, new[] { "User-Agent: TwinRealms-Updater" });
            if (err != Error.Ok) UpdateFailed($"Could not start the download ({err}).");
            return;
        }
        if (build != "" && build == local) try { System.IO.File.Delete(System.IO.Path.Combine(Updater.Dir, "last_build.txt")); } catch { }
        if (protocol == Net.Protocol) { Updater.OnlineOk = true; Updater.OnlineWhy = ""; }
        else
        {
            Updater.OnlineOk = false;
            Updater.OnlineWhy = Updater.Exported ? $"this game does not match the server (server version {version})."
                                                 : $"this copy runs from source with network version {Net.Protocol}, the server runs {protocol}. Pull the newest code.";
        }
        StartTitle();
    }

    void OnZipDone(long result, long code, string[] headers, byte[] body)
    {
        if (_upStage != "zip") return;
        if (result != (long)HttpRequest.Result.Success || code < 200 || code >= 300) { UpdateFailed($"The download failed ({(HttpRequest.Result)result}, {code})."); return; }
        try { Install(); } catch (Exception e) { UpdateFailed("The update could not be installed: " + e.Message); }
    }

    void UpdateTick()
    {
        if (_upStage != "zip" || _upHttp == null || _upBar == null || _upMsg == null) return;
        long got = _upHttp.GetDownloadedBytes(), all = _upHttp.GetBodySize();
        if (all > 0) { _upBar.Value = 100.0 * got / all; float s = Math.Max(.1f, (Time.GetTicksMsec() - _upT0) / 1000f); _upMsg.Text = $"Downloading the update to {(_upNewVer == "" ? "the newest version" : "version " + _upNewVer)}: {got / 1048576} of {all / 1048576} MB ({got / 1048576f / s:0.0} MB/s)"; }
    }

    /// <summary>Unpacks the zip and hands over to a script that replaces the game files once this process has closed.</summary>
    void Install()
    {
        _upStage = "install"; if (_upMsg != null) _upMsg.Text = "Installing the update ..."; if (_upBar != null) _upBar.Value = 100;
        string dir = Updater.Dir, zip = System.IO.Path.Combine(dir, "update.zip"), fresh = System.IO.Path.Combine(dir, "new");
        if (System.IO.Directory.Exists(fresh)) System.IO.Directory.Delete(fresh, true);
        System.IO.Compression.ZipFile.ExtractToDirectory(zip, fresh);
        // the zip holds a TwinRealms/ folder: find the folder with the game's exe in it
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
        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c \"{cmd}\"") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = dir };
        System.Diagnostics.Process.Start(psi);
        GD.Print($"[UPDATE] installing {_upNewVer} into {dst}, restarting");
        GetTree().Quit();
    }

    void UpdateFailed(string why)
    {
        GD.Print("[UPDATE] " + why);
        _upStage = ""; _upHttp?.QueueFree(); _upHttp = null;
        Updater.OnlineOk = false; Updater.OnlineWhy = why;
        var v = Frame("title", "TWIN REALMS", new Vector2(640, 320), () => { });
        Lbl("Version " + Game.Version, 13, Dim, v);
        Row(v, why, new Color(1f, .7f, .5f), 16).CustomMinimumSize = new Vector2(580, 0);
        Row(v, "Play Online is locked until the game is up to date. You can still play on this computer.", Dim, 14).CustomMinimumSize = new Vector2(580, 0);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); v.AddChild(row);
        row.AddChild(Btn("Try again", ShowUpdate));
        row.AddChild(Btn("Play offline", StartTitle));
        row.AddChild(Btn("Quit game", () => GetTree().Quit()));
    }

    void StartTitle() { _upStage = ""; _upHttp?.QueueFree(); _upHttp = null; ShowTitle(); }
}
