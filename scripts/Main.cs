using Godot;

namespace TwinRealms;

/// <summary>Game root: shows the title screen, then builds the world, player, camera and HUD.</summary>
public partial class Main : Node3D
{
    float _autosave; bool _started;

    public override void _Ready()
    {
        Inputs.Setup(); Game.Root = this; Dungeon.Active = false; Arenas.Active = false; World.Realm = 0; World.RealmT = 0; Ui.InGame = false;
        Game.LoadSettings();
        AddChild(new Sfx { Name = "Sfx" });
        var ui = new Ui { Name = "Ui" }; AddChild(ui);
        Ui.OnNew = (cls, name) => { Game.NewGame(cls, name); Game.D.PosX = 0; Game.D.PosZ = 9; Begin(); };
        Ui.OnContinue = () => { if (!Game.Load()) Game.NewGame("warrior", "Hero"); Begin(); };
        if (Observer.Wanted()) { Game.NewGame("warrior", "Observer"); Begin(); AddChild(new Observer { Name = "Observer" }); return; }
        if (Bot.Wanted()) { Game.NewGame("warrior", "Bot"); Begin(); AddChild(new Bot { Name = "Bot" }); return; }
        ui.ShowTitle();
        // a calm backdrop behind the title screen
        var cam = new Camera3D { Name = "TitleCam", Position = new Vector3(0, 30, 60), RotationDegrees = new Vector3(-12, 0, 0), Current = true }; AddChild(cam);
        AddChild(new WorldEnvironment { Name = "TitleEnv", Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(.04f, .03f, .08f) } });
    }

    void Begin()
    {
        if (_started) return; _started = true;
        GetNodeOrNull("TitleCam")?.QueueFree(); GetNodeOrNull("TitleEnv")?.QueueFree();
        Game.RecalcTree();
        var world = new World { Name = "World" }; AddChild(world);
        float px = Game.D.PosX, pz = Game.D.PosZ; var pos = World.OnGround(px, pz, 0.6f);
        var player = new Player { Name = "Player", Position = pos };
        var rig = new CameraRig { Name = "CameraRig", Target = player, Position = pos + new Vector3(0, 1.6f, 0) };
        player.Rig = rig; AddChild(player); AddChild(rig); rig.ExcludeFromCollision(player);
        AddChild(new Hud { Name = "Hud" });
        AddChild(new Ambient { Name = "Ambient" });
        Ui.InGame = true; Input.MouseMode = Input.MouseModeEnum.Captured;
        Ui.Banner(World.GetPoi("village")?.Name ?? "Oakhaven", "Press I for your pack, K for the skill tree, J for quests");
        Game.Save();
    }

    public override void _Process(double delta)
    {
        if (!_started) return;
        float dt = (float)delta; Game.PlayTime += dt; Arenas.Tick(dt);
        _autosave += dt; if (_autosave > 60f && !Arenas.Active) { _autosave = 0; Game.Save(); }
    }
}
