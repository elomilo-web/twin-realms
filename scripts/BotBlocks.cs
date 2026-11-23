using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace TwinRealms;

/// <summary>Bot checks for the block world (tools/run.ps1 bot block): digging, building, water and ice, fire, spell power, dungeons, saving, structures.</summary>
public partial class Bot
{
    async Task<bool> BlocksReady(float timeout = 40f, int ring = 2)
    {
        var bw = BlockWorld.I; float t = 0;
        while (bw != null && !bw.ReadyAround(P.GlobalPosition, ring) && t < timeout) { await Wait(.25f); t += .25f; }
        return bw != null && bw.ReadyAround(P.GlobalPosition, ring);
    }
    /// <summary>The top solid block of a column in the loaded area (y of the block, not of its top face).</summary>
    static int TopOf(BlockArea a, int x, int z) { for (int y = Blocks.MaxY - 1; y > Blocks.MinY; y--) { byte b = a.Get(x, y, z); if (b != 0 && b != 255 && Blocks.Def(b).Solid) return y; } return int.MinValue; }

    async Task BlockChecks()
    {
        var bw = BlockWorld.I; Check("block world exists", bw != null && BlockWorld.Enabled); if (bw == null) return;
        if (Dungeon.Active) Dungeon.Leave(false);
        var spot = World.OnGround(-60, -112, .8f); P.Teleport(spot, 0f); Heal();
        var t0 = Time.GetTicksMsec(); bool ready = await BlocksReady();
        Check("blocks load around the player", ready, $"{(Time.GetTicksMsec() - t0) / 1000f:0.0} s");
        if (!ready) return;
        var a = bw.Area!; await Wait(.5f);
        Check("the smooth ground is cut out where blocks are", BlockWorld.Cut is { } cut && cut.HasPoint(new Vector2(spot.X, spot.Z)));
        await Shot("40_blocks_ground");

        // the map keeps its shape: block tops follow the smooth ground, structures stand where they were
        var gen = new OverworldGen(); int off = 0, n = 0;
        foreach (var poi in World.Pois) { if (Mathf.Abs(poi.X) > World.Half || Mathf.Abs(poi.Z) > World.Half) continue; n++; var c = gen.Column(Mathf.FloorToInt(poi.X), Mathf.FloorToInt(poi.Z)); if (Mathf.Abs(c.top - World.GroundAt(poi.X + .5f, poi.Z + .5f)) > .51f) off++; }
        Check("structures stand on the converted ground where they were", n > 0 && off == 0, $"{off} of {n} places off by more than half a block");

        // dig a block: it breaks and drops
        int bx = Mathf.FloorToInt(spot.X) + 2, bz = Mathf.FloorToInt(spot.Z); int by = TopOf(a, bx, bz);
        byte was = a.Get(bx, by, bz); string drop = Blocks.DropItem(Blocks.Def(was).Drops); int had = Game.D.Mats.GetValueOrDefault(drop);
        Player.BuildMode = true; Player.ForcedTarget = (new Vector3I(bx, by, bz), Vector3I.Up);
        float need = Blocks.DigTime(Blocks.Def(was), Game.D.WType());
        Press(Inputs.Attack, true); await Wait(need + .5f); Press(Inputs.Attack, false); await Wait(.1f);
        Check("digging breaks the block", a.Get(bx, by, bz) == 0, $"{Blocks.Def(was).Name}, {need:0.0} s");
        Check("a dug block drops into the pack", drop == "" || Game.D.Mats.GetValueOrDefault(drop) > had, $"{drop}: {had} -> {Game.D.Mats.GetValueOrDefault(drop)}");

        // place a block and stand on it
        string cob = Blocks.Item("cobblestone"); Game.D.Mats[cob] = Game.D.Mats.GetValueOrDefault(cob) + 5;
        int px = bx - 4, pz = bz + 3, py = TopOf(a, px, pz);
        P.Teleport(new Vector3(px + .5f, py + 1.5f, pz + 3.5f), 0f); await Wait(.4f);
        P.CycleBlock(0); for (int i = 0; i < 20 && P.BlockSelected != "cobblestone"; i++) P.CycleBlock(1);
        Player.ForcedTarget = (new Vector3I(px, py, pz), Vector3I.Up);
        Press(Inputs.Heavy, true); await Wait(.15f); Press(Inputs.Heavy, false); await Wait(.15f);
        bool placed = a.Get(px, py + 1, pz) == Blocks.Id("cobblestone");
        Check("a block can be placed", placed, $"selected {P.BlockSelected}, at {px},{py + 1},{pz}: {Blocks.Def(a.Get(px, py + 1, pz) is var q && q != 255 ? q : (byte)0).Name}");
        Player.ForcedTarget = null; Player.BuildMode = false;
        P.Teleport(new Vector3(px + .5f, py + 3f, pz + .5f), 0f); await Wait(1.2f);
        Check("you can stand on a placed block", placed && P.IsOnFloor() && P.GlobalPosition.Y > py + 1.85f, $"feet at {P.GlobalPosition.Y:0.00}, block top {py + 2}");
        Check("no block can be placed inside the player", BlockFx.CanPlace(Mathf.FloorToInt(P.GlobalPosition.X), Mathf.FloorToInt(P.GlobalPosition.Y + .5f), Mathf.FloorToInt(P.GlobalPosition.Z)) != null);

        // frost turns water to ice you can walk on
        int wx = Mathf.FloorToInt(spot.X) - 6, wz = Mathf.FloorToInt(spot.Z) - 6, wy = TopOf(a, wx, wz);
        for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) { bw.Change(wx + dx, wy + 1, wz + dz, Blocks.Dirt); bw.Change(wx + dx, wy, wz + dz, Blocks.Water); }
        for (int dz = -2; dz <= 2; dz++) for (int dx = -2; dx <= 2; dx++) if (Math.Abs(dx) == 2 || Math.Abs(dz) == 2) bw.Change(wx + dx, wy, wz + dz, Blocks.Stone);   // a rim so it stays a pool
        for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) bw.Change(wx + dx, wy + 1, wz + dz, 0);
        await Wait(.6f);
        int water = 0; for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) if (a.Get(wx + dx, wy, wz + dz) == Blocks.Water) water++;
        BlockFx.Skill(Skills.Compute("nova"), new Vector3(wx + .5f, wy + 1.2f, wz + .5f), 3f);
        int ice = 0; for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) if (a.Get(wx + dx, wy, wz + dz) == Blocks.Ice) ice++;
        Check("a frost spell freezes water into ice", water >= 6 && ice >= 6, $"{water} water, {ice} ice");
        P.Teleport(new Vector3(wx + .5f, wy + 2.5f, wz + .5f), 0f); await Wait(1.2f);
        Check("you can walk on the ice", ice > 0 && P.IsOnFloor() && P.GlobalPosition.Y > wy + .85f, $"feet at {P.GlobalPosition.Y:0.00}, ice top {wy + 1}");
        await Shot("41_blocks_ice");

        // fire sets wood burning and it burns out
        int fx = Mathf.FloorToInt(spot.X) + 6, fz = Mathf.FloorToInt(spot.Z) - 6, fy = TopOf(a, fx, fz) + 1; byte planks = Blocks.Id("planks");
        for (int i = 0; i < 4; i++) for (int k = 0; k < 2; k++) bw.Change(fx + k, fy + i, fz, planks);
        P.Teleport(new Vector3(fx + .5f, fy + .5f, fz + 6.5f), 0f); await Wait(.3f);
        BlockFx.Skill(Skills.Compute("fire"), new Vector3(fx + 1f, fy + 1.5f, fz + .5f), 3f);
        int burning = 0; for (int i = 0; i < 4; i++) for (int k = 0; k < 2; k++) if (bw.Burning(fx + k, fy + i, fz)) burning++;
        Check("a fire spell sets wood burning", burning > 0, $"{burning} of 8 planks burning");
        await Wait(1.5f); await Shot("42_blocks_fire");
        float burn = Blocks.Def(planks).Burn; float tw = 0; int left = 8;
        while (tw < burn * 3 + 6 && left > 0) { await Wait(1f); tw += 1f; left = 0; for (int i = 0; i < 4; i++) for (int k = 0; k < 2; k++) if (a.Get(fx + k, fy + i, fz) == planks) left++; }
        Check("burning wood burns away", left == 0, $"{left} planks left after {tw:0} s");

        // a strong spell breaks more than a weak one; a weak hit does nothing to stone
        int Cube(int cx, int cz)
        {
            int cy = TopOf(a, cx, cz) - 4; int n2 = 0;
            for (int y = cy; y < cy + 5; y++) for (int z = cz - 3; z <= cz + 3; z++) for (int x = cx - 3; x <= cx + 3; x++) { bw.Change(x, y, z, Blocks.Stone); n2++; }
            return cy + 5;
        }
        int Stones(int cx, int cy, int cz) { int s = 0; for (int y = cy - 5; y < cy; y++) for (int z = cz - 3; z <= cz + 3; z++) for (int x = cx - 3; x <= cx + 3; x++) if (a.Get(x, y, z) == Blocks.Stone) s++; return s; }
        int ax = Mathf.FloorToInt(spot.X) - 10, az = Mathf.FloorToInt(spot.Z) + 10, bx2 = ax + 14;
        int ay = Cube(ax, az), by2 = Cube(bx2, az); int sa = Stones(ax, ay, az), sb = Stones(bx2, by2, az);
        var weak = Skills.Compute("fire"); var strong = Skills.Compute("meteor");
        BlockFx.Skill(weak, new Vector3(ax + .5f, ay, az + .5f), weak.Radius);
        BlockFx.Skill(strong, new Vector3(bx2 + .5f, by2, az + .5f), strong.Radius);
        int brokeWeak = sa - Stones(ax, ay, az), brokeStrong = sb - Stones(bx2, by2, az);
        Check("a weak spell does nothing to stone", brokeWeak == 0, $"Firebolt ({weak.Dmg:0} damage) broke {brokeWeak}");
        Check("a strong spell blows a crater", brokeStrong > brokeWeak && brokeStrong >= 8, $"Meteor ({strong.Dmg:0} damage, radius {strong.Radius:0.#}) broke {brokeStrong}");
        P.Teleport(new Vector3(bx2 + .5f, by2 + 3f, az + 8.5f), Mathf.Pi); await Wait(.4f); await Shot("43_blocks_crater");

        // save and load keep the changes
        int sx = Mathf.FloorToInt(spot.X) + 1, sz = Mathf.FloorToInt(spot.Z) + 4, sy = TopOf(a, sx, sz); bw.Change(sx, sy, sz, 0);
        P.Teleport(spot, 0f); Game.Save(); bw.ReloadOverworld(); ready = await BlocksReady();
        a = bw.Area!;
        Check("saving and loading keep dug blocks", ready && a.Get(sx, sy, sz) == 0 && a.Get(px, py + 1, pz) == Blocks.Id("cobblestone"), $"dug {a.Get(sx, sy, sz)}, placed {a.Get(px, py + 1, pz)}");

        // dungeon floors stay finishable
        foreach (var key in new[] { "barrow", "depths" })
        {
            Dungeon.Enter(key, 1); await Wait(1.5f);
            var da = bw.Area; Check($"dungeon {key} is made of blocks", da != null && da.Key.StartsWith("dun:"), da?.Key ?? "none");
            if (da == null) { Dungeon.Leave(false); continue; }
            Enemy? boss = null; foreach (var nd in GetTree().GetNodesInGroup("enemies")) if (nd is Enemy e && e.Def.Boss && e.FromDungeon) boss = e;
            bool Reach()
            {
                if (boss == null) return false;
                var s = new Vector3I(Mathf.FloorToInt(P.GlobalPosition.X), 0, Mathf.FloorToInt(P.GlobalPosition.Z)); var g = new Vector2I(Mathf.FloorToInt(boss.GlobalPosition.X), Mathf.FloorToInt(boss.GlobalPosition.Z));
                var seen = new HashSet<(int, int)> { (s.X, s.Z) }; var q = new Queue<(int, int)>(); q.Enqueue((s.X, s.Z));
                bool Walk(int x, int z) { byte f = da.Get(x, -1, z); return f != 255 && f != 0 && Blocks.Def(f).Solid && da.Get(x, 0, z) == 0 && da.Get(x, 1, z) == 0; }
                while (q.Count > 0 && seen.Count < 60000)
                {
                    var (x, z) = q.Dequeue(); if (Math.Abs(x - g.X) <= 2 && Math.Abs(z - g.Y) <= 2) return true;
                    foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }) { int nx = x + dx, nz = z + dz; if (seen.Add((nx, nz)) && Walk(nx, nz)) q.Enqueue((nx, nz)); }
                }
                return false;
            }
            Check($"dungeon {key}: the boss room can be reached", Reach());
            // a boss stamping and a huge blast in the boss room do not cut the way
            if (boss != null) { BlockFx.Enemy(boss.GlobalPosition + Vector3.Up * .2f, 8f, 5000f, true); BlockFx.Hit(boss.GlobalPosition, 6f, 5000f, "physical", BlockFx.Src.Skill, false); }
            int pathFloor = 0; if (boss != null) for (int dz = -2; dz <= 2; dz++) for (int dx = -2; dx <= 2; dx++) { byte f = da.Get(Mathf.FloorToInt(boss.GlobalPosition.X) + dx, -1, Mathf.FloorToInt(boss.GlobalPosition.Z) + dz); if (f != 0 && f != 255) pathFloor++; }
            Check($"dungeon {key}: blasts do not break the boss room floor", pathFloor == 25 && Reach(), $"{pathFloor}/25 floor blocks");
            await Shot("44_blocks_dungeon_" + key);
            Dungeon.Leave(false); await Wait(1f);
        }
        Check("back in the overworld blocks after a dungeon", bw.Area != null && bw.Area.Key == "overworld");
    }
}
