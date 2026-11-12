using Godot;

namespace TwinRealms;

/// <summary>Game root: shows the title screen, then builds the world, player, camera and HUD.</summary>
public partial class Main : Node3D
{
    float _autosave; bool _started;

    public override void _Ready()
    {
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--list-anims") >= 0) { Avatar.ListSources(); GetTree().Quit(); return; }
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--bake-anims") >= 0) { Avatar.Bake(); GetTree().Quit(); return; }   // tools: rebuild assets/models/anim/clips.res
        if (AnimTool.Wanted()) { Game.Root = this; AddChild(new AnimTool { Name = "AnimTool" }); return; }   // tools: measure attack clips (impact timing)
        GD.Print("[MAIN] scene start"); Inputs.Setup(); Game.Root = this; Dungeon.Active = false; Arenas.Active = false; Threat.Reset(); World.Realm = 0; World.RealmT = 0; Ui.InGame = false;
        bool testing = Observer.Wanted() || Bot.Wanted() || Shots.Wanted() || FitBot.Wanted() || MpBot.Wanted() != null;
        if (testing) Game.SetPath = "user://test_settings.json";
        Game.LoadSettings();
        if (testing) Inputs.ResetAll(false);   // test bots press the default keys
        if (testing)
        {
            // test bots: no sound; the gameplay bots (bot, mp) also run on the lowest graphics so they render fast.
            // The picture bots (observe, shots, fit) keep full graphics, since their screenshots are the point. Nothing is saved.
            var s = Game.Set; s.Master = 0;
            if (Bot.Wanted() || MpBot.Wanted() != null) { s.Quality = 0; s.RenderScale = .5f; s.ViewDist = 300f; s.Grass = 0f; s.VSync = false; s.Outlines = false; }
            Sfx.ApplyVolumes(); Game.ApplyDisplay();
        }
        AddChild(new Sfx { Name = "Sfx" });
        AddChild(new Flag { Name = "Flag" });   // F8: playtest flag (screenshot + report in user://playtest/)
        AddChild(new Net { Name = "Net" });
        var ui = new Ui { Name = "Ui" }; AddChild(ui);
        Ui.OnNew = (cls, name, race) => { Game.NewGame(cls, name, race); Game.D.Body = Ui.PickedBody; Game.D.PosX = 0; Game.D.PosZ = 9; Begin(); };
        Ui.OnContinue = () => { if (!Game.Load()) Game.NewGame("warrior", "Hero"); Begin(); };   // plays the hero in Game.Slot (chosen on the character select screen)
        // multiplayer from the title: bring the saved hero (or the one picked on the title screen)
        void Hero() { if (!Game.HasSave() || !Game.Load()) { Game.NewGame(Ui.PickedClass, Ui.PickedName, Ui.PickedRace); Game.D.Body = Ui.PickedBody; Game.D.PosX = 0; Game.D.PosZ = 9; } }
        Ui.OnHost = port => { Hero(); Begin(); Net.I!.Host(port); };
        Ui.OnJoin = (addr, port) => { Hero(); Net.I!.Join(addr, port, JoinWorld); };
        if (Observer.Wanted() || Bot.Wanted() || Shots.Wanted()) Game.SavePath = "user://test_save.json";
        if (FitBot.Wanted()) { AddChild(new FitBot { Name = "FitBot" }); return; }   // gear fit studio: no world, no title
        if (Shots.Wanted()) { Game.NewGame("warrior", "Shots"); Begin(); AddChild(new Shots { Name = "Shots" }); return; }
        if (MpBot.Wanted() is string m) { Game.SavePath = $"user://mp_{m}_save.json"; Net.ChestPath = "user://mp_test_party_chest.json"; }
        if (Observer.Wanted()) { Game.NewGame("warrior", "Observer"); Begin(); AddChild(new Observer { Name = "Observer" }); return; }
        if (Bot.Wanted()) { Game.NewGame("warrior", "Bot"); Begin(); AddChild(new Bot { Name = "Bot" }); return; }
        if (MpBot.Wanted() is string mode) { AddChild(new MpBot { Name = "MpBot", Mode = mode }); if (mode == "host") { Game.NewGame("paladin", "HostBot", "dwarf"); Begin(); } return; }
        ui.ShowTitle();
        // a calm backdrop behind the title screen
        var cam = new Camera3D { Name = "TitleCam", Position = new Vector3(0, 30, 60), RotationDegrees = new Vector3(-12, 0, 0), Current = true }; AddChild(cam);
        AddChild(new WorldEnvironment { Name = "TitleEnv", Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(.04f, .03f, .08f) } });
    }

    /// <summary>The host accepted us: build the world next to the host's hero.</summary>
    public void JoinWorld()
    {
        var at = Net.I!.WelcomePos; Game.D.PosX = at.X + 2.5f; Game.D.PosZ = at.Z + 2.5f; Game.D.Time = Net.I.WelcomeTime;
        Ui.I?.Close(); Begin();
        Ui.Banner("Joined", $"{Net.I.NameOf(1)}'s world");
    }

    void Begin()
    {
        if (_started) return; _started = true;
        GetNodeOrNull("TitleCam")?.QueueFree(); GetNodeOrNull("TitleEnv")?.QueueFree();
        Game.RecalcTree();
        AddChild(new Toon { Name = "Toon" });   // first, so every mesh built after it gets the toon look
        ulong t0 = Time.GetTicksMsec(); var world = new World { Name = "World" }; AddChild(world);
        GD.Print($"[MAIN] world built in {(Time.GetTicksMsec() - t0) / 1000f:0.0} s");   // a joining game is silent on the network this long
        float px = Game.D.PosX, pz = Game.D.PosZ; var pos = World.OnGround(px, pz, 0.6f);
        var player = new Player { Name = "Player", Position = pos };
        var rig = new CameraRig { Name = "CameraRig", Target = player, Position = pos + new Vector3(0, 1.6f, 0) };
        player.Rig = rig; AddChild(player); AddChild(rig); rig.ExcludeFromCollision(player);
        AddChild(new Hud { Name = "Hud" });
        AddChild(new Ambient { Name = "Ambient" });
        Ui.InGame = true; Input.MouseMode = Input.MouseModeEnum.Captured;
        Ui.Banner(World.GetPoi("village")?.Name ?? "Oakhaven", $"{Inputs.KeyOf(Inputs.Pack)} pack, {Inputs.KeyOf(Inputs.Tree)} skill tree, {Inputs.KeyOf(Inputs.SkillsUi)} skills, {Inputs.KeyOf(Inputs.Journal)} quests, {Inputs.KeyOf(Inputs.Lock)} lock on");
        if (Game.TreeRefunded) { Game.TreeRefunded = false; Ui.Note("The skill tree has grown five times larger. Your tree points were refunded: press K.", new Color(1f, .85f, .4f)); }
        Game.Save();
    }

    public override void _Process(double delta)
    {
        if (!_started) return;
        float dt = (float)delta; Game.PlayTime += dt; Game.D.Played += dt; Arenas.Tick(dt); Threat.Tick(dt);
        _autosave += dt; if (_autosave > 60f && !Arenas.Active) { _autosave = 0; Game.Save(); }
    }
}
