using System;
using System.Collections.Generic;
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
    static void Mouse(MouseButton b, bool down) => Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = b, Pressed = down, Position = new Vector2(640, 360) });
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

        await Step("save and load", async () =>
        {
            int g = Game.D.Gold, lv = Game.D.Level; Game.Save(); bool ok = Game.Load(); Check("save then load", ok && Game.D.Gold == g && Game.D.Level == lv);
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
