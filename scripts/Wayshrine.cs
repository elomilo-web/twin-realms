using Godot;
using System.Collections.Generic;

namespace TwinRealms;

// A gothic wayshrine beside each fast-travel POI: plinth, obelisk with a rune band, hovering sigil.
// Dark until the POI is discovered; Wayshrine.Activate(id) lights it live (Hud discovery hook).
public partial class Wayshrine : Node3D
{
    static readonly Dictionary<string, Wayshrine> All = new();
    static Mesh? _plinth, _obe, _band, _sigil; static StandardMaterial3D? _stone, _off, _on; static Mesh? _mote; static StandardMaterial3D? _moteMat;
    bool _blocks; float _sigilY = 4.4f;
    string _id = ""; MeshInstance3D? _bandMi, _sigilMi; OmniLight3D? _light; CpuParticles3D? _motes; float _flash, _t;

    public static void BuildAll(Node3D parent)
    {
        All.Clear();
        _stone = (GothicMats.For("G_Stone") as StandardMaterial3D) ?? new StandardMaterial3D { AlbedoColor = new Color(.45f, .44f, .42f), Roughness = 1f };
        _off = new StandardMaterial3D { AlbedoColor = new Color(.08f, .08f, .1f), Roughness = .9f };
        _on = new StandardMaterial3D { AlbedoColor = new Color(.9f, .75f, .35f), EmissionEnabled = true, Emission = new Color(1f, .8f, .35f), EmissionEnergyMultiplier = 2.2f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        _plinth = new BoxMesh { Size = new Vector3(1.4f, .5f, 1.4f) };
        _obe = new BoxMesh { Size = new Vector3(.45f, 3.2f, .45f) };
        _band = new TorusMesh { InnerRadius = .27f, OuterRadius = .31f, Rings = 12, RingSegments = 4 };
        _sigil = new TorusMesh { InnerRadius = .26f, OuterRadius = .32f, Rings = 16, RingSegments = 4 };
        _mote = new QuadMesh { Size = new Vector2(.07f, .07f) };
        _moteMat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1f, .85f, .45f), BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, VertexColorUseAsAlbedo = true };
        foreach (var p in World.Pois)
        {
            if (p.Icon == "peak" || p.Icon == "lake") { /* still travel targets */ }
            // Oakhaven's plaza centre holds the well and stalls: its shrine stands at the south edge (devloop #10)
            var off = p.Id == "village" ? new Vector2(3f, -12f) : new Vector2(4.5f, 4f);
            float x = p.X + off.X, z = p.Z + off.Y;
            // lakes and shores: walk outward until the ground is above the water
            for (int k = 0; k < 12 && !p.Sky && Lands.WaterAt(x, z) > World.GroundAt(x, z) - .2f; k++) { var dir = off.Normalized(); x += dir.X * 4f; z += dir.Y * 4f; }
            float y = p.Sky ? p.Y : World.GroundAt(x, z);
            bool blk = Structures.On && !p.Sky;   // F-19: plinth and column as blocks (the sky islands have no blocks)
            if (blk)
            {
                int bx = Mathf.FloorToInt(x), bz = Mathf.FloorToInt(z);
                // not into a house, tomb, tent or dungeon mound: walk round the spot in growing rings until a dry 7 x 7 is free
                for (int ring = 0, k = 0; !Structures.Free(bx, bz, 3) && ring < 12; k++)
                {
                    if (k >= 8) { k = 0; ring++; }
                    float a = k / 8f * Mathf.Tau; int tx = Mathf.FloorToInt(x + Mathf.Cos(a) * ring * 3), tz = Mathf.FloorToInt(z + Mathf.Sin(a) * ring * 3);
                    if (Structures.Free(tx, tz, 3) && Lands.WaterAt(tx, tz) < World.GroundAt(tx, tz) - .2f) { bx = tx; bz = tz; }
                }
                y = World.GroundAt(bx + .5f, bz + .5f); int by = Structures.Floor(y);
                Structures.Place(StructGen.Wayshrine(), bx, by, bz); x = bx + .5f; z = bz + .5f; y = by;
            }
            var w = new Wayshrine { _id = p.Id, Position = new Vector3(x, y, z), _blocks = blk };
            parent.AddChild(w); w.Setup(Game.D.Disc.Contains(p.Id)); All[p.Id] = w;
        }
    }

    public static void Activate(string id) { if (All.TryGetValue(id, out var w) && IsInstanceValid(w)) w.Light(true); }

    void Setup(bool lit)
    {
        if (!_blocks)
        {
            var pl = new MeshInstance3D { Mesh = _plinth, MaterialOverride = _stone, Position = Vector3.Up * .25f }; AddChild(pl);
            AddChild(new MeshInstance3D { Mesh = _obe, MaterialOverride = _stone, Position = Vector3.Up * 2.1f });
        }
        else _sigilY = 5.6f;   // the block column (StructGen.Wayshrine) is 4.5 high, the rune stone at 2-3
        _bandMi = new MeshInstance3D { Mesh = _band, Position = Vector3.Up * (_blocks ? 3.05f : 2.3f), Scale = _blocks ? new Vector3(2.4f, 1.6f, 2.4f) : Vector3.One, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off }; AddChild(_bandMi);
        _sigilMi = new MeshInstance3D { Mesh = _sigil, Position = Vector3.Up * _sigilY, RotationDegrees = new Vector3(90, 0, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off }; AddChild(_sigilMi);
        _light = new OmniLight3D { Position = Vector3.Up * (_blocks ? 3.2f : 2.6f), OmniRange = 6f, LightEnergy = 1.2f, LightColor = new Color(1f, .82f, .45f), ShadowEnabled = false, DistanceFadeEnabled = true, DistanceFadeBegin = 70f, DistanceFadeLength = 10f, Visible = false }; AddChild(_light);
        _motes = new CpuParticles3D { Amount = 8, Lifetime = 2.5f, Emitting = false, Mesh = _mote, MaterialOverride = _moteMat, Position = Vector3.Up * .6f, EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere, EmissionSphereRadius = .5f,
            Direction = Vector3.Up, Spread = 10f, InitialVelocityMin = .5f, InitialVelocityMax = .9f, Gravity = Vector3.Zero, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off }; AddChild(_motes);
        Light(lit, false);
    }

    void Light(bool on, bool flash = true)
    {
        var m = on ? _on : _off; if (_bandMi != null) _bandMi.MaterialOverride = m; if (_sigilMi != null) _sigilMi.MaterialOverride = m;
        if (_light != null) _light.Visible = on; if (_motes != null) _motes.Emitting = on;
        if (on && flash) _flash = .8f; SetProcess(on);
    }

    public override void _Process(double delta)
    {
        float d = (float)delta; _t += d;
        if (_sigilMi != null) { _sigilMi.RotateY(d * .8f); _sigilMi.Position = new Vector3(0, _sigilY + Mathf.Sin(_t * 1.5f) * .12f, 0); }
        if (_light != null) { _flash = Mathf.Max(0, _flash - d); _light.LightEnergy = 1.2f + _flash * 6f; }
    }
}
