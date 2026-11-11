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
        _free ??= new Camera3D { Fov = 60f, Far = 4500f }; if (_free.GetParent() == null) Game.Root!.AddChild(_free);
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
        if (OS.GetCmdlineUserArgs().Contains("--fxdrift")) { await FxDrift(); return; }
        if (OS.GetCmdlineUserArgs().Contains("--feel")) { await FeelCheck(); return; }
        if (OS.GetCmdlineUserArgs().Contains("--combat")) { await CombatCheck(); return; }
        if (OS.GetCmdlineUserArgs().Contains("--lands")) { await LandsTour(); return; }
        if (OS.GetCmdlineUserArgs().Contains("--hands"))
        {
            // which hand each clip reaches out with (model space: -Z is forward, +X is the model's left after the 180 turn)
            var av = new Avatar(); Game.Root!.AddChild(av); av.GlobalPosition = World.OnGround(-60, -60, 0); av.Build(new Appearance(), "hands"); await Wait(.3f);
            foreach (var clip in new[] { "Spell_Simple_Shoot", "Spell_Simple_Idle", "Pistol_Aim_Neutral", "Pistol_Shoot", "OverhandThrow", "Idle" })
                foreach (float fr in new[] { .3f, .6f })
                {
                    av.PoseAt(clip, fr); await Wait(.05f); var sk = av.Skel!;
                    Vector3 H(string b) => av.ToLocal(sk.GlobalTransform * sk.GetBoneGlobalPose(sk.FindBone(b)).Origin);
                    var l = H("hand_l"); var r = H("hand_r");
                    Say($"hands {clip}@{fr}: left {l.X:0.00},{l.Y:0.00},{l.Z:0.00}  right {r.X:0.00},{r.Y:0.00},{r.Z:0.00}  (more negative Z = further forward)");
                }
            return;
        }

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
        Free(World.OnGround(-48, 40, 2.2f), World.OnGround(-80, 20, 4f)); await Wait(1.2f); await Shot("nature_ground_eye");
        Free(null);
        // the hero out in the fields (play camera): ground, grass and trees as the player sees them
        P.Teleport(World.OnGround(-46, 34, .5f), 2.2f); Cam(2.2f + Mathf.Pi, 4.2f); await Wait(1.5f); await Shot("field_hero");
        P.Teleport(World.OnGround(30, -40, .5f), -.6f); Cam(-.6f + Mathf.Pi, 4.2f); await Wait(1.5f); await Shot("field_hero_2");
        World.TimeOfDay = .69f; P.Teleport(World.OnGround(-46, 34, .5f), 2.2f); Cam(2.2f + Mathf.Pi, 4.2f); await Wait(1.5f); await Shot("field_hero_evening");
        World.TimeOfDay = .45f; P.Teleport(plaza, Mathf.Pi);
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

    /// <summary>The wider world (data/lands.json): wide views of every land, its places, the sea bridges and the sky islands; the hero in a few of them; an updraft ride; the world map.</summary>
    async Task LandsTour()
    {
        World.TimeOfDay = .42f; await Wait(.5f);
        Free(new Vector3(0, 650, 1150), new Vector3(0, 0, -150)); await Wait(2f); await Shot("world_from_south");
        Free(new Vector3(-1300, 520, -1300), new Vector3(100, 0, 100)); await Wait(2f); await Shot("world_from_northwest");
        Free(World.OnGround(18, -215, 18f), new Vector3(0, 40, -900)); await Wait(1.5f); await Shot("home_coast_to_frost");
        Free(World.OnGround(80, 30, 60f), new Vector3(70, 150, -70)); await Wait(1.2f); await Shot("home_sky_lumen");
        foreach (var L in Lands.All)
        {
            Vector2 dk = default; foreach (var d in L.Docks.Values) { dk = d; break; }
            var c = new Vector3(L.Cx, 0, L.Cz); var dir = (new Vector3(dk.X, 0, dk.Y) - c).Normalized();
            Free(new Vector3(dk.X, 0, dk.Y) + dir * 120f + Vector3.Up * 140f, c + Vector3.Up * 20f); await Wait(1.5f); await Shot("land_" + L.Id);
            foreach (var p in L.Places)
            {
                var pc = new Vector3(p.X, p.H, p.Z); var off = new Vector3(Mathf.Cos(p.X * .01f + 1), 0, Mathf.Sin(p.Z * .01f + 1)).Normalized();
                float back = p.Kind == "cave" ? 26f : p.R * 1.7f + 20f;
                var from = pc + off * back; from.Y = Mathf.Max(World.GroundAt(from.X, from.Z), p.H) + (p.Kind == "cave" ? 8f : p.R * .9f + 10f);
                Free(from, pc + Vector3.Up * 4f); await Wait(1.2f); await Shot("place_" + L.Id + "_" + p.Id);
            }
        }
        int bi = 0;
        foreach (var b in Lands.Bridges)
        {
            var mid = (b.A + b.B) / 2; var d = (b.B - b.A).Normalized(); var side = new Vector2(-d.Y, d.X);
            Free(new Vector3(mid.X + side.X * 70, 30, mid.Y + side.Y * 70), new Vector3(mid.X, 6, mid.Y)); await Wait(1.2f); await Shot("bridge_" + (bi++));
        }
        foreach (var s in Lands.Sky)
        {
            float top = Lands.SkyTop(s, s.X, s.Z); if (float.IsNaN(top)) top = s.Y;
            Free(new Vector3(s.X + s.R * 2.2f, top - 30, s.Z + s.R * 2.2f), new Vector3(s.X, top - 15, s.Z)); await Wait(1.2f); await Shot("sky_" + s.Id);
        }
        Free(null);
        // the hero out there (play camera)
        foreach (var (id, pid) in new[] { ("frost", "hollowfrost"), ("sun", "kesh"), ("mire", "bogwater"), ("ember", "ashford"), ("elder", "aurelion") })
        {
            var L = Lands.All.Find(l => l.Id == id); var p = L?.Places.Find(q => q.Id == pid); if (p == null) continue;
            P.Teleport(World.OnGround(p.X + 4, p.Z + p.R * .3f, .6f), Mathf.Pi * .8f); Cam(Mathf.Pi * .8f + Mathf.Pi, 5f); await Wait(2.2f); await Shot("hero_" + id + "_" + pid);
        }
        var lumen = Lands.SkyGet("lumen");
        if (lumen != null)
        {
            float top = Lands.SkyTop(lumen, lumen.X, lumen.Z);
            P.Teleport(new Vector3(lumen.X + 6, top + 1, lumen.Z + 6), .5f); Cam(.5f + Mathf.Pi, 5.5f); await Wait(2f); await Shot("hero_sky_lumen");
            // ride the updraft from the ground
            Updraft? up = null; foreach (var n in GetTree().Root.FindChildren("Updraft_lumen", "", true, false)) up = n as Updraft;
            if (up != null)
            {
                P.Teleport(up.GlobalPosition + Vector3.Up * .6f, 0f); Cam(Mathf.Pi, 7f); await Wait(1.2f); await Shot("updraft_ride_start");
                float t = 0, maxY = -1e9f; while (t < 9f) { await Wait(.25f); t += .25f; maxY = Mathf.Max(maxY, P.GlobalPosition.Y); }
                Say($"updraft lumen: base {up.GlobalPosition.Y:0.0} top {up.Top:0.0} hero reached {maxY:0.0}, now {P.GlobalPosition.Y:0.0}");
                await Shot("updraft_ride_top");
            }
            else Say("updraft lumen: NOT FOUND");
        }
        P.Teleport(World.OnGround(1.5f, 6f, .5f), Mathf.Pi);
        Hud(true); Ui.I?.OpenMap(); await Wait(.8f); await Shot("world_map"); Ui.I?.Close(); Hud(false);
        Say($"lands: {Lands.All.Count} lands, {Lands.Sky.Count} sky islands, {Lands.Bridges.Count} bridges, {World.Pois.Count} points of interest");
    }

    /// <summary>Walks and runs in all eight directions (with and without a locked foe) and logs the clip, facing and movement; one picture each.</summary>
    async Task MoveCheck()
    {
        var spot = World.OnGround(1.5f, 6f, .5f); World.TimeOfDay = .45f;
        Game.D.Eq["weapon"] = new Item { Name = "Sword", Slot = "weapon", Wt = "sword" }; Game.Notify();
        // start-up: what plays in the first moments after pressing forward, from idle and right after an attack
        foreach (bool afterAtk in new[] { false, true })
        {
            P.Teleport(spot, 0); Cam(0, 5f); await Wait(1f);
            if (afterAtk) { Input.ActionPress(Inputs.Attack); await Wait(.05f); Input.ActionRelease(Inputs.Attack); await Wait(.1f); }
            Input.ActionPress(Inputs.Forward); var log = new List<string>(); float t0 = 0;
            for (int i = 0; i < 10; i++) { await Wait(.06f); t0 += .06f; log.Add($"{t0:0.00}s {new Vector2(P.Velocity.X, P.Velocity.Z).Length():0.0}m/s {P.AvClip}"); }
            Input.ActionRelease(Inputs.Forward);
            Say($"move start {(afterAtk ? "after attack" : "from idle")}: " + string.Join(" | ", log)); await Wait(.6f);
        }
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

    /// <summary>Casts each zone/strike/nova skill while standing still and logs how far each spell animation and its zone move.</summary>
    async Task FxDrift()
    {
        SpellFxSprite.NoHug = OS.GetCmdlineUserArgs().Contains("--nohug");
        var spot = World.OnGround(-70, -95, .5f); World.TimeOfDay = .45f; _keepAlive = true;
        foreach (var id in new[] { "hex", "entangle", "consecrate", "rain", "meteor", "smite", "nova", "warcry", "whirlwind", "fire" })
        {
            ClearField(); await Wait(.1f);
            Game.D.Eq["weapon"] = new Item { Name = id == "rain" ? "Bow" : "Sword", Slot = "weapon", Wt = id == "rain" ? "bow" : "sword" }; Game.Notify();
            if (!Game.D.Skills.ContainsKey(id)) Game.D.Skills[id] = new SkillState(); Game.D.Bar[0] = id;
            P.Teleport(spot, 0); Cam(0, 7f); await Wait(.4f);
            var foe = new Enemy { Type = "golem", Level = 22, AnyRealm = true, NoLeash = true, Position = World.OnGround(spot.X, spot.Z - 7, .3f) }; Game.Root!.AddChild(foe); await Wait(.4f); Targeting.Set(foe);
            Input.ActionPress("spell1"); await Wait(.05f); Input.ActionRelease("spell1"); await Wait(.5f);
            var start = new Dictionary<ulong, (string what, Vector3 p)>();
            foreach (var n in GetTree().Root.FindChildren("*", "", true, false))
                if (n is SpellFxSprite s && s.IsInsideTree()) start[s.GetInstanceId()] = (s.Name + (s.Ground ? " ground" : "") + " on " + s.GetParent().Name, s.GlobalPosition);
                else if (n is SkillZone z) start[z.GetInstanceId()] = ("zone", z.GlobalPosition);
            float moved0 = P.GlobalPosition.DistanceTo(spot);
            await Wait(1.2f);
            foreach (var (iid, (what, p)) in start)
                if (InstanceFromId(iid) is Node3D nn && IsInstanceValid(nn) && nn.IsInsideTree()) Say($"fxdrift {id}: {what} moved {nn.GlobalPosition.DistanceTo(p):0.00} m, delta {nn.GlobalPosition - p} (hero at {P.GlobalPosition - spot}, {P.GlobalPosition.DistanceTo(spot):0.00} m from start)");
                else Say($"fxdrift {id}: {what} gone");
        }
        _keepAlive = false; ClearField();
    }

    /// <summary>
    /// Movement feel numbers: foot skating while running (how fast the planted foot slides), floor contact running down a slope
    /// (frames off the floor, frames in the fall clip, foot gap to the ground), and stopping distance from a sprint.
    /// </summary>
    /// <summary>Combat feel numbers: do swings connect at the edge of reach (step-in), and how does an enemy blow land on the hero.</summary>
    async Task CombatCheck()
    {
        World.TimeOfDay = .45f; Game.D.Eq["weapon"] = new Item { Name = "Sword", Slot = "weapon", Wt = "sword", Dmg = 5 }; Game.D.Eq["shield"] = null; Game.Notify();
        var spot = World.OnGround(1.5f, 6f, .5f); _keepAlive = true;
        foreach (float gap in new[] { 1.5f, 3f, 4.2f, 5.5f })
        {
            ClearField(); P.Teleport(spot, 0); Cam(0, 5f); await Wait(.5f);
            var g = new Enemy { Type = "bandit", Level = 30, AnyRealm = true, NoLeash = true, Position = World.OnGround(spot.X, spot.Z - gap, .3f) }; Game.Root!.AddChild(g); await Wait(.4f); g.Stun(6f);
            float hp0 = g.Hp; var p0 = P.GlobalPosition;
            Input.ActionPress(Inputs.Attack); await Wait(.05f); Input.ActionRelease(Inputs.Attack);
            await Wait(.22f); if (gap == 3f) await Shot("combat_swing_connect"); await Wait(.4f);
            float edge = P.GlobalPosition.DistanceTo(g.GlobalPosition) - g.Def.R * g.ScaleF();
            Say($"combat swing at {gap:0.0} m: {(g.Hp < hp0 ? "HIT" : "miss")}, hero stepped {P.GlobalPosition.DistanceTo(p0):0.00} m, ends {edge:0.00} m from the foe's edge");
        }
        // an enemy blow on the hero
        ClearField(); P.Teleport(spot, 0); Cam(.6f, 5f); await Wait(.5f);
        var b = new Enemy { Type = "bandit", Level = 3, AnyRealm = true, NoLeash = true, Position = World.OnGround(spot.X, spot.Z - 3.2f, .3f) }; Game.Root!.AddChild(b); await Wait(.2f); b.Wake();
        float hpP = P.Hp; Vector3 before = P.GlobalPosition; float atBlow = -1; string clip = ""; float t = 0; bool shot = false;
        while (t < 6f && atBlow < 0)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); t += (float)GetProcessDeltaTime(); _keepAlive = false;
            if (P.Hp < hpP - .5f) { atBlow = P.GlobalPosition.DistanceTo(b.GlobalPosition) - b.Def.R * b.ScaleF(); before = P.GlobalPosition; }
            else { P.Hp = Game.D.MaxHp(); hpP = P.Hp; }
        }
        await Wait(.12f); clip = P.AvClip; if (!shot) { await Shot("combat_hero_struck"); shot = true; } await Wait(.4f);
        Say($"combat enemy blow: landed {(atBlow < 0 ? "never" : $"{atBlow:0.00} m from the bandit's edge")}, hero shoved {new Vector2(P.GlobalPosition.X - before.X, P.GlobalPosition.Z - before.Z).Length():0.00} m, clip after the blow {clip}");
        _keepAlive = false; ClearField();
    }

    Vector3 _pl, _pr; float _stDz, _stDt;
    async Task FeelCheck()
    {
        Game.D.Eq["weapon"] = new Item { Name = "Sword", Slot = "weapon", Wt = "sword" }; Game.Notify(); World.TimeOfDay = .45f;
        var sk = P.Av?.Skel; if (sk == null) { Say("feel: no avatar"); return; }
        // each locomotion clip's own speed: how far a foot travels backward (model space) while it is the lower foot, per second
        {
            var av = new Avatar(); Game.Root!.AddChild(av); av.GlobalPosition = World.OnGround(-60, -60, 0); av.Build(new Appearance(), "feel"); await Wait(.3f);
            var s2 = av.Skel!; int al = s2.FindBone("foot_l"), ar = s2.FindBone("foot_r"), pel = s2.FindBone("pelvis");
            foreach (var key in new[] { "walk", "run", "sprint" })
            {
                string clip = Avatar.Clip(key); float len = av.ClipLength(clip); if (len <= 0) continue;
                float back = 0, minY = 9, maxPel = -9, minPel = 9; Vector3? prev = null; int prevFoot = -1; const int N = 60;
                for (int i = 0; i <= N; i++)
                {
                    av.PoseAt(key, i / (float)N); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    var l = av.ToLocal(s2.GlobalTransform * s2.GetBoneGlobalPose(al).Origin); var r = av.ToLocal(s2.GlobalTransform * s2.GetBoneGlobalPose(ar).Origin);
                    var pv = av.ToLocal(s2.GlobalTransform * s2.GetBoneGlobalPose(pel).Origin); maxPel = Mathf.Max(maxPel, pv.Y); minPel = Mathf.Min(minPel, pv.Y);
                    int f = l.Y < r.Y ? 0 : 1; var low = f == 0 ? l : r; minY = Mathf.Min(minY, low.Y);
                    if (prev != null && f == prevFoot) back += Mathf.Max(0, low.Z - prev.Value.Z);   // model forward is -Z: a planted foot moves to +Z
                    prev = low; prevFoot = f;
                }
                Say($"feel clip {clip}: {len:0.00} s, planted foot travels {back:0.00} m per cycle = {back / len:0.00} m/s, lowest ankle {minY * 100:0} cm, pelvis bob {(maxPel - minPel) * 100:0} cm");
            }
            av.QueueFree();
        }
        int fl = sk.FindBone("foot_l"), fr = sk.FindBone("foot_r");
        Vector3 Foot(int b) => sk.GlobalTransform * sk.GetBoneGlobalPose(b).Origin;
        // a flat spot and a slope: search for the steepest walkable line near the village
        var flat = World.OnGround(1.5f, 6f, .5f);
        Vector3 slopeTop = flat, slopeDir = Vector3.Forward; float best = 0;
        for (float x = -100; x <= 100; x += 8) for (float z = -100; z <= 100; z += 8)
            foreach (var dd in new[] { Vector3.Forward, Vector3.Back, Vector3.Left, Vector3.Right })
            {
                float h0 = World.GroundAt(x, z), h1 = World.GroundAt(x + dd.X * 20, z + dd.Z * 20), gr = (h0 - h1) / 20f;
                if (gr > best && gr < .7f && World.GroundAt(x, z) > 2f) { best = gr; slopeTop = World.OnGround(x, z, .5f); slopeDir = dd; }
            }
        Say($"feel: slope found, grade {best:0.00} ({Mathf.RadToDeg(Mathf.Atan(best)):0} deg)");

        async Task Run(string label, Vector3 start, Vector3 dir, bool sprint, float secs)
        {
            P.Teleport(start, Util.YawTo(dir.X, dir.Z)); Cam(Util.YawTo(dir.X, dir.Z), 5f); await Wait(.6f);
            Input.ActionPress(Inputs.Forward); if (sprint) Input.ActionPress(Inputs.Sprint); await Wait(.5f);
            int n = 0, air = 0, fall = 0, sn = 0, lastFoot = -1; float skate = 0, gapSum = 0, gapMax = 0, sinkMax = 0; Vector3 lastLow = Vector3.Zero; var slides = new List<float>();
            var t = 0f; _stDz = 0; _stDt = 0;
            while (t < secs)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); float dt = (float)GetProcessDeltaTime(); t += dt; n++;
                if (!P.IsOnFloor()) air++; if (P.AvClip == Avatar.Clip("fall")) fall++;
                var a = Foot(fl); var b = Foot(fr); int lf = a.Y < b.Y ? 0 : 1; var low = lf == 0 ? a : b;
                float g = low.Y - World.GroundAt(low.X, low.Z) - .08f;   // ankle bone sits ~8 cm above the sole
                gapSum += Mathf.Abs(g); gapMax = Mathf.Max(gapMax, g); sinkMax = Mathf.Max(sinkMax, -g);
                { var la = P.Av!.ToLocal(a); var lb = P.Av!.ToLocal(b); if (n > 1) { _stDz += la.Y < lb.Y ? la.Z - _pl.Z : lb.Z - _pr.Z; _stDt += dt; } _pl = la; _pr = lb; }   // the lower foot (on the ground): its backward travel
            }
            var hv = new Vector2(P.Velocity.X, P.Velocity.Z).Length(); float hv0 = hv;
            Say($"feel {label}: speed {hv:0.0} m/s, clip {P.AvClip}, stance foot moves back {_stDz / Mathf.Max(.01f, _stDt):0.0} m/s (skate {hv0 - _stDz / Mathf.Max(.01f, _stDt):+0.0;-0.0} m/s), off floor {air * 100 / Mathf.Max(1, n)}% of frames, fall clip {fall * 100 / Mathf.Max(1, n)}%, foot gap avg {gapSum / n * 100:0} cm (float max {gapMax * 100:0}, sink max {sinkMax * 100:0})");
            // stopping
            var p0 = P.GlobalPosition; Input.ActionRelease(Inputs.Forward); Input.ActionRelease(Inputs.Sprint);
            float st = 0; while (new Vector2(P.Velocity.X, P.Velocity.Z).Length() > .2f && st < 2f) { await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); st += (float)GetPhysicsProcessDeltaTime(); }
            Say($"feel {label}: stops in {st:0.00} s over {new Vector2(P.GlobalPosition.X - p0.X, P.GlobalPosition.Z - p0.Z).Length():0.00} m");
            await Wait(.4f);
        }
        {   // raw check: the left foot relative to the body while jogging
            P.Teleport(flat, 0); Cam(0, 5f); await Wait(.5f); Input.ActionPress(Inputs.Forward); await Wait(.6f); var raw = new List<string>();
            for (int i = 0; i < 12; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); var lp = P.Av!.ToLocal(Foot(fl)); raw.Add($"{lp.Z:0.00}/{lp.Y:0.00}"); }
            Input.ActionRelease(Inputs.Forward); Say("feel raw foot_l z/y (avatar space): " + string.Join(" ", raw)); await Wait(.5f);
        }
        {   // standing across the slope, seen from the side: feet with and without foot IK
            var side = slopeDir.Cross(Vector3.Up); var mid = World.OnGround(slopeTop.X + slopeDir.X * 6, slopeTop.Z + slopeDir.Z * 6, .3f);
            foreach (bool ik in new[] { true, false })
            {
                P.Teleport(mid, Util.YawTo(side.X, side.Z)); await Wait(.8f);
                var fp = P.Av!.Skel!.GetNodeOrNull<FootPlant>("FootPlant"); if (fp != null) fp.Ik.Active = ik;
                // feet against the ground, read right after the IK solved (its signal), so the pose includes it
                float gapL = 0, gapR = 0; bool got = false; var sk2 = P.Av!.Skel!;
                void Read() { var a = Foot(fl); var b = Foot(fr); gapL = a.Y - World.GroundAt(a.X, a.Z); gapR = b.Y - World.GroundAt(b.X, b.Z); got = true; }
                if (fp != null) { if (ik) fp.Ik.ModificationProcessed += Read; else fp.ModificationProcessed += Read; }
                await Wait(.3f); if (fp != null) { if (ik) fp.Ik.ModificationProcessed -= Read; else fp.ModificationProcessed -= Read; }
                Say($"feel slope standing, IK {(ik ? "on" : "off")}: ankle above ground left {gapL * 100:0} cm, right {gapR * 100:0} cm (flat stance is about 10 cm){(got ? "" : " (no read)")}");
                Free(mid + side * 3.4f + Vector3.Up * 1.1f, mid + Vector3.Up * .5f); await Wait(.6f);
                await Shot("feel_slope_ik_" + (ik ? "on" : "off")); Free(null); if (fp != null) fp.Ik.Active = true;
            }
        }
        await Run("flat jog", flat, Vector3.Forward, false, 2f);
        await Run("flat sprint", flat, Vector3.Forward, true, 2f);
        await Run("downhill jog", slopeTop, slopeDir, false, 2.2f);
        await Run("downhill sprint", slopeTop, slopeDir, true, 2.2f);
        await Run("uphill jog", World.OnGround(slopeTop.X + slopeDir.X * 10f, slopeTop.Z + slopeDir.Z * 10f, .3f), -slopeDir, false, 2.2f);
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
