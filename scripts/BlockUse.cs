using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TwinRealms;

/// <summary>
/// Placed blocks you use (focus F-14, F-15): doors (2 tall, open/close with a swing, lockable by the one who placed them) and chests
/// (their own storage window, contents saved with the hero in PlayerStats.Chests, lockable the same way). Owners live in
/// PlayerStats.BlockOwners under "area|x,y,z" (a door under its lower half); the lock itself is meta bit 8 of the block, so it syncs in
/// multiplayer with the block. Locked blocks open only for their owner or the world's owner (admin = playing alone or hosting).
/// A visiting friend without the owner entry counts as a stranger. Lockpicking is focus F-25: <see cref="CanLockpick"/> is its hook.
/// </summary>
public static class BlockUse
{
    public const byte LockBit = 8;
    public static string Key(BlockArea a, int x, int y, int z) => $"{a.Key}|{x},{y},{z}";
    static BlockArea? A => BlockWorld.I?.Area;
    /// <summary>The world's owner may open anything: playing alone or hosting.</summary>
    public static bool Admin => !Net.IsClient;
    public static string Me => Game.D.Name;

    // ---------------------------------------------------------------- owners and locks
    public static bool IsDoor(byte id) => id != 0 && id != 255 && Blocks.Def(id).Shape == "door";
    public static bool IsChest(byte id) => id != 0 && id != 255 && Blocks.Def(id).Shape == "chest";
    /// <summary>The cell that carries a door's or chest's owner and lock (a door's lower half).</summary>
    public static Vector3I Anchor(BlockArea a, int x, int y, int z) => IsDoor(a.Get(x, y, z)) && (a.GetMeta(x, y, z) & 4) != 0 ? new Vector3I(x, y - 1, z) : new Vector3I(x, y, z);
    public static string Owner(BlockArea a, Vector3I c) => (Game.D.BlockOwners ??= new()).GetValueOrDefault(Key(a, c.X, c.Y, c.Z), "");
    public static void SetOwner(BlockArea a, Vector3I c, string who) { if (who == "") (Game.D.BlockOwners ??= new()).Remove(Key(a, c.X, c.Y, c.Z)); else (Game.D.BlockOwners ??= new())[Key(a, c.X, c.Y, c.Z)] = who; }
    public static bool Locked(BlockArea a, Vector3I c) => (a.GetMeta(c.X, c.Y, c.Z) & LockBit) != 0;
    /// <summary>May this one open (or break) the door/chest at c? Unlocked: anyone. Locked: its owner, or an admin.</summary>
    public static bool MayOpen(BlockArea a, Vector3I c, string who, bool admin) => !Locked(a, c) || admin || (who != "" && Owner(a, c) == who);
    /// <summary>Only its owner (or an admin) may lock or unlock it; a block nobody owns (built before F-14) can be claimed by locking it.</summary>
    public static bool MayLock(BlockArea a, Vector3I c, string who, bool admin) { string o = Owner(a, c); return admin || o == "" || o == who; }
    /// <summary>F-25 hook: whether this hero can try to pick the lock at c (skill, lockpicks in the pack). No lockpicking yet.</summary>
    public static bool CanLockpick(Vector3I c, Node3D who) => false;
    /// <summary>F-25 hook: a successful pick opens the lock (it stays unlocked until its owner locks it again).</summary>
    public static void Lockpicked(Vector3I c) { if (A is { } a) SetLock(a, c, false); }
    /// <summary>Locked blocks cannot be broken by blows, spells or monsters (only their owner digs them out by hand).</summary>
    public static bool Protects(BlockArea a, int x, int y, int z) { byte id = a.Get(x, y, z); return (IsDoor(id) || IsChest(id)) && Locked(a, Anchor(a, x, y, z)); }

    /// <summary>Sets or clears the lock on a door (both halves) or a chest.</summary>
    public static void SetLock(BlockArea a, Vector3I c, bool on)
    {
        var w = BlockWorld.I; if (w == null) return;
        void One(int y) { byte id = a.Get(c.X, y, c.Z); if (!IsDoor(id) && !IsChest(id)) return; byte m = a.GetMeta(c.X, y, c.Z); w.Change(c.X, y, c.Z, id, (byte)(on ? m | LockBit : m & ~LockBit)); }
        One(c.Y); if (IsDoor(a.Get(c.X, c.Y, c.Z))) One(c.Y + 1);
        Sfx.Play("lock", .8f, on ? .9f : 1.15f);
    }

    /// <summary>Just placed by this hero: they own it.</summary>
    public static void Placed(BlockArea a, int x, int y, int z, string who) { byte id = a.Get(x, y, z); if (IsDoor(id) || IsChest(id)) SetOwner(a, Anchor(a, x, y, z), who); }
    /// <summary>A door or chest went away (dug, blown up, burnt): forget its owner; a chest spills what it held.</summary>
    public static void Gone(BlockArea a, int x, int y, int z, byte oldId, byte oldMeta)
    {
        var d = Game.D; string k = Key(a, x, y, z);
        if ((oldMeta & 4) == 0) d.BlockOwners?.Remove(k);
        if (Blocks.Def(oldId).Shape != "chest" || d.Chests == null || !d.Chests.TryGetValue(k, out var items)) return;
        d.Chests.Remove(k); var at = new Vector3(x + .5f, y + .6f, z + .5f);
        foreach (var it in items) Loot.Drop(at, item: it);
        if (items.Count > 0) Ui.Note($"The chest spills {items.Count} item{(items.Count == 1 ? "" : "s")}", Ui.Dim);
    }

    // ---------------------------------------------------------------- doors
    public static bool IsOpen(byte id) => Blocks.Def(id).Key.EndsWith("_open");
    /// <summary>The same door in the other state (door &lt;-&gt; door_open), 0 if the data has no twin.</summary>
    public static byte Twin(byte id) { var k = Blocks.Def(id).Key; return Blocks.Id(k.EndsWith("_open") ? k[..^5] : k + "_open"); }

    /// <summary>Opens or closes the door at (x, y, z), both halves, with the swing and its sound. False when there is nothing to change.</summary>
    public static bool SetDoor(BlockArea a, int x, int y, int z, bool open)
    {
        var w = BlockWorld.I; if (w == null) return false;
        var c = Anchor(a, x, y, z); byte lid = a.Get(c.X, c.Y, c.Z);
        if (!IsDoor(lid) || IsOpen(lid) == open) return false;
        byte nid = Twin(lid); if (nid == 0) return false;
        for (int hy = c.Y; hy <= c.Y + 1; hy++)
        {
            byte id = a.Get(c.X, hy, c.Z); if (!IsDoor(id)) continue; byte m = a.GetMeta(c.X, hy, c.Z);
            DoorSwing.Start(a, c.X, hy, c.Z, nid, m);
            w.Change(c.X, hy, c.Z, nid, m);
        }
        bool iron = Blocks.Def(lid).Sound == "iron";
        Sfx.Play(iron ? "door_iron" : open ? "door_open" : "door_close", .8f, iron ? (open ? 1f : .85f) : 1f);
        return true;
    }
    /// <summary>The hero (or anyone named who) uses a door: null when it opened/closed, else why not.</summary>
    public static string? UseDoor(BlockArea a, int x, int y, int z, string who, bool admin)
    {
        var c = Anchor(a, x, y, z);
        if (!MayOpen(a, c, who, admin)) { Sfx.Play("lock", .6f, .7f); return $"Locked{(Owner(a, c) is { Length: > 0 } o ? " by " + o : "")}"; }
        SetDoor(a, x, y, z, !IsOpen(a.Get(c.X, c.Y, c.Z)));
        return null;
    }
    /// <summary>A closed door in a walker's way (the cell ahead at its feet or knees), or null.</summary>
    public static Vector3I? DoorAhead(Vector3 feet, Vector3 dir, float reach = .65f)
    {
        var a = A; if (a == null) return null; var p = feet + dir * reach;
        foreach (float h in new[] { .3f, 1.2f })
        {
            int x = Mathf.FloorToInt(p.X), y = Mathf.FloorToInt(feet.Y + h), z = Mathf.FloorToInt(p.Z); byte id = a.Get(x, y, z);
            if (IsDoor(id) && !IsOpen(id)) return Anchor(a, x, y, z);
        }
        return null;
    }

    // ---------------------------------------------------------------- chests
    public static List<Item> ChestItems(string key) { var d = Game.D.Chests ??= new(); if (!d.TryGetValue(key, out var l)) d[key] = l = new List<Item>(); return l; }
    /// <summary>The hero opens the chest at c: its window, or a note why not.</summary>
    public static void OpenChest(BlockArea a, Vector3I c)
    {
        if (!MayOpen(a, c, Me, Admin)) { Sfx.Play("lock", .6f, .7f); Ui.Note($"Locked{(Owner(a, c) is { Length: > 0 } o ? " by " + o : "")}", new Color(1f, .6f, .5f)); return; }
        Sfx.Play("chest_open", .8f);
        var def = Blocks.Def(a.Get(c.X, c.Y, c.Z));
        Ui.I?.OpenChest(Key(a, c.X, c.Y, c.Z), def.Name, Math.Max(1, def.Capacity));
    }
}

/// <summary>A door half swinging open or shut over a quarter second (the chunk mesh leaves that cell out meanwhile, BlockMesher.Hidden).</summary>
public partial class DoorSwing : Node3D
{
    public const float Dur = .25f;
    /// <summary>Captures: every swing holds at this fraction (0..1) instead of finishing; below 0 = normal.</summary>
    public static float Hold = -1f;
    static readonly Dictionary<long, DoorSwing> _live = new();
    public static int LiveCount => _live.Count;
    BlockArea _a = null!; int _x, _z; long _key; float _t, _from, _delta;

    // the panel's line in the cell for a wall side (0 +X, 1 -X, 2 +Z, 3 -Z): x or z of its middle
    static float Line(int dir) => dir switch { 0 => .94f, 1 => .06f, 2 => .94f, _ => .06f };
    static int OpenDir(int dir) => dir switch { 0 => 2, 2 => 1, 1 => 3, _ => 0 };   // the same 90 degree turn as BlockMesher
    static Vector2 Mid(int dir) => dir < 2 ? new Vector2(Line(dir), .5f) : new Vector2(.5f, Line(dir));

    /// <summary>Starts the swing of the half at (x, y, z) that becomes block toId (meta keeps the wall side).</summary>
    public static void Start(BlockArea a, int x, int y, int z, byte toId, byte meta)
    {
        var w = BlockWorld.I; if (w == null || !BlockWorld.Enabled) return;
        long key = BlockArea.BKey(x, y, z);
        if (_live.TryGetValue(key, out var old) && IsInstanceValid(old)) old.Finish();
        int dc = meta & 3, dop = OpenDir(dc); bool opening = BlockUse.IsOpen(toId);
        var hinge = new Vector2(dc < 2 ? Line(dc) : Line(dop), dc < 2 ? Line(dop) : Line(dc));
        float Yaw(Vector2 m) { var d = m - hinge; return Mathf.Atan2(-d.X, -d.Y); }
        float aC = Yaw(Mid(dc)), aO = Yaw(Mid(dop));
        var s = new DoorSwing { _a = a, _x = x, _z = z, _key = key, Position = new Vector3(x + hinge.X, y, z + hinge.Y), Name = "DoorSwing" };
        s._from = opening ? aC : aO; s._delta = Mathf.Wrap((opening ? aO : aC) - s._from, -Mathf.Pi, Mathf.Pi);
        s.Rotation = new Vector3(0, s._from, 0);
        var def = Blocks.Def(toId); var mat = PanelMat(toId);
        s.AddChild(new MeshInstance3D { Mesh = Box(new Vector3(.12f, 1f, 1f)), MaterialOverride = mat, Position = new Vector3(0, .5f, -.44f) });
        if ((meta & 4) == 0) s.AddChild(new MeshInstance3D { Mesh = Box(new Vector3(.22f, .08f, .08f)), MaterialOverride = mat, Position = new Vector3(0, .94f, -.78f) });   // the handle
        w.AddChild(s); _live[key] = s;
        BlockMesher.Hidden[key] = 1;
    }
    // the door's own side material (as in the chunk mesh, which multiplies per-vertex light): the boxes carry a vertex colour like the mesher's faces
    static Material? PanelMat(byte id) => Blocks.Material(Blocks.Def(id), Blocks.Face.Side);
    static ArrayMesh Box(Vector3 size)   // made per swing (24 vertices; a static cache would outlive the renderer at exit)
    {
        var arr = new BoxMesh { Size = size }.SurfaceGetArrays(0);
        var nrm = (Vector3[])arr[(int)Mesh.ArrayType.Normal]; var cols = new Color[nrm.Length];
        for (int i = 0; i < cols.Length; i++) { float l = nrm[i].Y > .5f ? 1f : nrm[i].Y < -.5f ? .58f : .86f; cols[i] = new Color(l, l, l); }
        arr[(int)Mesh.ArrayType.Color] = cols;
        var m = new ArrayMesh(); m.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr); return m;
    }
    public override void _Process(double delta)
    {
        _t += (float)delta / Dur;
        float k = Hold >= 0 ? Hold : Mathf.Min(1f, _t), e = 1f - (1f - k) * (1f - k);   // eases out: a push, then it settles
        Rotation = new Vector3(0, _from + _delta * e, 0);
        if (Hold < 0 && _t >= 1f) Finish();
    }
    void Finish()
    {
        BlockMesher.Hidden.TryRemove(_key, out _); _live.Remove(_key);
        if (_a.ChunkAt(_x, _z) is { } c) BlockWorld.I?.MarkDirty(_a, c);
        QueueFree();
    }
    /// <summary>Captures: ends every swing at once (after a held frame).</summary>
    public static void FinishAll() { foreach (var s in _live.Values.ToList()) if (IsInstanceValid(s)) s.Finish(); _live.Clear(); }
}

/// <summary>
/// Walking over the blocks without jumping (focus F-16): the hero, monsters and pets step up half a block (stairs, slabs, a full block from a
/// slab's top) in <see cref="StepUp"/>; villagers walk the block surface (<see cref="GroundY"/>), stairs and slabs included.
/// </summary>
public static class BlockStep
{
    /// <summary>Top of what one can stand on in cell (x, y, z) at the point (px, pz), or null (air, fluid, doors, fences, torches).</summary>
    public static float? TopAt(BlockArea a, int x, int y, int z, float px, float pz)
    {
        byte id = a.Get(x, y, z); if (id == 0 || id == 255) return null; var d = Blocks.Def(id); if (!d.Solid || d.IsFluid) return null;
        byte m = a.GetMeta(x, y, z);
        switch (d.Shape)
        {
            case "slab": return y + ((m & 4) != 0 ? 1f : .5f);
            case "stairs":
            {
                if ((m & 4) != 0) return y + 1f;   // upside down: flat on top
                float lx = px - x, lz = pz - z;
                bool high = (m & 3) switch { 0 => lx >= .5f, 1 => lx < .5f, 2 => lz >= .5f, _ => lz < .5f };
                return y + (high ? 1f : .5f);
            }
            case "chest": return y + .86f;
            case "": return y + 1f;
            default: return null;
        }
    }
    static bool Blocks_(BlockArea a, int x, int y, int z) { byte b = a.Get(x, y, z); if (b == 0 || b == 255) return false; var d = Blocks.Def(b); return d.Solid && !d.IsFluid; }

    /// <summary>Walking into a stair, a slab (or a full block from a slab's top): a half-block step is climbed without jumping. Returns the rise (0 = none).</summary>
    public static float StepUp(CharacterBody3D body, Vector3 v, float reach = .42f, float height = 1.75f)
    {
        if (!body.IsOnFloor() || v.Y > .1f || BlockWorld.I?.Area is not { } a) return 0;
        var hv = new Vector3(v.X, 0, v.Z); if (hv.LengthSquared() < .25f) return 0;
        var p = body.GlobalPosition + hv.Normalized() * reach; float feet = body.GlobalPosition.Y;
        int cx = Mathf.FloorToInt(p.X), cy = Mathf.FloorToInt(feet + .05f), cz = Mathf.FloorToInt(p.Z);
        if (TopAt(a, cx, cy, cz, p.X, p.Z) is not float top) return 0;
        float rise = top - feet; if (rise < .04f || rise > .56f) return 0;
        for (int y = Mathf.CeilToInt(top - .01f); y <= Mathf.FloorToInt(top + height); y++) if (Blocks_(a, cx, y, cz)) return 0;
        body.GlobalPosition += Vector3.Up * (rise + .01f);
        return rise + .01f;
    }

    /// <summary>
    /// Where a walker whose feet are at height feet stands at (x, z): the block surface there within a half-block step up (or any drop down to
    /// 3 blocks), or the smooth ground outside the block square. Null when the way is blocked (a wall, a closed door, no headroom).
    /// </summary>
    public static float? GroundY(float x, float z, float feet, float height = 1.7f)
    {
        var a = BlockWorld.I?.Area;
        if (a == null || !BlockWorld.Drawn(x, z)) return World.GroundAt(x, z);
        int bx = Mathf.FloorToInt(x), bz = Mathf.FloorToInt(z), start = Mathf.FloorToInt(feet + .56f);
        for (int y = start; y >= start - 4; y--)
        {
            byte b = a.Get(bx, y, bz); if (b == 255) return World.GroundAt(x, z);
            if (TopAt(a, bx, y, bz, x, z) is float top)
            {
                if (top - feet > .58f) return null;   // too high a step
                for (int hy = Math.Max(y + 1, Mathf.FloorToInt(top + .01f)); hy <=Mathf.FloorToInt(top + height); hy++) if (Blocks_(a, bx, hy, bz)) return null;
                return top;
            }
            if (Blocks_(a, bx, y, bz)) return null;   // a fence, a closed door: in the way
        }
        return null;
    }
}
