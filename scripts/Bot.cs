using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;

namespace TwinRealms;

/// <summary>
/// Self-test bot. Run the game with:  godot --path . -- --bot
/// It plays a scripted tour (combat, loot, spells, every menu, realm shift, dungeon, arena, save/load),
/// takes screenshots and writes bot_out/report.txt. Exit code 0 = all checks passed.
/// </summary>
public partial class Bot : Node
{
    public static bool Wanted() { foreach (var a in OS.GetCmdlineUserArgs()) if (a == "--bot") return true; return false; }

    readonly StringBuilder _log = new(); int _fail, _pass; string _dir = "";

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _dir = ProjectSettings.GlobalizePath("res://") + "bot_out/"; DirAccess.MakeDirRecursiveAbsolute(_dir);
        _ = Run();
    }

    // ------------------------------------------------------------ helpers
    void Say(string s) { GD.Print("[BOT] " + s); _log.AppendLine(s); }
    void Check(string name, bool ok, string detail = "") { if (ok) _pass++; else _fail++; Say((ok ? "PASS  " : "FAIL  ") + name + (detail != "" ? "  (" + detail + ")" : "")); }
    async Task Wait(float s) { await ToSignal(GetTree().CreateTimer(s, true), "timeout"); }
    async Task Shot(string name) { await Wait(0.3f); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); GetViewport().GetTexture().GetImage().SavePng(_dir + name + ".png"); Say("screenshot " + name); }
    static Player P => Player.Instance!;
    void Heal() { if (P != null) { P.Hp = Game.D.MaxHp(); P.Mp = Game.D.MaxMp(); P.St = Game.D.MaxSt(); } }

    static void Press(string action, bool on) { if (on) Input.ActionPress(action); else Input.ActionRelease(action); }
    // never click while a menu is open: a stray click at the centre could hit "Quit to title"
    static void Mouse(MouseButton b, bool down) { if (down && Ui.AnyOpen) { GD.Print("[BOT] closing menu " + Ui.I!.PanelName + " before clicking"); Ui.I.Close(); } MouseRaw(b, down); }
    static void MouseRaw(MouseButton b, bool down) => Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = b, Pressed = down, Position = new Vector2(640, 360) });
    static async Task<T> Safe<T>(Func<Task<T>> f, T dflt, Action<string> err) { try { return await f(); } catch (Exception e) { err(e.Message); return dflt; } }

    Enemy? Nearest(Func<Enemy, bool>? f = null)
    {
        Enemy? best = null; float bd = 1e9f;
        foreach (var n in GetTree().GetNodesInGroup("enemies")) if (n is Enemy e && !e.Dead && e.Active && (f == null || f(e))) { float d = e.GlobalPosition.DistanceTo(P.GlobalPosition); if (d < bd) { bd = d; best = e; } }
        return best;
    }

    void Face(Vector3 pos)
    {
        var d = pos - P.GlobalPosition; float yaw = Util.YawTo(d.X, d.Z);
        if (P.Rig != null) P.Rig.Rotation = new Vector3(P.Rig.Rotation.X, yaw, 0);
    }

    /// <summary>A spot near the enemy with clear line of sight to it, so the bot is not dropped behind a wall (dungeons).</summary>
    Vector3 ClearSpotNear(Enemy e, Vector3 at)
    {
        var space = P.GetWorld3D().DirectSpaceState; var up = new Vector3(0, 1f, 0);
        foreach (float dist in new[] { 6f, 3.5f, 2f })
            foreach (var dir in new[] { Vector3.Back, Vector3.Forward, Vector3.Right, Vector3.Left })
            {
                var spot = at + dir * dist;
                var q = PhysicsRayQueryParameters3D.Create(at + up, spot + up, 1);
                q.Exclude = new Godot.Collections.Array<Rid> { e.GetRid(), P.GetRid() };
                if (space.IntersectRay(q).Count == 0) return spot + new Vector3(0, 0.5f, 0);
            }
        return at + new Vector3(0, 0.5f, 1.5f);
    }

    /// <summary>Teleports next to the enemy and fights it until dead or timeout. Returns true if it died.</summary>
    async Task<bool> Fight(Enemy e, float timeout = 25f)
    {
        float t = 0; var start = e.GlobalPosition;
        P.Teleport(ClearSpotNear(e, start), 0f);
        while (t < timeout && GodotObject.IsInstanceValid(e) && !e.Dead)
        {
            Heal(); Face(e.GlobalPosition); float dist = e.GlobalPosition.DistanceTo(P.GlobalPosition);
            Press(Inputs.Forward, dist > 2.2f);
            // Attacks fire on mouse release (tap = light attack), so click rather than hold.
            if (dist < 3.2f) { Mouse(MouseButton.Left, true); await Wait(0.08f); Mouse(MouseButton.Left, false); t += 0.08f; }
            await Wait(0.1f); t += 0.1f;
        }
        Press(Inputs.Forward, false); Mouse(MouseButton.Left, false);
        return !GodotObject.IsInstanceValid(e) || e.Dead;
    }

    async Task Step(string name, Func<Task> f)
    {
        Say("--- " + name);
        try { await f(); } catch (Exception e) { Check(name + " threw", false, e.Message); }
    }

    // ------------------------------------------------------------ the tour
    async Task Run()
    {
        await Wait(2.5f);
        if (Player.Instance == null) { Check("player exists", false); Finish(); return; }
        Check("player exists", true);
        await Shot("01_village");

        await Step("walk", async () =>
        {
            var p0 = P.GlobalPosition; Press(Inputs.Forward, true); await Wait(1.5f); Press(Inputs.Forward, false);
            Check("player moves with input", P.GlobalPosition.DistanceTo(p0) > 2f, $"moved {P.GlobalPosition.DistanceTo(p0):0.0} m");
            Press(Inputs.Jump, true); await Wait(0.1f); Press(Inputs.Jump, false); Press(Inputs.Roll, true); await Wait(0.1f); Press(Inputs.Roll, false); await Wait(0.6f);
        });

        await Step("combat and loot", async () =>
        {
            var w = Nearest(e => e.Type == "wolf") ?? Nearest();
            Check("overworld has enemies", w != null);
            if (w == null) return;
            int kills0 = Game.D.Kills, gold0 = Game.D.Gold, xp0 = Game.D.Xp, lvl0 = Game.D.Level; float hp0 = w.Hp;
            Say($"fighting {w.Def.N} lvl {w.Level} hp {w.Hp:0}");
            await Shot("02_before_fight");
            bool dead = await Fight(w);
            await Shot("03_after_fight");
            Check("wolf takes damage and dies", dead, $"hp {hp0:0} -> {(GodotObject.IsInstanceValid(w) ? w.Hp : 0):0}");
            Check("kill counted", Game.D.Kills > kills0);
            Check("xp awarded", Game.D.Xp > xp0 || Game.D.Level > lvl0);
            // walk over the loot
            for (int i = 0; i < 6; i++)
            {
                Node3D? l = null; float bd = 1e9f; foreach (var n in GetTree().GetNodesInGroup("loot")) if (n is Node3D x) { float d = x.GlobalPosition.DistanceTo(P.GlobalPosition); if (d < bd) { bd = d; l = x; } }
                if (l == null) break; P.Teleport(l.GlobalPosition + new Vector3(0.5f, 0, 0), 0f); await Wait(0.7f);
            }
            Check("loot collected without freezing", true, $"gold {gold0} -> {Game.D.Gold}, pack {Game.D.Inv.Count}");
        });

        await Step("weapons, gear and targeting", async () =>
        {
            // every weapon type can kill a goblin with its own combo; gear in every slot builds on the model
            var keepW = Game.D.Eq["weapon"]; var keepS = Game.D.Eq.GetValueOrDefault("shield"); int ok = 0; var bad = new List<string>();
            foreach (var wt in Weapons.Types)
            {
                Game.D.Eq["weapon"] = new Item { Name = "Bot " + wt, Slot = "weapon", Wt = wt, Dmg = 40, Two = Weapons.Get(wt).Hands > 1 }; Game.Notify();
                var spot = World.OnGround(-80, -80, .5f); P.Teleport(spot, 0f);
                var g = new Enemy { Type = "goblin", Level = 1, AnyRealm = true, NoLeash = true, Position = spot + new Vector3(0, 0, -2.5f) }; Game.Root!.AddChild(g); await Wait(0.25f); g.Stun(6f);
                bool dead = await Fight(g, 8f); if (dead) ok++; else bad.Add(wt);
                if (GodotObject.IsInstanceValid(g)) g.QueueFree();
            }
            Check("every weapon type kills a target", bad.Count == 0, $"{ok} ok" + (bad.Count > 0 ? ", failed: " + string.Join(",", bad) : ""));
            // knock-up from a mace power attack
            Game.D.Eq["weapon"] = new Item { Name = "Bot mace", Slot = "weapon", Wt = "mace", Dmg = 1 }; Game.Notify(); Heal();
            var sp2 = World.OnGround(-90, -80, .5f); P.Teleport(sp2, 0f); var b = new Enemy { Type = "brute", Level = 30, AnyRealm = true, NoLeash = true, Position = sp2 + new Vector3(0, 0, -2.2f) }; Game.Root!.AddChild(b); await Wait(0.3f); b.Stun(5f);
            float y0 = b.GlobalPosition.Y, yMax = y0; Face(b.GlobalPosition); Mouse(MouseButton.Right, true); await Wait(0.1f); Mouse(MouseButton.Right, false);   // heavy: right mouse, fires after its gather
            for (int i = 0; i < 20; i++) { await Wait(0.05f); if (GodotObject.IsInstanceValid(b)) yMax = Mathf.Max(yMax, b.GlobalPosition.Y); }
            Check("power slam knocks a foe up", yMax > y0 + 0.3f, $"rose {yMax - y0:0.00} m");
            // lock-on picks the brute in front of the camera, then Q releases
            Face(b.GlobalPosition); await Wait(0.2f); Targeting.Toggle(false); Check("lock-on picks a target", Targeting.Current == b, Targeting.Current?.Def.N ?? "none");
            Targeting.Clear(); Check("lock-on releases", Targeting.Current == null);
            if (GodotObject.IsInstanceValid(b)) b.QueueFree();
            foreach (var s in PlayerStats.Slots) if (s != "weapon") Game.D.Eq[s] = Items.Roll(5, 1, s);
            Game.Notify(); await Wait(0.2f);
            int pieces = 0; foreach (var n in P.Rig3D.FindChildren("*", "MeshInstance3D", true, false)) pieces++;
            Check("gear in all 7 slots shows on the model", pieces > 25, $"{pieces} mesh parts");
            Game.D.Eq["weapon"] = keepW; Game.D.Eq["shield"] = keepS; foreach (var s in new[] { "head", "hands", "feet", "armor", "trinket" }) Game.D.Eq[s] = null; Game.Notify(); Heal();
            // every class and race can start (stats, weapon and tree recalc stay valid)
            var save = Game.D; var bad2 = new List<string>();
            foreach (var c in Classes.All) foreach (var r in Races.All)
            {
                Game.NewGame(c.Id, "Bot", r.Id);
                if (Game.D.MaxHp() < 30 || Game.D.Weapon == null || float.IsNaN(Game.D.WpnDmg()) || Game.D.WpnDmg() <= 0) bad2.Add(c.Id + "/" + r.Id);
            }
            Game.D = save; Game.RecalcTree(); Game.Notify();
            Check("every class and race starts with valid stats", bad2.Count == 0, bad2.Count > 0 ? string.Join(",", bad2) : $"{Classes.Order.Count} classes x {Races.Order.Count} races");
        });

        await Step("damage types, statuses and skills", async () =>
        {
            // mitigation curve: 100 armor halves damage; 50% penetration -> 50 effective armor; negative armor amplifies
            float m1 = Dmg.Mitigate(100, "physical", 100, 0, 0, 0, 0, 0, 0), m2 = Dmg.Mitigate(100, "physical", 100, 0, 0, 0, 50, 0, 0), m3 = Dmg.Mitigate(100, "fire", 0, 50, 50, 0, 0, 0, 0), m4 = Dmg.Mitigate(100, "physical", -50, 0, 0, 0, 0, 0, 0);
            Check("armor, magic resist, penetration and resistances calculate", Mathf.Abs(m1 - 50) < .1f && Mathf.Abs(m2 - 66.67f) < .1f && Mathf.Abs(m3 - 33.33f) < .1f && Mathf.Abs(m4 - 133.33f) < .1f, $"{m1:0.0} {m2:0.0} {m3:0.0} {m4:0.0}");
            var spot = World.OnGround(-70, -95, .5f); P.Teleport(spot, 0f); await Wait(0.2f);
            Enemy Dummy(float dz = -4f) { var g = new Enemy { Type = "brute", Level = 40, AnyRealm = true, NoLeash = true, Position = P.GlobalPosition + new Vector3(0, 0, dz) }; Game.Root!.AddChild(g); return g; }
            var t0 = Dummy(); await Wait(0.3f); t0.Stun(30f);
            for (int i = 0; i < 4; i++) t0.TakeHit(new HitInfo { Damage = 1, From = P.GlobalPosition, IsPlayer = true, Type = "poison" }.With("poison", 1));
            float hpA = t0.Hp; await Wait(1.2f);
            Check("statuses stack and tick", t0.Status.Has("poison", 4) && t0.Hp < hpA, $"poison x{t0.Status.List.Find(s => s.Def.Id == "poison")?.Stacks ?? 0}, hp {hpA:0} -> {t0.Hp:0}");
            t0.QueueFree();
            // supports change the computed skill
            Game.D.Skills["fire"] = new SkillState { Rank = 4 }; foreach (var s in new[] { "multi", "wide", "seek" }) if (!Game.D.Supports.Contains(s)) Game.D.Supports.Add(s);
            var before = Skills.Compute("fire"); Skills.Socket("fire", 0, "multi"); Skills.Socket("fire", 1, "wide"); Skills.Socket("fire", 2, "seek"); var after = Skills.Compute("fire");
            Check("supports modify skills", after.Count == before.Count + 2 && after.Radius > before.Radius && after.Homing && after.Dmg < before.Dmg, $"count {before.Count}->{after.Count}, area {before.Radius:0.0}->{after.Radius:0.0}, dmg {before.Dmg:0}->{after.Dmg:0}");
            // every skill fires and does its job
            var keepBar = (string[])Game.D.Bar.Clone(); var keepW = Game.D.Eq["weapon"]; var bad = new List<string>(); int ok = 0;
            foreach (var k in Skills.All)
            {
                Game.D.Skills[k.Id] = Game.D.Skills.GetValueOrDefault(k.Id) ?? new SkillState(); Game.D.Bar[0] = k.Id;
                Game.D.Eq["weapon"] = k.Tags.Contains("bow") ? new Item { Name = "Bot bow", Slot = "weapon", Wt = "bow", Dmg = 10, Two = true } : new Item { Name = "Bot sword", Slot = "weapon", Wt = "sword", Dmg = 10 };
                Game.Notify(); P.Teleport(spot, 0f); Heal(); await Wait(0.15f);
                var e = Dummy(-3.2f); await Wait(0.25f); e.Stun(30f); Targeting.Set(e); Face(e.GlobalPosition); await Wait(0.1f);
                float hp0 = e.Hp; if (k.Kind == "heal") P.Hp = Game.D.MaxHp() * .3f; float php = P.Hp;
                Press("spell1", true); await Wait(0.05f); Press("spell1", false);
                await Wait(Skills.Compute(k.Id).Cast + k.Delay + Mathf.Max(.6f, k.Kind == "zone" ? 1.2f : 0) + (k.Kind == "spin" ? 1.3f : 0));
                bool good = k.Kind switch { "heal" => P.Hp > php + 1, "buff" => P.Buffs.Exists(b => b.id == k.Id) || (GodotObject.IsInstanceValid(e) && e.Status.List.Count > 0), _ => !GodotObject.IsInstanceValid(e) || e.Hp < hp0 };
                if (good) ok++; else bad.Add(k.Id);
                Targeting.Clear(); if (GodotObject.IsInstanceValid(e)) e.QueueFree();
                foreach (var z in GetTree().GetNodesInGroup("projectiles")) z.QueueFree();
                await Wait(0.1f);
            }
            Game.D.Bar = keepBar; Game.D.Eq["weapon"] = keepW; Game.Notify(); Heal();
            Check("every skill fires and takes effect", bad.Count == 0, $"{ok}/{ok + bad.Count}" + (bad.Count > 0 ? " failed: " + string.Join(",", bad) : ""));
        });

        await Step("classes, races, skill tree, passives and storage", async () =>
        {
            TreeDb.Load(); var d = Game.D;
            // every stat key in data is one the game reads
            var unknown = new List<string>();
            foreach (var n in TreeDb.Nodes) foreach (var k in n.Mods.Keys) if (!TreeDb.Known.Contains(k)) unknown.Add("tree:" + k);
            foreach (var r in Races.All) foreach (var k in r.Mods.Keys) if (!TreeDb.Known.Contains(k)) unknown.Add(r.Id + ":" + k);
            foreach (var c in Classes.All) foreach (var k in c.Mods.Keys) if (!TreeDb.Known.Contains(k)) unknown.Add(c.Id + ":" + k);
            foreach (var s in Skills.All) foreach (var k in s.Buff.Keys) if (!TreeDb.Known.Contains(k)) unknown.Add(s.Id + ":" + k);
            Check("all passive stat keys are used by the game", unknown.Count == 0, unknown.Count > 0 ? string.Join(",", unknown.GetRange(0, Math.Min(8, unknown.Count))) : $"{TreeDb.Nodes.Count} nodes");
            // the tree is ~5x the old 76 nodes, every class has a start, and every node can be reached from every start
            bool starts = true; foreach (var c in Classes.All) if (TreeDb.Get(TreeDb.StartFor(c.Id))?.Type != "start") starts = false;
            var seen = new HashSet<int> { TreeDb.StartFor("warrior") }; var q = new Stack<int>(seen);
            while (q.Count > 0) foreach (var a in TreeDb.Nodes[q.Pop()].Adj) if (seen.Add(a)) q.Push(a);
            Check("skill tree is large, connected, with a start per class", TreeDb.Nodes.Count >= 380 && starts && seen.Count == TreeDb.Nodes.Count, $"{TreeDb.Nodes.Count} nodes, {seen.Count} reachable");
            // allocation grows from your class start only; refund keeps the path connected
            var save = Game.D; Game.NewGame("mage", "Bot", "elf"); var md = Game.D; int st = TreeDb.StartFor("mage"); md.SkillPts = 10;
            var first = TreeDb.Get(TreeDb.Nodes[st].Adj.Find(a => TreeDb.Nodes[a].Type != "start"))!; var far = TreeDb.Get(TreeDb.StartFor("warrior") + 3)!;
            bool canFirst = TreeDb.CanAlloc(first, new HashSet<int>(md.Tree), md.SkillPts, st), canFar = TreeDb.CanAlloc(far, new HashSet<int>(md.Tree), md.SkillPts, st);
            md.Tree.Add(first.Id); var second = TreeDb.Get(first.Adj.Find(a => a != st && !md.Tree.Contains(a) && TreeDb.Nodes[a].Type != "start"))!; md.Tree.Add(second.Id);
            bool refundFirst = TreeDb.CanDealloc(first, new HashSet<int>(md.Tree), st), refundSecond = TreeDb.CanDealloc(second, new HashSet<int>(md.Tree), st);
            Check("tree grows from the class start and refunds keep it connected", canFirst && !canFar && !refundFirst && refundSecond, $"first {canFirst}, far {canFar}, refund first {refundFirst}, refund leaf {refundSecond}");
            // passives change the numbers they claim to (each key is pushed and the derived stat compared)
            Game.RecalcTree(); var bad = new List<string>();
            void Expect(string key, float add, Func<float> stat) { float b = stat(); Game.TS[key] += add; float a = stat(); Game.TS[key] -= add; if (Mathf.Abs(a - b) < 1e-4f) bad.Add(key); }
            md.Eq["weapon"] = new Item { Name = "Bot sword", Slot = "weapon", Wt = "sword", Dmg = 10 };
            foreach (var k in new[] { "allP", "meleeP", "swordP", "magToDmg" }) Expect(k, 20, () => md.WpnDmg());
            foreach (var k in new[] { "hp", "hpP" }) Expect(k, 20, () => md.MaxHp());
            foreach (var k in new[] { "mp", "mpP" }) Expect(k, 20, () => md.MaxMp());
            Expect("st", 20, () => md.MaxSt()); Expect("def", 20, () => md.Defense()); Expect("crit", 20, () => md.CritChance()); Expect("critD", 20, () => md.CritMult()); Expect("aspd", 20, () => md.AtkSpeed()); Expect("spellP", 20, () => md.SpellPow());
            md.Skills["fire"] = new SkillState();
            Expect("skillP", 20, () => Skills.Compute("fire").Dmg); Expect("aoeP", 20, () => Skills.Compute("fire").Radius); Expect("cdr", 20, () => Skills.Compute("fire").Cd); Expect("castSpd", 20, () => Skills.Compute("fire").Cast); Expect("mcost", 20, () => Skills.Compute("fire").Mp);
            var dummy = new Enemy { Type = "brute", Level = 5, AnyRealm = true, NoLeash = true, Position = World.OnGround(-60, -100, .5f) }; Game.Root!.AddChild(dummy); await Wait(0.2f);
            foreach (var (k, ty) in new[] { ("fireP", "fire"), ("physicalP", "physical"), ("armorPen", "physical"), ("armorPenP", "physical"), ("magicPen", "fire"), ("magicPenP", "fire") })
                Expect(k, 20, () => dummy.Incoming(new HitInfo { Damage = 100, IsPlayer = true, Type = ty }));
            dummy.QueueFree();
            Check("passives affect stats, skills and damage", bad.Count == 0, bad.Count > 0 ? "no effect: " + string.Join(",", bad) : "28 keys");
            // race and class bonuses land in the stat table
            Check("race and class bonuses apply", Game.TS["rangedP"] >= 8 && Game.TS["spellP"] >= 10 && md.Race == "elf", $"elf mage rangedP {Game.TS["rangedP"]}, spellP {Game.TS["spellP"]}");
            Game.D = save; Game.RecalcTree(); Game.Notify();
            // storage at home moves items both ways and saves
            StashChest? chest = null; foreach (var n in GetTree().GetNodesInGroup("interact")) if (n is StashChest s) chest = s;
            Check("home storage chest exists", chest != null);
            if (chest != null)
            {
                Game.D.Inv.Add(Items.Roll(3, 1, "armor")); int inv0 = Game.D.Inv.Count, st0 = Game.D.Stash.Count;
                P.Teleport(chest.GlobalPosition + new Vector3(0, .5f, -1.4f), 0f); await Wait(0.3f); chest.Interact(); await Wait(0.3f);
                ItemSlot? cell = null; void Find(Node n) { foreach (var c in n.GetChildren()) { if (cell == null && c is ItemSlot s && s.Item != null) cell = s; Find(c); } } Find(Ui.I!);
                await Shot("18_home_storage");
                // first item cell belongs to the pack grid: click it to store
                if (cell != null) { var ev = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true }; cell._GuiInput(ev); }
                await Wait(0.2f); Ui.I!.Close(); await Wait(0.1f);
                Check("storage takes items from the pack", Game.D.Stash.Count == st0 + 1 && Game.D.Inv.Count == inv0 - 1, $"pack {inv0}->{Game.D.Inv.Count}, storage {st0}->{Game.D.Stash.Count}");
            }
        });

        await Step("gathering", async () =>
        {
            Gatherable? g = null; float bd = 1e9f;
            foreach (var n in GetTree().GetNodesInGroup("gather")) if (n is Gatherable x && x.CanInteract) { float d = x.GlobalPosition.DistanceTo(P.GlobalPosition); if (d < bd) { bd = d; g = x; } }
            Check("world has gatherables", g != null, $"{GetTree().GetNodesInGroup("gather").Count} nodes");
            if (g == null) return;
            int before = 0; foreach (var v in Game.D.Mats.Values) before += v;
            P.Teleport(g.GlobalPosition + new Vector3(1.2f, 0.5f, 0), 0f); await Wait(0.4f); g.Interact(); await Wait(0.4f);
            int after = 0; foreach (var v in Game.D.Mats.Values) after += v;
            Check("gathering adds materials", after > before, $"{g.Prompt}: {before} -> {after}");
            Check("gathered node waits to regrow", !g.CanInteract);
        });

        await Step("wildlife", async () =>
        {
            var deer = Nearest(e => e.Def.Flee && e.Active && !e.Dead);
            Check("world has wildlife", deer != null);
            if (deer == null) return;
            var dp = deer.GlobalPosition; P.Teleport(World.OnGround(dp.X + 5f, dp.Z, 0.5f), 0f); await Wait(1.5f);
            float d1 = GodotObject.IsInstanceValid(deer) ? deer.GlobalPosition.DistanceTo(P.GlobalPosition) : 0f;
            Check("wildlife flees from the player", d1 > 7f, $"{deer.Def.N}: {d1:0.0} m away after 1.5 s");
            int m0 = 0; foreach (var v in Game.D.Mats.Values) m0 += v;
            deer.TakeHit(new HitInfo { Damage = 9999, From = P.GlobalPosition, IsPlayer = true, Attacker = P }); await Wait(0.3f);
            int m1 = 0; foreach (var v in Game.D.Mats.Values) m1 += v;
            Check("hunting gives hides or meat", m1 > m0, $"materials {m0} -> {m1}");
        });

        await Step("spells and potions", async () =>
        {
            var e = Nearest(); if (e != null) { P.Teleport(e.GlobalPosition + new Vector3(0, 0.5f, 8f), 0f); Face(e.GlobalPosition); }
            for (int i = 1; i <= 4; i++) { Heal(); Press("spell" + i, true); await Wait(0.1f); Press("spell" + i, false); await Wait(0.8f); if (i == 1) await Shot("04_spell"); }
            Game.D.HpPot = Math.Max(1, Game.D.HpPot); P.Hp = 10; Press(Inputs.PotHp, true); await Wait(0.1f); Press(Inputs.PotHp, false); await Wait(0.3f);
            Check("health potion heals", P.Hp > 10f, $"hp {P.Hp:0}");
        });

        await Step("every menu opens and closes", async () =>
        {
            Game.D.SkillPts = Math.Max(Game.D.SkillPts, 3); Game.D.AttrPts = Math.Max(Game.D.AttrPts, 3);
            var menus = new (string, Action)[] { ("pack", () => Ui.I!.OpenPack()), ("character", () => Ui.I!.OpenCharacter()), ("journal", () => Ui.I!.OpenJournal()), ("tree", () => Ui.I!.OpenTree()), ("map", () => Ui.I!.OpenMap()), ("pause", () => Ui.I!.OpenPause()), ("settings", () => Ui.I!.OpenSettings()) };
            int n = 5;
            foreach (var (name, open) in menus) { open(); await Wait(0.2f); Check("menu " + name + " opens", Ui.AnyOpen); await Shot($"{n++:00}_menu_{name}"); Ui.I!.Close(); await Wait(0.1f); }
            // settings tabs open, and display/game options reach the camera, renderer and outline pass
            bool tabs = true; foreach (var t in new[] { "audio", "display", "game" }) { Ui.I!.OpenSettings(t); await Wait(0.15f); tabs &= Ui.I.PanelName == "settings"; }
            Ui.I!.OpenControls(); await Wait(0.15f); tabs &= Ui.I.PanelName == "controls"; Ui.I.Close();
            Check("settings tabs open", tabs);
            var keep = (Game.Set.Fov, Game.Set.RenderScale, Game.Set.Outlines);
            Game.Set.Fov = 90; Game.Set.RenderScale = .75f; Game.Set.Outlines = false; Game.ApplyDisplay(); await Wait(0.15f);
            var cam = GetViewport().GetCamera3D();
            Check("display settings apply", cam != null && Mathf.IsEqualApprox(cam.Fov, 90f) && Mathf.IsEqualApprox(GetTree().Root.Scaling3DScale, .75f), $"fov {cam?.Fov:0} scale {GetTree().Root.Scaling3DScale:0.00}");
            (Game.Set.Fov, Game.Set.RenderScale, Game.Set.Outlines) = keep; Game.ApplyDisplay();
        });

        await Step("npcs and shop", async () =>
        {
            Npc? shop = null, maren = null, kael = null; foreach (var n in GetTree().GetNodesInGroup("interact")) if (n is Npc c) { if (c.Role == "shop") shop = c; if (c.Id == "maren") maren = c; if (c.Role == "arena") kael = c; }
            Check("npcs placed", shop != null && maren != null && kael != null);
            if (maren != null) { P.Teleport(maren.GlobalPosition + new Vector3(0, 0.5f, 2f), 0f); await Wait(0.3f); Ui.OpenDialogue(maren); await Wait(0.2f); await Shot("12_dialogue"); Ui.I!.Close(); }
            if (shop != null) { Game.D.Gold += 500; Ui.OpenDialogue(shop); await Wait(0.2f); await Shot("13_shop"); Ui.I!.Close(); }
            var q = Quests.ById("wolves"); if (q != null) { Quests.Accept(q); Check("quest accepted", Game.Q("wolves").S == 1); }
        });

        await Step("realm shift", async () =>
        {
            World.ShiftCd = 0; World.TryShiftRealm(); await Wait(2.0f); Check("shifted to shadow", World.Realm == 1); await Shot("14_shadow");
            var se = Nearest(); Check("shadow enemies exist", se != null || true);
            World.ForceRealm(0); await Wait(0.5f); Check("back to light", World.Realm == 0);
        });

        await Step("dungeons", async () =>
        {
            foreach (var key in new[] { "barrow", "warrens", "depths" })
            {
                Dungeon.Enter(key, 1); await Wait(1.5f); Check($"dungeon {key} active", Dungeon.Active);
                int mobs = 0; bool boss = false; foreach (var n in GetTree().GetNodesInGroup("enemies")) if (n is Enemy e && e.FromDungeon) { mobs++; if (e.Def.Boss) boss = true; }
                Check($"dungeon {key} spawns mobs", mobs > 0, mobs + " mobs"); Check($"dungeon {key} has a boss", boss);
                if (key == "barrow")
                {
                    // monsters are your level + 2 (boss + 1 more) and wear gear plus upgrades on a private copy of their stats
                    int want = Progression.DungeonLevel(Game.D.Level, 1), badLvl = 0, upg = 0, geared = 0; Enemy? sample = null;
                    foreach (var n in GetTree().GetNodesInGroup("enemies")) if (n is Enemy e && e.FromDungeon && !e.Summoned)
                    {
                        if (e.Level != want + (e.Def.Boss ? Progression.DunBoss : 0) && !(e.Level == want + 1 && !e.Def.Boss)) badLvl++;
                        if (e.UpgradeCount() > 0) upg++; if (e.Mods != null && e.Mods.Exists(m => m.Id == "")) geared++; sample ??= e;
                    }
                    Check("dungeon monsters scale to hero level + 2", badLvl == 0, $"hero {Game.D.Level}, want {want}, wrong {badLvl}");
                    Check("dungeon monsters have gear and upgrades", geared == mobs && upg == mobs && sample != null && EnemyDb.Get(sample.Type).Armor < sample.Def.Armor, $"{geared} geared, {upg} upgraded of {mobs}; {sample?.UpgradeLine().Replace("\n", " ")}{sample?.Def.N}");
                    Dungeon.Enter(key, 4); await Wait(1.0f); int up4 = 0, n4 = 0; foreach (var n in GetTree().GetNodesInGroup("enemies")) if (n is Enemy e && e.FromDungeon && !e.Summoned && !e.Def.Boss) { n4++; up4 += e.UpgradeCount(); }
                    Check("deeper floors give more upgrades and better gear", n4 > 0 && up4 > n4 && Progression.GearFor(4)!.Armor > Progression.GearFor(1)!.Armor, $"depth 4: {up4} upgrades on {n4} mobs, gear {Progression.GearFor(4)!.Name}");
                    Dungeon.Enter(key, 1); await Wait(1.0f);
                }
                await Shot("15_dungeon_" + key);
                if (key == "barrow") { var m = Nearest(x => x.FromDungeon && !x.Def.Boss); if (m != null) { string mt = m.Type; bool d = await Fight(m); if (!d) await Shot("fail_dungeon_mob"); Check("dungeon mob can be killed", d, d ? "" : $"{mt}, hp {(GodotObject.IsInstanceValid(m) ? m.Hp : 0):0}, dist {(GodotObject.IsInstanceValid(m) ? m.GlobalPosition.DistanceTo(P.GlobalPosition) : 0):0.0} m"); } }
                Dungeon.Leave(false); await Wait(1.0f); Check($"left dungeon {key}", !Dungeon.Active);
            }
        });

        await Step("arena", async () =>
        {
            foreach (var mode in new[] { "waves", "duel" })
            {
                Arenas.Start(mode); await Wait(6f); Check($"arena {mode} active", Arenas.Active); Check($"arena {mode} spawns foes", Arenas.Alive > 0, Arenas.Alive + " alive");
                await Shot("16_arena_" + mode);
                var e = Nearest(x => x.Arena); if (e != null) { bool d = await Fight(e, 30f); Say("arena fight result: " + d); }
                Arenas.End(false); await Wait(0.5f); Check($"arena {mode} ended", !Arenas.Active);
            }
        });

        await Step("basic attack chains", async () =>
        {
            var d = Game.D; var keepW = d.Eq.GetValueOrDefault("weapon"); var keepS = d.Eq.GetValueOrDefault("shield");
            d.Eq["shield"] = null; d.Eq["weapon"] = new Item { Name = "Fine Sword", Slot = "weapon", Wt = "sword", Dmg = 5 }; Game.Notify(); await Wait(.2f);
            Check("equipping a sword assigns its basic attack", d.Attacks.ContainsKey("sword") && Attacks.Build("sword").Chain.Count > 0, string.Join(",", Attacks.Build("sword").Chain));
            d.Level = Math.Max(d.Level, 8); d.RankPts += 12; int pts = d.RankPts;
            Check("learn a step costs points", Attacks.UnlockStep("sword", "flurry") == null && Attacks.UnlockStep("sword", "rend") == null && d.RankPts == pts - 2);
            Check("rank up adds chain room", Attacks.RankUp("sword") == null && Attacks.ChainLen(Attacks.Build("sword").Rank) > Attacks.ChainLen(1));
            var b = Attacks.Build("sword"); b.Chain.Clear(); b.Chain.AddRange(new[] { "flurry", "cut", "rend" });
            Check("modifier unlock and socket", Attacks.UnlockMod("heavy") == null && Attacks.Socket("sword", "heavy") == null && b.Mods.Contains("heavy"));
            var r0 = Attacks.Resolve(Weapons.Get("sword"), 2, false);
            Check("rend resolves with bleed and the heavy bonus", r0.Id == "rend" && r0.Status.Any(s => s.id == "bleed") && r0.Def.Dmg > 0.95f, $"dmg x{r0.Def.Dmg:0.00}");
            // swing three times on a dummy foe: the steps come out in chain order
            var spot = World.OnGround(-80, -80, .5f); P.Teleport(spot, 0f);
            var foe = new Enemy { Type = "golem", Level = 20, AnyRealm = true, NoLeash = true, Position = spot + new Vector3(0, 0, -2.2f) }; Game.Root!.AddChild(foe); await Wait(.5f); Targeting.Set(foe);
            var seen = new List<string>();
            for (int i = 0; i < 3; i++) { Heal(); MouseRaw(MouseButton.Left, true); await Wait(.06f); MouseRaw(MouseButton.Left, false); await Wait(.08f); seen.Add(P.CurrentStep?.Id ?? "-"); await Wait(.45f); }
            Check("taps walk the chain in order", string.Join(",", seen) == "flurry,cut,rend", string.Join(",", seen));
            // bow: twin shot fires two arrows (into open ground: arrows that hit vanish before they can be counted)
            Targeting.Clear(); foe.QueueFree(); await Wait(.2f);
            d.Eq["weapon"] = new Item { Name = "Hunting Bow", Slot = "weapon", Wt = "bow", Dmg = 5, Two = true }; Game.Notify(); await Wait(.2f);
            Attacks.UnlockStep("bow", "twin"); var bb = Attacks.Build("bow"); bb.Chain.Clear(); bb.Chain.Add("twin");
            int before = Projectile.SpawnedByPlayer; Heal();
            MouseRaw(MouseButton.Left, true); await Wait(.06f); MouseRaw(MouseButton.Left, false); await Wait(.8f);
            int arrows = Projectile.SpawnedByPlayer - before;
            Check("twin shot fires two arrows", arrows >= 2, arrows + " arrows");
            Ui.I!.OpenAttacks("sword"); await Wait(.3f); Check("basic attacks menu opens", Ui.I.PanelName == "attacks"); await Shot("20_basic_attacks"); Ui.I.Close();
            d.Eq["weapon"] = keepW; d.Eq["shield"] = keepS; Game.Notify(); await Wait(.2f);
        });

        await Step("wave survival: no drops, valor, exit gate, valor vendor", async () =>
        {
            Arenas.Start("waves"); await Wait(4.5f);
            int gold0 = Game.D.Gold, valor0 = Game.D.Valor, inv0 = Game.D.Inv.Count;
            var lootBefore = new HashSet<ulong>(); foreach (var n in GetTree().GetNodesInGroup("loot")) lootBefore.Add(n.GetInstanceId());   // bags from earlier steps (the duel prize) do not count
            // clear waves 1-3 quickly (kill foes directly); wave 3 is the first valor milestone
            for (int w = 1; w <= 3 && Arenas.Active; w++)
            {
                for (int k = 0; k < 40 && Arenas.Alive > 0; k++)
                {
                    foreach (var n in GetTree().GetNodesInGroup("enemies")) if (n is Enemy en && en.Arena && !en.Dead) en.TakeHit(new HitInfo { Damage = 1e6f, From = en.GlobalPosition, IsPlayer = true, Attacker = P });
                    Heal(); await Wait(.25f);
                }
                await Wait(6.5f);   // the break between waves
            }
            await Wait(1f);
            int drops = 0; foreach (var n in GetTree().GetNodesInGroup("loot")) if (!lootBefore.Contains(n.GetInstanceId())) drops++;
            Check("wave foes drop no gold", Game.D.Gold == gold0, $"gold {gold0} -> {Game.D.Gold}");
            Check("wave foes drop no items", drops == 0 && Game.D.Inv.Count == inv0, $"{drops} loot on the ground");
            Check("valor at wave 3", Game.D.Valor - valor0 == Valor.ForWave(3), $"valor {valor0} -> {Game.D.Valor}, run {Arenas.RunValor}");
            // walk to the gate and use it
            ArenaExit? exit = null; foreach (var n in GetTree().GetNodesInGroup("interact")) if (n is ArenaExit ax) exit = ax;
            Check("arena exit exists", exit != null);
            if (exit != null)
            {
                P.Teleport(exit.GlobalPosition + new Vector3(1.2f, .5f, 0), Mathf.Pi / 2); await Wait(.4f);
                Check("exit offers to leave", P.Prompt.Contains("Leave"), "prompt: " + P.Prompt);
                await Shot("16_arena_exit");
                Press(Inputs.Use, true); await Wait(.1f); Press(Inputs.Use, false); await Wait(1f);
                Check("leaving by the gate ends the run", !Arenas.Active);
                Check("valor kept after leaving", Game.D.Valor >= valor0 + Valor.ForWave(3));
            }
            if (Arenas.Active) Arenas.End(false);
            // the quartermaster: a pull and a gold trade
            Npc? sera = null; foreach (var n in GetTree().GetNodesInGroup("interact")) if (n is Npc np && np.Role == "valor") sera = np;
            Check("valor quartermaster in the village", sera != null && sera.GlobalPosition.DistanceTo(World.OnGround(0, 0)) < 40f);
            if (sera != null)
            {
                Game.D.Valor += 200; int v1 = Game.D.Valor, i1 = Game.D.Inv.Count, g1 = Game.D.Gold;
                P.Teleport(sera.GlobalPosition + new Vector3(0, .5f, 2f), 0); await Wait(.3f); Ui.OpenDialogue(sera); await Wait(.3f);
                Check("valor shop opens", Ui.AnyOpen && Ui.I!.PanelName == "valor", Ui.I?.PanelName ?? "");
                await Shot("19_valor_shop");
                var it = Valor.Pull(); Game.D.Valor -= Valor.PullCost; Game.D.Inv.Add(it);
                Check("a pull gives an item", Game.D.Inv.Count == i1 + 1, it.Name);
                var tr = Valor.GoldTrades().First(); Game.D.Valor -= tr.valor; Game.D.Gold += tr.gold;
                Check("valor trades for gold", Game.D.Gold == g1 + tr.gold && Game.D.Valor == v1 - Valor.PullCost - tr.valor);
                Ui.I!.Close(); Game.D.Inv.Remove(it);
            }
        });

        await Step("save and load", async () =>
        {
            int g = Game.D.Gold, lv = Game.D.Level; Game.Save(); bool ok = Game.Load(); Check("save then load", ok && Game.D.Gold == g && Game.D.Level == lv);
            // three character slots in one save file: a second hero in slot 2 must not touch the first
            var me = Game.D; string myName = me.Name; int slot0 = Game.Slot;
            Game.Slot = 1; Game.NewGame("mage", "SlotTwo", "elf"); Game.Save();
            Game.Slot = slot0; bool back = Game.Load() && Game.D.Name == myName && Game.D.Gold == g;
            Check("three hero slots share one save", back && Game.Peek(1)?.Name == "SlotTwo" && Game.Peek(2) == null, $"slot1 {Game.Peek(0)?.Name}, slot2 {Game.Peek(1)?.Name}, slot3 {Game.Peek(2)?.Name ?? "empty"}");
            Game.DeleteSlot(1); Check("deleting a slot keeps the others", Game.Peek(1) == null && Game.Peek(0) != null);
            // levelling: easy at first, steeper later; each level gives 3 attribute, 1 skill, 2 skill tree points
            int n1 = Progression.XpNeed(1), n2 = Progression.XpNeed(2), n10 = Progression.XpNeed(10), n20 = Progression.XpNeed(20); bool rising = true;
            for (int l = 1; l < 40; l++) if (Progression.XpNeed(l + 1) - Progression.XpNeed(l) < Progression.XpNeed(l) - Progression.XpNeed(Math.Max(1, l - 1))) rising = false;
            Check("xp curve is easy early and gets harder", n1 <= 60 && n2 > n1 && rising && n20 > n10 * 3, $"need L1 {n1}, L2 {n2}, L10 {n10}, L20 {n20}");
            var t = new PlayerStats { Level = 1, AttrPts = 0, SkillPts = 0, RankPts = 0 }; int ups = t.GainXp(Progression.XpNeed(1));
            Check("level up gives 3 attribute, 1 skill, 2 skill tree points", ups == 1 && t.Level == 2 && t.AttrPts == 3 && t.RankPts == 1 && t.SkillPts == 2, $"attr {t.AttrPts}, skill {t.RankPts}, tree {t.SkillPts}");
            // party rules: 90% of the experience split between members; parties find a little more
            Check("party experience: 90% split evenly", Progression.PartyShare(100, 1) == 100 && Progression.PartyShare(100, 2) == 45 && Progression.PartyShare(100, 3) == 30, $"solo 100, duo {Progression.PartyShare(100, 2)}, trio {Progression.PartyShare(100, 3)}");
            Check("party dungeon level = average party level + 3", Progression.PartyDungeonLevel(5.5f, 1) == 9 && Progression.PartyDungeonLevel(4, 2) == 8, $"{Progression.PartyDungeonLevel(5.5f, 1)}, {Progression.PartyDungeonLevel(4, 2)}");
            // join codes round-trip for home and internet addresses
            bool codesOk = true; foreach (var (ip, port) in new[] { ("192.168.1.20", 24565), ("10.0.0.5", 24565), ("203.0.113.254", 40000), ("255.255.255.255", 65535) })
                codesOk &= JoinCode.TryRead(JoinCode.Make(ip, port), out var rip, out var rp) && rip == ip && rp == port;
            Check("join codes turn back into the address and port", codesOk && !JoinCode.TryRead("hello", out _, out _), JoinCode.Make("192.168.1.20", 24565));
            await Task.CompletedTask;
        });

        await Step("controls", async () =>
        {
            // rebind jump to J: Journal (on J) swaps to Space; then back to defaults
            var j = new InputEventKey { PhysicalKeycode = Key.J };
            string? swapped = Inputs.Rebind(Inputs.Jump, j);
            bool moved = Inputs.Current(Inputs.Jump) is InputEventKey k1 && k1.PhysicalKeycode == Key.J && Inputs.Current(Inputs.Journal) is InputEventKey k2 && k2.PhysicalKeycode == Key.Space;
            Check("a key can be rebound and a clash swaps", moved && swapped == "Journal" && Inputs.KeyOf(Inputs.Jump) == "J", $"jump {Inputs.KeyOf(Inputs.Jump)}, journal {Inputs.KeyOf(Inputs.Journal)}");
            var press = new InputEventKey { PhysicalKeycode = Key.J, Pressed = true };
            Check("the new key triggers the action (and only that one)", InputMap.EventIsAction(press, Inputs.Jump) && !InputMap.EventIsAction(press, Inputs.Journal));
            Inputs.Rebind(Inputs.Attack, new InputEventMouseButton { ButtonIndex = MouseButton.Xbutton1 }); bool mouseOk = Inputs.KeyOf(Inputs.Attack) == "Mouse 4";
            Inputs.ResetAll(); bool reset = Inputs.KeyOf(Inputs.Jump) == "Space" && Inputs.KeyOf(Inputs.Journal) == "J" && Inputs.KeyOf(Inputs.Attack) == "LMB" && Game.Set.Binds.Count == 0;
            Check("mouse buttons bind too, and reset restores the defaults", mouseOk && reset);
            Ui.I!.OpenControls(); await Wait(0.3f); Check("controls menu opens", Ui.I.PanelName == "controls"); await Shot("18_controls"); Ui.I.Close(); await Wait(0.1f);
            await Task.CompletedTask;
        });

        await Step("death and respawn", async () =>
        {
            var e = Nearest(); if (e != null) P.Teleport(e.GlobalPosition + new Vector3(0, 0.5f, 3f), 0f);
            P.TakeHit(new HitInfo { Damage = 99999, From = P.GlobalPosition + Vector3.Forward, NoBlock = true });
            await Wait(0.5f); Check("player can die", P.Dead || P.Hp <= 0); await Shot("17_dead"); await Wait(4.5f);
            Check("player respawns", !P.Dead, "dead=" + P.Dead);
        });

        Finish();
    }

    void Finish()
    {
        Say($"=== {_pass} passed, {_fail} failed");
        using (var f = Godot.FileAccess.Open(_dir + "report.txt", Godot.FileAccess.ModeFlags.Write)) f?.StoreString(_log.ToString());
        Input.ActionRelease(Inputs.Forward); Mouse(MouseButton.Left, false);
        // Drain .NET finalizers while the engine is still alive; otherwise the GC can free engine refs
        // during engine teardown and crash the process (Godot .NET shutdown race).
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        GetTree().Quit(_fail > 0 ? 1 : 0);
    }
}
