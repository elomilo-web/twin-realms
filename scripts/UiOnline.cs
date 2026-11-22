using Godot;

namespace TwinRealms;

/// <summary>Play Online: log in or create an account on the online server, then pick a hero saved on the server (the title screen in online mode).</summary>
public partial class Ui
{
    static string _olEmail = "", _olPw = "";

    public void ShowOnline()
    {
        InGame = false; Input.MouseMode = Input.MouseModeEnum.Visible; OnlineCfg.Load();
        if (!Updater.OnlineOk) { ShowTitle(); return; }   // only after the launch check confirmed this game matches the server
        var v = Frame("title", "PLAY ONLINE", new Vector2(700, 500), ShowOnline);
        Row(v, "Log in to the Twin Realms server to play in one shared world with everyone online. Your heroes are saved on the server, so you can play them from any computer.", Colors.White, 15).CustomMinimumSize = new Vector2(640, 0);
        bool busy = Net.OnlineState == "connecting";
        LineEdit Field(string label, string text, string hint, System.Action<string> set, bool secret = false)
        {
            Lbl(label, 14, UiTheme.Accent, v);
            var e = new LineEdit { Text = text, PlaceholderText = hint, Secret = secret, CustomMinimumSize = new Vector2(0, 36), Editable = !busy }; v.AddChild(e);
            e.TextChanged += s => set(s); return e;
        }
        var email = Field("EMAIL", _olEmail, "you@example.com", s => _olEmail = s);
        var pw = Field("PASSWORD", _olPw, $"at least {OnlineCfg.MinPassword} characters", s => _olPw = s, true);
        void Go(string op) { Net.LastNote = ""; Net.I?.Connect(OnlineCfg.Server, op, _olEmail, _olPw); _olPw = ""; }
        pw.TextSubmitted += _ => Go("login");
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); v.AddChild(row);
        var login = Btn("Log in", () => Go("login")); login.Disabled = busy; login.CustomMinimumSize = new Vector2(160, 44); row.AddChild(login);
        var reg = Btn("Create account", () => Go("register")); reg.Disabled = busy; reg.CustomMinimumSize = new Vector2(180, 44); row.AddChild(reg);
        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        row.AddChild(Btn("Back", () => { if (busy) Net.I?.Logout(); ShowTitle(); }));
        if (Net.AuthMsg != "") Row(v, Net.AuthMsg, busy ? UiTheme.Highlight : new Color(1f, .7f, .5f), 15).CustomMinimumSize = new Vector2(640, 0);
        Row(v, "Create account makes a new account with this email and password. Passwords are stored only as a salted hash.", Dim, 13).CustomMinimumSize = new Vector2(640, 0);
        if (_olEmail == "") email.CallDeferred(Control.MethodName.GrabFocus); else pw.CallDeferred(Control.MethodName.GrabFocus);
    }

    /// <summary>Login finished, failed or the connection dropped: show the right screen (only while on the title screens).</summary>
    void OnOnlineChanged()
    {
        if (InGame || _upStage != "" || (_panelName != "title" && _panelName != "")) return;   // _upStage: the launch update check owns the screen   // "" = Play was pressed and entering failed
        if (Game.Online && Net.OnlineState == "lobby") { ShowTitle(); return; }
        if (!Game.Online) ShowOnline();
    }
}
