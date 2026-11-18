using Godot;

namespace TwinRealms;

/// <summary>Game root: shows the title screen, then builds the world, player, camera and HUD.</summary>
public partial class Main : Node3D
{
    float _autosave; bool _started;

    public override void _Ready()
    {
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--bake-anims") >= 0) { Avatar.Bake(); GetTree().Quit(); return; }   // tools: rebuild assets/models/anim/clips.res
        GD.Print("[MAIN] scene start"); Inputs.Setup(); Game.Root = this; Dungeon.Active = false; Arenas.Active = false; World.Realm = 0; World.RealmT = 0; Ui.InGame = false;
        if (ServerWanted()) { RunServer(); return; }
        bool testing = Observer.Wanted() || Bot.Wanted() || MpBot.Wanted() != null || OnlineBot.Wanted();
        if (testing) Game.SetPath = "user://test_settings.json";
        Game.LoadSettings();
        if (testing) Inputs.ResetAll(false);   // test bots press the default keys
        AddChild(new Sfx { Name = "Sfx" });
        AddChild(new Net { Name = "Net" });
        var ui = new Ui { Name = "Ui" }; AddChild(ui);
        // logged in to the online server: the hero goes into the shared world instead of a private one
        Ui.OnNew = (cls, name, race) => { Game.NewGame(cls, name, race); Game.D.Body = Ui.PickedBody; Game.D.PosX = 0; Game.D.PosZ = 9; if (Game.Online) Net.I!.EnterOnline(JoinWorld); else Begin(); };
        Ui.OnContinue = () => { if (!Game.Load()) Game.NewGame("warrior", "Hero"); if (Game.Online) Net.I!.EnterOnline(JoinWorld); else Begin(); };   // plays the hero in Game.Slot (chosen on the character select screen)
        // multiplayer from the title: bring the saved hero (or the one picked on the title screen)
        void Hero() { if (!Game.HasSave() || !Game.Load()) { Game.NewGame(Ui.PickedClass, Ui.PickedName, Ui.PickedRace); Game.D.Body = Ui.PickedBody; Game.D.PosX = 0; Game.D.PosZ = 9; } }
        Ui.OnHost = port => { Hero(); Begin(); Net.I!.Host(port); };
        Ui.OnJoin = (addr, port) => { Hero(); Net.I!.Join(addr, port, JoinWorld); };
        if (Observer.Wanted() || Bot.Wanted()) Game.SavePath = "user://test_save.json";
        if (MpBot.Wanted() is string m) { Game.SavePath = $"user://mp_{m}_save.json"; Net.ChestPath = "user://mp_test_party_chest.json"; }
        if (Observer.Wanted()) { Game.NewGame("warrior", "Observer"); Begin(); AddChild(new Observer { Name = "Observer" }); return; }
        if (Bot.Wanted()) { Game.NewGame("warrior", "Bot"); Begin(); AddChild(new Bot { Name = "Bot" }); return; }
        if (OnlineBot.Wanted()) { Game.SavePath = "user://online_test_local_save.json"; AddChild(new OnlineBot { Name = "OnlineBot" }); return; }
        if (MpBot.Wanted() is string mode) { AddChild(new MpBot { Name = "MpBot", Mode = mode }); if (mode == "host") { Game.NewGame("paladin", "HostBot", "dwarf"); Begin(); } return; }
        ui.ShowTitle();
        // a calm backdrop behind the title screen
        var cam = new Camera3D { Name = "TitleCam", Position = new Vector3(0, 30, 60), RotationDegrees = new Vector3(-12, 0, 0), Current = true }; AddChild(cam);
        AddChild(new WorldEnvironment { Name = "TitleEnv", Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(.04f, .03f, .08f) } });
    }

    /// <summary>The host accepted us: build the world next to the host's hero (online: where the hero was saved).</summary>
    public void JoinWorld()
    {
        var at = Net.I!.WelcomePos; Game.D.Time = Net.I.WelcomeTime;
        if (!Game.Online) { Game.D.PosX = at.X + 2.5f; Game.D.PosZ = at.Z + 2.5f; }
        Ui.I?.Close(); Begin();
        if (Game.Online) { Net.OnlineState = "in"; Ui.Banner("Twin Realms Online", $"{Net.I.Players.Count} heroes in the world"); }
        else Ui.Banner("Joined", $"{Net.I.NameOf(1)}'s world");
    }

    // ---- online server: godot --headless --path . -- --server   (port: PORT environment variable, else data/online.json)
    static bool ServerWanted() => System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--server") >= 0;
    void RunServer()
    {
        GD.Print("[SERVER] building the world ...");
        OnlineCfg.Load(); Engine.MaxFps = 60;
        Game.SavePath = "user://server_world_save.json"; Game.SetPath = "user://server_settings.json";
        Net.ChestPath = System.IO.Path.Combine(Accounts.Dir, "party_chest.json");
        AddChild(new Sfx { Name = "Sfx" });
        AddChild(new Net { Name = "Net" });
        AddChild(new Ui { Name = "Ui" });
        Net.Dedicated = true;
        Game.NewGame("warrior", "Server"); Game.D.PosX = 0; Game.D.PosZ = 9;
        Begin(true);
        // the server's own hero is only there because the world code expects one: hidden, frozen, never chased or listed
        if (Player.Instance is Player p) { p.Visible = false; p.ProcessMode = ProcessModeEnum.Disabled; p.CollisionLayer = 0; p.CollisionMask = 0; }
        if (GetNodeOrNull<Node>("CameraRig") is Node rig) rig.ProcessMode = ProcessModeEnum.Disabled;
        string env = System.Environment.GetEnvironmentVariable("PORT") ?? "";
        int port = int.TryParse(env, out var ep) && ep > 0 ? ep : OnlineCfg.Port;
        if (!Net.I!.Serve(port)) GetTree().Quit(1);
    }

    void Begin(bool server = false)
    {
        if (_started) return; _started = true;
        GetNodeOrNull("TitleCam")?.QueueFree(); GetNodeOrNull("TitleEnv")?.QueueFree();
        Game.RecalcTree();
        if (!server) AddChild(new Toon { Name = "Toon" });   // first, so every mesh built after it gets the toon look
        var world = new World { Name = "World" }; AddChild(world);
        float px = Game.D.PosX, pz = Game.D.PosZ; var pos = World.OnGround(px, pz, 0.6f);
        var player = new Player { Name = "Player", Position = pos };
        var rig = new CameraRig { Name = "CameraRig", Target = player, Position = pos + new Vector3(0, 1.6f, 0) };
        player.Rig = rig; AddChild(player); AddChild(rig); rig.ExcludeFromCollision(player);
        if (server) return;
        AddChild(new Hud { Name = "Hud" });
        AddChild(new Ambient { Name = "Ambient" });
        Ui.InGame = true; Input.MouseMode = Input.MouseModeEnum.Captured;
        Ui.Banner(World.GetPoi("village")?.Name ?? "Oakhaven", $"{Inputs.KeyOf(Inputs.Pack)} pack, {Inputs.KeyOf(Inputs.Tree)} skill tree, {Inputs.KeyOf(Inputs.SkillsUi)} skills, {Inputs.KeyOf(Inputs.Journal)} quests, {Inputs.KeyOf(Inputs.Lock)} lock on");
        if (Game.TreeRefunded) { Game.TreeRefunded = false; Ui.Note("The skill tree has grown five times larger. Your tree points were refunded: press K.", new Color(1f, .85f, .4f)); }
        Game.Save();
    }

    public override void _Process(double delta)
    {
        if (!_started || Net.Dedicated) return;
        float dt = (float)delta; Game.PlayTime += dt; Game.D.Played += dt; Arenas.Tick(dt);
        _autosave += dt; if (_autosave > (Game.Online ? OnlineCfg.SaveEvery : 60f) && !Arenas.Active) { _autosave = 0; Game.Save(); }
    }
}
