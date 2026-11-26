using Godot;

namespace TwinRealms;

/// <summary>Game root: shows the title screen, then builds the world, player, camera and HUD.</summary>
public partial class Main : Node3D
{
    float _autosave; bool _started, _live; static int QuitTestRuns;

    public override void _Ready()
    {
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--list-anims") >= 0) { Avatar.ListSources(); GetTree().Quit(); return; }
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--scaling-table") >= 0) { GD.Print(Progression.Table()); GetTree().Quit(); return; }   // tools: monster health/damage vs the reference hero (data/progression.json)
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--bake-anims") >= 0) { Avatar.Bake(); GetTree().Quit(); return; }   // tools: rebuild assets/models/anim/clips.res
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--gen-icons") >= 0) { IconArt.GenerateAll(); GetTree().Quit(); return; }   // tools: paint res://assets/icons/skills/*.png (IconArt.cs)
        if (AnimTool.Wanted()) { Game.Root = this; AddChild(new AnimTool { Name = "AnimTool" }); return; }   // tools: measure attack clips (impact timing)
        GD.Print("[MAIN] scene start"); GetTree().Root.PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;   // only hero and monster bodies interpolate (CameraRig, Enemy)
        Inputs.Setup(); Game.Root = this; Dungeon.Active = false; Arenas.Active = false; Threat.Reset(); World.Realm = 0; World.RealmT = 0; Ui.InGame = false;
        // "Quit to title" reloads this scene: forget the nodes of the old world, which are freed by now (applying the settings below touched the old sun and crashed)
        World.I = null; FloraField.I = null; Player.Instance = null; Enemy.CurrentBoss = null; Player.BuildMode = false;
        BlockWorld.InitGlobals();   // the terrain, water and grass shaders read the block world's cut-out square
        if (ServerWanted()) { RunServer(); return; }
        bool testing = Observer.Wanted() || Bot.Wanted() || Shots.Wanted() || Showcase.Wanted() || FitBot.Wanted() || MpBot.Wanted() != null || OnlineBot.Wanted() || DevCapture.Wanted();
        if (testing) Game.SetPath = "user://test_settings.json";
        Game.LoadSettings();
        if (testing) Inputs.ResetAll(false);   // test bots press the default keys
        if (testing)
        {
            // test bots: no sound; the gameplay bots (bot, mp) also run on the lowest graphics so they render fast.
            // The picture bots (observe, shots, fit) keep full graphics, since their screenshots are the point. Nothing is saved.
            var s = Game.Set; s.Master = 0;
            if (Bot.Wanted() || MpBot.Wanted() != null) { s.AutoQuality = false; s.Quality = 0; s.RenderScale = .5f; s.ViewDist = 300f; s.Grass = 0f; s.VSync = false; s.Outlines = false; }
            Sfx.ApplyVolumes(); Game.ApplyDisplay();
        }
        AddChild(new Sfx { Name = "Sfx" });
        AddChild(new Flag { Name = "Flag" });   // F8: playtest flag (screenshot + report in user://playtest/)
        AddChild(new CommandKey { Name = "CommandKey" });   // F9: "/stats dump" (Commands.cs) until the command bar exists
        AddChild(new Net { Name = "Net" });
        var ui = new Ui { Name = "Ui" }; AddChild(ui);
        // loading screens (LoadScreen.cs): not for the test bots, whose timings and pictures expect the world at once (the dev capture loop keeps them)
        Player.Frozen = false; LoadScreen.On = !testing || DevCapture.Wanted();
        AddChild(new LoadScreen { Name = "LoadScreen" });
        // logged in to the online server: the hero goes into the shared world instead of a private one
        Ui.OnNew = (cls, name, race) => { Game.NewGame(cls, name, race); Game.D.Body = Ui.PickedBody; Game.D.PosX = 0; Game.D.PosZ = 9; if (Game.Online) Net.I!.EnterOnline(JoinWorld); else BeginStaged(); };
        Ui.OnContinue = () => { if (!Game.Load()) Game.NewGame("warrior", "Hero"); if (Game.Online) Net.I!.EnterOnline(JoinWorld); else BeginStaged(); };   // plays the hero in Game.Slot (chosen on the character select screen)
        // multiplayer from the title: bring the saved hero (or the one picked on the title screen)
        void Hero() { if (!Game.HasSave() || !Game.Load()) { Game.NewGame(Ui.PickedClass, Ui.PickedName, Ui.PickedRace); Game.D.Body = Ui.PickedBody; Game.D.PosX = 0; Game.D.PosZ = 9; } }
        Ui.OnHost = port => { Hero(); BeginStaged(() => Net.I!.Host(port)); };
        Ui.OnJoin = (addr, port) => { Hero(); Net.I!.Join(addr, port, JoinWorld); };
        if (Observer.Wanted() || Bot.Wanted() || Shots.Wanted() || Showcase.Wanted() || DevCapture.Wanted()) Game.SavePath = "user://test_save.json";
        if (DevCapture.Wanted())
        {
            // dev loop eyes (scripts/dev/DevCapture.cs): default graphics, title screen first, then a fresh hero on its cue
            Game.Set = new Settings { Master = 0 }; Sfx.ApplyVolumes(); Game.ApplyDisplay();
            DevCapture.StartGame = () => { Ui.I?.Close(); Game.NewGame("warrior", "Capture"); Game.D.PosX = 0; Game.D.PosZ = 9; BeginStaged(); };
        }
        if (Showcase.Wanted()) { Game.NewGame("warrior", "Showcase"); Begin(); AddChild(new Showcase { Name = "Showcase" }); return; }   // a few pretty pictures
        if (FitBot.Wanted()) { AddChild(new FitBot { Name = "FitBot" }); return; }   // gear fit studio: no world, no title
        if (Shots.Wanted()) { Game.NewGame("warrior", "Shots"); Begin(); AddChild(new Shots { Name = "Shots" }); return; }
        if (MpBot.Wanted() is string m) { Game.SavePath = $"user://mp_{m}_save.json"; Net.ChestPath = "user://mp_test_party_chest.json"; }
        if (Observer.Wanted()) { Game.NewGame("warrior", "Observer"); Begin(); AddChild(new Observer { Name = "Observer" }); return; }
        if (Perf.Wanted()) { Game.SavePath = "user://test_save.json"; Game.SetPath = "user://perf_settings.json"; Game.Set = new Settings(); Game.ApplyDisplay(); Game.NewGame("warrior", "Perf"); Begin(); AddChild(new Perf { Name = "Perf" }); return; }   // performance probe, fresh default settings
        if (Bot.Wanted()) { Game.NewGame("warrior", "Bot"); Begin(); AddChild(new Bot { Name = "Bot" }); return; }
        if (OnlineBot.Wanted()) { Game.SavePath = "user://online_test_local_save.json"; AddChild(new OnlineBot { Name = "OnlineBot" }); return; }
        if (MpBot.Wanted() is string mode) { AddChild(new MpBot { Name = "MpBot", Mode = mode }); if (mode == "host") { Game.NewGame("paladin", "HostBot", "dwarf"); Begin(); } return; }
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--quit-test") >= 0)
        {
            Game.SavePath = "user://test_save.json"; QuitTestRuns++;
            if (QuitTestRuns == 1) { Game.NewGame("warrior", "QuitTest"); Begin(); GetTree().CreateTimer(8).Timeout += () => { GD.Print("[QT] quitting"); Ui.I!.OpenPause(); Ui.I.QuitToTitle(); }; return; }
            else GetTree().CreateTimer(4).Timeout += () =>
            {
                GD.Print($"[QT] title up={Ui.I?.Visible} started={_started}"); GetViewport().GetTexture().GetImage().SavePng("res://bot_out/quit_test.png");
                Ui.OnContinue?.Invoke();   // play the hero again: the second world must build as well as the first
                GetTree().CreateTimer(5).Timeout += () => { GD.Print($"[QT] replay started={_started} player={Player.Instance != null && IsInstanceValid(Player.Instance)} hp={Player.Instance?.Hp}"); GetTree().Quit(); };
            };
        }
        if (DevCapture.Wanted()) ui.ShowTitle(); else if (Updater.Wanted()) ui.ShowLauncher(); else ui.ShowTitle();  // the launcher: update check, patch notes, online login, then Play (Updater.cs)
        // a calm backdrop behind the title screen
        AddChild(new TitleBackdrop { Name = "TitleEnv" });   // dusk skyline, embers, drifting camera (TitleBackdrop.cs); Begin frees it by name
    }

    /// <summary>The host accepted us: build the world next to the host's hero (online: where the hero was saved).</summary>
    public void JoinWorld()
    {
        var at = Net.I!.WelcomePos; Game.D.Time = Net.I.WelcomeTime;
        if (!Game.Online) { Game.D.PosX = at.X + 2.5f; Game.D.PosZ = at.Z + 2.5f; }
        Ui.I?.Close(); Begin();   // in one go: we are inside the host's welcome message, and its next messages expect the world
        if (Game.Online) { Net.OnlineState = "in"; int n = Net.I.Players.Count; LoadScreen.After(() => Ui.Banner("Twin Realms Online", $"{n} heroes in the world")); }
        else { string host = Net.I.NameOf(1); LoadScreen.After(() => Ui.Banner("Joined", $"{host}'s world")); }
    }

    // ---- online server: godot --headless --path . -- --server   (port: PORT environment variable, else data/online.json)
    static bool ServerWanted() => System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--server") >= 0;
    void RunServer()
    {
        GD.Print($"[SERVER] building the world ... ({Mem()})");
        OnlineCfg.Load(); Engine.MaxFps = 60;
        Game.SavePath = "user://server_world_save.json"; Game.SetPath = "user://server_settings.json";        Net.ChestPath = System.IO.Path.Combine(Accounts.Dir, "party_chest.json");
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
        GD.Print($"[SERVER] world ready ({Mem()})");
        if (!Net.I!.Serve(port)) GetTree().Quit(1);
    }
    /// <summary>Server log: memory in use (the host's memory limit decides whether the server survives building the world).</summary>
    static string Mem()
    {
        using var me = System.Diagnostics.Process.GetCurrentProcess();
        return $"memory {me.WorkingSet64 >> 20} MB, engine {OS.GetStaticMemoryUsage() >> 20} MB, .NET {System.GC.GetTotalMemory(false) >> 20} MB, textures {(long)Performance.GetMonitor(Performance.Monitor.RenderTextureMemUsed) >> 20} MB, objects {(long)Performance.GetMonitor(Performance.Monitor.ObjectCount)}";
    }

    /// <summary>The world built in one go (server, bots, joining a host from inside the welcome message): the loading screen,
    /// when there is one, is drawn by hand between the build steps since no frame runs until this returns.</summary>
    void Begin(bool server = false)
    {
        if (_started) return; _started = true;
        bool cover = !server && LoadScreen.On;
        if (cover) { LoadScreen.Show(PlaceName(), StartSub(), null, true); LoadScreen.Draw(); }
        ulong t0 = Time.GetTicksMsec(); var world = MakeWorld(server);
        foreach (var (at, stage) in world.Stages()) if (cover) { LoadScreen.SetProgress(at, stage); LoadScreen.Draw(); }
        GD.Print($"[MAIN] world built in {(Time.GetTicksMsec() - t0) / 1000f:0.0} s");   // a joining game is silent on the network this long
        FinishBegin(server);
    }
    /// <summary>The world built a step a frame behind the loading screen (playing from the title, hosting): the window stays alive and the progress moves.</summary>
    async void BeginStaged(System.Action? built = null)
    {
        if (_started) return;
        if (!LoadScreen.On) { Begin(); built?.Invoke(); return; }
        _started = true;
        LoadScreen.Show(PlaceName(), StartSub());
        ulong ts = Time.GetTicksMsec();   // the fade in (.25 s, at least two frames) so the screen is really up before the first hitch
        for (int f = 0; f < 2 || Time.GetTicksMsec() - ts < 260; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        ulong t0 = Time.GetTicksMsec(); var world = MakeWorld(false);
        foreach (var (at, stage) in world.Stages()) { LoadScreen.SetProgress(at, stage); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        GD.Print($"[MAIN] world built in {(Time.GetTicksMsec() - t0) / 1000f:0.0} s (staged)");
        FinishBegin(false); built?.Invoke();
    }
    static string PlaceName()
    {   // the place the hero wakes in (the points of interest are known from the second world on; the first is the village)
        if (World.Pois.Count == 0) return new Vector2(Game.D.PosX, Game.D.PosZ).Length() < 60f ? "Oakhaven" : "Twin Realms";
        Poi? best = null; float bd = 1e9f;
        foreach (var p in World.Pois) { float d = new Vector2(p.X - Game.D.PosX, p.Z - Game.D.PosZ).Length() - p.R; if (d < bd) { bd = d; best = p; } }
        return best != null && bd < 150f ? best.Name : "Twin Realms";
    }
    static string StartSub() => Net.Active && !Net.IsHost ? (Game.Online ? "entering the shared world" : $"joining {Net.I!.NameOf(1)}'s world") : $"{Game.D.Name}, the realm awakens";
    World MakeWorld(bool server)
    {
        GetNodeOrNull("TitleCam")?.QueueFree(); GetNodeOrNull("TitleEnv")?.QueueFree();
        Game.RecalcTree();
        if (!server) AddChild(new Toon { Name = "Toon" });   // first, so every mesh built after it gets the toon look
        var world = new World { Name = "World" }; World.Staged = true;
        try { AddChild(world); } finally { World.Staged = false; }
        return world;
    }
    void FinishBegin(bool server)
    {
        _live = true;
        float px = Game.D.PosX, pz = Game.D.PosZ; var pos = World.OnGround(px, pz, 0.6f);
        var player = new Player { Name = "Player", Position = pos };
        var rig = new CameraRig { Name = "CameraRig", Target = player, Position = pos + new Vector3(0, 1.6f, 0) };
        player.Rig = rig; AddChild(player); AddChild(rig); rig.ExcludeFromCollision(player);
        if (server) return;
        AddChild(new BlockWorld());   // the ground around the player as blocks (BlockWorld.cs)
        AddChild(new Hud { Name = "Hud" });
        AddChild(new Ambient { Name = "Ambient" }); AddChild(new Weather { Name = "Weather" });
        AddChild(new FishField { Name = "FishField" }); AddChild(new Fishing { Name = "Fishing" });   // fish in the water and the fishing rod (F-22, F-23)
        Ui.InGame = true; Input.MouseMode = Input.MouseModeEnum.Captured;
        if (LoadScreen.Busy) LoadScreen.SetProgress(.97f, "Lighting the lamps");
        Warmup.Run(this);   // fire each lazily-built effect once so the first real use does not hitch (Warmup.cs)
        if (LoadScreen.I != null) MoveChild(LoadScreen.I, -1);   // last child: its _Input sees (and swallows) keys before the hero and the HUD
        string keys = $"{Inputs.KeyOf(Inputs.Pack)} pack, {Inputs.KeyOf(Inputs.Tree)} skill tree, {Inputs.KeyOf(Inputs.SkillsUi)} skills, {Inputs.KeyOf(Inputs.Journal)} quests, {Inputs.KeyOf(Inputs.Lock)} lock on";
        bool refunded = Game.TreeRefunded; Game.TreeRefunded = false;
        // the loading screen stays until the blocks around the hero exist (or 20 s), then the welcome banner
        LoadScreen.Lift(() =>
        {
            Ui.Banner(World.GetPoi("village")?.Name ?? "Oakhaven", keys);
            if (refunded) Ui.Note("The skill tree has grown five times larger. Your tree points were refunded: press K.", new Color(1f, .85f, .4f));
        }, 0f, 20f);
        Game.Save();
    }

    /// <summary>Out of focus or minimised: drop to a few frames a second so the game does not keep the CPU and graphics card busy in the background (test bots excepted).</summary>
    public override void _Notification(int what)
    {
        if (Bot.Wanted() || MpBot.Wanted() != null || Perf.Wanted()) return;
        if (what == NotificationApplicationFocusOut) Game.SetBackground(true);
        else if (what == NotificationApplicationFocusIn) Game.SetBackground(false);
    }
    public override void _Process(double delta)
    {
        if (!_live || Net.Dedicated) return;
        float dt = (float)delta; Game.PlayTime += dt; Game.D.Played += dt; Arenas.Tick(dt); Threat.Tick(dt);
        if (Dungeon.Active) Dungeon.LairTick();   // raid lair: the boss room intro (DungeonLair.cs)
        _autosave += dt; if (_autosave > (Game.Online ? OnlineCfg.SaveEvery : 60f) && !Arenas.Active) { _autosave = 0; Game.Save(); }
    }
}
