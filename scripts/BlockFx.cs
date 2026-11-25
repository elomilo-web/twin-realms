using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>
/// How hits change the block world, the same for every skill, basic attack, crafted spell and monster (no per-skill code): the hit's
/// resolved damage and area decide how big a crater it makes and how hard a block it can break; its damage type decides what else
/// happens to the blocks around it (data/blocks.json "power" and "elements"). Also digging and placing by hand.
/// </summary>
public static class BlockFx
{
    public enum Src { Skill, Basic, Enemy, Boss }

    /// <summary>A player skill went off at a point (area hits, novas, slams, strikes, projectile bursts, zone ticks with mul 0 = element only).</summary>
    public static int Skill(SkillCalc c, Vector3 at, float radius, float mul = 1f)
    {
        string type = c.Type; string baseId = Spellcraft.Base(c.Def.Id);
        bool earth = type == "physical" && Blocks.Node("power.earthSkills") is { } es && es.EnumerateArray().Any(x => x.GetString() == baseId || x.GetString() == c.Def.Id);
        return Hit(at, radius, c.Dmg * mul, earth ? "earth" : type, Src.Skill, mul <= 0);
    }
    /// <summary>A plain weapon swing or shot.</summary>
    public static int Basic(Vector3 at, float dmg, string type) => Hit(at, 0, dmg, type, Src.Basic, false);
    /// <summary>A monster's blow or shot (bosses capped harder).</summary>
    public static int Enemy(Vector3 at, float radius, float dmg, bool boss, string type = "physical") => Hit(at, radius, dmg, type, boss ? Src.Boss : Src.Enemy, false);

    /// <summary>Breaks what the hit is strong enough to break and applies its element around it. Returns the number of blocks changed.</summary>
    public static int Hit(Vector3 at, float radius, float dmg, string type, Src src, bool elementOnly)
    {
        var w = BlockWorld.I; var a = w?.Area; if (w == null || a == null || !BlockWorld.Enabled || dmg <= 0 && !elementOnly) return 0;
        float mul = src switch { Src.Basic => Blocks.F("power.basicMul", .55f), Src.Enemy or Src.Boss => Blocks.F("power.enemyMul", .35f), _ => Blocks.F("power.mul", 1f) };
        float S = dmg * mul, cost = Blocks.F("power.hardnessCost", 30), edge = Blocks.F("power.edgeLoss", .5f);
        float scale = Mathf.Clamp(S / Mathf.Max(1, Blocks.F("power.refStrength", 120)), Blocks.F("power.minScale", .35f), Blocks.F("power.maxScale", 1.4f));
        float R = (radius > 0 ? radius * Blocks.F("power.areaMul", .75f) : Blocks.F("power.pointRadius", .7f)) * scale;
        R = Mathf.Min(R, src == Src.Boss ? Blocks.F("power.bossMaxRadius", 1.5f) : Blocks.F("power.maxRadius", 6.5f));
        float maxHard = src is Src.Enemy or Src.Boss ? Blocks.F("power.enemyMaxHardness", .8f) : 1e9f;
        bool dungeon = a.Key.StartsWith("dun:");
        int changed = 0, cap = (int)Blocks.F("power.maxBlocks", 90); float dropCh = Blocks.F("power.dropChance", .25f);
        var drops = new Dictionary<string, int>();
        if (!elementOnly && R > .05f)
        {
            var cells = Sphere(at, R + .5f).OrderBy(p => p.d).ToList();
            foreach (var (x, y, z, d) in cells)
            {
                if (changed >= cap) break;
                byte id = a.Get(x, y, z); if (id == 0 || id == 255) continue;
                var def = Blocks.Def(id); if (!def.Breakable || def.IsFluid || a.Gen.Protected(x, y, z)) continue;
                if (src is Src.Enemy or Src.Boss && (def.Hardness > maxHard || (dungeon && y < 0))) continue;   // monsters never sink a dungeon floor
                float need = def.Hardness * cost * (w.Frozen(x, y, z) ? .5f : 1f), eff = S * (1 - edge * Mathf.Clamp(d / Mathf.Max(R, .01f), 0, 1));
                if (eff < need) continue;
                w.Change(x, y, z, 0); changed++;
                if (src != Src.Enemy && src != Src.Boss && GD.Randf() < dropCh * def.DropChance) { string item = Blocks.DropItem(def.Drops); if (item != "") drops[item] = drops.GetValueOrDefault(item) + 1; }
            }
            if (changed > 0) Dust(at, R, changed);
        }
        changed += Element(w, a, at, Mathf.Max(radius, R), type, src);
        if (drops.Count > 0 && Player.Instance is { } pl) foreach (var (item, n) in drops) GatherDb.Give(item, n, at);
        return changed;
    }

    /// <summary>What a damage type does to blocks near a hit (data/blocks.json "elements").</summary>
    static int Element(BlockWorld w, BlockArea a, Vector3 at, float r, string type, Src src)
    {
        if (Blocks.Node("elements." + type) is not { } e || e.ValueKind != JsonValueKind.Object) return 0;
        float reach = JsonUtil.F(e, "reach", 1f), ign = JsonUtil.F(e, "ignite"), melt = JsonUtil.F(e, "melt"), frz = JsonUtil.F(e, "freeze"), frozenS = JsonUtil.F(e, "frozen"),
              rot = JsonUtil.F(e, "rot"), wither = JsonUtil.F(e, "wither"), bless = JsonUtil.F(e, "bless"), pack = JsonUtil.F(e, "pack"), shatter = JsonUtil.F(e, "shatter");
        float R = r + reach; if (R <= .05f) return 0;
        bool monster = src is Src.Enemy or Src.Boss; int n = 0, cap = (int)Blocks.F("power.maxBlocks", 90) * 2;
        foreach (var (x, y, z, d) in Sphere(at, R + .5f))
        {
            if (n >= cap) break;
            byte id = a.Get(x, y, z); if (id == 0 || id == 255) continue;
            var def = Blocks.Def(id);
            bool open = a.Get(x, y + 1, z) is 0 or 255 || !Blocks.Def(a.Get(x, y + 1, z)).Opaque;   // an exposed surface
            if (frz > 0 && def.FreezesTo != "" && (def.Fluid != "water" || open) && GD.Randf() < frz) { w.Change(x, y, z, Blocks.Id(def.FreezesTo)); n++; continue; }
            if (def.IsFluid || a.Gen.Protected(x, y, z)) continue;
            if (ign > 0 && def.Burn > 0 && GD.Randf() < ign) { if (w.Ignite(x, y, z)) n++; continue; }
            if (melt > 0 && def.MeltsTo != "?" && GD.Randf() < melt) { w.Change(x, y, z, (byte)Math.Max(0, Blocks.To(def.MeltsTo))); n++; continue; }
            if (shatter > 0 && def.Shatter && GD.Randf() < shatter) { w.Change(x, y, z, 0); n++; continue; }
            if (monster) continue;   // monsters only burn, melt and freeze: they do not reshape the ground
            if (rot > 0 && def.RotsTo != "?" && GD.Randf() < rot) { w.Change(x, y, z, (byte)Math.Max(0, Blocks.To(def.RotsTo))); n++; continue; }
            if (wither > 0 && def.WithersTo != "?" && GD.Randf() < wither) { w.Change(x, y, z, (byte)Math.Max(0, Blocks.To(def.WithersTo))); n++; continue; }
            if (bless > 0 && def.BlessTo != "?" && GD.Randf() < bless) { w.Change(x, y, z, (byte)Math.Max(0, Blocks.To(def.BlessTo))); n++; continue; }
            if (pack > 0 && def.PacksTo != "" && GD.Randf() < pack) { w.Change(x, y, z, Blocks.Id(def.PacksTo)); n++; continue; }
            if (frozenS > 0 && def.Solid && open) { w.Freeze(x, y, z, frozenS); n++; }
        }
        return n;
    }

    static IEnumerable<(int x, int y, int z, float d)> Sphere(Vector3 c, float r)
    {
        int x0 = Mathf.FloorToInt(c.X - r), x1 = Mathf.FloorToInt(c.X + r), y0 = Mathf.FloorToInt(c.Y - r), y1 = Mathf.FloorToInt(c.Y + r), z0 = Mathf.FloorToInt(c.Z - r), z1 = Mathf.FloorToInt(c.Z + r);
        for (int y = y0; y <= y1; y++) for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
        {
            float d = new Vector3(x + .5f - c.X, y + .5f - c.Y, z + .5f - c.Z).Length();
            if (d <= r) yield return (x, y, z, d);
        }
    }
    static void Dust(Vector3 at, float r, int n)
    {
        var g = new Gradient(); g.SetColor(0, new Color(.5f, .45f, .4f, .9f)); g.SetColor(1, new Color(.45f, .4f, .35f, 0));
        Vfx.Sparks(at, g, Math.Min(40, 8 + n), 5, .6f, .7f, -8f, false, 180f, null, .3f, "dirt");
        if (n > 6) Sfx.Play("hit", .7f);
    }

    // ---------------------------------------------------------------- by hand (build mode)
    /// <summary>Breaks a block the player dug out: always drops (its dropChance aside). False if it cannot be broken here.</summary>
    public static bool Dig(int x, int y, int z)
    {
        var w = BlockWorld.I; var a = w?.Area; if (w == null || a == null) return false;
        byte id = a.Get(x, y, z); if (id == 0 || id == 255) return false; var def = Blocks.Def(id);
        if (!def.Breakable || def.IsFluid || a.Gen.Protected(x, y, z)) return false;
        w.Change(x, y, z, 0);
        string item = Blocks.DropItem(def.Drops);
        if (item != "" && GD.Randf() < def.DropChance) GatherDb.Give(item, 1, new Vector3(x + .5f, y + .5f, z + .5f));
        var col = Blocks.Tint(def); var g = new Gradient(); g.SetColor(0, col); g.SetColor(1, new Color(col, 0));
        var c = new Vector3(x + .5f, y + .5f, z + .5f);
        Vfx.Sparks(c, g, 12, 3, .4f, .4f, -9f, false, 180f, null, .12f, "dirt");
        var pg = new Gradient(); pg.SetColor(0, new Color(col.Lerp(new Color(.5f, .48f, .45f), .4f), .7f)); pg.SetColor(1, new Color(col, 0));
        Vfx.Sparks(c, pg, 10, 1.4f, .5f, .5f, -1.5f, false, 180f, null, .3f, "dirt");   // quick soft puff
        Sfx.Play("hit", .5f);
        return true;
    }
    /// <summary>Small chips in the block's colour; call on each dig tick (the player's dig loop) with the block being dug.</summary>
    public static void Chip(int x, int y, int z)
    {
        var a = BlockWorld.I?.Area; if (a == null) return;
        byte id = a.Get(x, y, z); if (id == 0 || id == 255) return;
        var col = Blocks.Tint(Blocks.Def(id)); var g = new Gradient(); g.SetColor(0, col); g.SetColor(1, new Color(col, 0));
        Vfx.Sparks(new Vector3(x + .5f, y + .5f, z + .5f), g, 3, 2.2f, .35f, .35f, -10f, false, 180f, null, .07f, "dirt");
    }
    /// <summary>Why a block cannot go here (null = it can): something solid in the way, a player or monster inside it, or a kept-clear path.</summary>
    public static string? CanPlace(int x, int y, int z)
    {
        var a = BlockWorld.I?.Area; if (a == null) return "No block world here";
        byte cur = a.Get(x, y, z); if (cur == 255) return "Too far";
        if (cur != 0 && !Blocks.Def(cur).IsFluid) return "Something is there";
        if (a.Gen.KeepClear(x, y, z)) return "This way must stay clear";
        var box = new Aabb(new Vector3(x, y, z), Vector3.One);
        bool Inside(Node3D n, float r, float h) { var p = n.GlobalPosition; return box.Intersects(new Aabb(new Vector3(p.X - r, p.Y, p.Z - r), new Vector3(2 * r, h, 2 * r))); }
        if (Player.Instance is { } pl && Inside(pl, .35f, 1.8f)) return "You are standing there";
        foreach (var n in pl_Tree().GetNodesInGroup("enemies")) if (n is TwinRealms.Enemy e && !e.Dead && Inside(e, e.Def.R * e.ScaleF(), Mathf.Max(1f, e.HeightM()))) return "A monster is there";
        foreach (var h in Net.I?.Heroes.Values ?? Enumerable.Empty<RemoteHero>()) if (GodotObject.IsInstanceValid(h) && Inside(h, .35f, 1.8f)) return "A friend is there";
        return null;
        static SceneTree pl_Tree() => (SceneTree)Engine.GetMainLoop();
    }
    public static bool Place(int x, int y, int z, byte id, byte meta)
    {
        var w = BlockWorld.I; if (w == null || CanPlace(x, y, z) != null) return false;
        if (!w.Change(x, y, z, id, meta)) return false;
        Sfx.Play("click", .6f); return true;
    }
}
