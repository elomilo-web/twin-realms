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

    void CamAt(Vector3 pos, Vector3 look) { _cam.GlobalPosition = pos; _cam.LookAt(look, Vector3.Up); _cam.MakeCurrent(); }
    void CamBack() { P.Rig?.Camera.MakeCurrent(); }
    void SetHud(bool on) { var h = GetNodeOrNull<CanvasLayer>("../Hud"); if (h != null) h.Visible = on; }
    void Sky(float tod, int realm) { World.TimeOfDay = tod; World.ForceRealm(realm); }

    /// <summary>Scene shot from a camera placed relative to a ground point.</summary>
    async Task Scene(string name, float x, float z, Vector3 off, float lookUp = 2f, float tod = 0.35f, int realm = 0)
    {
        Sky(tod, realm); var g = World.OnGround(x, z); P.Teleport(g + new Vector3(0, 0.5f, 0), 0f);
        var cp = g + off; cp.Y = Mathf.Max(cp.Y, World.GroundAt(cp.X, cp.Z) + 2f); CamAt(cp, g + new Vector3(0, lookUp, 0));
        await Wait(0.8f); await Shot(name); Perf(name);
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
        _m["engine_version"] = Engine.GetVersionInfo()["string"]; _m["viewport"] = GetViewport().GetVisibleRect().Size.ToString();
        var errors = new Godot.Collections.Array();
        async Task Safe(string n, Func<Task> f) { try { await f(); } catch (Exception e) { errors.Add(n + ": " + e.Message); GD.PrintErr("[OBSERVE] " + n + " failed: " + e.Message); } }

        SetHud(false);
        await Safe("scenes", async () =>
        {
            await Scene("w01_village_day", 0, 14, new Vector3(0, 6, 22), 3, 0.4f);
            await Scene("w02_village_dusk", 0, 14, new Vector3(0, 6, 22), 3, 0.76f);
            await Scene("w03_village_night", 0, 14, new Vector3(0, 6, 22), 3, 0.95f);
            await Scene("w04_plaza_close", -4, 6, new Vector3(5, 2, 7), 1.5f, 0.45f);
            await Scene("w05_hills_vista", -60, 40, new Vector3(0, 10, 35), 6, 0.4f);
            await Scene("w06_forest", -45, -40, new Vector3(5, 3, 12), 3, 0.42f);
            var ap = World.GetPoi("arena")!; await Scene("w07_arena_outside", ap.X, ap.Z, new Vector3(-55, 22, 0), 5, 0.4f);
            var bp = World.GetPoi("barrow")!; await Scene("w08_dungeon_mouth", bp.Door.X, bp.Door.Z, new Vector3(-8, 4, -2), 4, 0.4f);
            await Scene("w09_shadow_village", 0, 14, new Vector3(0, 6, 22), 3, 0.4f, 1);
            await Scene("w10_shadow_wilds", -60, 40, new Vector3(0, 10, 35), 6, 0.5f, 1);
            // gatherables: frame the berry bush nearest the village plus whatever grows around it
            Node3D? gb = null; float gbd = 1e9f;
            foreach (var n in GetTree().GetNodesInGroup("gather")) if (n is Gatherable g && g.Def.Shape == "berry") { float d = g.GlobalPosition.Length(); if (d < gbd) { gbd = d; gb = g; } }
            if (gb != null) { var gp = gb.GlobalPosition; await Scene("w11_gatherables", gp.X, gp.Z, new Vector3(3.5f, 2.2f, 4.5f), 0.6f, 0.4f); }
            Node3D? ore = null; float od = 1e9f;
            foreach (var n in GetTree().GetNodesInGroup("gather")) if (n is Gatherable g && (g.Def.Shape == "ore" || g.Def.Shape == "crystal")) { float d = g.GlobalPosition.Length(); if (d < od) { od = d; ore = g; } }
            if (ore != null) { var op = ore.GlobalPosition; await Scene("w12_ore", op.X, op.Z, new Vector3(3f, 2.2f, 3.5f), 0.6f, 0.4f); }
            Sky(0.4f, 0);
        });
        await Safe("dungeon", async () =>
        {
            Dungeon.Enter("barrow", 1); await Wait(1.2f); var pp = P.GlobalPosition;
            CamAt(pp + new Vector3(0, 3.5f, 6f), pp + new Vector3(0, 1.5f, 0)); await Wait(0.6f); await Shot("d01_dungeon_start"); Perf("dungeon");
            Dungeon.Leave(false); await Wait(1f);
        });
        CamBack(); SetHud(true);

        // HUD in the village with a live fight going on
        await Safe("hud", async () =>
        {
            P.Teleport(World.OnGround(0, 12, 0.5f), 0f); Game.D.SkillPts = 2; Game.D.AttrPts = 2; var q = Quests.ById("wolves"); if (q != null && Game.Q("wolves").S == 0) Quests.Accept(q);
            await Wait(1f); await Shot("h01_hud"); Perf("hud");
        });

        // menus
        var ui = new Godot.Collections.Array();
        await Safe("menus", async () =>
        {
            for (int i = 0; i < 20; i++) Game.D.Inv.Add(Items.Roll(1 + i / 3, i / 6f));
            var menus = new (string, Action)[] { ("pack", () => Ui.I!.OpenPack()), ("character", () => Ui.I!.OpenCharacter()), ("journal", () => Ui.I!.OpenJournal()), ("tree", () => Ui.I!.OpenTree()), ("map", () => Ui.I!.OpenMap()), ("pause", () => Ui.I!.OpenPause()), ("settings", () => Ui.I!.OpenSettings()) };
            foreach (var (name, open) in menus) { open(); await Wait(0.4f); await Shot("m_" + name); AuditUi(name, ui); Ui.I!.Close(); await Wait(0.1f); }
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
            P.Mp = Game.D.MaxMp();
            await Strip("spell_fire", async () => { Press("spell1", true); await Wait(0.05f); Press("spell1", false); await Wait(0.5f); });
            await Strip("spell_nova", async () => { P.Mp = Game.D.MaxMp(); Press("spell2", true); await Wait(0.05f); Press("spell2", false); await Wait(0.6f); });
        });
        CamBack(); SetHud(true);

        await Safe("hitboxes", HitboxAudit);
        _m["errors"] = errors;
        Finish("ok");
    }

    void Finish(string status)
    {
        _m["status"] = status;
        using var f = Godot.FileAccess.Open(_dir + "metrics.json", Godot.FileAccess.ModeFlags.Write); f?.StoreString(Json.Stringify(_m, "  "));
        GD.Print("[OBSERVE] done: " + status); GetTree().Quit(0);
    }
}
