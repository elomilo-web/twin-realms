using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;

namespace TwinRealms;

/// <summary>
/// Focus F-14..F-17 captures ("blocks:doors", "blocks:chest", "blocks:stairs", "blocks:torches"): a flat block pad in front of the hero
/// (-Z for yaw 0) with the scene built on it, plus logged checks ([F14]..[F17] lines): NPC door use, locks, chest save round trip,
/// stair placement metas, hero and villager walking up stairs, torch light flicker.
/// </summary>
public partial class DevCapture
{
    readonly List<Node> _devNodes = new();
    int _bx, _bz, _L;
    BlockWorld W => BlockWorld.I!;
    BlockArea Ar => BlockWorld.I!.Area!;
    void SetB(int dx, int dy, int dz, string key, byte meta = 0) => W.Change(_bx + dx, _L + dy, _bz + dz, Blocks.Id(key), meta);
    /// <summary>A flat pad: stone below the hero's feet row, air above (x0..x1, z0..z1 relative to the hero's cell).</summary>
    void Pad(int x0, int x1, int z0, int z1)
    {
        _bx = Mathf.FloorToInt(P.GlobalPosition.X); _bz = Mathf.FloorToInt(P.GlobalPosition.Z); _L = Mathf.FloorToInt(P.GlobalPosition.Y + .05f);
        for (int x = x0; x <= x1; x++) for (int z = z0; z <= z1; z++)
        {
            SetB(x, -2, z, "stone"); SetB(x, -1, z, "cobblestone");
            for (int y = 0; y <= 5; y++) SetB(x, y, z, "");
        }
    }
    Vector3 C(float dx, float dy, float dz) => new(_bx + dx, _L + dy, _bz + dz);
    Npc DevNpc(string name, Vector3 at)
    {
        var n = new Npc { Id = "dev_" + name, NpcName = name, Role = "talk", Position = at }; Game.Root!.AddChild(n); _devNodes.Add(n); return n;
    }
    void DevBlocksCleanup()
    {
        DoorSwing.Hold = -1f; DoorSwing.FinishAll(); Player.ForcedUpper = false;
        foreach (var n in _devNodes) if (GodotObject.IsInstanceValid(n)) n.QueueFree();
        _devNodes.Clear();
    }

    async Task BlocksAction(string what, float castWait)
    {
        if (BlockWorld.I?.Area == null) { Say("blocks: no block world"); return; }
        for (int i = 0; i < 40 && !W.ReadyAround(P.GlobalPosition); i++) await Secs(.1f);
        var m = Game.D.Mats; foreach (var k in new[] { "planks", "cobblestone", "torch", "door", "door_dark", "door_iron", "chest", "planks_stairs", "cobble_stairs", "planks_slab", "cobble_slab" }) if (Blocks.Exists(k)) m[Blocks.Item(k)] = 20;
        var rec = Blocks.Root.GetProperty("recipes");
        string Rec(string k) => rec.TryGetProperty(k, out var r) ? $"{k} <- {string.Join(" + ", r.GetProperty("cost").EnumerateObject().Select(c => c.Value + " " + c.Name))}" : k + " NO RECIPE";
        switch (what)
        {
            case "doors": await Doors(Rec); break;
            case "chest": await ChestView(Rec); break;
            case "stairs": await Stairs(Rec); break;
            case "torches": await Torches(Rec); break;
        }
    }

    // ---------------------------------------------------------------- F-14 doors: three materials in a wall, NPC door use, locks, one mid-swing
    async Task Doors(Func<string, string> rec)
    {
        Say($"[F14] recipes: {rec("door")} | {rec("door_dark")} | {rec("door_iron")}");
        // the wall stands at +Z (its sunlit side faces the hero and the camera), doors hinged on that side (meta 3)
        Pad(-5, 5, -2, 8);
        for (int x = -4; x <= 4; x++) for (int y = 0; y <= 2; y++) SetB(x, y, 4, "cobblestone");
        var doors = new[] { (-2, "door_dark"), (0, "door"), (2, "door_iron"), (4, "door"), (-4, "door_dark") };   // the villager uses the dark door; the oak one swings in the picture
        foreach (var (x, k) in doors) { SetB(x, 0, 4, k, 3); SetB(x, 1, 4, k, 3 | 4); SetB(x, 2, 4, ""); BlockUse.Placed(Ar, _bx + x, _L, _bz + 4, Game.D.Name); }   // no lintel: the sun reaches the panels
        await Secs(.5f);
        // a villager walks through the oak door: it opens when the villager reaches it and shuts behind
        var oak = new Vector3I(_bx - 2, _L, _bz + 4); byte closedId = Ar.Get(oak.X, oak.Y, oak.Z);
        var npc = DevNpc("Walker", C(-1.5f, 0, 1.5f)); await Frames(3);
        npc.WalkTo(C(-1.5f, 0, 7.5f));
        bool sawOpen = false; float tOpen = -1, tClosed = -1, t = 0;
        while (t < 8f)
        {
            await Secs(.1f); t += .1f;
            bool open = BlockUse.IsOpen(Ar.Get(oak.X, oak.Y, oak.Z));
            if (open && !sawOpen) { sawOpen = true; tOpen = t; Say($"[F14] npc door: opened at {t:0.0}s, villager at z {npc.GlobalPosition.Z - _bz:0.00} (door cell z 4..5)"); }
            if (sawOpen && !open && tClosed < 0) { tClosed = t; Say($"[F14] npc door: closed again at {t:0.0}s, villager at z {npc.GlobalPosition.Z - _bz:0.00}"); }
            if (npc.Goal == null && (tClosed > 0 || !sawOpen)) break;
        }
        Say($"[F14] npc door result: opened {sawOpen}, closed after {tClosed >= 0}, villager past the wall {npc.GlobalPosition.Z - _bz > 5.5f} (z {npc.GlobalPosition.Z - _bz:0.00}), stopped '{npc.Stopped}', block back to {Blocks.Def(Ar.Get(oak.X, oak.Y, oak.Z)).Key} (was {Blocks.Def(closedId).Key})");
        // locks: the iron door locked by its owner; a stranger and a villager are refused, the owner opens it
        var iron = new Vector3I(_bx + 2, _L, _bz + 4);
        BlockUse.SetLock(Ar, iron, true);
        string? stranger = BlockUse.UseDoor(Ar, iron.X, iron.Y + 1, iron.Z, "Stranger", false);
        var npc2 = DevNpc("Guest", C(2.5f, 0, 1.5f)); await Frames(3); npc2.WalkTo(C(2.5f, 0, 7.5f));
        for (int i = 0; i < 40 && npc2.Goal != null; i++) await Secs(.1f);
        Say($"[F14] lock: owner '{BlockUse.Owner(Ar, iron)}' locked {BlockUse.Locked(Ar, iron)} (both halves {(Ar.GetMeta(iron.X, iron.Y + 1, iron.Z) & BlockUse.LockBit) != 0}); stranger: '{stranger ?? "opened!"}'; villager stopped '{npc2.Stopped}' at z {npc2.GlobalPosition.Z - _bz:0.00}; "
            + $"owner may open {BlockUse.MayOpen(Ar, iron, Game.D.Name, false)}, protected from blows {BlockUse.Protects(Ar, iron.X, iron.Y, iron.Z)}, CanLockpick hook {BlockUse.CanLockpick(iron, P)}");
        foreach (var n in _devNodes) if (GodotObject.IsInstanceValid(n)) n.QueueFree(); _devNodes.Clear();
        // the picture: dark door open, oak door caught mid-swing, iron door shut and locked
        BlockUse.SetDoor(Ar, oak.X, oak.Y, oak.Z, true); await Secs(.4f);
        string before = Blocks.Def(Ar.Get(_bx + 4, _L, _bz + 4)).Key;
        DoorSwing.Hold = .35f; bool swung = BlockUse.SetDoor(Ar, _bx + 4, _L, _bz + 4, true);   // the oak door at the wall's end (in the sun)
        int swings = W.GetChildren().OfType<DoorSwing>().Count();
        Say($"[F14] swing start: {before} -> {Blocks.Def(Ar.Get(_bx + 4, _L, _bz + 4)).Key}, SetDoor {swung}, live {DoorSwing.LiveCount}");
        Say($"[F14] swing: {swings} door halves animating (held at {DoorSwing.Hold:P0} of {DoorSwing.Dur} s), hidden in the chunk mesh {BlockMesher.Hidden.Count}, angles {string.Join(" ", W.GetChildren().OfType<DoorSwing>().Select(s => $"{Mathf.RadToDeg(s.Rotation.Y):0}"))}");
        await Secs(.4f);
    }

    // ---------------------------------------------------------------- F-15 chest: placed, filled, saved and read back, window open
    async Task ChestView(Func<string, string> rec)
    {
        Say($"[F15] recipe: {rec("chest")}");
        Pad(-4, 4, -6, 2);
        SetB(0, 0, -2, "chest", 2); BlockUse.Placed(Ar, _bx, _L, _bz - 2, Game.D.Name);
        SetB(-2, 0, -3, "chest", 2); SetB(2, 0, -3, "chest", 0);
        var c = new Vector3I(_bx, _L, _bz - 2); string key = BlockUse.Key(Ar, c.X, c.Y, c.Z);
        var items = BlockUse.ChestItems(key); items.Clear();
        var slots = new[] { "weapon", "armor", "shield", "head", "hands", "feet", "cape", "trinket" };
        for (int i = 0; i < 11; i++) items.Add(Items.Roll(8 + i * 2, 0, slots[i % slots.Length], i % 5));
        // persistence: the hero as Game.Save writes it, into a capture-only file, read back
        BlockWorld.Store(Game.D);
        var opt = new JsonSerializerOptions { IncludeFields = true };
        string path = ProjectSettings.GlobalizePath("user://devcap_chest_save.json");
        System.IO.File.WriteAllText(path, JsonSerializer.Serialize(Game.D, opt));
        var back = JsonSerializer.Deserialize<PlayerStats>(System.IO.File.ReadAllText(path), opt)!;
        string a = JsonSerializer.Serialize(Game.D.Chests[key], opt), b = back.Chests.TryGetValue(key, out var bl) ? JsonSerializer.Serialize(bl, opt) : "";
        var area = new BlockArea("overworld", new OverworldGen()); area.Decode(back.BlockDiffs.GetValueOrDefault("overworld", ""));
        bool blockSaved = area.Diff.TryGetValue(BlockArea.BKey(c.X, c.Y, c.Z), out var v) && (v & 255) == Blocks.Id("chest");
        Say($"[F15] chest persistence: {items.Count} items saved under '{key}', reloaded {bl?.Count ?? 0}, equal {a == b} ({a.Length} chars); chest block in the saved diff {blockSaved}; owner reloaded '{back.BlockOwners.GetValueOrDefault(key, "")}'");
        BlockUse.SetLock(Ar, c, true);
        Say($"[F15] lock: owner opens {BlockUse.MayOpen(Ar, c, Game.D.Name, false)}, stranger {BlockUse.MayOpen(Ar, c, "Stranger", false)}, protected {BlockUse.Protects(Ar, c.X, c.Y, c.Z)}, CanLockpick hook {BlockUse.CanLockpick(c, P)}");
        BlockUse.SetLock(Ar, c, false);
        await Secs(.4f);
        if (Ui.I != null) Ui.I.Visible = true;
        BlockUse.OpenChest(Ar, c);
        await Secs(.4f);
    }

    // ---------------------------------------------------------------- F-16 stairs: 4 directions, upside down, slabs; hero and villager walk up
    async Task Stairs(Func<string, string> rec)
    {
        Say($"[F16] recipes: {rec("planks_stairs")} | {rec("cobble_stairs")} | {rec("planks_slab")}");
        Pad(-6, 6, -8, 2);
        // placement rule (PlayerBlocks.PlaceMeta): facing from the camera, upside down against a ceiling or high on a wall
        Player.ForcedTarget = (new Vector3I(_bx, _L, _bz - 3), Vector3I.Zero);
        Player.ForcedUpper = false; byte mSide = P.DevPlaceMeta("stairs", new Vector3I(0, 0, 1)), mFloor = P.DevPlaceMeta("stairs", Vector3I.Up), mCeil = P.DevPlaceMeta("stairs", Vector3I.Down);
        Player.ForcedUpper = true; byte mHigh = P.DevPlaceMeta("stairs", new Vector3I(0, 0, 1)), sHigh = P.DevPlaceMeta("slab", new Vector3I(0, 0, 1));
        Player.ForcedTarget = null; Player.ForcedUpper = false;
        Say($"[F16] place metas: floor {mFloor} (dir {mFloor & 3}, upside down {(mFloor & 4) != 0}), ceiling {mCeil} (upside down {(mCeil & 4) != 0}), wall low {mSide} ({(mSide & 4) != 0}), wall high {mHigh} ({(mHigh & 4) != 0}), slab high {sHigh} (upper {(sHigh & 4) != 0})");
        // four directions (left), upside down + slabs (right)
        SetB(-5, 0, -1, "planks_stairs", 0); SetB(-3, 0, -1, "planks_stairs", 1); SetB(-5, 0, -3, "planks_stairs", 2); SetB(-3, 0, -3, "planks_stairs", 3);
        SetB(3, 0, -2, "cobblestone"); SetB(3, 1, -2, "cobble_stairs", 0 | 4); SetB(4, 0, -2, "planks_stairs", 1 | 4); SetB(5, 0, -2, "planks_stairs", 2 | 4);
        SetB(3, 0, -4, "planks_slab"); SetB(4, 0, -4, "planks_slab", 4); SetB(5, 0, -4, "cobble_slab");
        // two staircases side by side rising towards -Z (meta 3): the hero's at x 0, the villager's at x -1
        foreach (int sx in new[] { 0, -1 })
            for (int s = 0; s < 4; s++) { for (int y = 0; y < s; y++) SetB(sx, y, -2 - s, "cobblestone"); SetB(sx, s, -2 - s, sx == 0 ? "planks_stairs" : "cobble_stairs", 3); }
        await Secs(.6f);
        // the hero walks up without jumping: forward for a while, height sampled
        P.Teleport(C(.5f, 0, .5f), 0f); var rigRot = P.Rig!.Rotation; P.Rig.Rotation = new Vector3(rigRot.X, 0f, 0f); await Secs(.3f);
        var ys = new List<string>(); bool jumped = false; var p0 = P.GlobalPosition;
        Input.ActionPress(Inputs.Forward);
        for (int i = 0; i < 16; i++) { await Secs(.08f); ys.Add($"{P.GlobalPosition.Y - _L:0.00}"); if (P.Velocity.Y > 3f) jumped = true; }
        Input.ActionRelease(Inputs.Forward); P.Rig.Rotation = rigRot;
        Say($"[F16] hero walk (feet above pad): {string.Join(" ", ys)}; rose {P.GlobalPosition.Y - p0.Y:0.00} m over dz {P.GlobalPosition.Z - p0.Z:0.00}, jump-speed seen {jumped}");
        // a villager walks up the other staircase
        var npc = DevNpc("Climber", C(-.5f, 0, .5f)); await Frames(3); npc.WalkTo(C(-.5f, 4f, -4.7f));
        var ny = new List<string>();
        for (int i = 0; i < 40 && npc.Goal != null; i++) { await Secs(.12f); ny.Add($"{npc.GlobalPosition.Y - _L:0.00}"); }
        Say($"[F16] villager walk: {string.Join(" ", ny)}; reached {(npc.Goal == null && npc.Stopped == "" ? "the top" : "stopped '" + npc.Stopped + "'")} at height {npc.GlobalPosition.Y - _L:0.00}");
        // the picture: villager halfway, hero on the second step
        npc.Goal = null; npc.GlobalPosition = C(-.5f, 2.5f, -3.25f);
        P.Teleport(C(.5f, 1.52f, -2.3f), 0f); await Secs(.4f);
        Say($"[F16] picture: hero feet at {P.GlobalPosition.Y - _L:0.00} (second step top 1.5), on floor {P.IsOnFloor()}");
    }

    // ---------------------------------------------------------------- F-17 torches at night: on the floor and on walls, flickering
    async Task Torches(Func<string, string> rec)
    {
        Say($"[F17] recipe: {rec("torch")}");
        Pad(-5, 5, -7, 2);
        for (int x = -4; x <= 4; x++) for (int y = 0; y <= 2; y++) SetB(x, y, -4, "cobblestone");
        SetB(4, 0, -1, "cobblestone"); SetB(4, 1, -1, "cobblestone");
        // wall torches: the wall is on -Z of the torch cell (side 3 -> meta 4), one on a pillar's west face (wall on +X: side 0 -> meta 1)
        SetB(-2, 1, -3, "torch", 4); SetB(2, 1, -3, "torch", 4); SetB(3, 1, -1, "torch", 1);
        SetB(-1, 0, -1, "torch", 0); SetB(1, 0, -2, "torch", 0);
        SetB(0, 0, -3, "chest", 2);   // the chest model in torchlight (its window covers it in blocks_chest)
        await Secs(1f);
        var lights = W.GetChildren().OfType<Torch>().Where(t => t.GlobalPosition.DistanceTo(P.GlobalPosition) < 10f).ToList();
        int flames = lights.Count(t => t.Flames);
        var ls = lights.Select(t => t.GetChildren().OfType<OmniLight3D>().FirstOrDefault()).Where(l => l != null).ToList();
        var samples = new List<string>();
        for (int i = 0; i < 6 && ls.Count > 0; i++) { var l = ls[0]!; samples.Add($"{l.LightEnergy:0.00}/{l.LightColor.G:0.00}"); await Secs(.07f); }
        Say($"[F17] torches: {lights.Count} lights near the hero ({flames} with flame particles), flames at {string.Join(" ", lights.Select(t => { var q = t.GlobalPosition - C(0, 0, 0); return $"({q.X:0.00},{q.Y:0.00},{q.Z:0.00})"; }))}; flicker energy/green of one light: {string.Join(" ", samples)}");
    }
}
