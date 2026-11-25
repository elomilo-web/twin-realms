using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// Scenery life that follows the player (no gameplay): butterflies over meadows by day, fireflies at night,
/// drifting violet spores in the Shadow realm, and bird flocks circling high overhead. Tuning in data/ambient.json.
/// </summary>
public partial class Ambient : Node3D
{
    class Fly { public Node3D N = null!; public Node3D L = null!, R = null!; public Vector3 Home, Target; public float Ph, Speed; }
    class Bird { public Node3D N = null!; public Node3D L = null!, R = null!; public float Ang, Off, Ph; public Vector3 Centre; }
    readonly List<Fly> _flies = new(); readonly List<Bird> _birds = new();
    CpuParticles3D _fireflies = null!, _spores = null!;
    float _r = 22, _maxH = 14, _night = .45f, _bH = 34, _bR = 45, _bSpeed = .12f;
    readonly RandomNumberGenerator _rg = new();

    public override void _Ready()
    {
        using var doc = JsonUtil.Load("res://data/ambient.json"); var r = doc.RootElement;
        int nFly = 14, nFire = 90, nSpore = 70, flocks = 3, per = 6; float box = 22, sbox = 24; var cols = new List<Color>();
        if (r.TryGetProperty("butterflies", out var b)) { nFly = (int)JsonUtil.F(b, "count", 14); _r = JsonUtil.F(b, "radius", 22); _maxH = JsonUtil.F(b, "maxHeight", 14); if (b.TryGetProperty("colors", out var cs)) foreach (var c in cs.EnumerateArray()) cols.Add(Color.FromHtml(c.GetString() ?? "ffffff")); }
        if (r.TryGetProperty("fireflies", out var f)) { nFire = (int)JsonUtil.F(f, "amount", 90); box = JsonUtil.F(f, "box", 22); _night = JsonUtil.F(f, "night", .45f); }
        if (r.TryGetProperty("spores", out var s)) { nSpore = (int)JsonUtil.F(s, "amount", 70); sbox = JsonUtil.F(s, "box", 24); }
        if (r.TryGetProperty("birds", out var bd)) { flocks = (int)JsonUtil.F(bd, "flocks", 3); per = (int)JsonUtil.F(bd, "perFlock", 6); _bH = JsonUtil.F(bd, "height", 34); _bR = JsonUtil.F(bd, "radius", 45); _bSpeed = JsonUtil.F(bd, "speed", .12f); }
        if (cols.Count == 0) cols.Add(Colors.White);

        // butterflies: a body and two wing quads that flap
        var wing = new QuadMesh { Size = new Vector2(.26f, .22f), CenterOffset = new Vector3(.13f, 0, 0) };
        var body = new StandardMaterial3D { AlbedoColor = new Color(.1f, .08f, .06f) };
        for (int i = 0; i < nFly; i++)
        {
            var c = cols[i % cols.Count];
            var wm = new StandardMaterial3D { AlbedoColor = c, CullMode = BaseMaterial3D.CullModeEnum.Disabled, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = .25f };
            var n = new Node3D(); AddChild(n);
            n.AddChild(new MeshInstance3D { Mesh = new CapsuleMesh { Radius = .015f, Height = .1f }, MaterialOverride = body, Rotation = new Vector3(Mathf.Pi / 2, 0, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            var L = new Node3D(); var R = new Node3D(); n.AddChild(L); n.AddChild(R);
            L.AddChild(new MeshInstance3D { Mesh = wing, MaterialOverride = wm, Rotation = new Vector3(-Mathf.Pi / 2, 0, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            R.AddChild(new MeshInstance3D { Mesh = wing, MaterialOverride = wm, Rotation = new Vector3(-Mathf.Pi / 2, Mathf.Pi, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            _flies.Add(new Fly { N = n, L = L, R = R, Ph = _rg.Randf() * 10, Speed = _rg.RandfRange(1.2f, 2.2f) });
        }
        // birds: dark chevrons circling
        var bm = new StandardMaterial3D { AlbedoColor = new Color(.12f, .12f, .14f), CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        var bw = new QuadMesh { Size = new Vector2(.7f, .22f), CenterOffset = new Vector3(.35f, 0, 0) };
        for (int fl = 0; fl < flocks; fl++)
        {
            var centre = new Vector3(_rg.RandfRange(-60, 60), 0, _rg.RandfRange(-60, 60)); float a0 = _rg.Randf() * Mathf.Tau;
            for (int i = 0; i < per; i++)
            {
                var n = new Node3D(); AddChild(n); var L = new Node3D(); var R = new Node3D(); n.AddChild(L); n.AddChild(R);
                L.AddChild(new MeshInstance3D { Mesh = bw, MaterialOverride = bm, Rotation = new Vector3(-Mathf.Pi / 2, 0, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
                R.AddChild(new MeshInstance3D { Mesh = bw, MaterialOverride = bm, Rotation = new Vector3(-Mathf.Pi / 2, Mathf.Pi, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
                _birds.Add(new Bird { N = n, L = L, R = R, Ang = a0 - i * .07f, Off = (i % 2 == 0 ? 1 : -1) * (i / 2) * 1.6f, Ph = _rg.Randf() * 6, Centre = centre });
            }
        }
        // fireflies and spores: soft additive particles in a box that moves with the player
        CpuParticles3D Motes(int amount, float bx, Color col, float size, float life)
        {
            var g = new Gradient(); g.SetColor(0, new Color(col, 0)); g.SetColor(1, new Color(col, 0)); g.AddPoint(.3f, new Color(col, 1)); g.AddPoint(.7f, new Color(col, .9f));
            var p = new CpuParticles3D
            {
                Amount = amount, Lifetime = life, Emitting = false, LocalCoords = false, Preprocess = life, EmissionShape = CpuParticles3D.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(bx, 2.2f, bx),
                Gravity = new Vector3(0, .05f, 0), Direction = Vector3.Up, Spread = 180, InitialVelocityMin = .1f, InitialVelocityMax = .5f, ColorRamp = g,
                Mesh = new QuadMesh { Size = new Vector2(size, size), Material = Vfx.SpriteMat() }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(p); return p;
        }
        _fireflies = Motes(nFire, box, new Color(1f, .9f, .4f), .14f, 4f);
        _spores = Motes(nSpore, sbox, new Color(.8f, .45f, 1f), .2f, 6f);
        // shadow realm: slow falling ash / soul motes around the camera (soft grey-violet, alpha) plus a few faint additive glowers, and a weak cold rim light
        {
            var ag = new Gradient(); var ac = new Color(.62f, .6f, .74f); ag.SetColor(0, new Color(ac, 0)); ag.SetColor(1, new Color(ac, 0)); ag.AddPoint(.2f, new Color(ac, .55f)); ag.AddPoint(.8f, new Color(ac, .4f));
            var amat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, VertexColorUseAsAlbedo = true, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
            _ash = new CpuParticles3D
            {
                Amount = 225, Lifetime = 14f, Emitting = false, Visible = false, LocalCoords = false, Preprocess = 14f, EmissionShape = CpuParticles3D.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(20, 7.5f, 20),
                Gravity = new Vector3(0, -.12f, 0), Direction = Vector3.Down, Spread = 180, InitialVelocityMin = .05f, InitialVelocityMax = .3f, TangentialAccelMin = -.15f, TangentialAccelMax = .15f,   // CpuParticles3D has no turbulence (GPU only): a little swirl instead
                ColorRamp = ag, Mesh = new QuadMesh { Size = new Vector2(.07f, .07f), Material = amat }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(_ash);
            _soul = Motes(25, 20f, new Color(.7f, .6f, 1f), .12f, 9f); _soul.EmissionBoxExtents = new Vector3(20, 7.5f, 20); _soul.Visible = false;
            _rim = new DirectionalLight3D { ShadowEnabled = false, LightEnergy = 0, LightColor = new Color(.78f, .74f, 1f), Visible = false, LightSpecular = .5f }; AddChild(_rim);
        }
        foreach (var fl in _flies) Reseat(fl, Player.Instance?.GlobalPosition ?? Vector3.Zero, true);
    }
    CpuParticles3D _ash = null!, _soul = null!; DirectionalLight3D _rim = null!;

    // ------------------------------------------------------------------ soundscape (data/ambient.json "soundscape")
    sealed class Cond { public string Time = "", Realm = ""; public string[] Biome = System.Array.Empty<string>(), NotBiome = System.Array.Empty<string>(); public float Water = -1, Alt = -1; public bool Place; }
    sealed class Entry { public string Id = ""; public float Vol; public float P0 = 1, P1 = 1; public Cond C = new(); }
    readonly List<Entry> _layers = new(), _shots = new();
    int _maxLayers = 3; float _every0 = 8, _every1 = 25, _d0 = 15, _d1 = 40, _altSea = .25f, _scan, _shotT = 6; bool _sndLoaded;
    // surroundings, refreshed twice a second
    string _biome = ""; bool _nearWater, _inPlace; float _alt;

    static string[] Strs(System.Text.Json.JsonElement e, string k) { var l = new List<string>(); if (e.TryGetProperty(k, out var a) && a.ValueKind == System.Text.Json.JsonValueKind.Array) foreach (var s in a.EnumerateArray()) l.Add((s.GetString() ?? "").ToLowerInvariant()); return l.ToArray(); }
    void LoadSoundscape(System.Text.Json.JsonElement root)
    {
        _sndLoaded = true; if (!root.TryGetProperty("soundscape", out var ss)) return;
        Sfx.FadeSecs = JsonUtil.F(ss, "fade", 2.5f); _maxLayers = (int)JsonUtil.F(ss, "maxLayers", 3); _altSea = JsonUtil.F(ss, "altSea", .25f);
        void Pair(string k, ref float a, ref float b) { if (ss.TryGetProperty(k, out var e) && e.ValueKind == System.Text.Json.JsonValueKind.Array && e.GetArrayLength() >= 2) { a = (float)e[0].GetDouble(); b = (float)e[1].GetDouble(); } }
        Pair("every", ref _every0, ref _every1); Pair("dist", ref _d0, ref _d1);
        void Read(string k, List<Entry> into, string idKey)
        {
            if (!ss.TryGetProperty(k, out var arr) || arr.ValueKind != System.Text.Json.JsonValueKind.Array) return;
            foreach (var e in arr.EnumerateArray())
            {
                var en = new Entry { Id = e.TryGetProperty(idKey, out var i) ? i.GetString() ?? "" : "", Vol = JsonUtil.F(e, "vol", .5f) };
                if (e.TryGetProperty("pitch", out var pa) && pa.ValueKind == System.Text.Json.JsonValueKind.Array && pa.GetArrayLength() >= 2) { en.P0 = (float)pa[0].GetDouble(); en.P1 = (float)pa[1].GetDouble(); }
                if (e.TryGetProperty("when", out var w))
                {
                    en.C.Time = w.TryGetProperty("time", out var t) ? t.GetString() ?? "" : ""; en.C.Realm = w.TryGetProperty("realm", out var r) ? r.GetString() ?? "" : "";
                    en.C.Biome = Strs(w, "biome"); en.C.NotBiome = Strs(w, "notBiome"); en.C.Water = JsonUtil.F(w, "water", -1); en.C.Alt = JsonUtil.F(w, "alt", -1);
                    en.C.Place = w.TryGetProperty("place", out var pl) && pl.ValueKind == System.Text.Json.JsonValueKind.True;
                }
                if (en.Id != "") into.Add(en);
            }
        }
        Read("layers", _layers, "file"); Read("oneshots", _shots, "id");
    }

    /// <summary>0..1 how strongly an entry applies right now (time and realm blend smoothly; the other conditions are on/off).</summary>
    float Weight(Cond c, float night, float shadow)
    {
        float w = 1;
        if (c.Time == "day") w *= 1 - night; else if (c.Time == "night") w *= night;
        if (c.Realm == "light") w *= 1 - shadow; else if (c.Realm == "shadow") w *= shadow;
        if (c.Biome.Length > 0 && !HasAny(c.Biome)) return 0;
        if (c.NotBiome.Length > 0 && HasAny(c.NotBiome)) return 0;
        if (c.Water >= 0 && !_nearWater) return 0;
        if (c.Alt >= 0 && _alt < c.Alt) return 0;
        if (c.Place && !_inPlace) return 0;
        return w;
    }
    bool HasAny(string[] subs) { foreach (var s in subs) if (_biome.Contains(s)) return true; return false; }

    void Survey(Vector3 pp)
    {
        _biome = (Lands.LandAt(pp.X, pp.Z)?.Default ?? "meadow").ToLowerInvariant(); _alt = pp.Y - _altSea;
        _inPlace = false; foreach (var poi in World.Pois) if (new Vector2(poi.X - pp.X, poi.Z - pp.Z).Length() < poi.R) { _inPlace = true; break; }
        _nearWater = false;
        for (int ring = 0; ring < 4 && !_nearWater; ring++)
        {
            float d = ring * 8.5f; int n = ring == 0 ? 1 : 8;
            for (int k = 0; k < n; k++) { float a = k * Mathf.Tau / n; float x = pp.X + Mathf.Cos(a) * d, z = pp.Z + Mathf.Sin(a) * d; if (World.GroundAt(x, z) < Lands.WaterAt(x, z) - .05f) { _nearWater = true; break; } }
        }
    }

    void Soundscape(float dt, Vector3 pp)
    {
        if (!Sfx.AmbienceOn || Sfx.I == null) return;
        if (!_sndLoaded) { using var doc = JsonUtil.Load("res://data/ambient.json"); LoadSoundscape(doc.RootElement); }
        if (_layers.Count == 0) return;
        float shadow = World.RealmT, night = Mathf.Clamp((World.Night - shadow * .7f) / Mathf.Max(.01f, 1f - shadow), 0, 1);
        _scan -= dt; if (_scan <= 0) { _scan = .5f; Survey(pp); Mix(night, shadow); }
        _shotT -= dt;
        if (_shotT <= 0)
        {
            _shotT = _rg.RandfRange(_every0, _every1);
            var ok = new List<Entry>(); foreach (var s in _shots) if (Weight(s.C, night, shadow) > .5f && Sfx.Has(s.Id)) ok.Add(s);
            if (ok.Count > 0)
            {
                var e = ok[_rg.RandiRange(0, ok.Count - 1)]; float a = _rg.Randf() * Mathf.Tau, d = _rg.RandfRange(_d0, _d1);
                var at = pp + new Vector3(Mathf.Cos(a) * d, 0, Mathf.Sin(a) * d); at.Y = Mathf.Max(World.GroundAt(at.X, at.Z), _altSea) + (e.Id is "bird_chirp" or "owl" ? 4f : 1f);
                Sfx.PlayAt(this, e.Id, at, e.Vol, _rg.RandfRange(e.P0, e.P1));
            }
        }
    }

    /// <summary>Per file the loudest applicable entry; only the loudest maxLayers files get a target, the rest fade out.</summary>
    void Mix(float night, float shadow)
    {
        var best = new Dictionary<string, float>();
        foreach (var l in _layers) { float g = l.Vol * Weight(l.C, night, shadow); best[l.Id] = Mathf.Max(best.GetValueOrDefault(l.Id), g); }
        var keys = new List<string>(best.Keys); keys.Sort((a, b) => best[b].CompareTo(best[a]));
        for (int i = 0; i < keys.Count; i++) Sfx.SetLayer(keys[i], i < _maxLayers ? best[keys[i]] : 0f);
    }

    void Reseat(Fly f, Vector3 around, bool snap)
    {
        float a = _rg.Randf() * Mathf.Tau, d = _rg.RandfRange(4, _r); var p = around + new Vector3(Mathf.Cos(a) * d, 0, Mathf.Sin(a) * d);
        p.Y = World.GroundAt(p.X, p.Z) + _rg.RandfRange(.4f, 1.6f); f.Home = p; f.Target = p; if (snap) f.N.GlobalPosition = p;
    }

    void SetRealmFx(float rt)
    {
        bool on = rt > .02f;
        if (_ash.Visible != on) { _ash.Visible = _soul.Visible = _rim.Visible = on; _ash.Emitting = _soul.Emitting = on; }
        if (!on) { _rim.LightEnergy = 0; return; }
        var cam = GetViewport().GetCamera3D(); if (cam == null) return;
        var cp = cam.GlobalPosition; _ash.GlobalPosition = cp + Vector3.Up * 2f; _soul.GlobalPosition = cp + Vector3.Up * 2f;
        _ash.Color = new Color(1, 1, 1, rt);   // fades the whole emitter with the realm blend
        _soul.Color = new Color(1, 1, 1, rt);
        // rim: light travels from behind the subject toward the camera (opposite the view direction)
        _rim.GlobalTransform = new Transform3D(cam.GlobalTransform.Basis.Orthonormalized().Rotated(Vector3.Up, Mathf.Pi), cp); _rim.LightEnergy = .35f * rt;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta; var pl = Player.Instance; if (pl == null) return;
        bool over = !Dungeon.Active && World.I != null && World.I.Over.Visible; Visible = over; if (!over) { _fireflies.Emitting = _spores.Emitting = false; SetRealmFx(0); return; }
        SetRealmFx(World.RealmT);
        Soundscape(dt, pl.GlobalPosition);
        var pp = pl.GlobalPosition; float t = (float)Time.GetTicksMsec() / 1000f;
        bool shadow = World.RealmT > .5f; float night = World.Night;
        // particles follow the player
        _fireflies.GlobalPosition = pp + Vector3.Up * 1.2f; _spores.GlobalPosition = pp + Vector3.Up * 1.5f;
        _fireflies.Emitting = !shadow && night > _night; _spores.Emitting = shadow;
        // butterflies: daytime in the Light, on low meadows only; they wander around a home point and get reseated near the player
        bool day = !shadow && night < .35f;
        foreach (var f in _flies)
        {
            f.N.Visible = day && f.Home.Y < _maxH + 2;
            if ((f.Home - pp).Length() > _r * 1.6f || !f.N.Visible && day) { Reseat(f, pp, true); continue; }
            if (!f.N.Visible) continue;
            f.Ph += dt; var cur = f.N.GlobalPosition;
            if ((f.Target - cur).Length() < .3f) { var o = new Vector3(_rg.RandfRange(-2.5f, 2.5f), 0, _rg.RandfRange(-2.5f, 2.5f)); f.Target = f.Home + o; f.Target.Y = World.GroundAt(f.Target.X, f.Target.Z) + _rg.RandfRange(.3f, 1.5f); }
            var dir = (f.Target - cur).Normalized(); var np = cur + dir * f.Speed * dt + new Vector3(0, Mathf.Sin(f.Ph * 9f) * .6f * dt, 0);
            f.N.GlobalPosition = np; if (dir.LengthSquared() > .01f) f.N.Rotation = new Vector3(0, Mathf.Atan2(-dir.X, -dir.Z), 0);
            float flap = Mathf.Sin(f.Ph * 28f) * 1.1f; f.L.Rotation = new Vector3(0, 0, flap + .2f); f.R.Rotation = new Vector3(0, 0, -flap - .2f);
        }
        // birds: wide circles high above, wings beating then gliding; hidden at night and in the Shadow
        bool birds = !shadow && night < .55f;
        foreach (var b in _birds)
        {
            b.N.Visible = birds; if (!birds) continue;
            b.Ang += _bSpeed * dt; b.Ph += dt;
            var c = new Vector3(pp.X + b.Centre.X * .5f, 0, pp.Z + b.Centre.Z * .5f); float rr = _bR + b.Off;
            var pos = c + new Vector3(Mathf.Cos(b.Ang) * rr, _bH + Mathf.Sin(b.Ang * 3 + b.Off) * 2f + Mathf.Abs(b.Off) * .4f, Mathf.Sin(b.Ang) * rr);
            b.N.GlobalPosition = pos; var tan = new Vector3(-Mathf.Sin(b.Ang), 0, Mathf.Cos(b.Ang)); b.N.Rotation = new Vector3(0, Mathf.Atan2(-tan.X, -tan.Z), -.25f);
            float beat = Mathf.Sin(b.Ph * 7f) > -.2f ? Mathf.Sin(b.Ph * 14f) * .6f : .1f; b.L.Rotation = new Vector3(0, 0, beat); b.R.Rotation = new Vector3(0, 0, -beat);
        }
    }
}
