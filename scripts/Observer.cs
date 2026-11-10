using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace TwinRealms;

/// <summary>
/// Look-and-feel observer. Run:  godot --path . -- --observe
/// Captures beauty shots (day/night/realms/places), every menu, animation strips (attack, block, roll, cast, walk),
/// and writes observe_out/metrics.json: performance, UI layout problems, hitbox vs visual mismatches.
/// The /improve command reads this to decide what to fix next.
/// </summary>
public partial class Observer : Node
{
    public static bool Wanted() { foreach (var a in OS.GetCmdlineUserArgs()) if (a == "--observe") return true; return false; }

    string _dir = ""; Camera3D _cam = null!; readonly Godot.Collections.Dictionary _m = new();
    static Player P => Player.Instance!;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _dir = ProjectSettings.GlobalizePath("res://") + "observe_out/"; DirAccess.MakeDirRecursiveAbsolute(_dir);
        foreach (var f in DirAccess.GetFilesAt(_dir)) DirAccess.RemoveAbsolute(_dir + f);
        _ = Run();
    }

    async Task Wait(float s) { await ToSignal(GetTree().CreateTimer(s, true), "timeout"); }
    async Task<Image> Grab() { await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); return GetViewport().GetTexture().GetImage(); }
    async Task Shot(string name) { await Wait(0.35f); (await Grab()).SavePng(_dir + name + ".png"); }
    void Perf(string tag)
    {
        _m[tag + "_fps"] = Engine.GetFramesPerSecond();
        _m[tag + "_drawcalls"] = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        _m[tag + "_objects"] = Performance.GetMonitor(Performance.Monitor.ObjectCount);
        _m[tag + "_nodes"] = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
    }

    Enemy? Nearest(string type) { Enemy? best = null; float bd = 1e9f; foreach (var n in GetTree().GetNodesInGroup("enemies")) if (n is Enemy e && e.Type == type && !e.Dead) { float d = e.GlobalPosition.DistanceTo(P.GlobalPosition); if (d < bd) { bd = d; best = e; } } return best; }
    void CamAt(Vector3 pos, Vector3 look) { _cam.GlobalPosition = pos; _cam.LookAt(look, Vector3.Up); _cam.MakeCurrent(); }
    void CamBack() { P.Rig?.Camera.MakeCurrent(); }
    void SetHud(bool on) { var h = GetNodeOrNull<CanvasLayer>("../Hud"); if (h != null) h.Visible = on; }
    void Sky(float tod, int realm) { World.TimeOfDay = tod; World.ForceRealm(realm); }

    /// <summary>Scene shot from a camera placed relative to a ground point.</summary>
    async Task Scene(string name, float x, float z, Vector3 off, float lookUp = 2f, float tod = 0.35f, int realm = 0)
    {
        Sky(tod, realm); var g = World.OnGround(x, z); P.Teleport(g + new Vector3(0, 0.5f, 0), 0f);
        var cp = g + off; cp.Y = Mathf.Max(cp.Y, World.GroundAt(cp.X, cp.Z) + 2f); CamAt(cp, g + new Vector3(0, lookUp, 0));
        await Wait(0.8f); Ui.ClearBanner(); await Shot(name); Perf(name);
    }

    // ------------------------------------------------------------------ animation strips
    async Task Strip(string name, Func<Task> action, int frames = 8, float dt = 0.07f)
    {
        var imgs = new List<Image>(); var pp = P.GlobalPosition;
        CamAt(pp + new Vector3(3.2f, 1.7f, 2.2f), pp + new Vector3(0, 1.1f, 0));
        var task = action();
        for (int i = 0; i < frames; i++) { await Wait(dt); var im = await Grab(); im.Resize(360, 202); imgs.Add(im); }
        await task;
        var sheet = Image.CreateEmpty(360 * 4, 202 * ((frames + 3) / 4), false, Image.Format.Rgb8);
        for (int i = 0; i < imgs.Count; i++) sheet.BlitRect(imgs[i], new Rect2I(0, 0, 360, 202), new Vector2I((i % 4) * 360, (i / 4) * 202));
        sheet.SavePng(_dir + "anim_" + name + ".png");
    }
    static void Mouse(MouseButton b, bool down) => Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = b, Pressed = down, Position = new Vector2(640, 360) });
    static void Press(string a, bool on) { if (on) Input.ActionPress(a); else Input.ActionRelease(a); }

    // ------------------------------------------------------------------ UI layout audit
    void AuditUi(string menu, Godot.Collections.Array issues)
    {
        if (Ui.I == null) return; var vp = GetViewport().GetVisibleRect(); int small = 0;
        void Walk(Node n)
        {
            foreach (var c in n.GetChildren())
            {
                if (c is Control ctl && ctl.IsVisibleInTree())
                {
                    var r = ctl.GetGlobalRect();
                    if (r.Size.X > 1 && (r.Position.X < -1 || r.Position.Y < -1 || r.End.X > vp.Size.X + 1 || r.End.Y > vp.Size.Y + 1) && !InScroll(ctl))
                        issues.Add($"{menu}: {ctl.GetType().Name} '{(ctl is Label l0 ? l0.Text : ctl.Name)}' leaves the screen {r}");
                    if (ctl is Label l && l.Text.Length > 0 && l.AutowrapMode == TextServer.AutowrapMode.Off && l.GetMinimumSize().X > l.Size.X + 2 && l.Size.X > 0 && l.ClipText == false && l.GetMinimumSize().X > 0 && l.Size.X < l.GetMinimumSize().X - 2)
                        issues.Add($"{menu}: label text is wider than its box: '{l.Text}'");
                    if (ctl is Label l2 && l2.GetThemeFontSize("font_size") < 13 && l2.Text.Length > 0) small++;
                }
                Walk(c);
            }
        }
        // rows inside a ScrollContainer are clipped by it, so being past the window edge is fine there
        static bool InScroll(Node n) { for (var p = n.GetParent(); p != null; p = p.GetParent()) if (p is ScrollContainer) return true; return false; }
        Walk(Ui.I);
        if (small > 0) issues.Add($"{menu}: {small} labels use a font smaller than 13px");
    }

    // ------------------------------------------------------------------ hitbox audit
    async Task HitboxAudit()
    {
        var rows = new Godot.Collections.Array(); Sky(0.35f, 0);
        using var doc = JsonUtil.Load("res://data/enemies.json");
        var spot = World.OnGround(0, -60, 1f);
        foreach (var prop in doc.RootElement.GetProperty("enemies").EnumerateObject())
        {
            var e = new Enemy { Type = prop.Name, Level = 1, AnyRealm = true, NoLeash = true, Position = spot }; Game.Root!.AddChild(e); await Wait(0.2f);
            float visTop = 0, visW = 0; float cols = 0, capH = 0, capR = 0;
            void Walk(Node n, Transform3D parent)
            {
                foreach (var c in n.GetChildren())
                {
                    var t = parent; if (c is Node3D n3) t = parent * n3.Transform;
                    if (c is MeshInstance3D mi) { var ab = t * mi.GetAabb(); visTop = Mathf.Max(visTop, ab.End.Y); visW = Mathf.Max(visW, Mathf.Max(ab.Size.X, ab.Size.Z)); }
                    if (c is CollisionShape3D cs && cs.Shape is CapsuleShape3D cap) { capH = cap.Height; capR = cap.Radius; cols++; }
                    Walk(c, t);
                }
            }
            Walk(e, Transform3D.Identity);
            var row = new Godot.Collections.Dictionary { ["type"] = prop.Name, ["visual_height"] = visTop, ["visual_width"] = visW, ["hurtbox_height"] = capH, ["hurtbox_radius"] = capR, ["reach"] = e.Def.Reach, ["aggro"] = e.Def.Aggro };
            if (capH > 0 && Mathf.Abs(visTop - capH) > 0.35f * Mathf.Max(visTop, capH)) row["WARNING"] = "hurtbox height differs from the visual by more than 35%";
            if (capR > 0 && visW > 0 && capR * 2 < visW * 0.6f) row["WARNING2"] = "hurtbox is much narrower than the visual: attacks will seem to pass through";
            rows.Add(row); e.QueueFree();
        }
        _m["hitboxes"] = rows;
    }

    // ------------------------------------------------------------------ run
    async Task Run()
    {
        await Wait(3f); if (Player.Instance == null) { Finish("no player"); return; }
        _cam = new Camera3D { Fov = 70, Far = 700 }; AddChild(_cam);
        foreach (var a in OS.GetCmdlineUserArgs()) if (a == "--quick") { await Quick(); return; }
        _m["engine_version"] = Engine.GetVersionInfo()["string"]; _m["viewport"] = GetViewport().GetVisibleRect().Size.ToString();
        var errors = new Godot.Collections.Array();
        async Task Safe(string n, Func<Task> f) { try { await f(); } catch (Exception e) { errors.Add(n + ": " + e.Message); GD.PrintErr("[OBSERVE] " + n + " failed: " + e.Message); } }

        SetHud(false);
        await Safe("scenes", async () =>
        {
            // data/observe_scenes.json: the improvement loop edits this file to look at new places, times and subjects
            using var sd = JsonUtil.Load("res://data/observe_scenes.json");
            foreach (var sc in sd.RootElement.GetProperty("scenes").EnumerateArray())
            {
                string name = JsonUtil.S(sc, "name", "scene"); float x = JsonUtil.F(sc, "x"), z = JsonUtil.F(sc, "z");
                if (sc.TryGetProperty("poi", out var pj) && World.GetPoi(pj.GetString() ?? "") is Poi po) { x = po.Door != Vector3.Zero ? po.Door.X : po.X; z = po.Door != Vector3.Zero ? po.Door.Z : po.Z; }
                if (sc.TryGetProperty("npc", out var nj)) foreach (var n in GetTree().GetNodesInGroup("interact")) if (n is Npc c && c.Id == nj.GetString()) { x = c.GlobalPosition.X; z = c.GlobalPosition.Z; }
                if (sc.TryGetProperty("enemy", out var ej)) { var e = Nearest(ej.GetString() ?? ""); if (e != null) { x = e.GlobalPosition.X; z = e.GlobalPosition.Z; } }
                var off = new Vector3(0, 5, 12); if (sc.TryGetProperty("off", out var oj)) { var arr = new List<float>(); foreach (var v in oj.EnumerateArray()) arr.Add(v.GetSingle()); if (arr.Count == 3) off = new Vector3(arr[0], arr[1], arr[2]); }
                _cam.Fov = JsonUtil.F(sc, "fov", 70);
                await Scene(name, x, z, off, JsonUtil.F(sc, "look", 2f), JsonUtil.F(sc, "tod", 0.4f), (int)JsonUtil.F(sc, "realm", 0));
            }
            _cam.Fov = 70; Sky(0.4f, 0);
        });
        await Safe("dungeon", async () =>
        {
            Dungeon.Enter("barrow", 1); await Wait(1.2f); var pp = P.GlobalPosition;
            CamAt(pp + new Vector3(0, 3.5f, 6f), pp + new Vector3(0, 1.5f, 0)); await Wait(0.6f); await Shot("d01_dungeon_start"); Perf("dungeon");
            CamAt(pp + new Vector3(3f, 4.8f, 9f), pp + new Vector3(-2, 1.5f, -6)); await Wait(0.3f); await Shot("d02_dungeon_room");
            // close-up of an upgraded dungeon monster (name plate shows its upgrades and gear tier)
            Enemy? dm = null; float best = 1e9f; foreach (var n in GetTree().GetNodesInGroup("enemies")) if (n is Enemy e && e.FromDungeon && !e.Def.Boss && e.GlobalPosition.DistanceTo(pp) < best) { best = e.GlobalPosition.DistanceTo(pp); dm = e; }
            if (dm != null) { dm.ProcessMode = ProcessModeEnum.Disabled; var ep = dm.GlobalPosition; CamAt(ep + new Vector3(2.2f, 2.6f, 4.2f), ep + new Vector3(0, 1.6f, 0)); await Wait(0.4f); await Shot("d05_dungeon_mob"); dm.ProcessMode = ProcessModeEnum.Inherit; }
            Dungeon.Leave(false); await Wait(0.6f); Dungeon.Enter("depths", 1); await Wait(1.2f); pp = P.GlobalPosition;
            CamAt(pp + new Vector3(2f, 4f, 8f), pp + new Vector3(0, 1.5f, -4)); await Wait(0.4f); await Shot("d03_depths");
            Dungeon.Leave(false); await Wait(0.6f); Dungeon.Enter("warrens", 1); await Wait(1.2f); pp = P.GlobalPosition;
            CamAt(pp + new Vector3(-2f, 4f, 8f), pp + new Vector3(0, 1.5f, -4)); await Wait(0.4f); await Shot("d04_warrens");
            Dungeon.Leave(false); await Wait(1f);
        });
        CamBack(); SetHud(true);

        // HUD in the village with a live fight going on
        await Safe("hud", async () =>
        {
            P.Teleport(World.OnGround(0, 12, 0.5f), 0f); Game.D.SkillPts = 2; Game.D.AttrPts = 2; var q = Quests.ById("wolves"); if (q != null && Game.Q("wolves").S == 0) Quests.Accept(q);
            await Wait(1f); await Shot("h01_hud"); Perf("hud");
            float keep = P.Hp; P.Hp = Game.D.MaxHp() * 0.15f; await Wait(0.5f); await Shot("h02_low_hp"); P.Hp = keep;
        });

        // menus
        var ui = new Godot.Collections.Array();
        await Safe("menus", async () =>
        {
            for (int i = 0; i < 20; i++) Game.D.Inv.Add(Items.Roll(1 + i / 3, i / 6f));
            var menus = new (string, Action)[] { ("pack", () => Ui.I!.OpenPack()), ("character", () => Ui.I!.OpenCharacter()), ("journal", () => Ui.I!.OpenJournal()), ("tree", () => Ui.I!.OpenTree()), ("map", () => Ui.I!.OpenMap()), ("pause", () => Ui.I!.OpenPause()), ("settings", () => Ui.I!.OpenSettings()), ("controls", () => Ui.I!.OpenControls()),("skills", () => { LearnAll(); Ui.I!.OpenSkills(); }), ("stash", () => { for (int i = 0; i < 12; i++) Game.D.Stash.Add(Items.Roll(2 + i / 2, i / 4f)); Ui.I!.OpenStash(); }), ("multiplayer", () => Ui.I!.OpenMultiplayer()), ("partychest", () => Ui.I!.OpenPartyChest()), ("totals", () => Ui.I!.OpenTotals()) };
            foreach (var (name, open) in menus) { open(); await Wait(0.4f); await Shot("m_" + name); AuditUi(name, ui); Ui.I!.Close(); await Wait(0.1f); }
            Ui.I!.ShowTitle(); await Wait(0.4f); await Shot("m_title"); AuditUi("title", ui);
            Ui.I.ShowCreate(); await Wait(0.4f); await Shot("m_create"); AuditUi("create", ui); Ui.InGame = true; Ui.I.Close(); await Wait(0.1f);
            Game.D.SkillPts = 30; Ui.I.OpenTree(); await Wait(0.4f); await Shot("m_tree_start"); Ui.I.Close(); await Wait(0.1f);
            // pack with the tooltip open on an item of the same slot as something worn (shows comparison arrows)
            Ui.I!.OpenPack(); await Wait(0.4f);
            ItemSlot? pick = null; void Find(Node n) { foreach (var c in n.GetChildren()) { if (pick == null && c is ItemSlot s && s.Item != null && !s.Wide && s.Item.Slot == "weapon") pick = s; Find(c); } } Find(Ui.I);
            pick?.SimulateHover(); await Wait(0.2f); await Shot("m_pack_tooltip"); Ui.I.Close(); await Wait(0.1f);
            foreach (var n in GetTree().GetNodesInGroup("interact")) if (n is Npc c && (c.Role == "shop" || c.Role == "quest" || c.Role == "smith")) { Ui.OpenDialogue(c); await Wait(0.4f); await Shot("m_npc_" + c.Role); AuditUi("npc_" + c.Role, ui); Ui.I!.Close(); await Wait(0.1f); }
        });
        _m["ui_issues"] = ui;

        // animations
        await Safe("anims", async () =>
        {
            P.Teleport(World.OnGround(0, 18, 0.5f), 0f); Sky(0.4f, 0); SetHud(false);
            await Strip("light_attack", async () => { Mouse(MouseButton.Left, true); await Wait(0.08f); Mouse(MouseButton.Left, false); await Wait(0.3f); });
            await Wait(0.8f);
            await Strip("combo_3", async () => { for (int i = 0; i < 3; i++) { Mouse(MouseButton.Left, true); await Wait(0.06f); Mouse(MouseButton.Left, false); await Wait(0.18f); } }, 12, 0.07f);
            await Wait(0.8f);
            await Strip("power_attack", async () => { Mouse(MouseButton.Left, true); await Wait(0.7f); Mouse(MouseButton.Left, false); await Wait(0.2f); }, 12, 0.08f);
            await Wait(0.8f);
            await Strip("block", async () => { Mouse(MouseButton.Right, true); await Wait(0.5f); Mouse(MouseButton.Right, false); });
            await Strip("roll", async () => { Press(Inputs.Forward, true); Press(Inputs.Roll, true); await Wait(0.1f); Press(Inputs.Roll, false); await Wait(0.45f); Press(Inputs.Forward, false); });
            await Strip("walk_sprint", async () => { Press(Inputs.Forward, true); Press(Inputs.Sprint, true); await Wait(0.6f); Press(Inputs.Forward, false); Press(Inputs.Sprint, false); }, 8, 0.1f);
            await Strip("idle", async () => { await Wait(0.8f); }, 8, 0.1f);
            // impact read: a power attack landing on a brute standing in front of the hero (hit-stop, flash, recoil, sparks)
            {
                P.Teleport(World.OnGround(0, 18, 0.5f), 0f); var tp = P.GlobalPosition + new Vector3(0, 0, -2.4f);
                var dummy = new Enemy { Type = "brute", Level = 1, AnyRealm = true, NoLeash = true, Position = World.OnGround(tp.X, tp.Z, 0.2f) }; Game.Root!.AddChild(dummy); await Wait(0.3f); dummy.Stun(4f); // a stunned target stands still for the shot
                // power hold 0.4 s, then the blow lands ~0.4 s after release; 16 frames x 0.06 s covers wind-up, impact and recovery
                await Strip("hit_enemy", async () => { Mouse(MouseButton.Left, true); await Wait(0.4f); Mouse(MouseButton.Left, false); await Wait(0.6f); }, 16, 0.06f);
                dummy.QueueFree(); P.Hp = Game.D.MaxHp(); await Wait(0.3f);
            }
            // death: a goblin falls, releases motes and dissolves into the ground
            {
                P.Teleport(World.OnGround(0, 18, 0.5f), 0f); var tp = P.GlobalPosition + new Vector3(0, 0, -3f);
                var dummy = new Enemy { Type = "goblin", Level = 1, AnyRealm = true, NoLeash = true, RespawnTime = 0, Position = World.OnGround(tp.X, tp.Z, 0.2f) }; Game.Root!.AddChild(dummy); await Wait(0.3f); dummy.Stun(4f);
                await Strip("death_dissolve", async () => { dummy.TakeHit(new HitInfo { Damage = 99999, From = P.GlobalPosition, Knock = 2f, IsPlayer = true, Attacker = P }); await Wait(3f); }, 12, 0.25f);
                if (IsInstanceValid(dummy)) dummy.QueueFree(); await Wait(0.3f);
            }
            LearnAll(); Game.D.Bar = new[] { "fire", "nova", "heal", "zap", "meteor", "whirlwind" }; Game.Notify();
            P.Mp = Game.D.MaxMp();
            await Strip("spell_fire", async () => { Press("spell1", true); await Wait(0.05f); Press("spell1", false); await Wait(0.5f); });
            await Strip("spell_nova", async () => { P.Mp = Game.D.MaxMp(); Press("spell2", true); await Wait(0.05f); Press("spell2", false); await Wait(0.6f); });
            // every weapon type: its own combo (3 clicks) in one strip each, then a power attack strip for the heavy ones
            var keepW = Game.D.Eq["weapon"]; var keepS = Game.D.Eq.GetValueOrDefault("shield");
            foreach (var wt in new[] { "greatsword", "axe", "mace", "dagger", "spear", "bow", "staff" })
            {
                Game.D.Eq["weapon"] = new Item { Name = "Test " + wt, Slot = "weapon", Wt = wt, Dmg = 5, Rar = 2, Two = Weapons.Get(wt).Hands > 1 }; Game.Notify(); await Wait(0.3f);
                P.Teleport(World.OnGround(0, 18, 0.5f), 0f); await Wait(0.3f);
                int n = Weapons.Get(wt).Combo.Count;
                await Strip("weapon_" + wt, async () => { for (int i = 0; i < n; i++) { Mouse(MouseButton.Left, true); await Wait(0.06f); Mouse(MouseButton.Left, false); await Wait(Weapons.Get(wt).Combo[i].Dur * .55f); } }, 12, 0.09f);
                if (wt is "greatsword" or "mace" or "spear") await Strip("power_" + wt, async () => { Mouse(MouseButton.Left, true); await Wait(0.5f); Mouse(MouseButton.Left, false); await Wait(0.9f); }, 12, 0.1f);
                await Wait(0.6f);
            }
            Game.D.Eq["weapon"] = keepW; Game.D.Eq["shield"] = keepS; Game.Notify(); await Wait(0.3f);
            await Strip("spell_heal", async () => { P.Mp = Game.D.MaxMp(); Press("spell3", true); await Wait(0.05f); Press("spell3", false); await Wait(0.6f); });
            // chain lightning needs targets: two stunned goblins in front of the hero
            {
                var pp = P.GlobalPosition; var foes = new List<Enemy>();
                foreach (var dx in new[] { -1.5f, 2f }) { var e = new Enemy { Type = "goblin", Level = 30, AnyRealm = true, NoLeash = true, Position = World.OnGround(pp.X + dx, pp.Z - 6f - Mathf.Abs(dx), 0.2f) }; Game.Root!.AddChild(e); foes.Add(e); }
                await Wait(0.3f); foreach (var e in foes) e.Stun(5f);
                await Strip("spell_zap", async () => { P.Mp = Game.D.MaxMp(); Press("spell4", true); await Wait(0.05f); Press("spell4", false); await Wait(0.5f); }, 8, 0.05f);
                foreach (var e in foes) if (IsInstanceValid(e)) { e.Stun(20f); }
                await Strip("skill_meteor", async () => { P.Mp = Game.D.MaxMp(); Targeting.Set(foes[0]); Press("spell5", true); await Wait(0.05f); Press("spell5", false); await Wait(1.6f); Targeting.Clear(); }, 12, 0.14f);
                foreach (var (id, key) in new[] { ("hex", "spell5"), ("smite", "spell5"), ("thornvolley", "spell5"), ("leap", "spell5") })
                {
                    foreach (var e in foes) if (IsInstanceValid(e)) { e.Hp = e.MaxHp; e.Stun(20f); }
                    P.Teleport(World.OnGround(pp.X, pp.Z, 0.5f), 0f); Game.D.Bar[4] = id; await Wait(0.3f);
                    var tgt = foes.Find(e => IsInstanceValid(e) && !e.Dead);
                    await Strip("skill_" + id, async () => { P.Mp = Game.D.MaxMp(); P.St = Game.D.MaxSt(); if (tgt != null) Targeting.Set(tgt); Press(key, true); await Wait(0.05f); Press(key, false); await Wait(1.3f); Targeting.Clear(); }, 8, 0.15f);
                }
                P.Teleport(World.OnGround(pp.X, pp.Z, 0.5f), 0f); await Wait(0.3f);
                await Strip("skill_whirlwind", async () => { P.St = Game.D.MaxSt(); Press("spell6", true); await Wait(0.05f); Press("spell6", false); await Wait(1.3f); }, 8, 0.15f);
                foreach (var e in foes) if (IsInstanceValid(e)) e.QueueFree();
            }
        });
        CamBack(); SetHud(true);

        // gear on the body: epic items in every slot, front and back close-ups, then a plate set with a greatsword
        await Safe("gear", async () =>
        {
            SetHud(false); Sky(0.45f, 0); P.Teleport(World.OnGround(0, 17, 0.5f), 0f); var keep = new Dictionary<string, Item?>(Game.D.Eq);
            Item I(string slot, string name, int rar, string? wt = null) => new() { Name = name, Slot = slot, Rar = rar, Wt = wt, Def = 5, Dmg = wt != null ? 8 : 0 };
            void Set(params Item[] items) { foreach (var it in items) Game.D.Eq[it.Slot] = it; Game.Notify(); }
            Set(I("weapon", "Runed Longsword", 3, "sword"), I("shield", "Runed Kite Shield", 3), I("head", "Runed Helm", 3), I("armor", "Runed Brigandine", 3), I("hands", "Runed Gloves", 3), I("feet", "Runed Boots", 3), I("trinket", "Runed Amulet", 3));
            await Wait(0.6f); var pp = P.GlobalPosition;
            CamAt(pp + new Vector3(.6f, 1.6f, -2.8f), pp + new Vector3(0, 1.15f, 0)); _cam.Fov = 45; await Wait(0.4f); await Shot("h03_gear_front");
            CamAt(pp + new Vector3(-1.8f, 1.7f, 2.4f), pp + new Vector3(0, 1.15f, 0)); await Wait(0.3f); await Shot("h04_gear_back");
            Set(I("weapon", "Mythic Executioner", 4, "greatsword"), I("head", "Mythic Greathelm", 4), I("armor", "Mythic Plate", 4), I("hands", "Mythic Gauntlets", 4), I("feet", "Mythic Greaves", 4));
            await Wait(0.6f); CamAt(pp + new Vector3(.9f, 1.6f, -2.9f), pp + new Vector3(0, 1.15f, 0)); await Wait(0.3f); await Shot("h05_plate_greatsword");
            Set(I("weapon", "Fine Glaive", 1, "spear"), I("head", "Fine Hood", 1), I("armor", "Fine Robe", 1), I("hands", "Fine Wraps", 1), I("feet", "Fine Treads", 1));
            await Wait(0.6f); CamAt(pp + new Vector3(.9f, 1.6f, -2.9f), pp + new Vector3(0, 1.15f, 0)); await Wait(0.3f); await Shot("h06_robe_spear");
            // elemental glow: a legendary fire sword and an epic frost greatsword
            { var fw = I("weapon", "Mythic Ember Longsword", 4, "sword"); fw.Elem = "fire"; var fa = I("armor", "Mythic Ember Plate", 4); fa.Elem = "fire"; Set(fw, fa, I("shield", "Runed Kite Shield", 3));
              await Wait(0.6f); CamAt(pp + new Vector3(.9f, 1.5f, -2.6f), pp + new Vector3(0, 1.1f, 0)); await Wait(0.3f); await Shot("h09_fire_legendary"); }
            { var gw = I("weapon", "Runed Frost Greatsword", 3, "greatsword"); gw.Elem = "frost"; Set(gw);
              await Wait(0.6f); CamAt(pp + new Vector3(-1.2f, 1.5f, -2.6f), pp + new Vector3(0, 1.1f, 0)); await Wait(0.3f); await Shot("h10_frost_epic"); }
            Set(I("weapon", "Fine Longbow", 2, "bow")); await Wait(0.5f); Mouse(MouseButton.Left, true); await Wait(0.45f); CamAt(pp + new Vector3(-2.6f, 1.6f, -1.2f), pp + new Vector3(0, 1.3f, 0)); await Shot("h08_bow_draw"); Mouse(MouseButton.Left, false); await Wait(0.8f);
            _cam.Fov = 70; foreach (var kv in keep) Game.D.Eq[kv.Key] = kv.Value; foreach (var s in new[] { "head", "hands", "feet" }) if (!keep.ContainsKey(s)) Game.D.Eq.Remove(s); Game.Notify();
            // lock-on: target the nearest wolf from behind the hero, with the HUD
            var wolf = Nearest("wolf"); if (wolf != null)
            {
                P.Teleport(World.OnGround(wolf.GlobalPosition.X + 6, wolf.GlobalPosition.Z + 6, .5f), 0f); CamBack(); SetHud(true); await Wait(0.5f);
                Targeting.Set(wolf); await Wait(1.2f); await Shot("h07_lockon"); Targeting.Clear(); SetHud(false);
            }
            CamBack();
            // night glow around the hero
            Sky(0.97f, 0); P.Teleport(World.OnGround(-30, 40, .5f), 0f); await Wait(1.5f); var gp = P.GlobalPosition;
            CamAt(gp + new Vector3(2.5f, 2.2f, 4f), gp + new Vector3(0, 1f, 0)); await Wait(0.4f); await Shot("w28_night_glow"); Sky(0.45f, 0); CamBack(); SetHud(true);
        });
        await Safe("hitboxes", HitboxAudit);
        _m["errors"] = errors;
        Finish("ok");
    }

    /// <summary>--observe --quick: only the character shots (models, gear, glow, a few animation strips). About a minute.</summary>
    async Task Quick()
    {
        SetHud(false);
        foreach (var (name, npc, enemy, off, look) in new (string, string, string, Vector3, float)[] {
            ("q01_plaza", "", "", new Vector3(5, 2, 7), 1.5f), ("q02_maren", "maren", "", new Vector3(1.5f, 1.6f, 3), 1.4f), ("q03_brynn", "brynn", "", new Vector3(1.2f, 1.4f, 2.8f), 1.3f),
            ("q04_bandit", "", "bandit", new Vector3(2, 1.5f, 3.5f), 1.1f), ("q05_brute", "", "brute", new Vector3(3, 2.2f, 5f), 1.6f) })
        {
            float x = -4, z = 6;
            if (npc != "") foreach (var n in GetTree().GetNodesInGroup("interact")) if (n is Npc c && c.Id == npc) { x = c.GlobalPosition.X; z = c.GlobalPosition.Z; }
            if (enemy != "") { var e = Nearest(enemy); if (e == null) continue; x = e.GlobalPosition.X; z = e.GlobalPosition.Z; e.ProcessMode = ProcessModeEnum.Disabled; }
            _cam.Fov = 50; await Scene(name, x, z, off, look, .45f);
        }
        _cam.Fov = 45; Sky(.45f, 0); P.Teleport(World.OnGround(0, 17, 0.5f), 0f); await Wait(.6f); var pp = P.GlobalPosition;
        Item I(string slot, string name, int rar, string? wt = null, string? el = null) => new() { Name = name, Slot = slot, Rar = rar, Wt = wt, Def = 5, Dmg = wt != null ? 8 : 0, Elem = el };
        void Set(params Item[] items) { foreach (var it in items) Game.D.Eq[it.Slot] = it; Game.Notify(); }
        async Task Front(string n) { CamAt(pp + new Vector3(.9f, 1.5f, -2.8f), pp + new Vector3(0, 1.05f, 0)); await Wait(.4f); await Shot(n); CamAt(pp + new Vector3(-1.6f, 1.6f, 2.2f), pp + new Vector3(0, 1.05f, 0)); await Wait(.3f); await Shot(n + "_back"); }
        await Front("q10_default");
        Set(I("weapon", "Fine Longsword", 1, "sword"), I("shield", "Fine Kite Shield", 1), I("armor", "Fine Brigandine", 1), I("hands", "Fine Gloves", 1), I("feet", "Fine Boots", 1), I("head", "Fine Hood", 1)); await Wait(.6f); await Front("q11_leather");
        Set(I("weapon", "Mythic Ember Executioner", 4, "greatsword", "fire"), I("armor", "Mythic Plate", 4, null, "fire"), I("hands", "Mythic Gauntlets", 4), I("feet", "Mythic Greaves", 4), I("head", "Mythic Greathelm", 4)); await Wait(.6f); await Front("q12_plate_fire");
        Set(I("weapon", "Runed Frost Spear", 3, "spear", "frost"), I("armor", "Runed Robe", 3, null, "frost"), I("head", "Runed Circlet", 3)); await Wait(.6f); await Front("q13_robe_frost");
        Set(I("weapon", "Fine Storm Wand", 2, "wand", "storm")); await Wait(.6f); await Front("q14_wand_storm");
        Set(I("weapon", "Runed Longsword", 3, "sword", "venom"), I("shield", "Runed Aegis", 3, null, "holy")); await Wait(.6f); await Front("q15_sword_venom");
        _cam.Fov = 70; P.Teleport(World.OnGround(0, 18, 0.5f), 0f); await Wait(.4f);
        await Strip("q_combo", async () => { for (int i = 0; i < 3; i++) { Mouse(MouseButton.Left, true); await Wait(0.06f); Mouse(MouseButton.Left, false); await Wait(0.2f); } }, 12, 0.07f);
        await Strip("q_walk", async () => { Press(Inputs.Forward, true); Press(Inputs.Sprint, true); await Wait(0.6f); Press(Inputs.Forward, false); Press(Inputs.Sprint, false); }, 8, 0.1f);
        await Strip("q_roll", async () => { Press(Inputs.Forward, true); Press(Inputs.Roll, true); await Wait(0.1f); Press(Inputs.Roll, false); await Wait(0.45f); Press(Inputs.Forward, false); });
        Finish("ok");
    }

    /// <summary>Learns every skill and support so menus and strips can show them.</summary>
    static void LearnAll()
    {
        foreach (var k in Skills.All) if (!Game.D.Skills.ContainsKey(k.Id)) Game.D.Skills[k.Id] = new SkillState { Rank = 3 };
        foreach (var s in Skills.AllSupports) if (!Game.D.Supports.Contains(s.Id)) Game.D.Supports.Add(s.Id);
        Game.D.Skills["fire"].Rank = 4; Skills.Socket("fire", 0, "multi"); Skills.Socket("fire", 1, "seek"); Game.D.RankPts = 5;
    }

    void Finish(string status)
    {
        _m["status"] = status;
        using var f = Godot.FileAccess.Open(_dir + "metrics.json", Godot.FileAccess.ModeFlags.Write); f?.StoreString(Json.Stringify(_m, "  "));
        GD.Print("[OBSERVE] done: " + status); GetTree().Quit(0);
    }
}
