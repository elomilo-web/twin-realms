using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace TwinRealms;

/// <summary>
/// Screenshot tour. Run the game with:  godot --path . -- --shots   (or tools/run.ps1 shots, tools/shots.sh)
/// Writes PNGs to screenshots/: the hero by day, by night and in a dungeon, the village, the kit weapons, and every skill being cast
/// (two moments each: just after the cast and a little later, against two foes), then quits.
/// </summary>
public partial class Shots : Node
{
    public static bool Wanted() { foreach (var a in OS.GetCmdlineUserArgs()) if (a == "--shots") return true; return false; }

    string _dir = ""; int _n;
    static Player P => Player.Instance!;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _dir = ProjectSettings.GlobalizePath("res://") + "screenshots/"; DirAccess.MakeDirRecursiveAbsolute(_dir);
        _ = Run();
    }

    void Say(string s) => GD.Print("[SHOTS] " + s);
    async Task Wait(float s) { await ToSignal(GetTree().CreateTimer(s, true), "timeout"); }
    async Task Shot(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(_dir + $"{++_n:000}_{name}.png"); Say("screenshot " + name);
    }
    void Heal() { P.Hp = Game.D.MaxHp(); P.Mp = Game.D.MaxMp(); P.St = Game.D.MaxSt(); }
    bool _keepAlive;
    public override void _Process(double delta) { if (_keepAlive && Player.Instance is { Dead: false }) Heal(); }   // foes hit back while we film skills
    /// <summary>Foes, lingering skill zones and leftover animations from the last skill.</summary>
    void ClearField()
    {
        foreach (var n in GetTree().GetNodesInGroup("enemies")) if (n is Node nn) nn.QueueFree();
        foreach (var c in Game.Root!.GetChildren()) if (c is SkillZone || c is SpellFxSprite || c is Projectile) c.QueueFree();
        Targeting.Clear();
    }
    void Hud(bool on) { if (Game.Root?.GetNodeOrNull<CanvasLayer>("Hud") is { } h) h.Visible = on; }

    /// <summary>The follow camera: yaw (radians) and distance. The hero turns with the camera when casting.</summary>
    void Cam(float yaw, float dist) { P.Rig!.Rotation = new Vector3(0, yaw, 0); P.Rig.Distance = dist; }

    /// <summary>A free camera for wide views; null puts the follow camera back.</summary>
    Camera3D? _free;
    void Free(Vector3? from, Vector3 at = default)
    {
        if (from == null) { _free?.QueueFree(); _free = null; P.Rig!.Camera.Current = true; return; }
        _free ??= new Camera3D { Fov = 60f, Far = 2000f }; if (_free.GetParent() == null) Game.Root!.AddChild(_free);
        _free.GlobalPosition = from.Value; _free.LookAt(at, Vector3.Up); _free.Current = true;
    }

    async Task Run()
    {
        try { await Tour(); } catch (Exception e) { GD.PrintErr("[SHOTS] tour threw: " + e); }
        Say("done: " + _n + " screenshots in " + _dir);
        GetTree().Quit();
    }

    async Task Tour()
    {
        await Wait(3f);
        if (Player.Instance == null) { GD.PrintErr("[SHOTS] no player"); return; }
        Game.D.Level = 30; Game.RecalcTree(); Heal(); Hud(false);
        foreach (var n in GetTree().GetNodesInGroup("enemies")) if (n is Node nn) nn.QueueFree();   // a quiet village for the pictures
        if (OS.GetCmdlineUserArgs().Contains("--weapons")) { await WeaponRow(); return; }   // just the weapon audit and the hero holding each type
        if (OS.GetCmdlineUserArgs().Contains("--move")) { await MoveCheck(); return; }

        // ---- the hero: day, night (plaza, lamps lit), close up, facing the camera
        var plaza = World.OnGround(1.5f, 6f, .5f);
        World.TimeOfDay = .45f; P.Teleport(plaza, Mathf.Pi); Cam(0f, 3.4f); await Wait(1.5f);
        await Shot("hero_day");
        Cam(-.9f, 3.0f); await Wait(.8f); await Shot("hero_day_side");
        World.TimeOfDay = .02f; P.Teleport(plaza, Mathf.Pi); Cam(0f, 3.4f); await Wait(2f);
        await Shot("hero_night");
        Cam(-.9f, 3.0f); await Wait(.8f); await Shot("hero_night_side");

        // ---- the village: wide views from outside and from the plaza, by day and at dusk
        World.TimeOfDay = .42f; await Wait(.6f);
        Free(World.OnGround(0, 46, 20f), World.OnGround(0, 0, 0)); await Wait(1f); await Shot("village_south");
        Free(World.OnGround(-38, -30, 18f), World.OnGround(0, 0, 0)); await Wait(1f); await Shot("village_northwest");
        Free(World.OnGround(5, 3, 2.4f), World.OnGround(-7, 19, 3f)); await Wait(.6f); await Shot("village_plaza_north");
        Free(World.OnGround(4, -4, 2.4f), World.OnGround(-3, -21, 3f)); await Wait(.6f); await Shot("village_plaza_south");
        Free(World.OnGround(-5, -3, 2.4f), World.OnGround(-18, 7, 3f)); await Wait(.6f); await Shot("village_plaza_west");
        World.TimeOfDay = .76f; await Wait(.6f);
        Free(World.OnGround(0, 46, 20f), World.OnGround(0, 0, 0)); await Wait(1f); await Shot("village_dusk");
        World.TimeOfDay = .45f;
        // forest and rocks outside the village
        Free(World.OnGround(-60, 60, 14f), World.OnGround(-90, 20, 0)); await Wait(1.2f); await Shot("nature_forest");
        Free(World.OnGround(70, -40, 10f), World.OnGround(100, -80, 0)); await Wait(1.2f); await Shot("nature_hills");
        Free(null);
        if (OS.GetCmdlineUserArgs().Contains("--look")) return;   // just the hero, village and nature (to compare the look)

        // ---- kit weapons in a row (checks orientation: grip at the bottom, blade up after the -Z turn)
        await WeaponRow();

        // ---- every skill, cast at two foes, by day in an open field
        await Skills();

        // ---- the hero in a dungeon (last: entering one replaces the world around us)
        Dungeon.Enter("barrow", 1); await Wait(2f);
        foreach (var n in GetTree().GetNodesInGroup("enemies")) if (n is Enemy e && e.GlobalPosition.DistanceTo(P.GlobalPosition) < 14f) e.QueueFree();
        P.Teleport(P.GlobalPosition, Mathf.Pi); Cam(0f, 3.4f); await Wait(1.2f); Heal();
        await Shot("hero_dungeon");
        Cam(-.9f, 4.5f); await Wait(.8f); await Shot("hero_dungeon_side");
        Hud(true);
    }

    /// <summary>Walks and runs in all eight directions (with and without a locked foe) and logs the clip, facing and movement; one picture each.</summary>
    async Task MoveCheck()
    {
        var spot = World.OnGround(1.5f, 6f, .5f); World.TimeOfDay = .45f;
        Game.D.Eq["weapon"] = new Item { Name = "Sword", Slot = "weapon", Wt = "sword" }; Game.Notify();
        var dirs = new (string n, string[] keys)[] { ("fwd", new[] { Inputs.Forward }), ("back", new[] { Inputs.Back }), ("left", new[] { Inputs.Left }), ("right", new[] { Inputs.Right }),
            ("fwd_left", new[] { Inputs.Forward, Inputs.Left }), ("back_right", new[] { Inputs.Back, Inputs.Right }) };
        foreach (bool locked in new[] { false, true })
            foreach (bool sprint in new[] { false, true })
                foreach (var (n, keys) in dirs)
                {
                    Targeting.Clear(); foreach (var e in GetTree().GetNodesInGroup("enemies")) if (e is Node en) en.QueueFree();
                    P.Teleport(spot, 0); Cam(0, 5f); await Wait(.4f);
                    if (locked) { var foe = new Enemy { Type = "golem", Level = 1, AnyRealm = true, NoLeash = true, Position = World.OnGround(spot.X, spot.Z - 9, .3f) }; Game.Root!.AddChild(foe); await Wait(.3f); Targeting.Set(foe); }
                    foreach (var k in keys) Input.ActionPress(k); if (sprint) Input.ActionPress(Inputs.Sprint);
                    await Wait(.9f);
                    var vel = P.Velocity; float vy = Util.YawTo(vel.X, vel.Z), d = Mathf.RadToDeg(Mathf.Abs(Util.AngDiff(P.ModelYaw, vy)));
                    Say($"move {(locked ? "locked" : "free")} {(sprint ? "sprint" : "walk")} {n}: speed {new Vector2(vel.X, vel.Z).Length():0.0}, clip {P.AvClip}{(P.Backpedal ? " (reversed)" : "")}, facing vs moving {d:0} deg");
                    await Shot($"move_{(locked ? "locked" : "free")}_{(sprint ? "sprint" : "jog")}_{n}");
                    foreach (var k in keys) Input.ActionRelease(k); Input.ActionRelease(Inputs.Sprint); await Wait(.3f);
                }
        Targeting.Clear();
    }

    async Task WeaponRow()
    {
        // audit wall, one picture per weapon type: every base item name (as loot names them), each as Common, Rare Ember, Epic Frost +3, Legendary Umbral +6
        var at = World.OnGround(-70, -88, 40f);   // up in the open sky: a plain background
        World.TimeOfDay = .45f; P.Teleport(World.OnGround(1.5f, 6f, .5f), 0);   // the hero waits in the (already discovered) village
        var variants = new (string pre, int rar, string? elem, int up)[] { ("Rusty", 0, null, 0), ("Fine", 2, "fire", 0), ("Runed", 3, "frost", 3), ("Mythic", 4, "shadow", 6) };
        foreach (var t in Weapons.Types)
        {
            var row = new Node3D(); Game.Root!.AddChild(row); row.GlobalPosition = at;
            var names = Weapons.KitNames(t).Select(x => x.name).Reverse().ToList(); float gx = 0, step = t is "spear" or "staff" or "greatsword" ? .6f : .5f;
            foreach (var n in names)
            {
                for (int v = 0; v < variants.Length; v++)
                {
                    var (pre, rar, elem, up) = variants[v]; string word = GearFx.Get(elem)?.Word ?? "";
                    var it = new Item { Wt = t, Slot = "weapon", Rar = rar, Elem = elem, Up = up, Name = $"{pre} {(word != "" ? word + " " : "")}{n}" };
                    var holder = new Node3D { Position = new Vector3(gx + v * step, 0, 0), Rotation = new Vector3(Mathf.Pi / 2, 0, 0) };   // -Z back to +Y: blades up
                    row.AddChild(holder); Weapons.Build(holder, t, it); GearFx.Apply(holder, it, true, 0, .9f);
                }
                row.AddChild(new Label3D { Text = n + "\n" + Weapons.KitModelFor(t, new Item { Name = n }).Model, Position = new Vector3(gx + step * 1.5f, -.25f, 0), FontSize = 40, PixelSize = .004f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, OutlineSize = 10 });
                gx += step * variants.Length + .5f;
            }
            foreach (var c in row.GetChildren()) if (c is Node3D n3) n3.Position -= new Vector3((gx - .5f) / 2f, 0, 0);
            float w = gx; Free(at + new Vector3(0, .75f, Mathf.Max(2.6f, w * .5f)), at + new Vector3(0, .75f, 0)); await Wait(.8f); await Shot("weapons_audit_" + t);
            Free(null); row.QueueFree(); await Wait(.1f);
        }
        // the hero holding one kit weapon of each type (grip in the hand, blade out)
        var pos = World.OnGround(1.5f, 6f, .5f); P.Teleport(pos, Mathf.Pi * .75f);
        foreach (var t in Weapons.Types)
        {
            Game.D.Eq["weapon"] = new Item { Name = "Shots " + t, Slot = "weapon", Wt = t, Dmg = 5, Two = Weapons.Get(t).Hands > 1 }; Game.Notify();
            P.Teleport(pos, Mathf.Pi * .75f); Cam(-.35f, 2.8f); await Wait(.7f); await Shot("hero_holds_" + t);
        }
        Game.D.Eq["weapon"] = new Item { Name = "Shots Sword", Slot = "weapon", Wt = "sword", Dmg = 5 }; Game.Notify();
    }

    async Task Skills()
    {
        var spot = World.OnGround(-70, -95, .5f);
        var sword = new Item { Name = "Shots Sword", Slot = "weapon", Wt = "sword", Dmg = 5 };
        var bow = new Item { Name = "Shots Bow", Slot = "weapon", Wt = "bow", Dmg = 5, Two = true };
        World.TimeOfDay = .45f; _keepAlive = true;
        foreach (var k in TwinRealms.Skills.All)
        {
            bool needBow = k.Tags.Contains("bow");
            Game.D.Eq["weapon"] = needBow ? bow : sword; Game.Notify();
            if (!Game.D.Skills.ContainsKey(k.Id)) Game.D.Skills[k.Id] = new SkillState();
            Game.D.Bar[0] = k.Id;
            // fresh foes each time: the hero faces -Z (camera yaw 0), foes 7 m ahead
            ClearField(); await Wait(.1f);
            if (P.Dead) { P.Teleport(spot, 0f); await Wait(.5f); }
            P.Teleport(spot, 0f); Cam(-.85f, 7f); await Wait(.4f);
            var foes = new List<Enemy>();
            foreach (var off in new[] { new Vector3(-1.4f, 0, -7f), new Vector3(1.6f, 0, -8.5f) })
            {
                var e = new Enemy { Type = "golem", Level = 22, AnyRealm = true, NoLeash = true, Position = World.OnGround(spot.X + off.X, spot.Z + off.Z, .3f) };
                Game.Root!.AddChild(e); foes.Add(e);
            }
            await Wait(.5f);
            if (foes[0].IsInsideTree()) Targeting.Set(foes[0]);
            // a free camera off to the right, so beams and projectiles are seen along their length (the lock-on camera would look straight down them)
            Free(spot + new Vector3(7.5f, 3.2f, -1.5f), spot + new Vector3(0, 1f, -4f));
            // four moments after the key: cast, release, impact, aftermath
            Input.ActionPress("spell1"); await Wait(.05f); Input.ActionRelease("spell1");
            float t = .05f; int i = 0;
            foreach (float at in new[] { .2f, .42f, .68f, 1.05f }) { await Wait(at - t); t = at; await Shot($"skill_{k.Id}_{"abcd"[i++]}"); }
            await Wait(.4f);
        }
        _keepAlive = false; ClearField(); Free(null);
    }
}
