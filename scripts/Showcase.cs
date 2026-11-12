using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace TwinRealms;

/// <summary>
/// Showcase bot: a handful of good-looking pictures of the game (for a store page, a post, a friend), not a test.
/// Run with:  godot --path . -- --showcase   (or tools/run.ps1 showcase). Top graphics, 1920x1080, writes screenshots/showcase/, then quits.
/// </summary>
public partial class Showcase : Node
{
    public static bool Wanted() => OS.GetCmdlineUserArgs().Contains("--showcase");

    string _dir = ""; int _n;
    static Player P => Player.Instance!;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _dir = ProjectSettings.GlobalizePath("res://") + "screenshots/showcase/"; DirAccess.MakeDirRecursiveAbsolute(_dir);
        _ = Run();
    }

    void Say(string s) => GD.Print("[SHOWCASE] " + s);
    async Task Wait(float s) { await ToSignal(GetTree().CreateTimer(s, true), "timeout"); }
    async Task Shot(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(_dir + $"{++_n:00}_{name}.png"); Say("picture " + name);
    }
    void Heal() { P.Hp = Game.D.MaxHp(); P.Mp = Game.D.MaxMp(); P.St = Game.D.MaxSt(); }
    bool _keepAlive;
    public override void _Process(double delta) { if (_keepAlive && Player.Instance is { Dead: false }) Heal(); }
    void Hud(bool on) { if (Game.Root?.GetNodeOrNull<CanvasLayer>("Hud") is { } h) h.Visible = on; }
    void ClearFoes() { foreach (var n in GetTree().GetNodesInGroup("enemies")) if (n is Node nn) nn.QueueFree(); Targeting.Clear(); }

    /// <summary>The follow camera: yaw (radians) and distance.</summary>
    void Cam(float yaw, float dist) { P.Rig!.Rotation = new Vector3(0, yaw, 0); P.Rig.Distance = dist; }

    /// <summary>A free camera for wide views; null puts the follow camera back.</summary>
    Camera3D? _free;
    void Free(Vector3? from, Vector3 at = default)
    {
        if (from == null) { _free?.QueueFree(); _free = null; P.Rig!.Camera.Current = true; return; }
        _free ??= new Camera3D { Fov = 55f, Far = 4500f }; if (_free.GetParent() == null) Game.Root!.AddChild(_free);
        _free.GlobalPosition = from.Value; _free.LookAt(at, Vector3.Up); _free.Current = true;
    }

    async Task Run()
    {
        try { await Tour(); } catch (Exception e) { GD.PrintErr("[SHOWCASE] tour threw: " + e); }
        Say("done: " + _n + " pictures in " + _dir);
        GetTree().Quit();
    }

    async Task Tour()
    {
        // best looks: Ultra, far view, full grass, a 1080p window
        var s = Game.Set; s.Quality = 3; s.RenderScale = 1f; s.ViewDist = 3000f; s.Grass = 1f; Game.ApplyDisplay();
        DisplayServer.WindowSetSize(new Vector2I(1920, 1080));
        await Wait(3f);
        if (Player.Instance == null) { GD.PrintErr("[SHOWCASE] no player"); return; }
        Game.D.Level = 30; Game.RecalcTree(); Heal(); Hud(false); ClearFoes();
        if (Ui.I != null) Ui.I.Visible = false;   // no "entering <land>" banners or notes over the pictures
        Game.D.Eq["weapon"] = new Item { Name = "Mythic Ember Longsword", Slot = "weapon", Wt = "sword", Dmg = 5, Rar = 4, Elem = "fire", Up = 6 }; Game.Notify();

        // 1. the village from the hills at golden hour
        World.TimeOfDay = .70f; await Wait(.8f);
        Free(World.OnGround(0, 50, 26f), World.OnGround(0, 0, 2f)); await Wait(1.5f); await Shot("village_golden_hour");

        // 2. the hero in the plaza by day, three-quarter view (the light eases to the new hour, so wait a bit)
        var plaza = World.OnGround(1.5f, 6f, .5f);
        World.TimeOfDay = .5f; Free(null); P.Teleport(plaza, Mathf.Pi * .8f); Cam(-.6f, 3.2f); await Wait(3f); await Shot("hero_plaza");

        // 3. out in the fields in the evening (the play camera)
        World.TimeOfDay = .68f; P.Teleport(World.OnGround(-46, 34, .5f), 2.2f); Cam(2.2f + Mathf.Pi, 4.5f); await Wait(2f); await Shot("hero_fields_evening");

        // 4. a fight: meteor on two golems, HUD on, as it looks while playing
        await Fight();

        // 5. the whole world from high above
        World.TimeOfDay = .40f; await Wait(.5f);
        Free(new Vector3(0, 650, 1150), new Vector3(0, 0, -150)); await Wait(2.5f); await Shot("world_aerial");
        Free(null);

        // 6-7. the hero in two other lands
        foreach (var (id, pid) in new[] { ("frost", "hollowfrost"), ("ember", "ashford") })
        {
            var L = Lands.All.Find(l => l.Id == id); var p = L?.Places.Find(q => q.Id == pid); if (p == null) { Say("missing place " + pid); continue; }
            P.Teleport(World.OnGround(p.X + 4, p.Z + p.R * .3f, .6f), Mathf.Pi * .8f); Cam(Mathf.Pi * .8f + Mathf.Pi, 5f); await Wait(2.5f); await Shot("land_" + id);
        }

        // 8. a sky island
        if (Lands.SkyGet("lumen") is { } lumen)
        {
            float top = Lands.SkyTop(lumen, lumen.X, lumen.Z);
            P.Teleport(new Vector3(lumen.X + 6, top + 1, lumen.Z + 6), .5f); Cam(.5f + Mathf.Pi, 6f); await Wait(2.5f); await Shot("sky_island");
        }

        // 9. the village at night, lamps lit
        World.TimeOfDay = .02f; P.Teleport(plaza, Mathf.Pi);
        Free(World.OnGround(4, 30, 9f), World.OnGround(0, 0, 2f)); await Wait(2.5f); await Shot("village_night");
        Free(null);

        // 10. a dungeon (last: entering one replaces the world around us)
        Dungeon.Enter("barrow", 1); await Wait(2.5f);
        foreach (var n in GetTree().GetNodesInGroup("enemies")) if (n is Enemy e && e.GlobalPosition.DistanceTo(P.GlobalPosition) < 14f) e.QueueFree();
        P.Teleport(P.GlobalPosition, Mathf.Pi); Cam(-.7f, 4f); await Wait(1.5f); Heal(); await Shot("dungeon");
    }

    async Task Fight()
    {
        var spot = World.OnGround(-70, -95, .5f); World.TimeOfDay = .45f; _keepAlive = true;
        if (!Game.D.Skills.ContainsKey("meteor")) Game.D.Skills["meteor"] = new SkillState { Rank = 3 }; Game.D.Bar[0] = "meteor"; Game.Notify();
        P.Teleport(spot, 0f); Cam(-.9f, 7.5f); await Wait(.5f);
        Enemy? first = null;
        foreach (var off in new[] { new Vector3(-1.2f, 0, -7f), new Vector3(1.8f, 0, -8.5f) })
        {
            var e = new Enemy { Type = "golem", Level = 22, AnyRealm = true, NoLeash = true, Position = World.OnGround(spot.X + off.X, spot.Z + off.Z, .3f) };
            Game.Root!.AddChild(e); first ??= e;
        }
        await Wait(.6f); if (first is { } f && f.IsInsideTree()) Targeting.Set(f);
        Hud(true);
        Input.ActionPress("spell1"); await Wait(.05f); Input.ActionRelease("spell1");
        await Wait(1.05f); await Shot("combat_meteor");   // the meteor lands about a second after the cast
        await Wait(1f); Hud(false); _keepAlive = false; ClearFoes();
    }
}
