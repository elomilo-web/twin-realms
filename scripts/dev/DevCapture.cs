using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;

namespace TwinRealms;

/// <summary>
/// Dev loop eyes (autoload). Does nothing unless the game is started with the user arg --devcapture
/// (optionally --devcapture-only=name,name). Then: title screen, a new hero, every viewpoint in .devloop/viewpoints.json
/// in file order, 1280x720 PNGs to .devloop/screenshots/current/ (the last run moves to previous/), report.json with
/// FPS avg/min per view, and quit. Run it with tools/capture.bat (tools/capture.ps1 adds the engine errors to the report).
/// Viewpoint fields: name, scene (title | world | arena | dungeon:&lt;key&gt;), action (idle | run | sprint | swing | cast | ui:&lt;menu&gt;),
/// player_at [x,z] or spot "auto:&lt;n&gt;" (n-th flat dry open point on rings of 60/80/100 m), yaw, time (0..1), realm (0|1), cast:&lt;skillId&gt; (grants it, bar 0, waits cast_wait s, default .35), freeze_foes (bool), cam_yaw + cam_dist (follow camera) or camera_pos + look_at (free camera;
/// relative: true makes them offsets from the hero), hud (bool), foes (enemy types spawned 6 m in front), wait_frames.
/// </summary>
public partial class DevCapture : Node
{
    public static bool Wanted() => OS.GetCmdlineUserArgs().Contains("--devcapture");
    public static DevCapture? I;
    /// <summary>Main sets this: closes the title and builds the world with a fresh hero.</summary>
    public static Action? StartGame;

    string _root = "", _cur = "";
    readonly List<Dictionary<string, object>> _report = new();
    static Player P => Player.Instance!;

    public override void _Ready()
    {
        if (!Wanted()) { QueueFree(); return; }
        I = this; ProcessMode = ProcessModeEnum.Always;
        _root = ProjectSettings.GlobalizePath("res://") + ".devloop/";
        _ = Run();
    }

    static void Say(string s) => GD.Print("[DEVCAP] " + s);
    async Task Frames(int n) { for (int i = 0; i < Math.Max(1, n); i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    async Task Secs(float s) => await ToSignal(GetTree().CreateTimer(s, true), "timeout");

    async Task Run()
    {
        try { await Tour(); } catch (Exception e) { GD.PrintErr("[DEVCAP] tour threw: " + e); _report.Add(new() { ["name"] = "_tour", ["error"] = e.Message }); }
        try { WriteReport(); } catch (Exception e) { GD.PrintErr("[DEVCAP] report failed: " + e.Message); }
        Say("done"); GetTree().Quit();
    }

    void RotateFolders()
    {
        string shots = _root + "screenshots/", prev = shots + "previous", cur = shots + "current";
        DirAccess.MakeDirRecursiveAbsolute(shots);
        if (System.IO.Directory.Exists(cur))
        {
            if (System.IO.Directory.Exists(prev)) System.IO.Directory.Delete(prev, true);
            System.IO.Directory.Move(cur, prev);
        }
        System.IO.Directory.CreateDirectory(cur); _cur = cur + "/";
    }

    List<JsonElement> LoadViews()
    {
        var path = _root + "viewpoints.json";
        if (!System.IO.File.Exists(path)) { GD.PrintErr("[DEVCAP] missing " + path); return new(); }
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var all = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement : doc.RootElement.GetProperty("viewpoints");
        var list = all.EnumerateArray().Select(e => e.Clone()).ToList();
        var only = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--devcapture-only="));
        if (only != null)
        {
            var want = only.Split('=', 2)[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
            list = list.Where(v => want.Contains(Str(v, "name"))).ToList();
        }
        return list;
    }

    static string Str(JsonElement v, string k, string def = "") => v.TryGetProperty(k, out var x) && x.ValueKind == JsonValueKind.String ? x.GetString()! : def;
    static float Num(JsonElement v, string k, float def) => v.TryGetProperty(k, out var x) && x.ValueKind == JsonValueKind.Number ? x.GetSingle() : def;
    static bool Flag(JsonElement v, string k, bool def) => v.TryGetProperty(k, out var x) && (x.ValueKind is JsonValueKind.True or JsonValueKind.False) ? x.GetBoolean() : def;
    static Vector3? Vec(JsonElement v, string k)
    {
        if (!v.TryGetProperty(k, out var x) || x.ValueKind != JsonValueKind.Array) return null;
        var a = x.EnumerateArray().Select(e => e.GetSingle()).ToArray();
        return a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : null;
    }

    async Task Tour()
    {
        RotateFolders();
        var views = LoadViews(); Say(views.Count + " viewpoints");
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed); DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        await Frames(30);
        // the title screen first: it only exists before the world is built
        foreach (var v in views.Where(v => Str(v, "scene") == "title")) await Capture(v);
        StartGame?.Invoke();
        await Secs(4f);
        if (Player.Instance == null) { GD.PrintErr("[DEVCAP] no player after start"); return; }
        Game.D.Level = 20; Game.RecalcTree(); Skills.GrantStarting(Game.D);
        Game.Notify();   // the warrior's own starting kit: the pictures show real gear
        foreach (var v in views.Where(v => Str(v, "scene") != "title")) await Capture(v);
    }

    Camera3D? _free;
    void FreeCam(Vector3? from, Vector3 at)
    {
        if (from == null) { _free?.QueueFree(); _free = null; if (Player.Instance?.Rig != null) P.Rig!.Camera.Current = true; return; }
        _free ??= new Camera3D { Fov = 60f, Far = 4500f }; if (_free.GetParent() == null) Game.Root!.AddChild(_free);
        _free.GlobalPosition = from.Value; if (from.Value.DistanceTo(at) > .01f) _free.LookAt(at, Vector3.Up); _free.Current = true;
    }

    void Heal() { if (Player.Instance is { } p) { p.Hp = Game.D.MaxHp(); p.Mp = Game.D.MaxMp(); p.St = Game.D.MaxSt(); } }
    void ClearFoes() { foreach (var n in GetTree().GetNodesInGroup("enemies")) if (n is Node nn) nn.QueueFree(); Targeting.Clear(); }
    void Hud(bool on) { if (Game.Root?.GetNodeOrNull<CanvasLayer>("Hud") is { } h) h.Visible = on; }

    /// <summary>The n-th accepted point of the deterministic ring scan (radius 60/80/100, every 20 deg), or null.</summary>
    Vector2? FindSpot(int n)
    {
        var space = GetViewport().World3D.DirectSpaceState;
        var shape = new SphereShape3D { Radius = 3.5f };   // centred 5 m up: catches trunks and canopies, not the block ground's colliders
        int found = 0;
        foreach (float r in new[] { 60f, 80f, 100f })
            for (int deg = 0; deg < 360; deg += 20)
            {
                float x = r * Mathf.Cos(Mathf.DegToRad(deg)), z = r * Mathf.Sin(Mathf.DegToRad(deg));
                float g = World.GroundAt(x, z);
                if (g <= Lands.WaterAt(x, z) + 1f) continue;
                if (Mathf.Abs(World.GroundAt(x + 3f, z) - g) >= 1.2f || Mathf.Abs(World.GroundAt(x - 3f, z) - g) >= 1.2f
                    || Mathf.Abs(World.GroundAt(x, z + 3f) - g) >= 1.2f || Mathf.Abs(World.GroundAt(x, z - 3f) - g) >= 1.2f) continue;
                if (World.Pois.Any(p => new Vector2(p.X - x, p.Z - z).Length() <= 25f)) continue;
                var q = new PhysicsShapeQueryParameters3D { Shape = shape, Transform = new Transform3D(Basis.Identity, new Vector3(x, g + 5f, z)) };
                if (Player.Instance != null) q.Exclude = new Godot.Collections.Array<Rid> { P.GetRid() };
                if (space.IntersectShape(q, 1).Count > 0) continue;
                if (found++ == n) return new Vector2(x, z);
            }
        return null;
    }

    async Task Capture(JsonElement v)
    {
        string name = Str(v, "name", "view"), scene = Str(v, "scene", "world"), action = Str(v, "action", "idle");
        int wait = (int)Num(v, "wait_frames", 60);
        var entry = new Dictionary<string, object> { ["name"] = name };
        try
        {
            if (scene != "title")
            {
                (Vector3 from, Vector3 at)? lakeCam = null;
                if (Player.Instance != null) P.Visible = true;
                Ui.I?.Close(); if (Ui.I != null) Ui.I.Visible = action.StartsWith("ui:");
                Input.MouseMode = Input.MouseModeEnum.Captured;
                if (scene == "arena" && !Arenas.Active) { Arenas.Start("waves"); await Secs(1.5f); }
                else if (scene.StartsWith("dungeon:") && !Dungeon.Active) { if (Arenas.Active) Arenas.Leave(); Dungeon.Enter(scene[8..], 1); await Secs(3f); }
                else if (scene == "world" && Arenas.Active) Arenas.Leave();
                if (v.TryGetProperty("time", out _)) World.TimeOfDay = Num(v, "time", .5f);
                if (v.TryGetProperty("realm", out _)) World.ForceRealm((int)Num(v, "realm", 0));
                else if (World.Realm != 0 && scene == "world") World.ForceRealm(0);   // views without "realm" are Light (shadow_realm used to leak into the next views)
                if (Str(v, "spot").StartsWith("auto:") && int.TryParse(Str(v, "spot")[5..], out int spotN))
                {   // the n-th flat, dry, open point away from every POI (rings 60/80/100 m, every 20 deg)
                    var sp = FindSpot(spotN);
                    if (sp == null) throw new Exception("no auto spot " + spotN);
                    P.Teleport(World.OnGround(sp.Value.X, sp.Value.Y, .5f), Num(v, "yaw", 0f)); await Secs(.6f);
                }
                else if (v.TryGetProperty("player_at", out var pa) && pa.ValueKind == JsonValueKind.Array)
                {
                    var a = pa.EnumerateArray().Select(e => e.GetSingle()).ToArray();
                    P.Teleport(World.OnGround(a[0], a[1], .5f), Num(v, "yaw", 0f)); await Secs(.6f);
                }
                else if (Str(v, "lake") is { Length: > 0 } lkName && Lands.All.SelectMany(l => l.Lakes).FirstOrDefault(l => l.Name == lkName) is { } lk)
                {   // the shore of a named lake, facing the water
                    // water fills to 1.35 R (Lands.WaterAt): stand past it
                    float sx = lk.X + lk.R * 1.35f + 10f, sz = lk.Z;
                    P.Teleport(World.OnGround(sx, sz, .5f), Mathf.Pi / 2); await Secs(1.2f);
                    // absolute camera above the east shore looking west across the surface, wherever the hero ended up; the hero is hidden
                    lakeCam = (new Vector3(lk.X + lk.R * 1.6f, lk.Level + 14f, lk.Z + lk.R * .25f), new Vector3(lk.X - lk.R * .3f, lk.Level, lk.Z));
                    P.Visible = false;
                    // water diagnostics (BACKLOG: lake shows dry): level vs ground at the centre, halfway and at the shore
                    foreach (float f in new[] { 0f, .5f, .9f })
                    {
                        float x = lk.X + lk.R * f, z = lk.Z;
                        Say($"lake {lkName} at {f:0.0}R ({x:0},{z:0}): level {lk.Level:0.00} water {Lands.WaterAt(x, z):0.00} landsGround {Lands.GroundAt(x, z):0.00} worldGround {World.GroundAt(x, z):0.00}");
                    }
                    Say($"lake {lkName}: hero at {P.GlobalPosition}");
                }
                else if (Str(v, "biome") is { Length: > 0 } bName && Lands.All.SelectMany(l => l.Regions).Where(r => r.b == bName).Select(r => ((float x, float z)?)(r.x, r.z)).FirstOrDefault() is { } reg)
                {   // the middle of the first region of that biome (data/lands.json "biomes")
                    P.Teleport(World.OnGround(reg.x, reg.z, .6f), Num(v, "yaw", 0f)); await Secs(1.5f);
                }
                else if (Str(v, "place") is { Length: > 0 } pl && pl.Split('/') is { Length: 2 } lp
                         && Lands.All.Find(l => l.Id == lp[0])?.Places.Find(q => q.Id == lp[1]) is { } place)
                {
                    P.Teleport(World.OnGround(place.X + 4, place.Z + place.R * .3f, .6f), Num(v, "yaw", Mathf.Pi * .8f)); await Secs(1.2f);
                }
                // wild foes near the hero spoil the pictures (hit flash, damage numbers): clear them before spawning the view's own foes
                foreach (var n in GetTree().GetNodesInGroup("enemies")) if (n is Enemy we && we.GlobalPosition.DistanceTo(P.GlobalPosition) < 60f) we.QueueFree();
                if (scene.StartsWith("dungeon:") || scene == "arena") { foreach (var n in GetTree().GetNodesInGroup("enemies")) if (n is Enemy e && e.GlobalPosition.DistanceTo(P.GlobalPosition) < 10f) e.QueueFree(); }
                Heal(); Hud(Flag(v, "hud", false));
                if (v.TryGetProperty("foes", out var fs) && fs.ValueKind == JsonValueKind.Array)
                {
                    var fwd = -P.GlobalTransform.Basis.Z with { Y = 0 }; fwd = fwd.LengthSquared() > .01f ? fwd.Normalized() : Vector3.Forward;
                    int i = 0; bool side = Flag(v, "foe_side", false);
                    var right = fwd.Cross(Vector3.Up);
                    Vector3? firstFoe = null;
                    foreach (var f in fs.EnumerateArray())
                    {
                        var at = side ? P.GlobalPosition + right * (2.2f + 1.5f * i++) : P.GlobalPosition + fwd * 6f + right * ((i++ - .5f) * 2.5f);
                        var foe = new Enemy { Type = f.GetString()!, Level = 18, AnyRealm = true, NoLeash = true, Position = World.OnGround(at.X, at.Z, .3f) };
                        Game.Root!.AddChild(foe);
                        firstFoe ??= foe.Position;
                        if (Flag(v, "freeze_foes", false)) { foe.Rotation = new Vector3(0, Mathf.Pi, 0); foe.SetProcess(false); foe.SetPhysicsProcess(false); }   // stands still: no attacks, no interrupts
                    }
                    await Secs(1f);
                    if (action.StartsWith("cast") && firstFoe != null)
                    {   // face the first foe so targeted spells fire at it
                        var d = firstFoe.Value - P.GlobalPosition;
                        if (new Vector2(d.X, d.Z).LengthSquared() > .01f) P.Teleport(P.GlobalPosition, Mathf.Atan2(-d.X, -d.Z));
                    }
                }
                // camera: free (absolute or relative to the hero) or the follow camera
                var cp = Vec(v, "camera_pos"); var la = Vec(v, "look_at") ?? Vector3.Zero;
                if (lakeCam != null && cp == null) FreeCam(lakeCam.Value.from, lakeCam.Value.at);
                else if (cp != null) { if (Flag(v, "relative", false)) { cp += P.GlobalPosition; la += P.GlobalPosition; } FreeCam(cp, la); }
                else { FreeCam(null, default); P.Rig!.Rotation = new Vector3(Num(v, "cam_pitch", -.25f), Num(v, "cam_yaw", P.Rotation.Y + Mathf.Pi), 0); P.Rig.Distance = Num(v, "cam_dist", 4.5f); }
                await DoAction(action, Num(v, "cast_wait", .35f));
            }
            // measure frame times while the view settles
            var dts = new List<double>(); ulong last = Time.GetTicksUsec();
            for (int i = 0; i < wait; i++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                ulong now = Time.GetTicksUsec(); dts.Add((now - last) / 1e6); last = now;
            }
            if (dts.Count > 4) dts = dts.Skip(dts.Count / 4).ToList();   // the first frames after a teleport are spikes
            double avg = dts.Count > 0 ? dts.Count / dts.Sum() : 0, min = dts.Count > 0 ? 1.0 / dts.Max() : 0;
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var img = GetViewport().GetTexture().GetImage();
            if (img.GetWidth() != 1280 || img.GetHeight() != 720) img.Resize(1280, 720, Image.Interpolation.Bilinear);
            img.SavePng(_cur + name + ".png");
            entry["fps_avg"] = Math.Round(avg, 1); entry["fps_min"] = Math.Round(min, 1);
            entry["draw_calls"] = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
            Say($"{name}: {avg:0} fps avg, {min:0} min");
        }
        catch (Exception e) { GD.PrintErr($"[DEVCAP] {name} failed: {e.Message}"); entry["error"] = e.Message; }
        finally
        {
            foreach (var k in new[] { Inputs.Forward, Inputs.Sprint, Inputs.Attack, Inputs.Roll, "spell1" }) Input.ActionRelease(k);
            if (scene != "title") { Ui.I?.Close(); if (Ui.I != null) Ui.I.Visible = false; }
        }
        _report.Add(entry);
    }

    async Task DoAction(string action, float castWait = .35f)
    {
        if (action.StartsWith("place:"))
        {   // a block of that key two metres in front of the hero, on the ground (needs the block world)
            byte id = Blocks.Id(action[6..]);
            var fwd = -P.GlobalTransform.Basis.Z with { Y = 0 }; fwd = fwd.LengthSquared() > .01f ? fwd.Normalized() : Vector3.Forward;
            var at = P.GlobalPosition + fwd * 2f; int bx = Mathf.FloorToInt(at.X), bz = Mathf.FloorToInt(at.Z), by = Mathf.FloorToInt(P.GlobalPosition.Y + .05f);
            bool ok = id != 0 && (BlockFx.Place(bx, by, bz, id, 0) || BlockFx.Place(bx, by + 1, bz, id, 0));
            Say($"place {action[6..]} (id {id}) at {bx},{by},{bz}: {(ok ? "ok" : "failed")}");
            await Secs(.5f); return;
        }
        switch (action)
        {
            case "dodge": Input.ActionPress(Inputs.Roll); await Secs(.05f); Input.ActionRelease(Inputs.Roll); await Secs(.12f); break;
            case "run": Input.ActionPress(Inputs.Forward); await Secs(.8f); break;
            case "sprint": Input.ActionPress(Inputs.Forward); Input.ActionPress(Inputs.Sprint); await Secs(1f); break;
            case "swing": Input.ActionPress(Inputs.Attack); await Secs(.05f); Input.ActionRelease(Inputs.Attack); await Secs(.2f); break;
            case "swing3": for (int s = 0; s < 3; s++) { Input.ActionPress(Inputs.Attack); await Secs(.05f); Input.ActionRelease(Inputs.Attack); await Secs(.3f); } break;
            case "cast":
                var id = Game.D.Skills.Keys.FirstOrDefault(k => Skills.Get(k) is { } s && s.Kind is not "resurrect" && s.Kind is not "buff");
                if (id != null) { Game.D.Bar[0] = id; Game.Notify(); }
                Input.ActionPress("spell1"); await Secs(.05f); Input.ActionRelease("spell1"); await Secs(castWait); break;
            case var c when c.StartsWith("cast:"):
                var sid = c[5..];
                if (!Game.D.Skills.ContainsKey(sid)) Game.D.Skills[sid] = new SkillState { Rank = 3 };
                Game.D.Bar[0] = sid; Game.Notify(); Heal();
                Input.ActionPress("spell1"); await Secs(.05f); Input.ActionRelease("spell1"); await Secs(castWait); break;
            case "ui:pack": Ui.I?.OpenPack(); await Secs(.4f); break;
            case "ui:spellcraft": Ui.I?.OpenSpellcraft(null); await Secs(.4f); break;
            case "ui:character": Ui.I?.OpenCharacter(); await Secs(.4f); break;
            case "ui:tree": Ui.I?.OpenTree(); await Secs(.4f); break;
            case "ui:skills": Ui.I?.OpenSkills(); await Secs(.4f); break;
            case "ui:map": Ui.I?.OpenMap(); await Secs(.4f); break;
            case "ui:pause": Ui.I?.OpenPause(); await Secs(.4f); break;
            case var u when u.StartsWith("ui:"):
                {
                    var ui = Ui.I; if (ui == null) break;
                    var arg = u.IndexOf(':', 3) >= 0 ? u[(u.IndexOf(':', 3) + 1)..] : "";
                    var nm = u[3..]; if (arg.Length > 0) nm = nm[..(nm.Length - arg.Length - 1)];
                    switch (nm)
                    {
                        case "settings": ui.OpenSettings(); break;
                        case "controls": ui.OpenControls(); break;
                        case "journal": ui.OpenJournal(); break;
                        case "attacks": ui.OpenAttacks(null); break;
                        case "macros": ui.OpenMacros(); break;
                        case "mods": ui.OpenMods(); break;
                        case "shop": ui.OpenShop(arg.Length > 0 ? arg : "buy"); break;
                        case "smith": ui.OpenSmith(); break;
                        case "craft": ui.OpenCraft(arg.Length > 0 ? arg : "gear"); break;
                        case "valor": ui.OpenValor(arg.Length > 0 ? arg : "gacha"); break;
                        case "totals": ui.OpenTotals(); break;
                        case "multiplayer": ui.OpenMultiplayer(); break;
                        case "stash": ui.OpenStash(); break;
                        case "create": ui.ShowCreate(); break;
                    }
                    await Secs(.4f); break;
                }
        }
    }

    void WriteReport()
    {
        var fps = _report.Where(r => r.ContainsKey("fps_avg")).Select(r => Convert.ToDouble(r["fps_avg"])).ToList();
        var o = new Dictionary<string, object>
        {
            ["time"] = DateTime.Now.ToString("s"), ["views"] = _report,
            ["fps_avg_all"] = fps.Count > 0 ? Math.Round(fps.Average(), 1) : 0, ["errors"] = new List<string>()
        };
        System.IO.File.WriteAllText(_cur + "report.json", JsonSerializer.Serialize(o, new JsonSerializerOptions { WriteIndented = true }));
    }
}
