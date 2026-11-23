using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TwinRealms;

/// <summary>
/// Block world over time: water and lava flow, fire spreads and burns out, frozen blocks thaw. All of it runs in ticks
/// (data/blocks.json world.tick), only in loaded chunks around the player, with a budget per tick, on the host only in multiplayer.
/// Water (Minecraft-like): a source (level 0) feeds flowing water that spreads sideways one level per block up to 'spread', falls
/// straight down without limit, dries up when nothing feeds it, and two sources beside each other over solid ground make a new one,
/// so a hole dug by a lake or the sea fills back up. Lava does the same, shorter and slower; touching water it turns to obsidian
/// (a source) or stone (flowing).
/// </summary>
public partial class BlockWorld
{
    public const byte Falling = 8;
    readonly Queue<long> _fq = new(); readonly HashSet<long> _fset = new(); int _fluidTicks;
    readonly Dictionary<long, float> _burning = new();
    internal readonly Dictionary<long, float> _frozen = new();

    // ---------------------------------------------------------------- every change goes through here
    /// <summary>Changes a block in the current area: wakes the water around it, ends fire or frost on it, and sends it to the others in multiplayer.</summary>
    public bool Change(int x, int y, int z, byte id, byte meta = 0, bool send = true)
    {
        var a = Area; if (a == null) return false;
        if (!a.Set(x, y, z, id, meta)) return false;
        long k = BlockArea.BKey(x, y, z); _burning.Remove(k); _frozen.Remove(k);
        Wake(x, y, z);
        if (send && Net.Active) _out.Add((x, y, z, id, meta));
        return true;
    }
    void Wake(int x, int y, int z)
    {
        void Q(int a, int b, int c) { long k = BlockArea.BKey(a, b, c); if (_fset.Add(k)) _fq.Enqueue(k); }
        Q(x, y, z); Q(x + 1, y, z); Q(x - 1, y, z); Q(x, y + 1, z); Q(x, y - 1, z); Q(x, y, z + 1); Q(x, y, z - 1);
    }
    void FluidReset() { _fq.Clear(); _fset.Clear(); }

    // ---------------------------------------------------------------- fluids
    void FluidTick(BlockArea a)
    {
        _fluidTicks++; int budget = (int)Blocks.F("world.maxFluid", 500); var later = new List<long>();
        while (budget-- > 0 && _fq.Count > 0)
        {
            long k = _fq.Dequeue(); _fset.Remove(k);
            var (x, y, z) = BlockArea.Unkey(k);
            if (!FluidStep(a, x, y, z)) later.Add(k);
        }
        foreach (var k in later) if (_fset.Add(k)) _fq.Enqueue(k);
    }
    static readonly (int, int)[] Side4 = { (1, 0), (-1, 0), (0, 1), (0, -1) };
    /// <summary>One cell's flow. False = come back on a later tick (slow lava).</summary>
    bool FluidStep(BlockArea a, int x, int y, int z)
    {
        byte id = a.Get(x, y, z); if (id == 255 || id == 0) return true;
        var d = Blocks.Def(id); if (!d.IsFluid) return true;
        int slow = Math.Max(1, (int)Blocks.F($"fluids.{d.Fluid}.slow", 1)); if (_fluidTicks % slow != 0) return false;
        int spread = (int)Blocks.F($"fluids.{d.Fluid}.spread", 7);
        byte meta = a.GetMeta(x, y, z); int lvl = meta & 7; bool falling = (meta & Falling) != 0, source = lvl == 0 && !falling;
        // lava meeting water hardens
        if (id == Blocks.Lava)
        {
            bool wet = a.Get(x, y + 1, z) == Blocks.Water; foreach (var (dx, dz) in Side4) wet |= a.Get(x + dx, y, z + dz) == Blocks.Water;
            if (wet) { Change(x, y, z, Blocks.Id(source ? Blocks.Root.GetProperty("fluids").GetProperty("lavaMeetsWater").GetProperty("source").GetString() ?? "obsidian" : "stone")); return true; }
        }
        // still fed? (sources always are)
        if (!source)
        {
            bool fed = a.Get(x, y + 1, z) == id;
            if (!falling && !fed)
                foreach (var (dx, dz) in Side4)
                {
                    if (a.Get(x + dx, y, z + dz) != id) continue; byte nm = a.GetMeta(x + dx, y, z + dz);
                    int nl = (nm & Falling) != 0 ? 0 : nm & 7; if (nl < lvl) { fed = true; break; }
                }
            if (!fed) { Change(x, y, z, 0, 0); return true; }
            // a new source between two sources over solid ground (water only: lakes and the sea refill holes)
            if (id == Blocks.Water && !falling)
            {
                int src = 0; foreach (var (dx, dz) in Side4) if (a.Get(x + dx, y, z + dz) == id && (a.GetMeta(x + dx, y, z + dz) & 15) == 0) src++;
                byte below0 = a.Get(x, y - 1, z);
                if (src >= 2 && (below0 != 0 && below0 != 255 && (Blocks.Def(below0).Solid || (below0 == id && (a.GetMeta(x, y - 1, z) & 15) == 0)))) { Change(x, y, z, id, 0); return true; }
            }
        }
        // fall first
        byte below = a.Get(x, y - 1, z);
        if (below == 255) return true;
        if (below == 0 && !a.Gen.KeepClear(x, y - 1, z)) { Change(x, y - 1, z, id, Falling); return true; }
        if (below == id && (a.GetMeta(x, y - 1, z) & 15) != 0) return true;   // flowing water below keeps falling into itself
        if (below == 0) return true;
        // then spread sideways
        int next = falling ? 1 : lvl + 1; if (next > spread) return true;
        foreach (var (dx, dz) in Side4)
        {
            int nx = x + dx, nz = z + dz; byte nb = a.Get(nx, y, nz);
            if (nb == 255 || a.Gen.KeepClear(nx, y, nz)) continue;
            if (nb == 0) Change(nx, y, nz, id, (byte)next);
            else if (nb == id) { byte nm = a.GetMeta(nx, y, nz); int nl = nm & 7; if ((nm & Falling) == 0 && nl > next) Change(nx, y, nz, id, (byte)next); }
            else if (id == Blocks.Water && nb == Blocks.Lava) Wake(nx, y, nz);
        }
        return true;
    }

    // ---------------------------------------------------------------- fire and frost on blocks
    /// <summary>Sets a flammable block burning. False if it cannot burn or too much is already on fire.</summary>
    public bool Ignite(int x, int y, int z)
    {
        var a = Area; if (a == null) return false; byte id = a.Get(x, y, z); if (id == 0 || id == 255) return false;
        var d = Blocks.Def(id); long k = BlockArea.BKey(x, y, z);
        if (d.Burn <= 0 || _burning.ContainsKey(k) || _burning.Count >= (int)Blocks.F("world.maxBurning", 220)) return false;
        if (Net.IsClient) { _wantFire.Add((x, y, z)); return true; }   // the host runs fire; it sends the result
        _burning[k] = d.Burn; _frozen.Remove(k); return true;
    }
    public bool Burning(int x, int y, int z) => _burning.ContainsKey(BlockArea.BKey(x, y, z));
    public int BurningCount => _burning.Count;
    /// <summary>Frozen blocks: slippery and half as hard until they thaw.</summary>
    public void Freeze(int x, int y, int z, float secs)
    {
        var a = Area; if (a == null) return; byte id = a.Get(x, y, z); if (id == 0 || id == 255 || !Blocks.Def(id).Solid) return;
        long k = BlockArea.BKey(x, y, z); _burning.Remove(k); _frozen[k] = Mathf.Max(_frozen.GetValueOrDefault(k), secs);
    }
    public bool Frozen(int x, int y, int z) => _frozen.ContainsKey(BlockArea.BKey(x, y, z));

    void FireTick(BlockArea a, float dt)
    {
        if (_burning.Count == 0) return;
        float spread = Blocks.F("fire.spread", .18f); bool melt = Blocks.F("fire.meltNear", 1) > 0; int budget = (int)Blocks.F("world.maxFire", 120);
        foreach (var k in _burning.Keys.ToList())
        {
            if (budget-- <= 0) break;
            var (x, y, z) = BlockArea.Unkey(k); byte id = a.Get(x, y, z);
            if (id == 255) continue;
            var d = Blocks.Def(id); if (d.Burn <= 0) { _burning.Remove(k); continue; }
            // water next to it puts it out
            bool wet = false; foreach (var (dx, dy, dz) in Six) if (a.Get(x + dx, y + dy, z + dz) == Blocks.Water) { wet = true; break; }
            if (wet) { _burning.Remove(k); continue; }
            foreach (var (dx, dy, dz) in Six)
            {
                int nx = x + dx, ny = y + dy, nz = z + dz; byte nb = a.Get(nx, ny, nz); if (nb == 0 || nb == 255) continue;
                var nd = Blocks.Def(nb);
                if (nd.Burn > 0 && GD.Randf() < spread * dt) Ignite(nx, ny, nz);
                else if (melt && nd.MeltsTo != "?" && GD.Randf() < .5f * dt) Change(nx, ny, nz, (byte)Math.Max(0, Blocks.To(nd.MeltsTo)));
            }
            float t = _burning[k] - dt;
            if (t <= 0) { _burning.Remove(k); int to = Blocks.To(d.BurnsTo); Change(x, y, z, (byte)Math.Max(0, to)); }
            else _burning[k] = t;
        }
    }
    static readonly (int, int, int)[] Six = { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) };
    void StatusTick(float dt)
    {
        if (_frozen.Count == 0) return;
        foreach (var k in _frozen.Keys.ToList()) { float t = _frozen[k] - dt; if (t <= 0) _frozen.Remove(k); else _frozen[k] = t; }
    }

    // ---------------------------------------------------------------- what statuses look like: flames on burning blocks, a frost shell on frozen ones
    readonly Dictionary<long, Node3D> _fx = new();
    Mesh? _frostMesh; StandardMaterial3D? _frostMat;
    void StatusReady()
    {
        _frostMat = new StandardMaterial3D { AlbedoColor = new Color(.7f, .9f, 1f, .38f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = .05f, EmissionEnabled = true, Emission = new Color(.4f, .7f, 1f), EmissionEnergyMultiplier = .25f, ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel };
        _frostMesh = new BoxMesh { Size = new Vector3(1.04f, 1.04f, 1.04f), Material = _frostMat };
    }
    void ClearStatus() { _burning.Clear(); _frozen.Clear(); foreach (var n in _fx.Values) if (IsInstanceValid(n)) n.QueueFree(); _fx.Clear(); }
    void StatusFx()
    {
        int cap = (int)Blocks.F("world.maxStatusFx", 120);
        // drop the ones no longer burning or frozen
        foreach (var k in _fx.Keys.ToList()) if (!_burning.ContainsKey(k) && !_frozen.ContainsKey(k)) { if (IsInstanceValid(_fx[k])) _fx[k].QueueFree(); _fx.Remove(k); }
        if (_fx.Count >= cap) return;
        foreach (var k in _burning.Keys) { if (_fx.Count >= cap) break; if (!_fx.ContainsKey(k)) _fx[k] = Flame(k); }
        foreach (var k in _frozen.Keys) { if (_fx.Count >= cap) break; if (!_fx.ContainsKey(k)) _fx[k] = Frost(k); }
    }
    Node3D Flame(long k)
    {
        var (x, y, z) = BlockArea.Unkey(k);
        var g = new Gradient(); g.SetColor(0, new Color(1f, .75f, .3f, .9f)); g.AddPoint(.5f, new Color(1f, .35f, .08f, .7f)); g.SetColor(1, new Color(.3f, .1f, .05f, 0));
        var p = new CpuParticles3D { Amount = 10, Lifetime = .6f, Position = new Vector3(x + .5f, y + .6f, z + .5f), EmissionShape = CpuParticles3D.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(.45f, .45f, .45f),
            Direction = Vector3.Up, Spread = 20, Gravity = new Vector3(0, 2.5f, 0), InitialVelocityMin = .3f, InitialVelocityMax = 1f, ColorRamp = g,
            Mesh = new QuadMesh { Size = new Vector2(.55f, .55f), Material = Vfx.SpriteMat(true, "fire") }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(p);
        if (_fx.Count % 6 == 0) p.AddChild(new OmniLight3D { LightColor = new Color(1f, .55f, .2f), OmniRange = 5f, LightEnergy = 1.2f, Position = Vector3.Up * .3f });
        return p;
    }
    Node3D Frost(long k)
    {
        var (x, y, z) = BlockArea.Unkey(k);
        var m = new MeshInstance3D { Mesh = _frostMesh, Position = new Vector3(x + .5f, y + .5f, z + .5f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(m); return m;
    }
}
