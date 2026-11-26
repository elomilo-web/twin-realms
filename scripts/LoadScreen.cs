using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace TwinRealms;

/// <summary>
/// The loading screen: a dark dusk skyline (or a given picture) under a slow Ken Burns drift, drifting embers, the place name in
/// letter-spaced gold, a thin glowing progress bar with the current stage, and a rotating gameplay tip (data/tips.json).
/// Shown over everything (layer 120) while the world is built and behind long teleports (dungeons, the arena, fast travel, waking
/// at the resting place); it lifts once the blocks around the hero exist and there is ground under the feet. No input is needed:
/// while it is up the hero is frozen (Player.Frozen) and key / mouse / pad events are swallowed.
/// Sync callers (the world build, a door teleport) call <see cref="Draw"/> to put a frame on screen while the main thread is busy.
/// </summary>
public partial class LoadScreen : CanvasLayer
{
    public static LoadScreen? I;
    /// <summary>Off for the test bots (Main): every call then does nothing and "after" callbacks run at once.</summary>
    public static bool On = true;
    /// <summary>Up or still fading out.</summary>
    public static bool Busy => I != null && IsInstanceValid(I) && (I._want || I._alpha > 0f);
    /// <summary>Wanted up (not yet lifting).</summary>
    public static bool Covering => I != null && IsInstanceValid(I) && I._want;

    static readonly Color Gold = new(.85f, .69f, .36f), GoldDim = new(.62f, .5f, .28f), Parch = new(.86f, .81f, .69f), Muted = new(.62f, .58f, .5f), Iron = new(.16f, .15f, .14f, .85f);
    const float FadeIn = .25f, FadeOut = .4f, TipEvery = 7f;

    Control _root = null!;
    ColorRect _bg = null!;
    ShaderMaterial _bgMat = null!;
    Label _title = null!, _sub = null!, _stage = null!, _pct = null!;
    RichTextLabel _tip = null!;
    Control _track = null!, _fill = null!, _glow = null!, _head = null!;
    CpuParticles2D _embers = null!;
    bool _want; float _alpha, _target, _shown, _tipT, _tipA = 1f; int _gen, _tipI = -1;
    ulong _last, _shownAt;
    static readonly List<Action> _after = new();
    static readonly List<TaskCompletionSource> _waiters = new();
    static List<string>? _tips;

    static float Secs => Time.GetTicksMsec() / 1000f;

    // ================================================================ API
    /// <summary>Puts the screen up (fading in .25 s, or at once with instant). A new title resets the progress.</summary>
    public static void Show(string title, string subtitle = "", Texture2D? art = null, bool instant = false)
    {
        var s = I; if (!On || s == null || !IsInstanceValid(s)) return;
        s._gen++; s._want = true; s._shownAt = Time.GetTicksMsec();
        s._title.Text = Spaced(title.ToUpper()); s._sub.Text = subtitle.ToUpper(); s._sub.Visible = subtitle != "";
        s._target = s._shown = 0f; s._stage.Text = ""; s._pct.Text = "";
        s._bgMat.SetShaderParameter("art", art); s._bgMat.SetShaderParameter("has_art", art != null);
        s._bgMat.SetShaderParameter("seed", (float)GD.RandRange(0.0, 50.0));
        s.NextTip(); s._tipA = 1f; s._tipT = 0f;
        if (instant) s._alpha = 1f;
        s._root.Visible = true; s.Apply();
        Player.Frozen = true;
    }
    /// <summary>0..1 and the stage text ("Raising the land"); null keeps the current text.</summary>
    public static void SetProgress(float p, string? stage)
    {
        var s = I; if (s == null || !IsInstanceValid(s) || !s._want) return;
        s._target = Mathf.Clamp(Mathf.Max(p, s._target), 0f, 1f);
        if (stage != null) s._stage.Text = stage.ToUpper();
        s._pct.Text = Mathf.RoundToInt(s._target * 100) + "%";
    }
    /// <summary>Lifts the screen (fading out .4 s unless fade is false), unfreezes the hero and runs the queued "after" callbacks.</summary>
    public static void Hide(bool fade = true)
    {
        var s = I;
        if (s != null && IsInstanceValid(s)) { s._gen++; s._want = false; if (!fade) { s._alpha = 0f; s.Apply(); } }
        Player.Frozen = false;
        RunAfter();
    }
    /// <summary>Runs a = now, or when the screen lifts if it is up (banners that would be hidden under it).</summary>
    public static void After(Action a) { if (Covering) _after.Add(a); else a(); }

    /// <summary>Sync work behind the screen: snaps it up and draws it at once, so a long door teleport shows the screen, not a frozen frame.
    /// Then do the work and call <see cref="Lift"/>.</summary>
    public static void Cut(string title, string subtitle = "", string stage = "Raising the land")
    {
        if (!On || I == null || !IsInstanceValid(I)) return;
        Show(title, subtitle, null, true); SetProgress(0f, stage); Draw();
    }
    /// <summary>Lifts the screen once the ground around the hero is ready (no sooner than min seconds after it went up, no later than max
    /// seconds from now), then runs after. Returns when it has lifted.</summary>
    public static Task Lift(Action? after = null, float min = .6f, float max = 8f)
    {
        if (after != null) _after.Add(after);
        var s = I; if (s == null || !IsInstanceValid(s) || !s._want) { RunAfter(); return Task.CompletedTask; }   // not up (a short hop, the bots): at once
        var tcs = new TaskCompletionSource(); _waiters.Add(tcs);
        s.LiftLoop(s._gen, min, max);
        return tcs.Task;
    }
    /// <summary>Async work behind the screen: up (faded in), the work, then lifted when the ground is ready.</summary>
    public static async Task Cover(Func<Task> work, string title, string subtitle = "", Texture2D? art = null, float min = .6f, float max = 8f)
    {
        var s = I;
        if (!On || s == null || !IsInstanceValid(s)) { await work(); return; }
        Show(title, subtitle, art);
        ulong t0 = Time.GetTicksMsec();
        for (int f = 0; f < 2 || Time.GetTicksMsec() - t0 < (ulong)(FadeIn * 1000) + 10; f++) await s.ToSignal(s.GetTree(), SceneTree.SignalName.ProcessFrame);
        try { await work(); }
        finally { await Lift(null, min, max); }
    }
    /// <summary>Draws one frame now (the main thread is busy building: no frame would run). Not inside a physics step.</summary>
    public static void Draw()
    {
        var s = I; if (s == null || !IsInstanceValid(s) || !s._want || !s.IsInsideTree() || Engine.IsInPhysicsFrame() || DisplayServer.GetName() == "headless") return;
        s._shown = s._target; s._alpha = 1f; s.Apply();
        RenderingServer.ForceDraw(true, 0.0);
    }
    /// <summary>Dev capture: the screen with made-up progress, for a picture.</summary>
    public static void Preview(string title)
    {
        var s = I; if (s == null || !IsInstanceValid(s)) return;
        bool was = On; On = true;
        Show(title, "the realm awakens", null, true); SetProgress(.6f, "Planting the forests"); s._shown = s._target; s.Apply();
        On = was;
    }

    // ================================================================ lifting
    async void LiftLoop(int gen, float min, float max)
    {
        float from = _target; ulong t0 = Time.GetTicksMsec(); bool timedOut = false;
        if (_stage.Text == "") _stage.Text = "SHAPING THE GROUND";
        while (true)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!IsInstanceValid(this) || gen != _gen || !_want) return;   // shown again (a newer loop lifts) or hidden
            var (ok, frac) = GroundReady();
            SetProgress(from + (1f - from) * frac, null);
            float el = (Time.GetTicksMsec() - t0) / 1000f, up = (Time.GetTicksMsec() - _shownAt) / 1000f;
            if (ok && up >= min) break;
            if (el >= max) { timedOut = true; break; }
        }
        if (timedOut) GD.Print($"[LOAD] ground not ready after {max:0} s: lifting anyway");
        SetProgress(1f, null);
        Hide();
    }
    /// <summary>The block chunks around the hero (2 rings outdoors, 1 in a dungeon) are shown and something is under the feet.</summary>
    static (bool ok, float frac) GroundReady()
    {
        var p = Player.Instance; if (p == null || !IsInstanceValid(p) || !p.IsInsideTree()) return (true, 1f);
        var pos = p.GlobalPosition; float frac = 1f; bool blocks = true;
        if (BlockWorld.I is { } bw && IsInstanceValid(bw) && bw.Area is { } a)
        {
            int ring = Dungeon.Active ? 1 : 2, cx = a.CFloor(Mathf.FloorToInt(pos.X)), cz = a.CFloor(Mathf.FloorToInt(pos.Z)), n = 0, ready = 0;
            for (int dz = -ring; dz <= ring; dz++) for (int dx = -ring; dx <= ring; dx++)
            {
                if (!a.Gen.InBounds(cx + dx, cz + dz)) continue;
                n++; if (a.Chunks.TryGetValue(BlockArea.CKey(cx + dx, cz + dz), out var c) && c.State >= 5) ready++;
            }
            frac = n == 0 ? 1f : ready / (float)n; blocks = ready >= n;
        }
        var q = PhysicsRayQueryParameters3D.Create(pos + Vector3.Up * 1.2f, pos + Vector3.Down * 12f);
        q.Exclude = new Godot.Collections.Array<Rid> { p.GetRid() };
        bool floor = p.GetWorld3D().DirectSpaceState.IntersectRay(q).Count > 0;
        return (blocks && floor, floor ? frac : frac * .9f);
    }
    static void RunAfter()
    {
        var a = _after.ToArray(); _after.Clear();
        foreach (var f in a) { try { f(); } catch (Exception e) { GD.PrintErr("[LOAD] after: " + e.Message); } }
        var w = _waiters.ToArray(); _waiters.Clear();
        foreach (var t in w) t.TrySetResult();
    }

    // ================================================================ build
    public override void _Ready()
    {
        I = this; Layer = 120; ProcessMode = ProcessModeEnum.Always;
        _root = new Control { Name = "Root", MouseFilter = Control.MouseFilterEnum.Stop, Visible = false };
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); AddChild(_root);

        _bgMat = new ShaderMaterial { Shader = new Shader { Code = BackdropShader } };
        _bg = new ColorRect { Color = Colors.Black, Material = _bgMat, MouseFilter = Control.MouseFilterEnum.Ignore };
        _bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); _root.AddChild(_bg);

        // embers drifting up from the bottom edge
        var ramp = new Gradient(); ramp.SetColor(0, new Color(1f, .62f, .25f, 0f)); ramp.SetColor(1, new Color(1f, .35f, .1f, 0f));
        ramp.AddPoint(.15f, new Color(1f, .72f, .35f, .9f)); ramp.AddPoint(.7f, new Color(1f, .45f, .15f, .5f));
        var dotG = new Gradient(); dotG.SetColor(0, Colors.White); dotG.SetColor(1, new Color(1, 1, 1, 0));
        var dot = new GradientTexture2D { Gradient = dotG, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(.5f, .5f), FillTo = new Vector2(1f, .5f), Width = 16, Height = 16 };
        _embers = new CpuParticles2D
        {
            Amount = 70, Lifetime = 9f, Preprocess = 9f, Texture = dot, ColorRamp = ramp, Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add },
            EmissionShape = CpuParticles2D.EmissionShapeEnum.Rectangle, EmissionRectExtents = new Vector2(700, 20), Direction = new Vector2(.15f, -1f), Spread = 30f,
            InitialVelocityMin = 14f, InitialVelocityMax = 46f, Gravity = new Vector2(4f, -5f), ScaleAmountMin = .25f, ScaleAmountMax = .7f, AngularVelocityMin = -40f, AngularVelocityMax = 40f,
        };
        _root.AddChild(_embers);

        // the place name, a fading gold rule, the subtitle
        _title = Lbl(56, Gold, 9, 8); _title.SetAnchorsPreset(Control.LayoutPreset.TopWide); _title.AnchorTop = _title.AnchorBottom = .40f; _title.OffsetTop = -44; _title.OffsetBottom = 30; _title.OffsetLeft = _title.OffsetRight = 0;
        _title.HorizontalAlignment = HorizontalAlignment.Center; _root.AddChild(_title);
        var rule = new ColorRect { Color = Gold, Material = new ShaderMaterial { Shader = new Shader { Code = RuleShader } }, MouseFilter = Control.MouseFilterEnum.Ignore };
        rule.AnchorLeft = rule.AnchorRight = .5f; rule.AnchorTop = rule.AnchorBottom = .40f; rule.OffsetLeft = -260; rule.OffsetRight = 260; rule.OffsetTop = 38; rule.OffsetBottom = 40; _root.AddChild(rule);
        _sub = Lbl(17, Parch with { A = .85f }, 4, 4); _sub.SetAnchorsPreset(Control.LayoutPreset.TopWide); _sub.AnchorTop = _sub.AnchorBottom = .40f; _sub.OffsetTop = 50; _sub.OffsetBottom = 78; _sub.OffsetLeft = _sub.OffsetRight = 0;
        _sub.HorizontalAlignment = HorizontalAlignment.Center; _root.AddChild(_sub);

        // the progress bar: an iron track, a soft gold glow, the gold fill and a bright head
        _track = new ColorRect { Color = Iron, MouseFilter = Control.MouseFilterEnum.Ignore };
        _track.AnchorLeft = .2f; _track.AnchorRight = .8f; _track.AnchorTop = _track.AnchorBottom = 1f; _track.OffsetTop = -92; _track.OffsetBottom = -89; _root.AddChild(_track);
        _glow = new ColorRect { Color = Gold, Material = new ShaderMaterial { Shader = new Shader { Code = GlowShader } }, MouseFilter = Control.MouseFilterEnum.Ignore };
        _glow.AnchorLeft = 0; _glow.AnchorTop = 0; _glow.AnchorBottom = 1; _glow.OffsetTop = -9; _glow.OffsetBottom = 9; _track.AddChild(_glow);
        _fill = new ColorRect { Color = Gold, MouseFilter = Control.MouseFilterEnum.Ignore };
        _fill.AnchorLeft = 0; _fill.AnchorTop = 0; _fill.AnchorBottom = 1; _track.AddChild(_fill);
        _head = new ColorRect { Color = new Color(1f, .86f, .55f), Material = new ShaderMaterial { Shader = new Shader { Code = DotShader } }, MouseFilter = Control.MouseFilterEnum.Ignore };
        _head.AnchorTop = _head.AnchorBottom = .5f; _head.OffsetTop = -12; _head.OffsetBottom = 12; _track.AddChild(_head);
        _stage = Lbl(15, Parch with { A = .8f }, 3, 3); _stage.AnchorLeft = .2f; _stage.AnchorRight = .8f; _stage.AnchorTop = _stage.AnchorBottom = 1f; _stage.OffsetTop = -118; _stage.OffsetBottom = -96; _root.AddChild(_stage);
        _pct = Lbl(15, GoldDim, 2, 3); _pct.AnchorLeft = .2f; _pct.AnchorRight = .8f; _pct.AnchorTop = _pct.AnchorBottom = 1f; _pct.OffsetTop = -118; _pct.OffsetBottom = -96; _pct.HorizontalAlignment = HorizontalAlignment.Right; _root.AddChild(_pct);

        // the tip
        _tip = new RichTextLabel { BbcodeEnabled = true, ScrollActive = false, FitContent = false, MouseFilter = Control.MouseFilterEnum.Ignore, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _tip.AddThemeFontSizeOverride("normal_font_size", 15); _tip.AddThemeColorOverride("default_color", Muted);
        _tip.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, .8f)); _tip.AddThemeConstantOverride("outline_size", 4);
        _tip.AnchorLeft = .18f; _tip.AnchorRight = .82f; _tip.AnchorTop = _tip.AnchorBottom = 1f; _tip.OffsetTop = -74; _tip.OffsetBottom = -22; _root.AddChild(_tip);

        _last = Time.GetTicksMsec();
    }
    public override void _ExitTree()
    {   // "Quit to title" reloads the scene: drop the old world's callbacks, release anyone waiting
        if (I != this) return;
        I = null; Player.Frozen = false; _after.Clear();
        var w = _waiters.ToArray(); _waiters.Clear(); foreach (var t in w) t.TrySetResult();
    }

    static Label Lbl(int size, Color c, int spacing, int outline)
    {
        var l = new Label { MouseFilter = Control.MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
        l.AddThemeFontSizeOverride("font_size", size); l.AddThemeColorOverride("font_color", c);
        l.AddThemeColorOverride("font_outline_color", new Color(.06f, .035f, .015f, .9f)); l.AddThemeConstantOverride("outline_size", outline);
        l.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, .6f)); l.AddThemeConstantOverride("shadow_offset_y", 2);
        l.AddThemeFontOverride("font", new FontVariation { BaseFont = ThemeDB.FallbackFont, SpacingGlyph = spacing });   // the letter spacing of the UI's titles (Ui.cs)
        return l;
    }
    static string Spaced(string s) => s.Replace(" ", "  ");

    void NextTip()
    {
        if (_tips == null)
        {
            _tips = new();
            try { using var doc = JsonUtil.Load("res://data/tips.json"); if (doc.RootElement.TryGetProperty("tips", out var a)) foreach (var t in a.EnumerateArray()) if (t.GetString() is { Length: > 0 } s) _tips.Add(s); }
            catch (Exception e) { GD.PrintErr("[LOAD] tips: " + e.Message); }
            if (_tips.Count == 0) _tips.Add("A dodge roll leaves you untouchable for a heartbeat. Roll through a swing, not away from it.");
        }
        int n = _tips.Count, i = n <= 1 ? 0 : (int)(GD.Randi() % (uint)n); if (n > 1 && i == _tipI) i = (i + 1) % n; _tipI = i;
        _tip.Text = $"[center][color=#{Gold.ToHtml(false)}]TIP[/color]    {_tips[i]}[/center]";
    }

    // ================================================================ per frame
    public override void _Input(InputEvent e) { if (_want) GetViewport().SetInputAsHandled(); }   // nothing reaches the game under the screen

    public override void _Process(double delta)
    {
        ulong now = Time.GetTicksMsec(); float dt = Mathf.Min((now - _last) / 1000f, .1f); _last = now;   // real time: unaffected by pause or time scale
        if (!_root.Visible) return;
        _alpha = Mathf.MoveToward(_alpha, _want ? 1f : 0f, dt / (_want ? FadeIn : FadeOut));
        _shown = Mathf.MoveToward(_shown, _target, dt * Mathf.Max(.6f, (_target - _shown) * 4f));
        _tipT += dt;
        if (_tipT > TipEvery) { _tipA -= dt * 2.5f; if (_tipA <= 0) { NextTip(); _tipT = 0; } }
        else _tipA = Mathf.Min(1f, _tipA + dt * 2.5f);
        Apply();
        if (!_want && _alpha <= 0f) _root.Visible = false;
    }
    void Apply()
    {
        _root.Modulate = new Color(1, 1, 1, _alpha);
        _bgMat.SetShaderParameter("t", Secs);
        _fill.AnchorRight = _shown; _fill.OffsetRight = 0;
        _glow.AnchorRight = _shown; _glow.OffsetRight = 0; _glow.Visible = _shown > .002f;
        _head.AnchorLeft = _head.AnchorRight = _shown; _head.OffsetLeft = -12; _head.OffsetRight = 12; _head.Visible = _shown > .002f && _shown < .999f;
        _tip.Modulate = new Color(1, 1, 1, Mathf.Clamp(_tipA, 0, 1));
        var size = GetViewport()?.GetVisibleRect().Size ?? new Vector2(1280, 720);
        _embers.Position = new Vector2(size.X * .5f, size.Y + 16); _embers.EmissionRectExtents = new Vector2(size.X * .55f, 18);
    }

    // ================================================================ shaders
    /// <summary>Dusk over a gothic skyline: layered ridges with a spired keep, a pale moon, drifting fog, vignette and grain,
    /// all under a slow Ken Burns zoom and pan. With art set, the picture drifts the same way under the vignette.</summary>
    const string BackdropShader = @"shader_type canvas_item;
uniform float t = 0.0;
uniform float seed = 0.0;
uniform bool has_art = false;
uniform sampler2D art : filter_linear_mipmap, repeat_disable;
float h1(float n) { return fract(sin(n * 127.1 + seed) * 43758.5453); }
float h2(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float vn(vec2 p) { vec2 i = floor(p), f = fract(p); f = f * f * (3.0 - 2.0 * f);
  return mix(mix(h2(i), h2(i + vec2(1, 0)), f.x), mix(h2(i + vec2(0, 1)), h2(i + vec2(1, 1)), f.x), f.y); }
float fbm(vec2 p) { float s = 0.0, a = 0.5; for (int i = 0; i < 5; i++) { s += a * vn(p); p *= 2.03; a *= 0.5; } return s; }
float ridge(float x, float i) { return 0.40 - i * 0.075 + (fbm(vec2(x * (1.6 + i * 1.3) + i * 7.3 + seed, i * 3.1)) - 0.5) * (0.16 + i * 0.05); }
float keep(vec2 q, float base, float cx) {
  float m = 0.0;
  if (abs(q.x - cx) < 0.16 && q.y < base + 0.045) m = 1.0;   // the curtain wall
  for (int k = -4; k <= 4; k++) {
    float fk = float(k), hk = h1(fk + 11.0), tx = cx + fk * 0.038, w = 0.007 + hk * 0.006;
    float top = base + 0.06 + hk * 0.14 + (k == 0 ? 0.08 : 0.0), tip = 0.045 + hk * 0.04;
    if (abs(q.x - tx) < w && q.y < top) m = 1.0;
    float s = (q.y - top) / tip; if (s >= 0.0 && s < 1.0 && abs(q.x - tx) < w * 1.25 * (1.0 - s)) m = 1.0;
  }
  return m;
}
void fragment() {
  float aspect = SCREEN_PIXEL_SIZE.y / SCREEN_PIXEL_SIZE.x;
  float k = 0.5 - 0.5 * cos(t * 0.045);
  float zoom = 1.04 + 0.09 * k;
  vec2 pan = vec2(mix(-0.025, 0.025, k), mix(0.012, -0.01, k));
  vec2 uv = (UV - 0.5) / zoom + 0.5 + pan;
  vec3 col;
  if (has_art) {
    col = texture(art, uv).rgb * 0.8;
  } else {
    vec2 q = vec2(uv.x * aspect, 1.0 - uv.y);
    float y = q.y;
    col = mix(vec3(0.20, 0.085, 0.035), vec3(0.025, 0.03, 0.045), smoothstep(0.28, 0.95, y));
    col += vec3(0.30, 0.12, 0.04) * exp(-pow((y - 0.33) * 7.0, 2.0)) * 0.55;
    vec2 mp = vec2(aspect * 0.72, 0.74); float md = length(q - mp);
    col += vec3(0.80, 0.74, 0.62) * smoothstep(0.052, 0.046, md) * 0.75 + vec3(0.5, 0.42, 0.32) * exp(-md * 9.0) * 0.25;
    col *= 0.9 + 0.1 * fbm(q * 3.0 + vec2(t * 0.01, 0.0));
    float cx = aspect * 0.34;
    for (int i = 0; i < 4; i++) {
      float fi = float(i), hgt = ridge(q.x, fi);
      vec3 rc = mix(vec3(0.11, 0.07, 0.05), vec3(0.008, 0.008, 0.01), 0.35 + fi * 0.22);
      float m = q.y < hgt ? 1.0 : 0.0;
      if (i == 1) m = max(m, keep(q, ridge(cx, 1.0), cx));
      col = mix(col, rc, m);
      float fog = fbm(vec2(q.x * 2.5 + t * (0.012 + fi * 0.006) + fi * 5.0, q.y * 6.0 + fi));
      col += vec3(0.22, 0.13, 0.08) * smoothstep(0.08, 0.0, abs(q.y - hgt + 0.02)) * fog * (0.35 - fi * 0.07);
    }
    col += vec3(0.18, 0.1, 0.06) * fbm(vec2(q.x * 1.5 - t * 0.02, q.y * 4.0)) * smoothstep(0.35, 0.0, q.y) * 0.5;
  }
  vec2 v = (UV - 0.5) * vec2(1.25, 1.55);
  col *= smoothstep(1.15, 0.25, length(v));
  col *= 1.0 - 0.45 * smoothstep(0.55, 1.0, UV.y);   // darker at the bottom, under the bar and the tip
  col += (h2(UV * 900.0 + fract(t) * 37.0) - 0.5) * 0.025;
  COLOR = vec4(col, 1.0);
}";
    const string RuleShader = @"shader_type canvas_item;
void fragment() { float a = pow(1.0 - abs(UV.x - 0.5) * 2.0, 0.8); COLOR = vec4(COLOR.rgb, COLOR.a * a * 0.7); }";
    const string GlowShader = @"shader_type canvas_item;
void fragment() { float a = exp(-pow((UV.y - 0.5) * 5.0, 2.0)) * 0.35 * smoothstep(0.0, 0.08, UV.x); COLOR = vec4(COLOR.rgb, a); }";
    const string DotShader = @"shader_type canvas_item;
void fragment() { float d = length(UV - 0.5) * 2.0; COLOR = vec4(COLOR.rgb, COLOR.a * (exp(-d * d * 6.0) * 0.9 + smoothstep(0.25, 0.1, d))); }";
}
