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
    /// <summary>Until the loading screen has lifted (LoadScreen.cs), or max seconds.</summary>
    async Task LoadDone(float max = 20f)
    {
        ulong t0 = Time.GetTicksMsec();
        while (LoadScreen.Busy && Time.GetTicksMsec() - t0 < (ulong)(max * 1000)) await Frames(1);
        if (LoadScreen.Busy) GD.PrintErr($"[DEVCAP] loading screen still up after {max:0} s");
    }

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
        await LoadDone(90f);   // the world builds a step a frame behind the loading screen
        if (Player.Instance == null) { GD.PrintErr("[DEVCAP] no player after start"); return; }
        Game.D.Level = 20; Game.RecalcTree(); Skills.GrantStarting(Game.D);
        Game.Notify();   // the warrior's own starting kit: the pictures show real gear
        foreach (var v in views.Where(v => Str(v, "scene") != "title")) await Capture(v);
    }

    string? _quest;
    Camera3D? _free;
    void FreeCam(Vector3? from, Vector3 at)
    {
        if (from == null) { _free?.QueueFree(); _free = null; if (Player.Instance?.Rig != null) P.Rig!.Camera.Current = true; return; }
        _free ??= new Camera3D { Fov = 60f, Far = 4500f }; if (_free.GetParent() == null) Game.Root!.AddChild(_free);
        _free.GlobalPosition = from.Value; if (from.Value.DistanceTo(at) > .01f) _free.LookAt(at, Vector3.Up); _free.Current = true;
    }

    /// <summary>Hero back to neutral: no statuses/buffs, full bars, no hurt vignette, normal time.</summary>
    Enemy? _boss;
    void ResetHero()
    {
        if (_boss != null) { if (GodotObject.IsInstanceValid(_boss)) _boss.QueueFree(); _boss = null; }   // a frozen boss from the last view
        Enemy.CurrentBoss = null;
        if (Player.Instance is not { } p) return;
        p.Buffs.Clear(); p.Statuses.Clear(); Engine.TimeScale = 1.0; Heal(); TwinRealms.Hud.ResetFx();
    }
    // ---- per-view kit: "gear" {slot: name} (weapon: a weapon type, or "type:Name"; others: an item name matched like
    // data/avatar.json, e.g. "Plate", "Greathelm"), "pack_fill": n rolled items, "mats_fill": n material kinds. Restored after the view.
    Dictionary<string, Item?>? _savedEq; List<Item>? _savedInv; Dictionary<string, int>? _savedMats;
    void SetupKit(JsonElement v)
    {
        var d = Game.D; bool changed = false;
        if (v.TryGetProperty("gear", out var g) && g.ValueKind == JsonValueKind.Object)
        {
            _savedEq ??= new Dictionary<string, Item?>(d.Eq);
            foreach (var p in g.EnumerateObject())
            {
                string slot = p.Name, val = p.Value.GetString() ?? "";
                if (val == "") { d.Eq[slot] = null; continue; }
                if (slot == "weapon")
                {
                    var parts = val.Split(':'); var it = Items.Roll(20, 0, "weapon", 3, null, parts[0]);
                    if (parts.Length > 1) it.Name = parts[1];
                    d.Eq["weapon"] = it;
                }
                else { var it = Items.Roll(20, 0, slot, 3); it.Name = val; d.Eq[slot] = it; }
            }
            changed = true;
        }
        if (v.TryGetProperty("pack_fill", out _))
        {
            _savedInv ??= new List<Item>(d.Inv);
            int n = (int)Num(v, "pack_fill", 0); var slots = new[] { "weapon", "armor", "shield", "head", "hands", "feet", "cape", "trinket" };
            d.Inv.Clear(); for (int i = 0; i < n; i++) d.Inv.Add(Items.Roll(5 + i % 30, 0, slots[i % slots.Length], i % 5));
            changed = true;
        }
        if (v.TryGetProperty("mats_fill", out _))
        {
            _savedMats ??= new Dictionary<string, int>(d.Mats);
            int n = (int)Num(v, "mats_fill", 0), i = 0; GatherDb.Mat("");   // loads the table
            foreach (var k in GatherDb.Mats.Keys) { if (i++ >= n) break; d.Mats[k] = 3 + i * 7 % 60; }
            changed = true;
        }
        if (Flag(v, "learn_all", false))
        {   // every skill learned at rank 3 (menus at their fullest: the "windows never stretch" rule)
            _savedSkills ??= new Dictionary<string, SkillState>(d.Skills);
            foreach (var s in Skills.All) if (!d.Skills.ContainsKey(s.Id)) d.Skills[s.Id] = new SkillState { Rank = 3 };
            changed = true;
        }
        if (changed) Game.Notify();
        // "body": "female"/"male" (+ optional "race"): a dressed stand-in Avatar in the hero's place (the hero model is built once),
        // wearing the view's gear; the hero is hidden for the view and the stand-in removed after it (RestoreKit)
        if ((Str(v, "body").Length > 0 || Str(v, "race").Length > 0) && Player.Instance != null && Avatar.Available)
        {
            var ap = Races.Look(d);
            if (Str(v, "race") is { Length: > 0 } rid)
            {
                var src = Races.Get(rid).Look;
                ap.Skin = src.Skin; ap.Hair = src.Hair; ap.Eyes = src.Eyes; ap.Height = src.Height; ap.Bulk = src.Bulk;
                ap.PointyEars = src.PointyEars; ap.Tusks = src.Tusks; ap.Beard = src.Beard; ap.Horns = src.Horns;
            }
            if (Str(v, "body") is { Length: > 0 } bs) ap.Female = bs == "female";
            _standIn = new Avatar { Name = "DevStandIn" }; Game.Root!.AddChild(_standIn);
            _standIn.GlobalTransform = P.GlobalTransform;
            _standIn.Build(ap, d.Name);
            _standIn.Dress(s => d.Eq.GetValueOrDefault(s), d.WType(), Classes.Get(d.Class).Tunic);
            if (Flag(v, "fly", false)) _standIn.FlapWings(Num(v, "fly_t", .2f), true);   // Golden Wings spread as in flight
            P.Visible = false;
        }
        // "probe": radius in metres; logs every visible mesh near the hero (what is that object in the picture?)
        if (v.TryGetProperty("probe", out _) && Player.Instance != null)
        {
            float r = Num(v, "probe", 4f); var at = P.GlobalPosition;
            void Walk(Node n)
            {
                if (n is MeshInstance3D mi && mi.IsVisibleInTree() && mi.GlobalPosition.DistanceTo(at) < r && !mi.IsAncestorOf(P) && !P.IsAncestorOf(mi))
                    Say($"probe {mi.GetPath()} at {mi.GlobalPosition - at} mesh {mi.Mesh?.GetType().Name} {mi.Mesh?.ResourcePath}");
                foreach (var c in n.GetChildren()) Walk(c);
            }
            Walk(Game.Root!);
        }
    }
    Avatar? _standIn;
    void RestoreKit()
    {
        var d = Game.D; bool changed = false;
        if (_standIn != null) { if (GodotObject.IsInstanceValid(_standIn)) _standIn.QueueFree(); _standIn = null; if (Player.Instance != null) P.Visible = true; }
        if (_savedEq != null) { d.Eq.Clear(); foreach (var kv in _savedEq) d.Eq[kv.Key] = kv.Value; _savedEq = null; changed = true; }
        if (_savedInv != null) { d.Inv.Clear(); d.Inv.AddRange(_savedInv); _savedInv = null; changed = true; }
        if (_savedMats != null) { d.Mats.Clear(); foreach (var kv in _savedMats) d.Mats[kv.Key] = kv.Value; _savedMats = null; changed = true; }
        if (_savedSkills != null) { d.Skills.Clear(); foreach (var kv in _savedSkills) d.Skills[kv.Key] = kv.Value; _savedSkills = null; changed = true; }
        if (changed) Game.Notify();
    }
    Dictionary<string, SkillState>? _savedSkills;

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
                ResetHero();
                Ui.I?.Close(); if (Ui.I != null) Ui.I.Visible = action.StartsWith("ui:") || action.StartsWith("padnav:");
                Input.MouseMode = Input.MouseModeEnum.Captured;
                if (scene == "arena" && !Arenas.Active) { Arenas.Start("waves"); await LoadDone(); await Secs(1.5f); }
                else if (scene.StartsWith("dungeon:") && (!Dungeon.Active || Dungeon.Key != scene[8..]))
                {   // a different dungeon than the one we are in (barrow -> scarletta): leave first, then enter the asked one
                    if (Dungeon.Active) { Dungeon.Leave(false); await LoadDone(); await Secs(1f); }
                    if (Arenas.Active) Arenas.Leave(); Dungeon.Enter(scene[8..], 1); await LoadDone(); await Secs(3f);
                }
                else if (scene == "world" && Arenas.Active) Arenas.Leave();
                if (v.TryGetProperty("time", out _)) World.TimeOfDay = Num(v, "time", .5f);
                Weather.Force(v.TryGetProperty("rain", out _) ? Num(v, "rain", 0f) : 0f);   // views without "rain" are dry (rain used to leak into the next views)
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
                ResetHero(); Hud(Flag(v, "hud", false));
                SetupKit(v);
                // "pets": species to give the hero for this view (the first one active and out); restored afterwards
                if (v.TryGetProperty("pets", out var pts) && pts.ValueKind == JsonValueKind.Array) DevPets(pts.EnumerateArray().Select(x => x.GetString() ?? "wolf").ToArray());
                // "buffs": skill ids with a buff (warcry) go on the hero's buff list, status ids (burn, poison) on its statuses; "quest": accepted + "quest_progress" kills
                if (_quest != null) { Game.Q(_quest).S = 0; Game.Q(_quest).N = 0; _quest = null; }
                if (v.TryGetProperty("buffs", out var bf) && bf.ValueKind == JsonValueKind.Array)
                    foreach (var b in bf.EnumerateArray())
                    {
                        var bid = b.GetString() ?? "";
                        if (Skills.Get(bid) is { } sk && sk.Buff.Count > 0) P.Buffs.Add((bid, new Dictionary<string, float>(sk.Buff), 60f));
                        else P.Statuses.Add(bid, 20f, 0, 0);   // long duration: lasts through the capture
                    }
                if (Str(v, "quest") is { Length: > 0 } qid && Quests.ById(qid) is { } qd)
                {
                    Quests.Accept(qd); _quest = qid;
                    Game.Q(qid).N = Math.Min(qd.Need - 1, (int)Num(v, "quest_progress", 1f));
                }
                if (v.TryGetProperty("buffs", out _)) Game.RecalcTree();
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
                if (Str(v, "boss") is { Length: > 0 } bossType)
                {   // a boss 3.2 m in front of the hero; "boss_freeze" stops it after the action (the hits) so the frame is stable
                    var bf2 = -P.GlobalTransform.Basis.Z with { Y = 0 }; bf2 = bf2.LengthSquared() > .01f ? bf2.Normalized() : Vector3.Forward;
                    var bp = P.GlobalPosition + bf2 * Num(v, "boss_dist", 3.2f);
                    _boss = new Enemy { Type = bossType, Level = (int)Num(v, "boss_level", 30f), AnyRealm = true, NoLeash = true, Position = World.OnGround(bp.X, bp.Z, .3f) };
                    Game.Root!.AddChild(_boss);
                    await Secs(1f);
                    Enemy.CurrentBoss = _boss;
                }
                // camera: free (absolute or relative to the hero) or the follow camera
                var cp = Vec(v, "camera_pos"); var la = Vec(v, "look_at") ?? Vector3.Zero;
                if (lakeCam != null && cp == null) FreeCam(lakeCam.Value.from, lakeCam.Value.at);
                else if (cp != null) { if (Flag(v, "relative", false)) { cp += P.GlobalPosition; la += P.GlobalPosition; } FreeCam(cp, la); }
                else { FreeCam(null, default); P.Rig!.Rotation = new Vector3(Num(v, "cam_pitch", -.25f), Num(v, "cam_yaw", P.Rotation.Y + Mathf.Pi), 0); P.Rig.Distance = Num(v, "cam_dist", 4.5f); }
                await DoAction(action, Num(v, "cast_wait", .35f));
                if (_boss != null && GodotObject.IsInstanceValid(_boss) && Flag(v, "boss_freeze", false))
                { _boss.SetProcess(false); _boss.SetPhysicsProcess(false); Enemy.CurrentBoss = _boss; }
            }
            else if (action.StartsWith("padnav:")) await DoAction(action);   // the title menu itself (before the world exists)
            // measure frame times while the view settles
            var dts = new List<double>(); ulong last = Time.GetTicksUsec();
            for (int i = 0; i < wait; i++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (scene != "title" && Player.Instance is { Dead: false }) Heal();   // keep the hero alive while the view settles (the boss view killed it)
                ulong now = Time.GetTicksUsec(); dts.Add((now - last) / 1e6); last = now;
            }
            if (dts.Count > 4) dts = dts.Skip(dts.Count / 4).ToList();   // the first frames after a teleport are spikes
            double avg = dts.Count > 0 ? dts.Count / dts.Sum() : 0, min = dts.Count > 0 ? 1.0 / dts.Max() : 0;
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var img = GetViewport().GetTexture().GetImage();
            if (img.GetWidth() != 1280 || img.GetHeight() != 720) img.Resize(1280, 720, Image.Interpolation.Bilinear);
            img.SavePng(_cur + name + ".png");
            if (PetWorld.Out != null && GodotObject.IsInstanceValid(PetWorld.Out)) entry["pet"] = $"{PetWorld.Out.S.Species} at {PetWorld.Out.GlobalPosition} (hero {P.GlobalPosition}) visible {PetWorld.Out.Visible}";
            entry["fps_avg"] = Math.Round(avg, 1); entry["fps_min"] = Math.Round(min, 1);
            entry["draw_calls"] = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
            Say($"{name}: {avg:0} fps avg, {min:0} min");
        }
        catch (Exception e) { GD.PrintErr($"[DEVCAP] {name} failed: {e.Message}"); entry["error"] = e.Message; }
        finally
        {
            foreach (var k in new[] { Inputs.Forward, Inputs.Sprint, Inputs.Attack, Inputs.Roll, "spell1" }) Input.ActionRelease(k);
            if (action == "ui:loading") LoadScreen.Hide(false);
            if (scene != "title") { Ui.I?.Close(); if (Ui.I != null) Ui.I.Visible = false; }
            RestoreKit(); Player.BuildMode = false;
            foreach (var sc in GetTree().GetNodesInGroup("soulcubes")) if (sc is Node scn) scn.QueueFree();   // a cube still shaking must not bind into the real save
            if (_petsBak != null) { var dd = Game.D; dd.Pets = _petsBak.Value.pets; dd.ActivePet = _petsBak.Value.act; dd.Cubes = _petsBak.Value.cubes; _petsBak = null; PetWorld.Despawn(); }
            if (action.StartsWith("padnav:")) Inputs.SetUsingPad(false);
        }
        _report.Add(entry);
    }

    (List<PetState> pets, string act, Dictionary<string, int> cubes)? _petsBak;
    /// <summary>Gives the hero these pets (levels 8, 12, 16 ...), the first one out; the real ones come back after the view.</summary>
    void DevPets(string[] species)
    {
        var d = Game.D; _petsBak ??= (d.Pets, d.ActivePet, new Dictionary<string, int>(d.Cubes));   // the new list below keeps the real one untouched
        d.Pets = new List<PetState>(); int i = 0;
        foreach (var s in species) { var p = PetDb.New(s, 8 + 4 * i++, "runed"); p.Bond = 20 + 15 * i; p.Xp = p.XpNeed() / 3; d.Pets.Add(p); }
        d.ActivePet = d.Pets.Count > 0 ? d.Pets[0].Id : ""; d.Cubes["iron"] = 6; d.Cubes["runed"] = 3; d.Cubes["void"] = 1;
        PetWorld.Despawn(); Game.Notify();
    }

    async Task DoAction(string action, float castWait = .35f)
    {
        if (action.StartsWith("catch:"))
        {   // throw a cube of that tier at the nearest foe, worn down to 30% health; the picture lands while the cube shakes
            if (_petsBak == null) { _petsBak = (Game.D.Pets, Game.D.ActivePet, new Dictionary<string, int>(Game.D.Cubes)); Game.D.Pets = new List<PetState>(Game.D.Pets); }
            string tier = action[6..]; Game.D.Cubes[tier] = Cubes.Count(tier) + 2;
            Enemy? foe = null; float bd = 99;
            foreach (var n in GetTree().GetNodesInGroup("enemies")) if (n is Enemy e && !e.Dead && e.GlobalPosition.DistanceTo(P.GlobalPosition) is float dd && dd < bd) { bd = dd; foe = e; }
            if (foe == null) { Say("catch: no foe"); return; }
            foe.Hp = foe.MaxHp * .3f; Say($"catch: {foe.Type} chance {PetDb.CatchChance(foe, tier):P1}");
            Cubes.Throw(P, tier, foe); await Secs(castWait); return;
        }
        if (action.StartsWith("place:"))
        {   // a block of that key two metres in front of the hero, on the ground (needs the block world)
            byte id = Blocks.Id(action[6..]);
            var fwd = -P.GlobalTransform.Basis.Z with { Y = 0 }; fwd = fwd.LengthSquared() > .01f ? fwd.Normalized() : Vector3.Forward;
            var at = P.GlobalPosition + fwd * 2f; int bx = Mathf.FloorToInt(at.X), bz = Mathf.FloorToInt(at.Z), by = Mathf.FloorToInt(P.GlobalPosition.Y + .05f);
            bool ok = id != 0 && (BlockFx.Place(bx, by, bz, id, 0) || BlockFx.Place(bx, by + 1, bz, id, 0));
            Say($"place {action[6..]} (id {id}) at {bx},{by},{bz}: {(ok ? "ok" : "failed")}");
            await Secs(.5f); return;
        }
        if (action == "build")
        {   // build mode with the pickaxe in hand and the block hotbar: a few torches, stairs, slabs and a fence placed in front
            var m = Game.D.Mats; foreach (var k in new[] { "planks", "cobblestone", "torch", "planks_stairs", "cobble_stairs", "planks_slab", "glass", "fence", "log" }) if (Blocks.Exists(k)) m[Blocks.Item(k)] = 20;
            Game.D.Pick = Math.Max(Game.D.Pick, 3); Player.BuildMode = true; Game.Notify();
            var fwd = -P.GlobalTransform.Basis.Z with { Y = 0 }; fwd = fwd.LengthSquared() > .01f ? fwd.Normalized() : Vector3.Forward; var side = fwd.Cross(Vector3.Up);
            int by = Mathf.FloorToInt(P.GlobalPosition.Y + .05f);
            byte facing = (byte)(Mathf.Abs(fwd.X) > Mathf.Abs(fwd.Z) ? (fwd.X > 0 ? 0 : 1) : (fwd.Z > 0 ? 2 : 3));
            void Put(float f, float s, int dy, string key, byte meta = 0)
            {
                var at = P.GlobalPosition + fwd * f + side * s; byte bid = Blocks.Id(key); if (bid == 0) return;
                int bx = Mathf.FloorToInt(at.X), bz = Mathf.FloorToInt(at.Z);
                for (int up = 0; up < 3; up++) if (BlockFx.Place(bx, by + dy + up, bz, bid, meta)) return;
            }
            Put(2.5f, 0, 0, "planks"); Put(3.5f, 0, 0, "planks_stairs", facing); Put(3.5f, 1, 0, "cobble_stairs", facing); Put(2.5f, -1.2f, 0, "planks_slab");
            Put(2.5f, 1.4f, 0, "torch"); Put(2.5f, -2.4f, 0, "torch"); for (int i = -1; i <= 1; i++) Put(4.6f, i, 0, "fence");
            Put(3.5f, -1.2f, 0, "glass");
            await Secs(.6f); return;
        }
        if (action.StartsWith("padnav:")) { await PadNav(action[7..]); return; }
        switch (action)
        {
            case "dodge": Input.ActionPress(Inputs.Roll); await Secs(.05f); Input.ActionRelease(Inputs.Roll); await Secs(.12f); break;
            case "run": Input.ActionPress(Inputs.Forward); await Secs(.8f); break;
            case "sprint": Input.ActionPress(Inputs.Forward); Input.ActionPress(Inputs.Sprint); await Secs(1f); break;
            case "swing": { int shots0 = Projectile.SpawnedByPlayer; Input.ActionPress(Inputs.Attack); await Secs(.05f); Input.ActionRelease(Inputs.Attack); await Secs(castWait); Say($"swing: {Projectile.SpawnedByPlayer - shots0} player shots"); } break;
            case "swing3": for (int s = 0; s < 3; s++) { Input.ActionPress(Inputs.Attack); await Secs(.05f); Input.ActionRelease(Inputs.Attack); await Secs(.3f); } break;
            case "combo3":   // three presses, then .2 s after the last one: the finisher is in full swing
                for (int s = 0; s < 3; s++) { Input.ActionPress(Inputs.Attack); await Secs(.05f); Input.ActionRelease(Inputs.Attack); await Secs(s < 2 ? .3f : .15f); }
                break;
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
            case "ui:loading": LoadScreen.Preview(World.GetPoi("village")?.Name ?? "Oakhaven"); await Secs(.4f); break;   // hidden again in Capture's finally
            case "ui:spellcraft3": UiSpellcraft.DebugPreload3(); await Secs(.4f); break;
            case "castpaths":
            {   // focus F-01: every cast path through the one gate; [CAST] lines in the log show cooldowns starting / blocking
                Player.LogCasts = true; var d = Game.D;
                foreach (var s in new[] { "echo", "resonance", "loop" }) if (!d.Supports.Contains(s)) d.Supports.Add(s);
                d.Skills["fire"] = new SkillState { Rank = Skills.MaxRank };
                Say($"sockets: {Skills.Socket("fire", 0, "echo") ?? "echo"} {Skills.Socket("fire", 1, "resonance") ?? "resonance"} {Skills.Socket("fire", 2, "loop") ?? "loop"}");
                d.Bar[0] = "fire"; Game.Notify(); Heal(); float cd = Skills.Compute("fire").Cd;
                Say($"castpaths 1: hotbar key (root cast + echo repeats; base cooldown {cd:0.00}s)");
                Input.ActionPress("spell1"); await Secs(.05f); Input.ActionRelease("spell1");
                await Secs(.75f);   // after the last echo (.6 s) but before a full cooldown from it
                Say($"castpaths 2: hotbar again right after the last echo -> {(P.DebugCast("fire") ? "CAST (bug)" : "blocked (ok)")}  cd left {P.CdOf("fire"):0.00}s");
                Say("castpaths 3: auto (Arcane Loop) while on cooldown -> expect blocked"); P.AutoFire("fire", "timer");
                await Secs(cd + 1.5f); Heal();
                Say("castpaths 4: auto (Arcane Loop) off cooldown -> pays, starts the cooldown"); P.AutoFire("fire", "timer"); await Secs(.1f);
                Say($"castpaths 5: hotbar right after the auto-cast -> {(P.DebugCast("fire") ? "CAST (bug)" : "blocked (ok)")}  cd left {P.CdOf("fire"):0.00}s");
                await Secs(cd + 1.5f);
                var sp = UiSpellcraft.DebugForge3(); Heal();
                if (sp != null) { bool ok = P.DebugCast(sp); Say($"castpaths 6: crafted 3-section spell {sp}: {(ok ? "cast" : "NOT cast")}, cd left {P.CdOf(sp):0.00}s"); await Secs(3f); }
                Player.LogCasts = false; break;
            }
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
                        case "pets": ui.OpenPets(null, arg.Length > 0 ? arg : "skills"); break;
                    }
                    await Secs(.4f); break;
                }
        }
    }

    // ---- controller menu test: open a window with the mouse/keyboard, then drive it only with injected pad events
    static string Desc(Control? c)
    {
        if (c == null) return "<none>";
        string t = c is Button b ? b.Text : c is ItemSlot s ? "slot:" + (s.Item?.Name ?? "empty") : c.Name;
        if (t.Length > 28) t = t[..28];
        string cur = c is TreeView tv ? " @" + tv.PadCursor : c is MapView mv ? " @" + mv.PadCursor : c is ModLanes ml ? " @" + ml.PadCursor : "";
        return $"{c.GetType().Name}'{t.Replace('\n', ' ')}'{cur}";
    }
    async Task PadBtn(JoyButton b, int hold = 2)
    {
        Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = b, Pressed = true, Device = 0 }); await Frames(hold);
        Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = b, Pressed = false, Device = 0 }); await Frames(3);
    }
    async Task PadStick(JoyAxis a, float v, float secs)
    {
        Input.ParseInputEvent(new InputEventJoypadMotion { Axis = a, AxisValue = v, Device = 0 }); await Secs(secs);
        Input.ParseInputEvent(new InputEventJoypadMotion { Axis = a, AxisValue = 0, Device = 0 }); await Frames(3);
    }
    async Task PadNav(string menu)
    {
        var ui = Ui.I; if (ui == null) { Say("padnav: no ui"); return; }
        Inputs.SetUsingPad(false);   // opened from mouse / keyboard: the first pad press must still bring focus in
        switch (menu)
        {
            case "pause": ui.OpenPause(); break;
            case "settings": ui.OpenSettings("audio"); break;
            case "controls": ui.OpenControls(); break;
            case "pad": ui.OpenPad(); break;
            case "pack": ui.OpenPack(); break;
            case "character": ui.OpenCharacter(); break;
            case "skills": ui.OpenSkills(); break;
            case "tree": ui.OpenTree(); break;
            case "attacks": ui.OpenAttacks(null); break;
            case "macros": ui.OpenMacros(); break;
            case "spellcraft": ui.OpenSpellcraft(null); break;
            case "journal": ui.OpenJournal(); break;
            case "map": ui.OpenMap(); break;
            case "shop": ui.OpenShop("buy"); break;
            case "smith": ui.OpenSmith(); break;
            case "craft": ui.OpenCraft("gear"); break;
            case "stash": ui.OpenStash(); break;
            case "multiplayer": ui.OpenMultiplayer(); break;
            case "mods": ui.OpenMods(); break;
            case "title": if (Player.Instance == null) ui.ShowTitle(); break;
            case "create": ui.ShowCreate(); break;
        }
        await Secs(.4f);
        var vp = GetViewport();
        Say($"padnav {menu}: open '{ui.PanelName}' focus {Desc(vp.GuiGetFocusOwner())}");
        var seen = new HashSet<string>();   // distinct focus targets (a custom view counts each cursor position)
        var steps = new (string n, JoyButton b)[] { ("down", JoyButton.DpadDown), ("down", JoyButton.DpadDown), ("right", JoyButton.DpadRight), ("down", JoyButton.DpadDown), ("up", JoyButton.DpadUp) };
        foreach (var (n, b) in steps)
        {
            await PadBtn(b); var f = vp.GuiGetFocusOwner(); if (f != null) seen.Add(f.GetInstanceId() + Desc(f));
            Say($"padnav {menu}: {n} -> {Desc(f)}");
        }
        await PadStick(JoyAxis.LeftY, 1f, .7f);   // held stick: repeats
        { var f = vp.GuiGetFocusOwner(); if (f != null) seen.Add(f.GetInstanceId() + Desc(f)); Say($"padnav {menu}: stick-down held -> {Desc(f)}"); }
        await PadBtn(JoyButton.DpadUp);
        string before = ui.PanelName; var foc = vp.GuiGetFocusOwner();
        bool pressed = false; string what = Desc(foc);
        Action onP = () => pressed = true; Action<bool> onS = _ => pressed = true;
        if (foc is BaseButton bb) bb.Pressed += onP; else if (foc is ItemSlot isl) isl.Clicked += onS;
        bool skipA = menu == "title" || (foc is Button qb && (qb.Text.Contains("Quit") || qb.Text.Contains("Main menu") || qb.Text.Contains("Log out")));
        if (foc is MapView) skipA = true;   // A on the map fast-travels (the tour stays put)
        int tree0 = Game.D.Tree.Count;
        if (!skipA) await PadBtn(JoyButton.A); else Say($"padnav {menu}: A skipped on {what} (would leave the test)");
        if (foc is TreeView) pressed = Game.D.Tree.Count != tree0 || Game.D.SkillPts == 0;
        if (foc is OptionButton ob && GodotObject.IsInstanceValid(ob))
        {   // the list popup: down + A picks the next entry
            await Frames(3); bool open = ob.GetPopup().Visible; int sel0 = ob.Selected;
            await PadBtn(JoyButton.DpadDown); await PadBtn(JoyButton.A); await Frames(3);
            Say($"padnav {menu}: OptionButton popup open={open} selected {sel0}->{(GodotObject.IsInstanceValid(ob) ? ob.Selected : -9)} popupNow={(GodotObject.IsInstanceValid(ob) && ob.GetPopup().Visible)}");
        }
        if (foc != null && GodotObject.IsInstanceValid(foc)) { if (foc is BaseButton bb2) bb2.Pressed -= onP; else if (foc is ItemSlot is2) is2.Clicked -= onS; }
        await Frames(4);
        Say($"padnav {menu}: A on {what} -> pressed={pressed} panel '{before}'->'{ui.PanelName}' focus {Desc(vp.GuiGetFocusOwner())}");
        { string t0 = ui.PadTabState; if (t0 != "none" || menu == "settings") { await PadBtn(JoyButton.RightShoulder); await Frames(6); Say($"padnav {menu}: RB tabs {t0} -> {ui.PadTabState} panel '{ui.PanelName}' focus {Desc(vp.GuiGetFocusOwner())}"); } }
        Say($"padnav {menu}: RESULT {(seen.Count >= 2 ? "MOVES" : seen.Count == 1 ? "STUCK" : "NOFOCUS")} ({seen.Count} distinct) A={(skipA ? "skipped" : pressed ? "ok" : "no")}");
        await Secs(.2f);
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
