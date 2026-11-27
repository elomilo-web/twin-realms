using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TwinRealms;

/// <summary>
/// Changing the block world by hand: digging with the pickaxe (or the weapon as a tool) and placing, in build mode. That is the ONLY way
/// blocks are removed or changed by the heroes and the monsters (after-focus A-08, the owner: "spells and skills dont destroy blocks, blocks
/// are mined"): skills, crafted spells, shots, weapon swings and monster blows no longer crater, ignite, melt, freeze, rot, wither, bless,
/// pack or shatter blocks, in the overworld or anywhere else. Their own impact effects (sparks, rings, flames: SpellFx / Vfx in the skill
/// code) never went through here and stay. <see cref="Skill"/>, <see cref="Basic"/>, <see cref="Enemy"/> and <see cref="Hit"/> are kept
/// as calls that change nothing, so old callers and tests still compile; data/blocks.json "power" and "elements" are no longer read.
/// Inside a dungeon or the arena nothing is dug or placed at all (A-07, <see cref="NoEdit"/>); dungeon doors and scripted block changes
/// go through BlockWorld.Change / BlockUse and still work.
/// </summary>
public static class BlockFx
{
    public enum Src { Skill, Basic, Enemy, Boss }

    /// <summary>A player skill went off at a point. Changes no blocks (A-08); returns 0.</summary>
    public static int Skill(SkillCalc c, Vector3 at, float radius, float mul = 1f) => 0;
    /// <summary>A plain weapon swing or shot. Changes no blocks (A-08); returns 0.</summary>
    public static int Basic(Vector3 at, float dmg, string type) => 0;
    /// <summary>A monster's blow or shot. Changes no blocks (A-08); returns 0.</summary>
    public static int Enemy(Vector3 at, float radius, float dmg, bool boss, string type = "physical") => 0;
    /// <summary>A hit of any kind. Changes no blocks (A-08: blocks are only mined); returns 0.</summary>
    public static int Hit(Vector3 at, float radius, float dmg, string type, Src src, bool elementOnly) => 0;

    // ---------------------------------------------------------------- by hand (build mode)
    /// <summary>
    /// A-07: why nothing may be dug or placed here (null = it may): inside a dungeon (a block area "dun:...", or any dungeon or lair
    /// while Dungeon.Active) and while an arena fight runs. Dig, CanPlace and Place all ask this; PlayerBlocks shows it as a note.
    /// </summary>
    public static string? NoEdit()
    {
        if (Dungeon.Active || (BlockWorld.I?.Area?.Key.StartsWith("dun:") ?? false)) return "The dungeon's walls and floors cannot be dug or built on";
        if (Arenas.Active) return "No digging or building during an arena fight";
        return null;
    }

    /// <summary>Breaks a block the player dug out: always drops (its dropChance aside). False if it cannot be broken here.</summary>
    public static bool Dig(int x, int y, int z)
    {
        var w = BlockWorld.I; var a = w?.Area; if (w == null || a == null) return false;
        if (NoEdit() != null) return false;   // A-07: no land destruction in dungeons or the arena
        byte id = a.Get(x, y, z); if (id == 0 || id == 255) return false; var def = Blocks.Def(id);
        if (!def.Breakable || def.IsFluid || a.Gen.Protected(x, y, z)) return false;
        if (TownZones.Blocks(x, z) || OnlineRules.DenyDig(a, x, y, z) != null) return false;   // F-18 protected town zone, F-44 no mining on the online server
        if (BlockUse.Protects(a, x, y, z) && !BlockUse.MayOpen(a, BlockUse.Anchor(a, x, y, z), BlockUse.Me, BlockUse.Admin)) return false;   // a locked door or chest: only its owner digs it out
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
    /// <summary>Small chips in the block's colour at each pickaxe blow; they fly from <paramref name="at"/> (the point under the reticle on the struck face) or the block's centre.</summary>
    public static void Chip(int x, int y, int z, Vector3? at = null)
    {
        var a = BlockWorld.I?.Area; if (a == null) return;
        byte id = a.Get(x, y, z); if (id == 0 || id == 255) return;
        var col = Blocks.Tint(Blocks.Def(id)); var g = new Gradient(); g.SetColor(0, col); g.SetColor(1, new Color(col, 0));
        Vfx.Sparks(at ?? new Vector3(x + .5f, y + .5f, z + .5f), g, 3, 2.2f, .35f, .35f, -10f, false, 180f, null, .07f, "dirt");
    }
    /// <summary>Why a block cannot go here (null = it can): a dungeon or arena, something solid in the way, a player or monster inside it, or a kept-clear path.</summary>
    public static string? CanPlace(int x, int y, int z)
    {
        var a = BlockWorld.I?.Area; if (a == null) return "No block world here";
        if (NoEdit() is { } no) return no;   // A-07
        byte cur = a.Get(x, y, z); if (cur == 255) return "Too far";
        if (TownZones.Deny(x, z) is { } town) return town;   // F-18 protected town zone
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
    /// <summary>The feel of setting a block down by hand: a soft thud and a small dust puff round its foot in the block's colour.</summary>
    public static void PlacePuff(int x, int y, int z, byte id)
    {
        var col = Blocks.Tint(Blocks.Def(id)).Lerp(new Color(.55f, .5f, .45f), .5f); var g = new Gradient(); g.SetColor(0, new Color(col, .55f)); g.SetColor(1, new Color(col, 0));
        Vfx.Sparks(new Vector3(x + .5f, y + .08f, z + .5f), g, 8, 1.1f, .45f, .4f, -1f, false, 180f, null, .22f, "dirt");
        Sfx.Play("hit", .3f, .75f);
    }
}
