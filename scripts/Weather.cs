using Godot;

namespace TwinRealms;

/// <summary>Weather: clear / overcast / rain, re-rolled every few minutes (data/look.json "weather"). Rain is 0..1 and is read by WorldEnv.UpdateVisuals
/// for the mood. Draws camera-following rain streaks and ground splashes. Never in dungeons; the shadow realm keeps its ash motes (Ambient) instead.</summary>
public partial class Weather : Node3D
{
    /// <summary>Rain strength 0..1 (already faded out in the shadow realm and dungeons).</summary>
    public static float Rain;
    /// <summary>Cloudiness 0..1 (overcast or rain).</summary>
    public static float Cloud;
    /// <summary>DevCapture: lock the weather to this rain level (negative = release).</summary>
    public static void Force(float rain) { _forced = rain; }
    static float _forced = -1f;
    /// <summary>Lightning flash 0..1 (brief, decays on its own); WorldEnv may add it to sun/ambient energy.</summary>
    public static float Flash;

    AudioStreamPlayer? _rainP, _lightP; float _rainLv, _lightLv, _thunderIn = 60f, _thunderDelay = -1f;
    static AudioStream? LoadLoop(string path)
    {
        if (!ResourceLoader.Exists(path)) return null;
        var s = GD.Load<AudioStream>(path);
        if (s is AudioStreamWav w) { w.LoopMode = AudioStreamWav.LoopModeEnum.Forward; w.LoopBegin = 0; w.LoopEnd = w.Data.Length / (w.Stereo ? 4 : 2); }
        return s;
    }
    static AudioStreamPlayer? MakeLoop(Node parent, string id)
    {
        var s = LoadLoop($"res://assets/audio/sfx/{id}.wav"); if (s == null) return null;
        var p = new AudioStreamPlayer { Stream = s, VolumeDb = -80f, Bus = Sfx.BusAmbient }; parent.AddChild(p);   // F-39: the Ambient slider is that bus's volume
        p.Finished += () => p.Play();   // safety net if the loop flag is ignored
        return p;
    }
    static void Fade(AudioStreamPlayer? p, ref float lv, float target, float amb, float dt)
    {
        if (p == null) return;
        lv = Mathf.MoveToward(lv, target, dt / 2f);
        float v = lv * amb;
        if (v < .003f) { if (p.Playing) p.Stop(); p.VolumeDb = -80f; return; }
        if (!p.Playing) p.Play();
        p.VolumeDb = Mathf.LinearToDb(v);
    }
    void TickAudio(float dt)
    {
        float amb = Game.Set.Ambient, r = Rain;
        // -18 dB at .2, -6 dB at 1; silent below .05
        float g = r < .05f ? 0f : Mathf.Lerp(Mathf.DbToLinear(-18f), Mathf.DbToLinear(-6f), Mathf.Clamp((r - .2f) / .8f, 0f, 1f));
        if (r < .2f && r >= .05f) g = Mathf.DbToLinear(-18f) * (r - .05f) / .15f;
        float light = 1f - Mathf.Clamp((r - .2f) / .25f, 0f, 1f);   // light loop at low intensity, full loop above ~.45
        if (Dungeon.Active || amb <= 0f) { g = 0f; }
        Fade(_rainP, ref _rainLv, g * (1f - light), 1f, dt);
        Fade(_lightP, ref _lightLv, g * light, 1f, dt);

        Flash = Mathf.MoveToward(Flash, 0f, dt * 2.5f);
        if (_thunderDelay >= 0f)
        {
            _thunderDelay -= dt;
            if (_thunderDelay < 0f && Sfx.Has("thunder")) Sfx.Play("thunder", .8f);
            if (_thunderDelay < 0f) _thunderDelay = -1f;
        }
        else if (r > .7f && !Dungeon.Active && amb > 0f)
        {
            _thunderIn -= dt;
            if (_thunderIn <= 0f) { _thunderIn = _rg.RandfRange(40f, 120f); Flash = .6f; _thunderDelay = _rg.RandfRange(.5f, 3f); }
        }
    }

    float _target, _rain, _over, _overTarget, _timer = 120f, _clear = .6f, _overC = .25f, _min = 360f, _max = 900f, _fade = 40f;
    int _streaks = 1500, _splashes = 150;
    CpuParticles3D _drops = null!, _splash = null!;
    readonly RandomNumberGenerator _rg = new();

    public override void _Ready()
    {
        _rg.Randomize(); _thunderIn = _rg.RandfRange(40f, 120f);
        _rainP = MakeLoop(this, "rain_loop"); _lightP = MakeLoop(this, "rain_light_loop");
        using var doc = JsonUtil.Load("res://data/look.json");
        if (doc.RootElement.TryGetProperty("weather", out var w))
        {
            _clear = JsonUtil.F(w, "clear", .6f); _overC = JsonUtil.F(w, "overcast", .25f); _min = JsonUtil.F(w, "minSeconds", 360f); _max = JsonUtil.F(w, "maxSeconds", 900f);
            _fade = Mathf.Max(1f, JsonUtil.F(w, "fadeSeconds", 40f)); _streaks = (int)JsonUtil.F(w, "streaks", 1500); _splashes = (int)JsonUtil.F(w, "splashes", 150);
            _timer = JsonUtil.F(w, "firstAfter", 120f);
        }
        // streaks: thin stretched quads, pale grey-blue, additive, low alpha
        var rc = new Color(.66f, .74f, .86f, .32f);
        var rmat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add, VertexColorUseAsAlbedo = true, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, CullMode = BaseMaterial3D.CullModeEnum.Disabled, NoDepthTest = false };
        _drops = new CpuParticles3D
        {
            Amount = Mathf.Max(1, _streaks), Lifetime = 1f, Emitting = false, Visible = false, LocalCoords = false, Preprocess = 1f, EmissionShape = CpuParticles3D.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(15, 10, 15),
            Direction = new Vector3(.12f, -1f, .05f).Normalized(), Spread = 2f, Gravity = Vector3.Zero, InitialVelocityMin = 22f, InitialVelocityMax = 28f, Color = rc,
            Mesh = new QuadMesh { Size = new Vector2(.012f, .7f), Material = rmat }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_drops);
        // splashes: tiny short-lived puffs just above the ground near the camera
        var g = new Gradient(); var sc = new Color(.75f, .82f, .92f); g.SetColor(0, new Color(sc, .0f)); g.SetColor(1, new Color(sc, 0)); g.AddPoint(.15f, new Color(sc, .5f));
        _splash = new CpuParticles3D
        {
            Amount = Mathf.Max(1, _splashes), Lifetime = .35f, Emitting = false, Visible = false, LocalCoords = false, Preprocess = .35f, EmissionShape = CpuParticles3D.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(10, .05f, 10),
            Direction = Vector3.Up, Spread = 60, Gravity = new Vector3(0, -6f, 0), InitialVelocityMin = .5f, InitialVelocityMax = 1.2f, ColorRamp = g,
            Mesh = new QuadMesh { Size = new Vector2(.06f, .06f), Material = Vfx.SpriteMat() }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_splash);
    }

    void Roll()
    {
        float r = _rg.Randf();
        if (r < _clear) { _target = 0; _overTarget = 0; }
        else if (r < _clear + _overC) { _target = 0; _overTarget = 1; }
        else { _target = 1; _overTarget = 1; }
        _timer = _rg.RandfRange(_min, _max);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        bool dungeon = Dungeon.Active;
        if (_forced >= 0f) { _rain = _forced; _over = _forced; }
        else
        {
            _timer -= dt; if (_timer <= 0f) Roll();
            if (dungeon) { _target = 0; _overTarget = 0; }
            _rain = Mathf.MoveToward(_rain, _target, dt / _fade); _over = Mathf.MoveToward(_over, _overTarget, dt / _fade);
        }
        float vis = dungeon ? 0f : 1f - Mathf.Clamp(World.RealmT * 1.5f, 0f, 1f);   // ash replaces rain in the shadow realm
        Rain = _rain * vis; Cloud = Mathf.Max(_over, _rain) * vis;
        TickAudio(dt);

        bool on = Rain > .02f && World.I != null && World.I.Over.Visible;
        if (_drops.Visible != on) { _drops.Visible = _splash.Visible = on; _drops.Emitting = _splash.Emitting = on; }
        if (!on) return;
        var cam = GetViewport().GetCamera3D(); if (cam == null) return;
        var cp = cam.GlobalPosition;
        _drops.GlobalPosition = cp + Vector3.Up * 8f;
        // CpuParticles3D has no AmountRatio (GPU only): intensity shows through the streaks' alpha instead
        _drops.Color = new Color(.66f, .74f, .86f, .32f * Mathf.Clamp(Rain, .15f, 1f));
        var sp = new Vector3(cp.X, World.GroundAt(cp.X, cp.Z) + .05f, cp.Z); _splash.GlobalPosition = sp;
    }
}
