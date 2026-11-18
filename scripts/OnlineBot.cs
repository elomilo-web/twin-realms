using System;
using System.Text;
using System.Threading.Tasks;
using Godot;

namespace TwinRealms;

/// <summary>
/// Online server self-test (tools/online_test.sh starts a local server, then runs this):
///   godot --headless --path . -- --online-test --url=ws://127.0.0.1:8089
/// Creates an account, checks wrong passwords and duplicate accounts are refused, creates a hero, enters the shared world,
/// saves, logs out, logs in again and checks the hero came back from the server. Writes bot_out/online_<process id>.txt; exit code 0 = all passed.
/// </summary>
public partial class OnlineBot : Node
{
    public static bool Wanted() => Array.IndexOf(OS.GetCmdlineUserArgs(), "--online-test") >= 0;
    static string Arg(string k, string d) { foreach (var a in OS.GetCmdlineUserArgs()) if (a.StartsWith("--" + k + "=")) return a[(k.Length + 3)..]; return d; }

    readonly StringBuilder _log = new(); int _pass, _fail;
    void Say(string s) { GD.Print("[ONLINEBOT] " + s); _log.AppendLine(s); }
    void Check(string name, bool ok, string detail = "") { if (ok) _pass++; else _fail++; Say((ok ? "PASS  " : "FAIL  ") + name + (detail != "" ? "  (" + detail + ")" : "")); }
    async Task Wait(float s) { await ToSignal(GetTree().CreateTimer(s, true), "timeout"); }
    async Task<bool> Until(Func<bool> f, float timeout) { float t = 0; while (t < timeout) { if (f()) return true; await Wait(.2f); t += .2f; } return f(); }

    public override void _Ready() { ProcessMode = ProcessModeEnum.Always; _ = Run(); }

    async Task<bool> Login(string url, string op, string email, string pw)
    {
        Net.I!.Connect(url, op, email, pw);
        await Until(() => Net.OnlineState == "lobby" || Net.OnlineState == "", 20f);
        return Net.OnlineState == "lobby";
    }

    async Task Run()
    {
        string url = Arg("url", "ws://127.0.0.1:8089");
        string email = $"tester{OS.GetProcessId()}x{DateTime.UtcNow.Ticks % 100000000}@example.com", pw = "correct horse 42";
        try
        {
            await Wait(1f);
            Check("a short password is refused", !await Login(url, "register", email, "short"), Net.AuthMsg);
            Check("a new account is created", await Login(url, "register", email, pw), Net.AuthMsg);
            Check("the new account has no heroes", Game.Online && Game.Peek(0) == null && Game.Peek(1) == null && Game.Peek(2) == null);
            Net.I!.Logout();
            Check("the same email cannot register twice", !await Login(url, "register", email.ToUpper(), pw), Net.AuthMsg);
            Check("a wrong password is refused", !await Login(url, "login", email, "wrong password"), Net.AuthMsg);
            Check("logging in works", await Login(url, "login", "  " + email.ToUpper() + " ", pw), Net.AuthMsg);
            // create a hero in slot 2 and enter the shared world
            Game.Slot = 1;
            Ui.OnNew?.Invoke("ranger", "Netwalker", "elf");
            bool inWorld = await Until(() => Net.OnlineState == "in" && Player.Instance != null && Net.Active, 60f);
            Check("the hero enters the online world", inWorld, $"state {Net.OnlineState}, {Net.I.Players.Count} players");
            Check("we are on the roster, the server is not", Net.I.Players.ContainsKey(Net.MyId) && !Net.I.Players.ContainsKey(1), $"{Net.I.Players.Count} players");
            Check("we are a client, not the host", Net.IsClient);
            if (int.TryParse(Arg("expect", "1"), out int expect) && expect > 1)
            {
                // several test clients run at once: everyone must see the others' heroes moving in the shared world
                bool all = await Until(() => Net.I.Players.Count >= expect, 90f);
                Check($"all {expect} players are on the roster", all, $"{Net.I.Players.Count} players");
                bool drawn = await Until(() => { int n = 0; foreach (var id in Net.I.Players.Keys) if (id != Net.MyId && Net.I.HeroOf(id) is { HasPose: true }) n++; return n >= expect - 1; }, 30f);
                Check("the other heroes are drawn from their poses", drawn);
                await Wait(6f);   // let the others see us too before anyone logs out
            }
            await Wait(2f);
            Game.D.Gold = 4242; Game.D.Kills = 7; Game.Save();
            Net.I.Logout();   // straight away, like Quit to title: the last save must still reach the server
            Check("logging out returns to the local saves", !Game.Online && Net.OnlineState == "");
            Check("logging in again works", await Login(url, "login", email, pw), Net.AuthMsg);
            var h = Game.Peek(1);
            Check("the hero came back from the server", h != null && h.Name == "Netwalker" && h.Class == "ranger" && h.Race == "elf", h == null ? "slot empty" : h.Name);
            Check("progress saved in the world reached the server", h != null && h.Gold == 4242 && h.Kills == 7, h == null ? "" : $"gold {h.Gold}, kills {h.Kills}");
            Check("the last played slot is remembered", Game.Slot == 1, $"slot {Game.Slot}");
            // deleting during hero select goes through the login channel
            Game.DeleteSlot(1); await Wait(1f); Net.I.Logout();
            Check("logging in after a delete works", await Login(url, "login", email, pw), Net.AuthMsg);
            Check("the deleted hero stays deleted", Game.Peek(1) == null);
            Net.I.Logout();
            Check("five wrong passwords lock the account for a while", await Lockout(url, email));
        }
        catch (Exception e) { Check("no exceptions", false, e.ToString()); }
        Finish();
    }

    async Task<bool> Lockout(string url, string email)
    {
        for (int i = 0; i < OnlineCfg.FailedBeforeLock; i++) await Login(url, "login", email, "nope " + i);
        bool ok = await Login(url, "login", email, "correct horse 42");   // right password, but locked
        string msg = Net.AuthMsg; if (ok) Net.I!.Logout();
        return !ok && msg.Contains("Too many");
    }

    void Finish()
    {
        Say($"=== {_pass} passed, {_fail} failed");
        string dir = ProjectSettings.GlobalizePath("res://") + "bot_out/"; DirAccess.MakeDirRecursiveAbsolute(dir);
        using (var f = FileAccess.Open(dir + $"online_{OS.GetProcessId()}.txt", FileAccess.ModeFlags.Write)) f?.StoreString(_log.ToString());
        Net.I?.Logout();
        // drain .NET finalizers while the engine is alive (Godot .NET shutdown race, see Bot.Finish)
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        GetTree().Quit(_fail > 0 ? 1 : 0);
    }
}
