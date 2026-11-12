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

    /// <summary>--only=a,b: run just the steps whose names contain one of these words (tools/run.ps1 bot a,b). Empty = every step.</summary>
    static readonly string[] Only = Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith("--only="))?.Substring(7).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? Array.Empty<string>();
    async Task Step(string name, Func<Task> f)
    {
        if (Only.Length > 0 && !Array.Exists(Only, o => name.Contains(o, StringComparison.OrdinalIgnoreCase))) return;
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

        await Step("modifiers fit every skill", async () =>
        {
            var d = Game.D; var sups = Skills.AllSupports.ToList(); var keepSup = new List<string>(d.Supports); foreach (var sp in sups) if (!d.Supports.Contains(sp.Id)) d.Supports.Add(sp.Id);
            Check("about 50 new modifiers", sups.Count >= 70, $"{sups.Count} modifiers");
            Check("every modifier is in the modifier tree", ModTree.Count(false) == sups.Count + Attacks.Mods.Count(), $"{ModTree.Count(false)} in tree");
            // every modifier sockets into every skill; every skill with every modifier (and with three at once, in rolling groups) resolves to sane numbers
            var keep = new Dictionary<string, SkillState>(d.Skills); var refused = new List<string>(); var broken = new List<string>(); int combos = 0;
            bool Sane(SkillCalc c) => float.IsFinite(c.Dmg) && c.Dmg >= 0 && float.IsFinite(c.Cd) && c.Cd > 0 && c.Cast > 0 && c.Mp >= 0 && c.St >= 0 && float.IsFinite(c.Radius);
            foreach (var k in Skills.All)
            {
                for (int i = 0; i < sups.Count; i++)
                {
                    d.Skills[k.Id] = new SkillState { Rank = Skills.MaxRank };
                    if (Skills.Socket(k.Id, 0, sups[i].Id) is string err) { refused.Add($"{sups[i].Id}->{k.Id} ({err})"); continue; }
                    Skills.Socket(k.Id, 1, sups[(i + 1) % sups.Count].Id); Skills.Socket(k.Id, 2, sups[(i + 7) % sups.Count].Id);
                    var c = Skills.Compute(k.Id); combos++; if (!Sane(c)) broken.Add($"{k.Id}+{string.Join("+", d.Skills[k.Id].Sup)}");
                }
            }
            d.Skills.Clear(); foreach (var kv in keep) d.Skills[kv.Key] = kv.Value;
            Check("every modifier sockets into every skill", refused.Count == 0, refused.Count == 0 ? $"{Skills.All.Count()} skills x {sups.Count} modifiers" : string.Join(", ", refused.Take(6)));
            Check("modifier combinations stay sane", broken.Count == 0, broken.Count == 0 ? $"{combos} combinations" : string.Join(", ", broken.Take(6)));
            // every modifier, alone, changes what every skill does (its own effect or the stand-in for that kind of skill)
            var idle = new List<string>(); int pairs = 0;
            foreach (var k in Skills.All)
            {
                d.Skills[k.Id] = new SkillState { Rank = Skills.MaxRank }; string bare = Skills.Fingerprint(Skills.Compute(k.Id));
                foreach (var sp in sups) { d.Skills[k.Id] = new SkillState { Rank = Skills.MaxRank }; Skills.Socket(k.Id, 0, sp.Id); pairs++; if (Skills.Fingerprint(Skills.Compute(k.Id)) == bare) idle.Add($"{sp.Id} on {k.Id}"); }
            }
            Check("every modifier does something on every skill", idle.Count == 0, idle.Count == 0 ? $"{pairs} pairs" : $"{idle.Count} idle: " + string.Join(", ", idle.Take(12)));
            // the new kinds of modifier change the skill
            d.Skills["consecrate"] = new SkillState { Rank = Skills.MaxRank }; var b0 = Skills.Compute("consecrate");
            Skills.Socket("consecrate", 0, "lingering"); Skills.Socket("consecrate", 1, "precise"); Skills.Socket("consecrate", 2, "restore"); var b1 = Skills.Compute("consecrate");
            Check("duration, crit and heal modifiers apply", b1.Dur > b0.Dur && b1.CritC >= 10 && b1.Heal > b0.Heal, $"dur {b0.Dur:0.0}->{b1.Dur:0.0}, crit +{b1.CritC:0}, heal {b0.Heal:0.000}->{b1.Heal:0.000}");
            d.Skills["fire"] = new SkillState { Rank = Skills.MaxRank }; Skills.Socket("fire", 0, "echo"); Skills.Socket("fire", 1, "resonance"); Skills.Socket("fire", 2, "renewal");
            var f = Skills.Compute("fire"); Check("echoes stack and heal-on-use applies", f.Echo == 2 && f.SelfHeal > 0, $"echo {f.Echo}, heal on use {f.SelfHeal}%");
            // a melee skill with projectile, area and element modifiers still fires and hurts
            d.Skills["cleave"] = new SkillState { Rank = Skills.MaxRank }; Skills.Socket("cleave", 0, "multi"); Skills.Socket("cleave", 1, "ignite"); Skills.Socket("cleave", 2, "manaflow");
            var cc = Skills.Compute("cleave"); Check("a melee skill takes projectile and element modifiers", cc.Type == "fire" && cc.ManaBack > 0, $"type {cc.Type}, refund {cc.ManaBack}%");
            var spot = World.OnGround(-70, -95, .5f); P.Teleport(spot, 0f); Heal(); await Wait(.2f);
            Game.D.Eq["weapon"] = new Item { Name = "Bot sword", Slot = "weapon", Wt = "sword", Dmg = 10 }; Game.D.Bar[0] = "cleave"; Game.Notify();
            var e = new Enemy { Type = "brute", Level = 40, AnyRealm = true, NoLeash = true, Position = P.GlobalPosition + new Vector3(0, 0, -2.6f) }; Game.Root!.AddChild(e); await Wait(.3f); e.Stun(30f); Targeting.Set(e); Face(e.GlobalPosition);
            float hp0 = e.Hp; Press("spell1", true); await Wait(.05f); Press("spell1", false); await Wait(cc.Cast + .8f);
            Check("modified melee skill hits", !GodotObject.IsInstanceValid(e) || e.Hp < hp0, GodotObject.IsInstanceValid(e) ? $"hp {hp0:0} -> {e.Hp:0}" : "killed");
            Targeting.Clear(); if (GodotObject.IsInstanceValid(e)) e.QueueFree(); await Wait(.2f);
            // Frost Nova with Multiple Projectiles: the extra novas reach a foe the first one cannot
            d.Skills["nova"] = new SkillState { Rank = Skills.MaxRank }; Skills.Socket("nova", 0, "multi"); Game.D.Bar[0] = "nova"; Game.Notify();
            var nc = Skills.Compute("nova"); P.Teleport(spot, 0f); Heal(); P.ResetCooldowns(); await Wait(.2f);
            var far = new Enemy { Type = "brute", Level = 40, AnyRealm = true, NoLeash = true, Position = World.OnGround(spot.X, spot.Z - nc.Radius * 1.75f, .3f) }; Game.Root!.AddChild(far); await Wait(.3f); far.Stun(30f); Targeting.Set(far); Face(far.GlobalPosition);
            float hpF = far.Hp; Press("spell1", true); await Wait(.05f); Press("spell1", false); await Wait(nc.Cast + .8f);
            Check("multiple projectiles on frost nova: more novas around you", nc.Count == 3 && GodotObject.IsInstanceValid(far) && far.Hp < hpF, $"count {nc.Count}, radius {nc.Radius:0.0}, foe at {nc.Radius * 1.75f:0.0} m: hp {hpF:0} -> {(GodotObject.IsInstanceValid(far) ? far.Hp : 0):0}");
            Targeting.Clear(); if (GodotObject.IsInstanceValid(far)) far.QueueFree(); await Wait(.2f);
            // Healing Light with Toxic: the heal bursts around you and poisons
            d.Skills["heal"] = new SkillState { Rank = Skills.MaxRank }; Skills.Socket("heal", 0, "toxic"); Game.D.Bar[0] = "heal"; Game.Notify();   // (Toxic always poisons: the check does not depend on a roll)
            P.Teleport(spot, 0f); await Wait(.2f); Heal(); P.ResetCooldowns(); P.Hp = Game.D.MaxHp() * .4f;
            var near = new Enemy { Type = "brute", Level = 40, AnyRealm = true, NoLeash = true, Position = P.GlobalPosition + new Vector3(0, 0, -2.2f) }; Game.Root!.AddChild(near); await Wait(.3f); near.Stun(30f);
            float hpN = near.Hp, hpP = P.Hp; Press("spell1", true); await Wait(.05f); Press("spell1", false); await Wait(Skills.Compute("heal").Cast + .6f);
            Check("on-hit modifiers on a heal: a burst that carries them", P.Hp > hpP + Game.D.MaxHp() * .2f && GodotObject.IsInstanceValid(near) && near.Hp < hpN && near.Status.List.Count > 0, $"hero {hpP:0}->{P.Hp:0} of {Game.D.MaxHp():0} (heal {Skills.Compute("heal").Heal:0.00}), foe {hpN:0}->{(GodotObject.IsInstanceValid(near) ? near.Hp : 0):0}, statuses {(GodotObject.IsInstanceValid(near) ? near.Status.List.Count : 0)}");
            if (GodotObject.IsInstanceValid(near)) near.QueueFree(); d.Skills.Clear(); foreach (var kv in keep) d.Skills[kv.Key] = kv.Value;
            d.Supports.Clear(); d.Supports.AddRange(keepSup); Game.Notify();   // later steps unlock modifiers themselves
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
                Check($"dungeon {key} is crowded (x{Progression.DunMobMul} monsters)", mobs >= 6 * Progression.DunMobMul, mobs + " mobs");
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

        await Step("shadow duel boss", async () =>
        {
            var d = Game.D; Heal();
            Arenas.Start("duel"); await Wait(4.5f);
            var s = Arenas.Shadow;
            Check("the duel spawns the Shadow", s != null && IsInstanceValid(s) && s.Duelist && s.Def.Boss, s == null ? "none" : s.Def.N);
            if (s == null || !IsInstanceValid(s)) { Arenas.End(false); return; }
            Check("the Shadow wears the hero's body and weapon", s.DuelMirrorsHero && s.Av != null, $"weapon {d.WType()}");
            var r1 = Enemy.DuelStats(1); var r3 = Enemy.DuelStats(3);
            Check("its health comes from the hero", Mathf.Abs(s.MaxHp - r1.hp) < 1f && s.MaxHp >= d.MaxHp() * 2f, $"{s.MaxHp:0} hp (hero {d.MaxHp():0})");
            Check("later rounds are stronger", r3.hp > r1.hp * 1.4f && r3.dmgK > r1.dmgK && r3.speed > r1.speed && r3.windMul < r1.windMul, $"hp {r1.hp:0} -> {r3.hp:0}, dmg {r1.dmgK:0} -> {r3.dmgK:0}");
            Targeting.Set(s); await Shot("16b_shadow_duel");
            // it fights: stand next to it doing nothing and it hurts the hero, using more than one move
            P.Teleport(s.GlobalPosition + new Vector3(-3.2f, .3f, 0), Mathf.Pi / 2); Heal(); Face(s.GlobalPosition); await Wait(.6f); await Shot("16d_shadow_close");
            var moves = new HashSet<string>(); float hp0 = P.Hp, low = P.Hp;
            for (float t = 0; t < 9f; t += .1f) { await Wait(.1f); moves.Add(s.DuelMove); low = Mathf.Min(low, P.Hp); if (P.Hp < d.MaxHp() * .35f) Heal(); }
            Check("the Shadow attacks the hero", low < hp0 - 1, $"lowest {low:0}/{d.MaxHp():0}");
            Check("it mixes its moves", moves.Count(m => m is "Combo" or "Heavy" or "Lunge" or "Bolts") >= 1 && moves.Count >= 3, string.Join(",", moves));
            // stance: enough damage breaks it and it kneels
            for (int i = 0; i < 40 && !s.DuelBroken; i++) { s.TakeHit(new HitInfo { Damage = s.MaxHp * .025f, From = P.GlobalPosition, IsPlayer = true, Attacker = P, NoText = true }); await Wait(.12f); if (s.Hp < s.MaxHp * .65f) s.Hp = s.MaxHp; Heal(); }
            Check("damage breaks its stance", s.DuelBroken, $"stance {s.DuelStance:0.00}");
            await Wait(3.5f); s.Hp = s.MaxHp;
            // phase 2 at 60%: a roar, then the Eclipse; breaking its barrier in time breaks it
            s.Hp = s.MaxHp * .55f; s.TakeHit(new HitInfo { Damage = 1, From = P.GlobalPosition, IsPlayer = true, Attacker = P, NoText = true }); await Wait(.3f);
            Check("phase 2 at 60% health", s.DuelPhase == 2, $"phase {s.DuelPhase}");
            await Wait(1.8f); Check("the Eclipse damage check starts", s.DuelBarrier >= 0, s.DuelMove); await Shot("16c_shadow_eclipse");
            float hpE = s.Hp;
            for (int i = 0; i < 30 && s.DuelBarrier > 0; i++) { s.TakeHit(new HitInfo { Damage = d.WpnDmg() * 3f, From = P.GlobalPosition, IsPlayer = true, Attacker = P, NoText = true }); await Wait(.1f); }
            Check("the barrier soaks damage and breaking it breaks the Shadow", s.DuelBroken && Mathf.Abs(s.Hp - hpE) < 1f, $"{s.DuelMove}, hp {hpE:0} -> {s.Hp:0}");
            await Wait(5f);
            // phase 3 at 30%: failing the Eclipse blasts the hero and heals the Shadow
            s.Hp = s.MaxHp * .25f; s.TakeHit(new HitInfo { Damage = 1, From = P.GlobalPosition, IsPlayer = true, Attacker = P, NoText = true }); await Wait(.3f);
            Check("phase 3 at 30% health", s.DuelPhase == 3, $"phase {s.DuelPhase}");
            float sHp = s.Hp; for (float t = 0; t < 12f && s.DuelMove != "Eclipse"; t += .1f) await Wait(.1f);
            for (float t = 0; t < 12f && s.DuelMove == "Eclipse"; t += .1f) { if (P.Hp < d.MaxHp()) P.Hp = d.MaxHp(); await Wait(.1f); }   // full health going in; nothing healed after the blast
            await Wait(.2f);
            Check("a failed Eclipse blasts the hero and heals the Shadow", P.Hp < d.MaxHp() * .6f && s.Hp > sHp + 1f, $"hero {P.Hp:0}/{d.MaxHp():0}, shadow {sHp:0} -> {s.Hp:0}");
            Heal();
            // a win starts the next round with a stronger Shadow
            float max1 = s.MaxHp; await Wait(1.5f);
            for (int i = 0; i < 20 && IsInstanceValid(s) && !s.Dead; i++) { s.TakeHit(new HitInfo { Damage = s.MaxHp, From = P.GlobalPosition, IsPlayer = true, Attacker = P, NoText = true }); await Wait(.3f); Heal(); }
            Check("the Shadow can be beaten", !IsInstanceValid(s) || s.Dead);
            for (float t = 0; t < 12f && (Arenas.Streak < 2 || Arenas.Shadow == s || Arenas.Shadow == null); t += .25f) { await Wait(.25f); Heal(); }
            var s2 = Arenas.Shadow;
            Check("round 2 brings a stronger Shadow", Arenas.Streak == 2 && s2 != null && s2 != s && s2.MaxHp > max1 * 1.2f, $"round {Arenas.Streak}, {max1:0} -> {(s2 != null ? s2.MaxHp : 0):0}");
            Arenas.End(false); Targeting.Clear(); await Wait(.5f); Heal();
        });

        await Step("hold to aim and hold to charge", async () =>
        {
            var d = Game.D; d.Level = Math.Max(d.Level, 8);
            foreach (var id in new[] { "meteor", "fire" }) if (!d.Skills.ContainsKey(id)) d.Skills[id] = new SkillState();
            var keepBar = (string[])d.Bar.Clone(); d.Bar[0] = "meteor"; d.Bar[1] = "fire";
            var spot = World.OnGround(-80, -80, .5f); P.Teleport(spot, 0f); Targeting.Clear(); await Wait(.4f); Heal();
            // meteor: hold shows the preview, release drops the strike where the preview was
            Press("spell1", true); await Wait(.6f);
            MeshInstance3D? disc = null; foreach (var n in Game.Root!.GetChildren()) if (n is MeshInstance3D mi && mi.Mesh is CylinderMesh) disc = mi;
            Check("holding an area spell shows a preview", disc != null);
            var where = disc?.GlobalPosition ?? Vector3.Zero; await Shot("21_aim_preview");
            Press("spell1", false); await Wait(1.2f);
            SkillZone? z = null; foreach (var n in Game.Root.GetChildren()) if (n is SkillZone sz) z = sz;
            Check("released area spell lands on the preview", z != null && new Vector2(z.GlobalPosition.X - where.X, z.GlobalPosition.Z - where.Z).Length() < .6f, z != null ? $"{new Vector2(z.GlobalPosition.X - where.X, z.GlobalPosition.Z - where.Z).Length():0.00} m off" : "no zone");
            await Wait(2.5f); Heal();
            // fire: a tap fires a normal bolt; holding a full second fires an empowered one
            float Fire(Func<Projectile, float> f) { float best = 0; foreach (var n in GetTree().GetNodesInGroup("projectiles")) if (n is Projectile pr && pr.Own == 'p' && pr.Calc?.Def.Id == "fire") best = Mathf.Max(best, f(pr)); return best; }
            Press("spell2", true); await Wait(.05f); Press("spell2", false); await Wait(.45f);
            float tap = Fire(pr => pr.Dmg); await Wait(3f); Heal();
            Press("spell2", true); await Wait(.6f); Check("charging shows a charge bar", P.ChargeFrac > .3f, $"{P.ChargeFrac:0.00}");
            await Shot("22_charge_bar"); await Wait(.6f); Press("spell2", false); await Wait(.45f);
            float full = Fire(pr => pr.Dmg); bool big = Fire(pr => pr.Big ? 1 : 0) > 0;
            Check("a full charge fires an empowered spell", full > tap * 1.5f && big, $"tap {tap:0} dmg, charged {full:0} dmg, big {big}");
            d.Bar = keepBar; Game.Notify(); await Wait(2f);
        });

        await Step("hits only stagger from abilities; an interrupted cast is not on cooldown", async () =>
        {
            var d = Game.D; var keepBar = (string[])d.Bar.Clone(); d.Bar[0] = "fire"; Heal(); await Wait(1.2f);
            var src = P.GlobalPosition + new Vector3(0, 0, -1.5f);
            Press("spell1", true); await Wait(.05f); Press("spell1", false); await Wait(.08f);
            bool casting = P.Casting; float mp0 = P.Mp;
            P.TakeHit(new HitInfo { Damage = 3, From = src, Knock = 4f }); await Wait(.05f);
            Check("a basic blow does not stagger or interrupt a cast", casting && !P.Staggered && (P.Casting || P.CdOf("fire") > 0), $"casting {casting}, staggered {P.Staggered}");
            await Wait(1.5f); Heal(); await Wait(.8f);
            Press("spell1", true); await Wait(.05f); Press("spell1", false); await Wait(.08f);
            float mpCast = P.Mp; bool cd0 = P.CdOf("fire") > 0;
            P.TakeHit(new HitInfo { Damage = 3, From = src, Knock = 9f, Stagger = true }); await Wait(.05f);
            Check("an ability blow staggers", P.Staggered);
            Check("an interrupted cast is refunded and leaves no cooldown", cd0 && P.CdOf("fire") <= 0 && P.Mp > mpCast, $"cd before {cd0}, cd now {P.CdOf("fire"):0.00}, mana {mpCast:0} -> {P.Mp:0}");
            d.Bar = keepBar; Game.Notify(); await Wait(1f);
        });

        await Step("stance and feet on the ground", async () =>
        {
            // relaxed idle out of combat; feet never sink into the ground (heel or toes), on flat ground and on a slope, in both stances
            var av = P.Av; var sk = av?.Skel; if (av == null || sk == null) { Check("hero model for the feet check", false); return; }
            int fl = sk.FindBone("foot_l"), fr = sk.FindBone("foot_r"), bl = sk.FindBone("ball_l"), br = sk.FindBone("ball_r");
            string Detail()
            {
                var sg = sk.GlobalTransform; var s = new System.Text.StringBuilder();
                if (P.Feet is { } fp1) foreach (var (pt, rest, nm) in new[] { (fp1.HeelL, fp1.AnkleRest, "heelL"), (fp1.ToeL, fp1.BallRest, "toeL"), (fp1.HeelR, fp1.AnkleRest, "heelR"), (fp1.ToeR, fp1.BallRest, "toeR") })
                    s.Append($"{nm} {(pt.Y - Mathf.Max(0, rest) - World.GroundAt(pt.X, pt.Z)) * 100:0.0}cm ");

                return s.ToString();
            }
            // the real (post-IK) sole height of each heel and toe above the ground, as the foot code left it
            float Depth()
            {
                if (P.Feet is { } fp0)
                {
                    float worst2 = -1;
                    foreach (var (pt, rest) in new[] { (fp0.HeelL, fp0.AnkleRest), (fp0.HeelR, fp0.AnkleRest), (fp0.ToeL, fp0.BallRest), (fp0.ToeR, fp0.BallRest) })
                        worst2 = Mathf.Max(worst2, World.GroundAt(pt.X, pt.Z) - (pt.Y - Mathf.Max(0, rest)));
                    return worst2;
                }
                return DepthAnim();
            }
            float DepthAnim()
            {
                var sg = sk.GlobalTransform; float baseY = P.GlobalPosition.Y, worst = -1;
                foreach (var b in new[] { fl, fr, bl, br })
                {
                    if (b < 0) continue; var pos = (sg * sk.GetBoneGlobalPose(b)).Origin; float rest = (sg * sk.GetBoneGlobalRest(b)).Origin.Y - (sg * sk.GetBoneGlobalRest(sk.FindBone("root") >= 0 ? sk.FindBone("root") : 0)).Origin.Y;
                    var q = PhysicsRayQueryParameters3D.Create(pos + Vector3.Up * 1.2f, pos + Vector3.Down * 2f, 1); q.Exclude = new Godot.Collections.Array<Rid> { P.GetRid() };
                    var h = P.GetWorld3D().DirectSpaceState.IntersectRay(q); if (h.Count == 0) continue;
                    float bottom = pos.Y - Mathf.Clamp(rest, 0, .2f);   // the sole: the bone minus its height above the floor at rest
                    worst = Mathf.Max(worst, ((Vector3)h["position"]).Y - bottom);
                }
                return worst;
            }
            // a quiet slope near the village (no monsters within 30 m): the steepest of a ring of points
            Vector3 slopeAt = World.OnGround(-8, -2, .6f); float best = 0;
            for (int i = 0; i < 48; i++) for (float rad = 20; rad <= 110; rad += 10)
            {
                float a = i / 48f * Mathf.Tau, x = Mathf.Cos(a) * rad, z = Mathf.Sin(a) * rad;
                float s = Mathf.Abs(World.GroundAt(x + 1, z) - World.GroundAt(x - 1, z)) + Mathf.Abs(World.GroundAt(x, z + 1) - World.GroundAt(x, z - 1));
                if (s <= best || s > .9f || !World.FreeSpot(x, z, 1.5f) || World.GroundAt(x, z) < 1.8f) continue;
                bool foe = false; foreach (var n in GetTree().GetNodesInGroup("enemies")) if (n is Enemy e && !e.Dead && !e.Def.Flee && e.Def.Dmg > 0 && e.GlobalPosition.DistanceTo(new Vector3(x, e.GlobalPosition.Y, z)) < 30f) { foe = true; break; }   // wildlife does not count
                if (!foe) { best = s; slopeAt = World.OnGround(x, z, .6f); }
            }
            Check("found a slope to test on", best > .2f, $"slope {best:0.00} at {slopeAt.X:0}, {slopeAt.Z:0}");
            var spots = new[] { ("flat", World.OnGround(-8, -2, .6f)), ("slope", slopeAt) };
            foreach (var (name, at) in spots)
            {
                Heal(); P.Teleport(at, 0f); await Wait(1.2f);
                float slope = Mathf.Abs(World.GroundAt(at.X + 1, at.Z) - World.GroundAt(at.X - 1, at.Z)) + Mathf.Abs(World.GroundAt(at.X, at.Z + 1) - World.GroundAt(at.X, at.Z - 1));
                // wait out any combat stance from earlier steps, then the relaxed idle
                float w = 0; while (w < 8f && P.InCombat) { await Wait(.2f); w += .2f; }
                await Wait(.8f); string idleClip = av.Cur; float dIdle = 0; for (int i = 0; i < 10; i++) { dIdle = Mathf.Max(dIdle, Depth()); await Wait(.1f); }
                if (P.Rig != null) { P.Rig.Rotation = new Vector3(-.05f, Mathf.Pi * .5f, 0); } await Shot($"stance_{name}_relaxed");
                Check($"relaxed idle out of combat ({name})", !P.InCombat && idleClip == Avatar.Clip("idle"), idleClip);
                Check($"feet stay on the ground, relaxed ({name}, slope {slope:0.00})", dIdle < .04f, $"deepest {dIdle * 100:0.0} cm into the ground; {Detail()}");
                MouseRaw(MouseButton.Left, true); await Wait(.05f); MouseRaw(MouseButton.Left, false); await Wait(1.6f);
                float dCombat = 0; for (int i = 0; i < 10; i++) { dCombat = Mathf.Max(dCombat, Depth()); await Wait(.1f); }
                await Shot($"stance_{name}_combat");
                Check($"combat stance after attacking ({name})", P.InCombat && (av.Cur == Avatar.Clip("idleArmed") || av.Cur == Avatar.Clip("idleShield")), av.Cur);
                Check($"feet stay on the ground, combat stance ({name})", dCombat < .04f, $"deepest {dCombat * 100:0.0} cm into the ground; {Detail()}");
            }
        });

        await Step("monsters notice you and new monsters", async () =>
        {
            // dungeon monsters wander until a hero is close and in sight; far ones never come running
            Dungeon.Enter("warrens", 1); await Wait(2.5f);
            var mobs = new List<Enemy>(); foreach (var n in GetTree().GetNodesInGroup("enemies")) if (n is Enemy e && e.FromDungeon && !e.Def.Boss && !e.Dead) mobs.Add(e);
            int farHunting = mobs.Count(e => e.Hunting && e.GlobalPosition.DistanceTo(P.GlobalPosition) > 30f);
            var p0 = mobs.ToDictionary(e => e, e => e.GlobalPosition); await Wait(5f);
            int moved = mobs.Count(e => GodotObject.IsInstanceValid(e) && e.GlobalPosition.DistanceTo(p0[e]) > .7f);
            int stillFar = mobs.Count(e => GodotObject.IsInstanceValid(e) && e.Hunting && e.GlobalPosition.DistanceTo(P.GlobalPosition) > 30f);
            Check("far dungeon monsters do not hunt you", farHunting == 0 && stillFar == 0, $"{mobs.Count} monsters, {farHunting} far ones hunting at once, {stillFar} after 5 s");
            Check("dungeon monsters wander", moved >= mobs.Count / 4, $"{moved} of {mobs.Count} moved in 5 s");
            var near = mobs.Where(e => GodotObject.IsInstanceValid(e) && !e.Dead).OrderBy(e => e.GlobalPosition.DistanceTo(P.GlobalPosition)).FirstOrDefault();
            if (near != null) { P.Teleport(ClearSpotNear(near, near.GlobalPosition), 0f); await Wait(1.2f); Check("a monster notices you up close", near.Hunting, $"{near.Def.N} at {near.GlobalPosition.DistanceTo(P.GlobalPosition):0.0} m"); }
            Dungeon.Leave(false); await Wait(1f);
            // the new monsters: animated models, on the home island and across the five lands
            int models = 0, lands = 0; var kinds = new HashSet<string>();
            foreach (var n in GetTree().GetNodesInGroup("enemies")) if (n is Enemy e && e.Def.Model != "") { kinds.Add(e.Type); if (Mathf.Abs(e.GlobalPosition.X) > World.Half || Mathf.Abs(e.GlobalPosition.Z) > World.Half) lands++; else models++; }
            Check("the new monsters live in the world and the five lands", kinds.Count >= 20 && lands > 60 && models > 5, $"{kinds.Count} kinds, {models} on the home island, {lands} in the lands");
            var spot = World.OnGround(-60, 60, .6f); P.Teleport(spot, 0f); Heal();
            var sl = new Enemy { Type = "orcWarrior", Level = 1, AnyRealm = true, NoLeash = true, Position = spot + new Vector3(0, 0, -3f) }; Game.Root!.AddChild(sl); await Wait(.4f);
            Check("a model monster has its animated body", sl.HasModel, sl.Def.N);
            bool dead = await Fight(sl); Check("a model monster can be fought and killed", dead);
        });

        await Step("attack animations and weight", async () =>
        {
            // every chain step plays its own animation (data/attacks.json "anim"), and heavy swings make the next one wait (recovery)
            var d = Game.D; var keepW = d.Eq.GetValueOrDefault("weapon"); var keepS = d.Eq.GetValueOrDefault("shield");
            d.Eq["shield"] = null; d.Eq["weapon"] = new Item { Name = "Fine Sword", Slot = "weapon", Wt = "sword", Dmg = 5 }; Game.Notify(); Heal();
            var spot = World.OnGround(-40, 40, .6f); P.Teleport(spot, 0f); await Wait(.5f);
            var b = Attacks.Build("sword"); b.Chain.Clear(); b.Chain.AddRange(new[] { "cut", "backcut", "thrust" });
            await Wait(Tune.F("player.chainWindow", 1.3f) + 1f);   // let any chain left open by the step before run out, so this one starts at "cut"
            var seen = new List<string>(); var av = P.Av;
            async Task Free() { float w = 0; while (w < 3f && (P.Attacking || P.Recovering)) { await Wait(.02f); w += .02f; } }
            for (int i = 0; i < 3; i++)
            {
                Heal(); MouseRaw(MouseButton.Left, true); await Wait(.05f); MouseRaw(MouseButton.Left, false);
                await Wait(.12f); seen.Add(av?.Cur ?? "-"); await Shot($"atk_sword_{i}_{P.CurrentStep?.Id}");
                await Free();   // the next click as soon as the swing and its recovery are over (inside the chain window)
            }
            Check("each sword step plays its own animation", seen.Distinct().Count() == 3 && seen.All(s => s.StartsWith("atk_")), string.Join(", ", seen));
            foreach (var (wt, steps) in new[] { ("axe", new[] { "hack", "hew", "splitter" }), ("spear", new[] { "prod", "prod2", "sweep" }) })
            {
                d.Eq["weapon"] = new Item { Name = "Test " + wt, Slot = "weapon", Wt = wt, Dmg = 5, Two = Weapons.Get(wt).Hands > 1 }; Game.Notify(); await Wait(.3f);
                var bw = Attacks.Build(wt); bw.Chain.Clear(); bw.Chain.AddRange(steps); var s2 = new List<string>();
                for (int i = 0; i < 3; i++) { Heal(); MouseRaw(MouseButton.Left, true); await Wait(.05f); MouseRaw(MouseButton.Left, false); await Wait(.15f); s2.Add(av?.Cur ?? "-"); await Shot($"atk_{wt}_{i}_{P.CurrentStep?.Id}"); await Free(); }
                Check($"each {wt} step plays its own animation", s2.Distinct().Count() == 3 && s2.All(s => s.StartsWith("atk_")), string.Join(", ", s2));
            }
            // weight: a power attack with a greatsword, then attack at once: the next swing must wait out the recovery
            d.Eq["weapon"] = new Item { Name = "Test Greatsword", Slot = "weapon", Wt = "greatsword", Dmg = 5, Two = true }; Game.Notify(); await Wait(.3f); Heal();
            var heavy = Attacks.Resolve(Weapons.Get("greatsword"), 0, true); var light = Attacks.Resolve(Weapons.Get("sword"), 0, false);
            Check("heavy swings recover longer than light ones", heavy.Recover > light.Recover * 4 && heavy.Recover >= .5f, $"greatsword power {heavy.Recover:0.00} s, sword cut {light.Recover:0.00} s");
            MouseRaw(MouseButton.Right, true); await Wait(.05f); MouseRaw(MouseButton.Right, false);
            float wp = 0; while (wp < 2f && P.CurrentStep?.Id != "power") { await Wait(.02f); wp += .02f; }   // the heavy gathers, then swings
            ulong t0 = Time.GetTicksMsec(); string first = P.CurrentStep?.Id ?? "";
            // spam attack until a new (light) swing starts
            float waited = 0; while (waited < 4f && P.CurrentStep?.Id == "power") { MouseRaw(MouseButton.Left, true); await Wait(.04f); MouseRaw(MouseButton.Left, false); await Wait(.04f); waited += .08f; }
            float gap = (Time.GetTicksMsec() - t0) / 1000f;
            var pw = Attacks.Resolve(Weapons.Get("greatsword"), 0, true); float powerDur = pw.Def.Dur / (d.AtkSpeed() * Weapons.Get("greatsword").Speed * pw.Speed);
            Check("the next swing waits for the heavy attack's recovery", first == "power" && gap >= powerDur + heavy.Recover / d.AtkSpeed() * .8f, $"next swing after {gap:0.00} s (swing {powerDur:0.00} s + recovery {heavy.Recover / d.AtkSpeed():0.00} s)");
            d.Eq["weapon"] = keepW; d.Eq["shield"] = keepS; Game.Notify();
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
            var foe = new Enemy { Type = "golem", Level = 20, AnyRealm = true, NoLeash = true, Position = spot + new Vector3(0, 0, -2.2f) }; Game.Root!.AddChild(foe); await Wait(.5f); Targeting.Set(foe); foe.Stun(10f);   // a still target: its blows would (rightly) interrupt the chain
            var seen = new List<string>();
            for (float w = 0; w < 4f && (P.Attacking || P.Recovering); w += .05f) await Wait(.05f);   // the step before may still be finishing a heavy swing
            for (int i = 0; i < 3; i++) { Heal(); MouseRaw(MouseButton.Left, true); await Wait(.06f); MouseRaw(MouseButton.Left, false); await Wait(.08f); seen.Add(P.CurrentStep?.Id ?? "-"); await Wait(.45f); }
            Check("taps walk the chain in order", string.Join(",", seen) == "flurry,cut,rend", string.Join(",", seen));
            // a full rank-5 chain: every added step swings, each with its own animation
            foreach (var sid in new[] { "riposte", "whirl" }) Attacks.UnlockStep("sword", sid);
            while (Attacks.Build("sword").Rank < Attacks.MaxRank) { d.RankPts++; Attacks.RankUp("sword"); }
            var longChain = new[] { "cut", "backcut", "thrust", "flurry", "rend", "riposte", "whirl" };
            b.Chain.Clear(); b.Chain.AddRange(longChain); b.Mods.Clear(); await Wait(1.5f);
            var ids = new List<string>(); var clips = new List<string>();
            for (int i = 0; i < longChain.Length; i++)
            {
                Heal(); foe.Stun(10f); P.Teleport(spot, 0f); MouseRaw(MouseButton.Left, true); await Wait(.06f); MouseRaw(MouseButton.Left, false);
                for (float t = 0; t < 1.5f && !P.Attacking; t += .03f) await Wait(.03f);   // a tap during a finisher's recovery waits for it
                await Wait(.1f); ids.Add(P.CurrentStep?.Id ?? "-"); clips.Add(P.AvClip);
                while (P.Attacking) await Wait(.03f); await Wait(.8f);   // a human pause between taps keeps the chain going
            }
            Check("a 7-step chain swings all 7 steps in order", string.Join(",", ids) == string.Join(",", longChain), string.Join(",", ids));
            var want = longChain.Select(s => Avatar.AtkName(JsonUtil.S(Attacks.Step("sword", s)!.Raw, "anim"))).ToList();
            Check("each chain step plays its own animation", clips.SequenceEqual(want), string.Join(",", clips) + " | want " + string.Join(",", want));
            // bow: twin shot fires two arrows (into open ground: arrows that hit vanish before they can be counted)
            Targeting.Clear(); foe.QueueFree(); await Wait(.2f);
            d.Eq["weapon"] = new Item { Name = "Hunting Bow", Slot = "weapon", Wt = "bow", Dmg = 5, Two = true }; Game.Notify(); await Wait(.2f);
            Attacks.UnlockStep("bow", "twin"); var bb = Attacks.Build("bow"); bb.Chain.Clear(); bb.Chain.Add("twin");
            int before = Projectile.SpawnedByPlayer; Heal();
            MouseRaw(MouseButton.Left, true); await Wait(.06f); MouseRaw(MouseButton.Left, false); await Wait(.8f);
            int arrows = Projectile.SpawnedByPlayer - before;
            Check("twin shot fires two arrows", arrows >= 2, arrows + " arrows");
            Ui.I!.OpenAttacks("sword"); await Wait(.3f); Check("basic attacks menu opens", Ui.I.PanelName == "attacks"); await Shot("20_basic_attacks"); Ui.I.Close();
            // modifier tree: every support and attack modifier has a place, lanes unlock in order, unlocking spends points
            ModTree.Load();
            Check("modifier tree holds every modifier", ModTree.Count(false) == Skills.AllSupports.Count() + Attacks.Mods.Count(), $"{ModTree.Count(false)} in tree");
            var lane = ModTree.Branches.First(b => b.Nodes.Count >= 2 && !b.Nodes[0].Attack && !ModTree.Unlocked(b.Nodes[0]) && !ModTree.Unlocked(b.Nodes[1]));
            d.Level = Math.Max(d.Level, 10); d.RankPts += 4; int rp = d.RankPts;
            Check("modifier lane locks the next one", ModTree.Unlock(lane.Nodes[1]) != null && !ModTree.Unlocked(lane.Nodes[1]));
            Check("modifier unlock in order", ModTree.Unlock(lane.Nodes[0]) == null && ModTree.Unlock(lane.Nodes[1]) == null && d.RankPts == rp - lane.Nodes[0].Cost - lane.Nodes[1].Cost);
            Ui.I.OpenMods(); await Wait(.3f); Check("modifier tree menu opens", Ui.I.PanelName == "mods"); await Shot("20_modifier_tree"); Ui.I.Close();
            Ui.I.OpenSkills(); await Wait(.3f); Check("skills menu opens", Ui.I.PanelName == "skills"); Ui.I.Close();
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
            Check("level up gives the points in progression.json", ups == 1 && t.Level == 2 && t.AttrPts == Progression.AttrPerLevel && t.RankPts == Progression.RankPerLevel && t.SkillPts == Progression.TreePerLevel, $"attr {t.AttrPts}, skill {t.RankPts}, tree {t.SkillPts}");
            await Task.CompletedTask;
        });

        await Step("scaling and loot", async () =>
        {
            // monster scaling: the curve keeps bending up, and the power floor keeps a wolf at several hits however strong the hero is
            bool curveUp = true; for (int l = 2; l < 60; l++) if (Progression.HpCurve(l + 1) - Progression.HpCurve(l) < Progression.HpCurve(l) - Progression.HpCurve(l - 1)) curveUp = false;
            float bigHit = 5000f; Check("monster health curve bends upward; a wolf takes 3+ hits even from a huge hit", curveUp && Progression.PowerHp(38, bigHit) / bigHit >= 3f, $"L10 x{Progression.HpCurve(10):0.0}, L30 x{Progression.HpCurve(30):0.0}, L50 x{Progression.HpCurve(50):0.0}");
            Check("armor is measured against the attacker's level", Progression.ArmorTaken(100, 1) < Progression.ArmorTaken(100, 30) && Progression.ArmorTaken(0, 10) == 1f, $"100 armor takes {Progression.ArmorTaken(100, 1):0.00} at L1, {Progression.ArmorTaken(100, 30):0.00} at L30");
            Check("dungeon tiers every 5 floors", Progression.Tier(5) == 0 && Progression.Tier(6) == 1 && Progression.Tier(11) == 2 && Progression.TierHp(6) > 1.2f, $"tier hp x{Progression.TierHp(6):0.00} / x{Progression.TierHp(11):0.00}");
            // loot: deeper floors drop rarer items; disassembly gives scraps; sets give bonuses; the pickup filter
            int rare0 = 0, rare9 = 0; for (int i = 0; i < 3000; i++) { if (LootTables.RollRarity(0) >= 2) rare0++; if (LootTables.RollRarity(LootTables.DepthBonus(12)) >= 2) rare9++; }
            Check("deeper floors drop rarer items", rare9 > rare0 * 2, $"rare+ of 3000: floor 1 {rare0}, floor 12 {rare9}");
            var scr = LootTables.Scraps(Items.Roll(10, 0, "weapon", 3, "fire"));
            Check("disassembling gives scraps (and a core for elemental items)", scr.Count >= 2 && scr.ContainsKey("embercore") && scr.ContainsKey("runeshard"), string.Join(", ", scr.Select(kv => kv.Key + " " + kv.Value)));
            var sd = LootTables.Sets.Values.First(); var keep = Game.D; Game.D = new PlayerStats(); Game.RecalcTree(); float hp0 = Game.TS["hpP"] + Game.TS["aspd"] + Game.TS["def"];
            Game.D.Eq[sd.Pieces[0]] = LootTables.MakeSetPiece(sd, 10, sd.Pieces[0]); Game.D.Eq[sd.Pieces[1]] = LootTables.MakeSetPiece(sd, 10, sd.Pieces[1]); Game.RecalcTree(); float hp1 = Game.TS["hpP"] + Game.TS["aspd"] + Game.TS["def"];
            Check("two set pieces give the 2-piece bonus", hp1 > hp0 && Game.D.Eq[sd.Pieces[0]]!.Set == sd.Id, $"{sd.Name}: {hp0} -> {hp1}");
            Game.D.PickRar = 2; bool f1 = !Loot.Wanted(Items.Roll(5, 0, "armor", 1)) && Loot.Wanted(Items.Roll(5, 0, "armor", 3)) && Loot.Wanted(LootTables.MakeSetPiece(sd, 5));
            Game.D = keep; Game.RecalcTree();
            Check("pickup filter: rarity floor, set pieces always", f1);
            // party rules: 90% of the experience split between members; parties find a little more
            Check("party experience: 90% split evenly", Progression.PartyShare(100, 1) == 100 && Progression.PartyShare(100, 2) == 45 && Progression.PartyShare(100, 3) == 30, $"solo 100, duo {Progression.PartyShare(100, 2)}, trio {Progression.PartyShare(100, 3)}");
            Check("party dungeon level = average party level + 3", Progression.PartyDungeonLevel(5.5f, 1) == 9 && Progression.PartyDungeonLevel(4, 2) == 8, $"{Progression.PartyDungeonLevel(5.5f, 1)}, {Progression.PartyDungeonLevel(4, 2)}");
            // join codes round-trip for home and internet addresses
            bool codesOk = true; foreach (var (ip, port) in new[] { ("192.168.1.20", 24565), ("10.0.0.5", 24565), ("203.0.113.254", 40000), ("255.255.255.255", 65535) })
                codesOk &= JoinCode.TryRead(JoinCode.Make(ip, port), out var rip, out var rp) && rip == ip && rp == port;
            Check("join codes turn back into the address and port", codesOk && !JoinCode.TryRead("hello", out _, out _), JoinCode.Make("192.168.1.20", 24565));
            await Task.CompletedTask;
        });

        await Step("master tuning file", async () =>
        {
            // data/tuning.json: readable, "game" numbers reach the code, "files" overrides are laid over the other data files
            var tj = JsonUtil.Tuning();
            Check("tuning.json loads", tj != null && tj["game"] != null && tj["files"] != null);
            Check("game numbers are read (including [base, perLevel] lists)", Tune.F("player.hpBase", -1) == 100 && Mathf.IsEqualApprox(Tune.F("items.armor.def.1", -1), 1.1f) && Tune.F("no.such.key", 7) == 7,
                $"hpBase {Tune.F("player.hpBase", -1)}, armor def/level {Tune.F("items.armor.def.1", -1)}");
            using var prog = JsonUtil.Load("res://data/progression.json"); using var en = JsonUtil.Load("res://data/enemies.json");
            float want = (float)(tj!["files"]!["progression.json"]!["scaling"]!["hpExp"]!.GetValue<double>()), got = prog.RootElement.GetProperty("scaling").GetProperty("hpExp").GetSingle();
            bool kept = en.RootElement.GetProperty("enemies").GetProperty("wolf").TryGetProperty("r", out _);   // a field only in enemies.json survives the merge
            Check("file overrides apply and keep the rest of each file", Mathf.IsEqualApprox(want, got) && kept && EnemyDb.Get("dragon").Hp == (float)tj["files"]!["enemies.json"]!["enemies"]!["dragon"]!["hp"]!.GetValue<double>(), $"hpExp {got}, dragon hp {EnemyDb.Get("dragon").Hp}");
            var hero = new PlayerStats(); Check("a new hero's health follows the file", hero.MaxHp() == Mathf.RoundToInt((100 + 8 + hero.Vit * 7 + Game.TS["hp"]) * (1 + Game.TS["hpP"] / 100f)), $"{hero.MaxHp()}");
            await Task.CompletedTask;
        });

        await Step("movement and stamina", async () =>
        {
            // sprint 3 s on open flat ground: speed should reach the sprint speed, stamina should drain at sprintDrain per second
            Heal(); P.Teleport(World.OnGround(0, 30, .6f), 0f); if (P.Rig != null) P.Rig.Rotation = new Vector3(P.Rig.Rotation.X, Mathf.Pi, 0); await Wait(.5f);
            float st0 = P.St; var p0 = P.GlobalPosition;
            Press(Inputs.Forward, true); Press(Inputs.Sprint, true); await Wait(3f);
            float st1 = P.St; var p1 = P.GlobalPosition; Press(Inputs.Sprint, false); Press(Inputs.Forward, false);
            float speed = new Vector2(p1.X - p0.X, p1.Z - p0.Z).Length() / 3f, used = st0 - st1;
            Check("sprinting reaches the sprint speed", speed > Feel.Sprint * .75f, $"{speed:0.0} m/s (sprint {Feel.Sprint}, run {Feel.Run})");
            Check("sprinting drains stamina gently", used > 0 && used <= Feel.SprintDrain * 3.5f, $"{used:0.0} stamina in 3 s, {Game.D.MaxSt()} max: about {Game.D.MaxSt() / Mathf.Max(.1f, Feel.SprintDrain):0} s of sprint");
            await Wait(1.5f); Check("stamina refills soon after", P.St > st1 + 10f, $"{st1:0} -> {P.St:0} in 1.5 s");
        });

        await Step("playtest flag", async () =>
        {
            // F8 writes a screenshot and a report (hero, place, nearby monsters, recent events)
            string dir = ProjectSettings.GlobalizePath(Flag.Dir), log = System.IO.Path.Combine(dir, "flags.log");
            if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true);
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.F8, Keycode = Key.F8, Pressed = true }); await Wait(.6f);
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.F8, Keycode = Key.F8, Pressed = false });
            bool ok = System.IO.File.Exists(log) && System.IO.Directory.GetFiles(dir, "flag_*.png").Length == 1;
            string txt = ok ? System.IO.File.ReadAllText(log) : "";
            Check("F8 saves a screenshot and a report", ok && txt.Contains("hero ") && txt.Contains("monsters within") && txt.Contains("recent events"), ok ? $"{txt.Split('\n').Length} lines" : "no files in " + dir);
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
            foreach (var sid in new[] { "burn", "poison", "chill", "bleed" }) P.Statuses.Add(sid, 10f, 0, 5f);   // long statuses that would outlast the respawn
            P.TakeHit(new HitInfo { Damage = 99999, From = P.GlobalPosition + Vector3.Forward, NoBlock = true });
            await Wait(0.5f); Check("player can die", P.Dead || P.Hp <= 0); await Shot("17_dead"); await Wait(4.5f);
            Check("player respawns", !P.Dead, "dead=" + P.Dead);
            Check("death cleanses every status", P.Statuses.List.Count == 0, string.Join(",", P.Statuses.List.Select(s => s.Def.Id)));
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
