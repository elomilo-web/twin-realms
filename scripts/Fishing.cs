using System;
using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// The fishing rod (F-23). Owned as PlayerStats.Rod (crafted in the crafting menu's blocks tab or bought from the merchant), taken out
/// and put away with its own key (data/fishing.json rod.key, default 7). While it is in hand, attack fishes instead of swinging:
/// cast (the bobber flies in an arc onto the water, the line is a thin sagging mesh) > wait (a random bite time: rain, night and fishing
/// level shorten it) > bite (the bobber dips, a splash, a sound and a prompt: press attack in time) > reel (a timing minigame: hold attack
/// to push your green zone right, let go and it drifts left; keep the darting fish inside it until the bar fills) > the catch
/// (FishDb.Roll: biome, water kind, time, weather and fishing level). Fish and junk go to the pack as materials; fishing gives XP (LifeSkills).
/// </summary>
public partial class Fishing : Node3D
{
    public static Fishing? I;
    public const string RodAction = "fishing_rod";
    public enum St { Off, Ready, Cast, Wait, Bite, Reel, Show }
    public St State { get; private set; } = St.Off;
    public bool Holding { get; private set; }
    /// <summary>The rod is in hand: Player skips its attacks (attack casts and reels here).</summary>
    public static bool Busy => I != null && I.Holding;

    MeshInstance3D _bobber = null!, _line = null!; ImmediateMesh _lineMesh = null!;
    Vector3 _from, _to, _bob; float _t, _castT, _waitT, _biteT, _showT, _reelSnd;
    public FishDb.Pick? Hooked { get; private set; }
    FishingUi _ui = null!;
    // reel minigame: positions 0..1 along the bar
    public float Zone { get; private set; } = .3f; public float ZoneW { get; private set; } = .3f; public float FishAt { get; private set; } = .5f; public float Progress { get; private set; } = .3f;
    float _zoneV, _fishV, _fishGoal = .5f, _fishGoalT;
    public bool DevHold;   // dev captures: hold the reel button
    public string LastCatch = "", LastIcon = "";

    public override void _Ready()
    {
        I = this; FishDb.Load();
        if (!InputMap.HasAction(RodAction))
        {
            InputMap.AddAction(RodAction);
            var k = JsonUtil.S(FishDb.Rod, "key", "7"); Key key = k.Length == 1 && char.IsDigit(k[0]) ? Key.Key0 + (k[0] - '0') : OS.FindKeycodeFromString(k);
            InputMap.ActionAddEvent(RodAction, new InputEventKey { PhysicalKeycode = key });
            InputMap.ActionAddEvent(RodAction, new InputEventJoypadButton { ButtonIndex = JoyButton.DpadDown });
        }
        Avatar.CodeTools["code:rod"] = BuildRod;
        var bm = new StandardMaterial3D { AlbedoColor = new Color(.85f, .12f, .1f), Roughness = .4f };
        _bobber = new MeshInstance3D { Mesh = new SphereMesh { Radius = .07f, Height = .14f, RadialSegments = 10, Rings = 5 }, MaterialOverride = bm, Visible = false, TopLevel = true, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _bobber.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = .072f, Height = .07f, RadialSegments = 10, Rings = 3, IsHemisphere = true }, Position = new Vector3(0, .005f, 0), MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(.95f, .95f, .9f) }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        AddChild(_bobber);
        _lineMesh = new ImmediateMesh();
        _line = new MeshInstance3D { Mesh = _lineMesh, TopLevel = true, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false,
            MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(.92f, .92f, .88f, .85f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled } };
        AddChild(_line);
        var layer = new CanvasLayer { Layer = 6, Name = "FishingUi" }; AddChild(layer);
        _ui = new FishingUi { F = this }; layer.AddChild(_ui); _ui.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); _ui.MouseFilter = Control.MouseFilterEnum.Ignore;
    }
    public override void _ExitTree() { if (I == this) I = null; }

    /// <summary>The rod model: cork grip, a tapering dark-wood shaft with brass rings, a reel; RodTip marks the tip (the line starts there).</summary>
    static Node3D BuildRod()
    {
        var root = new Node3D { Name = "Rod", Rotation = new Vector3(-.5f, 0, 0) };   // tilted forward out of the fist
        StandardMaterial3D M(Color c, float rough = .6f, float metal = 0) => new() { AlbedoColor = c, Roughness = rough, Metallic = metal };
        void Part(Mesh m, Vector3 pos, Material mat) => root.AddChild(new MeshInstance3D { Mesh = m, Position = pos, Rotation = new Vector3(-Mathf.Pi / 2, 0, 0), MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        Part(new CylinderMesh { TopRadius = .022f, BottomRadius = .024f, Height = .32f, RadialSegments = 8 }, new Vector3(0, 0, -.02f), M(new Color(.7f, .55f, .36f), .9f));
        Part(new CylinderMesh { TopRadius = .005f, BottomRadius = .014f, Height = 1.55f, RadialSegments = 6 }, new Vector3(0, 0, -.95f), M(new Color(.3f, .2f, .12f), .5f));
        var brass = M(new Color(.85f, .66f, .3f), .35f, .8f);
        for (int i = 0; i < 4; i++) Part(new CylinderMesh { TopRadius = .016f - i * .002f, BottomRadius = .016f - i * .002f, Height = .02f, RadialSegments = 6 }, new Vector3(0, 0, -.35f - i * .36f), brass);
        root.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = .045f, BottomRadius = .045f, Height = .04f, RadialSegments = 10 }, Position = new Vector3(0, -.05f, -.22f), Rotation = new Vector3(0, 0, Mathf.Pi / 2), MaterialOverride = M(new Color(.25f, .25f, .27f), .4f, .7f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        root.AddChild(new Node3D { Name = "RodTip", Position = new Vector3(0, 0, -1.72f) });
        return root;
    }

    Vector3 Tip()
    {
        var pl = Player.Instance!;
        if (pl.Av is { } av) foreach (var n in av.SlotNodes("tool")) if (IsInstanceValid(n) && n.FindChild("RodTip", true, false) is Node3D t) return t.GlobalPosition;
        return pl.GlobalPosition + Vector3.Up * 2.2f + Fwd() * 1.2f;
    }
    static Vector3 Fwd()
    {
        var cam = Player.Instance?.GetViewport().GetCamera3D(); var f = cam != null ? -cam.GlobalTransform.Basis.Z : -Player.Instance!.GlobalTransform.Basis.Z;
        f.Y = 0; return f.LengthSquared() > 1e-4f ? f.Normalized() : Vector3.Forward;
    }

    void Note(string s, Color? c = null) => Ui.Note(s, c ?? new Color(.75f, .9f, 1f));

    /// <summary>Takes the rod out or puts it away.</summary>
    public void Toggle()
    {
        if (!Holding && Game.D.Rod <= 0) { Note($"You have no fishing rod. Craft one (Crafting > Blocks) or buy one from the merchant."); Sfx.UiError(); return; }
        if (Holding) Stop(); else { Holding = true; State = St.Ready; Note($"Fishing rod out: {Inputs.KeyOf(Inputs.Attack)} casts into water, {OS.GetKeycodeString(KeyOfRod())} puts it away"); }
    }
    static Key KeyOfRod() { foreach (var e in InputMap.ActionGetEvents(RodAction)) if (e is InputEventKey k) return k.PhysicalKeycode; return Key.Key7; }
    void Stop() { Holding = false; State = St.Off; Hooked = null; _bobber.Visible = _line.Visible = false; if (Player.Instance?.Av is { } av) av.HeldOverride = null; }
    void Retract() { State = St.Ready; Hooked = null; _bobber.Visible = _line.Visible = false; }

    /// <summary>Casts toward the camera's facing: the farthest water point within range, else the line lands on the ground.</summary>
    public void Cast()
    {
        var pl = Player.Instance!; var fw = Fwd(); var (r0, r1) = FishDb.Pair(FishDb.Rod, "range", (4f, 13f));
        Vector3? hit = null;
        for (float d = r1; d >= r0; d -= .5f)
        {
            var p = pl.GlobalPosition + fw * d; float s = FishDb.Surface(p.X, p.Z, Lands.WaterAt(p.X, p.Z) - .3f);
            if (FishDb.InWater(new Vector3(p.X, s - .25f, p.Z))) { hit = new Vector3(p.X, s, p.Z); break; }
        }
        if (hit is { } h) _to = h; else { _to = pl.GlobalPosition + fw * r0; _to.Y = World.GroundAt(_to.X, _to.Z); }
        LastCatch = "";
        _from = Tip(); _castT = 0; _t = .45f + _from.DistanceTo(_to) * .03f; State = St.Cast; Hooked = null;
        _bobber.Visible = _line.Visible = true; Sfx.Play("fish_cast", .8f);
    }
    public Vector3 Bobber => _bob;
    public bool InWaterCast { get; private set; }

    void Land()
    {
        InWaterCast = FishDb.InWater(_to + Vector3.Down * .25f);
        if (!InWaterCast) { Note("The line lands on dry ground: cast into water", Ui.Dim); State = St.Show; _showT = .6f; Hooked = null; return; }
        Sfx.Play("fish_splash", .7f); Fx.Burst(_to, new Color(.8f, .9f, 1f), 8, 1.6f, .5f, .05f, -5f);
        var b = FishDb.Bite; var (w0, w1) = FishDb.Pair(b, "wait", (3.5f, 11f)); var c = FishDb.Here(_to + Vector3.Down * .25f);
        float mul = (c.Weather != "clear" ? JsonUtil.F(b, "rainMul", .75f) : 1f) * (c.Time == "night" ? JsonUtil.F(b, "nightMul", .9f) : 1f) * Mathf.Max(.4f, 1 - JsonUtil.F(b, "levelMul", .025f) * (c.Level - 1));
        _waitT = (float)GD.RandRange(w0, w1) * mul; State = St.Wait;
    }

    /// <summary>A fish (or junk, treasure, a gem) takes the bait: rolled here, from the bobber's water.</summary>
    public void BiteNow()
    {
        var c = FishDb.Here(_to + Vector3.Down * .25f); Hooked = FishDb.Roll(c, () => GD.Randf());
        _biteT = JsonUtil.F(FishDb.Bite, "window", 1.4f); State = St.Bite;
        Sfx.Play("fish_bite", .9f); Fx.Burst(_to, new Color(.85f, .95f, 1f), 14, 2.4f, .6f, .06f, -6f);
        FloatText.Spawn(this, _to + Vector3.Up * .6f, "!", new Color(1f, .85f, .3f), 1.1f);
    }

    /// <summary>The reel minigame starts: the zone width shrinks with the catch's difficulty and grows with fishing level.</summary>
    public void StartReel()
    {
        var r = FishDb.Reel; int lv = LifeSkills.Level(FishDb.Skill); float diff = Hooked?.Diff ?? .3f;
        ZoneW = Mathf.Clamp(JsonUtil.F(r, "zone", .3f) - JsonUtil.F(r, "zoneDiff", .14f) * diff + JsonUtil.F(r, "zoneLevel", .006f) * (lv - 1) + StatPipeline.Key("fishEase") / 100f, .12f, .5f);   // F-24 perk: fishing levels widen the zone (life stage)
        Zone = .5f - ZoneW / 2; FishAt = .5f; _zoneV = 0; _fishV = 0; _fishGoal = .5f; _fishGoalT = 0; Progress = JsonUtil.F(r, "start", .3f); State = St.Reel;
    }

    void ReelTick(float dt, bool hold)
    {
        var r = FishDb.Reel; float diff = Hooked?.Diff ?? .3f;
        // the fish darts between goals; harder fish move further, faster and more often
        _fishGoalT -= dt;
        if (_fishGoalT <= 0) { _fishGoal = Mathf.Clamp(FishAt + (float)GD.RandRange(-.25f - diff * .45f, .25f + diff * .45f), .03f, .97f); _fishGoalT = (float)GD.RandRange(.5f, 1.6f) * (1.2f - diff * .7f); }
        _fishV = Mathf.Lerp(_fishV, (_fishGoal - FishAt) * (2f + diff * 4f), 1 - Mathf.Exp(-4f * dt)); FishAt = Mathf.Clamp(FishAt + _fishV * dt, 0, 1);
        // your zone: hold to push it right, let go and it falls back left; it bounces softly at the ends
        _zoneV += (hold ? JsonUtil.F(r, "lift", 2.6f) : -JsonUtil.F(r, "fall", 2f)) * dt; _zoneV *= Mathf.Exp(-1.2f * dt);
        Zone += _zoneV * dt; if (Zone < 0) { Zone = 0; _zoneV = -_zoneV * .3f; } if (Zone > 1 - ZoneW) { Zone = 1 - ZoneW; _zoneV = -_zoneV * .3f; }
        bool inside = FishAt >= Zone && FishAt <= Zone + ZoneW;
        Progress = Mathf.Clamp(Progress + (inside ? JsonUtil.F(r, "gain", .34f) : -JsonUtil.F(r, "drain", .24f)) * dt, 0, 1);
        _reelSnd -= dt; if (inside && _reelSnd <= 0) { _reelSnd = .22f; Sfx.Play("fish_reel", .35f); }
        if (Progress >= 1) Catch();
        else if (Progress <= 0) { Note("The line went slack: it got away", Ui.Dim); Sfx.Play("whiff"); State = St.Show; _showT = .8f; Hooked = null; }
    }

    /// <summary>Lands the hooked catch: materials, gold, an item or a gem, and fishing XP.</summary>
    public void Catch()
    {
        var p = Hooked; if (p == null) { Retract(); return; }
        var d = Game.D; var at = Player.Instance!.GlobalPosition; int xp;
        switch (p.Kind)
        {
            case "fish":
                GatherDb.Give("fish_" + p.Fish!.Id, 1, at);
                xp = (int)Mathf.Round(p.Fish.Xp * (1 + .5f * Mathf.InverseLerp(p.Fish.Sz0, p.Fish.Sz1, p.Size)));
                LastCatch = $"{p.Fish.Name}  {p.Size:0} cm"; break;
            case "junk": GatherDb.Give("junk_" + p.Junk!.Id, 1, at); xp = 2; LastCatch = p.Junk.Name; break;
            case "gem": GemDb.Give(d, p.Gem); Note("Fished up a gem: " + GemDb.Name(p.Gem), GemDb.Col(p.Gem)); xp = 15; LastCatch = GemDb.Name(p.Gem); break;
            default:
            {
                var t = p.Treasure!; xp = 8; int gold = t.G1 > 0 ? (int)GD.RandRange(t.G0, t.G1) : 0;
                if (t.Kind == "chest")
                {
                    var it = Items.Roll(d.Level, 0, null, (int)GD.RandRange(t.R0, t.R1));
                    if (d.Inv.Count < 28) { d.Inv.Add(it); Note("A sunken chest: " + it.Name, it.Color); } else { gold += it.Value; Note("A sunken chest (pack full: sold its " + it.Name + ")", Ui.Dim); }
                    LastCatch = "Sunken chest: " + it.Name;
                }
                else if (t.Kind == "mat") { int n = (int)GD.RandRange(t.N0, t.N1); GatherDb.Give(t.Mat, n, at); LastCatch = $"{n} {GatherDb.Mat(t.Mat).Name}"; }
                else LastCatch = $"Gold pouch: {gold} gold";
                if (gold > 0) { d.Gold += gold; FloatText.Spawn(this, at + Vector3.Up * 1.6f, $"+{gold} gold", new Color(1f, .82f, .3f), .9f); Sfx.Play("coin"); }
                break;
            }
        }
        LastIcon = p.Kind switch { "fish" => p.Fish!.Name, "junk" => p.Junk!.Name, "gem" => "gem:" + p.Gem, _ => p.Treasure!.Kind == "mat" ? GatherDb.Mat(p.Treasure.Mat).Name : "trinket" };
        LifeSkills.Gain(FishDb.Skill, xp); Game.Notify();
        Sfx.Play("fish_catch", .8f); Fx.Burst(_to, new Color(.8f, .95f, 1f), 16, 2.6f, .6f, .06f, -6f);
        State = St.Show; _showT = 2.6f; _bobber.Visible = _line.Visible = false;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta; var pl = Player.Instance; if (pl == null) return;
        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        if (captured && !Instruments.Busy && Input.IsActionJustPressed(RodAction)) Toggle();   // (an instrument in hand: 7 is a note key, Instruments.cs)
        if (Holding && (Player.BuildMode || pl.Mounted || pl.Dead || Dungeon.Active || Game.D.Rod <= 0)) Stop();
        if (pl.Av is { } av && !Instruments.Busy) av.HeldOverride = Holding ? "code:rod" : null;
        _ui.QueueRedraw();
        if (!Holding) return;
        bool press = captured && Input.IsActionJustPressed(Inputs.Attack), hold = DevHold || captured && Input.IsActionPressed(Inputs.Attack);
        float time = (float)Time.GetTicksMsec() / 1000f;
        switch (State)
        {
            case St.Ready: if (press) Cast(); break;
            case St.Cast:
            {
                _castT += dt; float k = Mathf.Clamp(_castT / _t, 0, 1);
                _bob = _from.Lerp(_to, k) + Vector3.Up * (Mathf.Sin(k * Mathf.Pi) * (1.2f + _from.DistanceTo(_to) * .18f));
                if (k >= 1) { _bob = _to; Land(); }
                break;
            }
            case St.Wait:
                _bob = _to + Vector3.Up * (Mathf.Sin(time * 2.2f) * .015f);
                if (GD.Randf() < dt * .4f) _bob.Y -= .03f;   // a nibble now and then
                _waitT -= dt; if (_waitT <= 0) BiteNow();
                if (press) { Note("Reeled in", Ui.Dim); Retract(); }
                break;
            case St.Bite:
                _bob = _to + Vector3.Down * (.03f + Mathf.Abs(Mathf.Sin(time * 14f)) * .06f);
                _biteT -= dt;
                if (press) StartReel();
                else if (_biteT <= 0) { Note("Too slow: it got away", Ui.Dim); State = St.Show; _showT = .6f; Hooked = null; }
                break;
            case St.Reel:
                _bob = _to + new Vector3(Mathf.Sin(time * 9f) * .08f, -.05f - Mathf.Abs(Mathf.Sin(time * 6f)) * .05f, Mathf.Cos(time * 7f) * .08f);
                ReelTick(dt, hold); break;
            case St.Show:
                _showT -= dt; if (_showT <= 0 || (press && _showT < 2f)) Retract(); break;
        }
        _bobber.GlobalPosition = _bob;
        if (_line.Visible) DrawLine(Tip(), _bob, State == St.Reel || State == St.Bite ? .05f : State == St.Cast ? .1f : .35f);
    }

    void DrawLine(Vector3 a, Vector3 b, float sag)
    {
        _lineMesh.ClearSurfaces(); _lineMesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
        // a thin ribbon facing the camera (1.5 mm wide reads as a line at a distance; plain Lines would be 1 px)
        var cam = GetViewport().GetCamera3D(); var cp = cam?.GlobalPosition ?? a + Vector3.Back * 5;
        const int N = 16; float wd = .006f; Vector3 Pt(int i) { float t = (float)i / N; return a.Lerp(b, t) + Vector3.Down * (sag * a.DistanceTo(b) * .12f * 4 * t * (1 - t)); }
        for (int i = 0; i < N; i++)
        {
            Vector3 p0 = Pt(i), p1 = Pt(i + 1); var side = (p1 - p0).Cross(cp - p0).Normalized() * wd;
            _lineMesh.SurfaceAddVertex(p0 - side); _lineMesh.SurfaceAddVertex(p0 + side); _lineMesh.SurfaceAddVertex(p1 + side);
            _lineMesh.SurfaceAddVertex(p0 - side); _lineMesh.SurfaceAddVertex(p1 + side); _lineMesh.SurfaceAddVertex(p1 - side);
        }
        _lineMesh.SurfaceEnd(); _line.GlobalTransform = Transform3D.Identity;
    }

    // ---------------------------------------------------------------- dev captures (DevCaptureFish.cs)
    public void DevHoldRod() { if (Game.D.Rod <= 0) Game.D.Rod = 1; if (!Holding) { Holding = true; State = St.Ready; } if (Player.Instance?.Av is { } av) { av.HeldOverride = "code:rod"; av.ShowTool(null); } }
    public void DevReset() { DevHold = false; Stop(); }
    public void DevWait(float secs) { if (State == St.Wait) _waitT = secs; }
    public void DevHook(FishDb.Pick p) { Hooked = p; }
    public void DevReelState(float zone, float fish, float progress) { Zone = zone; FishAt = fish; Progress = progress; _fishGoal = fish; _fishGoalT = 99; _fishV = 0; _zoneV = 0; }
    public float ShowLeft => _showT;
}

/// <summary>Fishing's screen layer: the bite prompt, the reel minigame bar and the catch card. Drawn, not built from controls.</summary>
public partial class FishingUi : Control
{
    public Fishing F = null!;
    public override void _Draw()
    {
        if (F == null || !F.Holding) return;
        var font = ThemeDB.FallbackFont; var sz = Size; var mid = new Vector2(sz.X / 2, sz.Y * .72f);
        void Text(string s, Vector2 at, int size, Color c) { var w = font.GetStringSize(s, HorizontalAlignment.Left, -1, size).X; DrawString(font, at + new Vector2(-w / 2 + 1, 1), s, HorizontalAlignment.Left, -1, size, new Color(0, 0, 0, .8f)); DrawString(font, at - new Vector2(w / 2, 0), s, HorizontalAlignment.Left, -1, size, c); }
        string atk = Inputs.KeyOf(Inputs.Attack);
        switch (F.State)
        {
            case Fishing.St.Ready: Text($"{atk}: cast", mid + new Vector2(0, 60), 16, new Color(.85f, .9f, 1f, .85f)); break;
            case Fishing.St.Wait: Text("Waiting for a bite...", mid + new Vector2(0, 60), 16, new Color(.85f, .9f, 1f, .8f)); break;
            case Fishing.St.Bite:
            {
                float pulse = .5f + .5f * Mathf.Sin((float)Time.GetTicksMsec() / 70f);
                Text("BITE!", mid, 44, new Color(1f, .82f, .3f).Lerp(Colors.White, pulse * .4f));
                Text($"press {atk} to strike", mid + new Vector2(0, 34), 18, Colors.White);
                break;
            }
            case Fishing.St.Reel:
            {
                float W = 460, H = 30; var o = mid - new Vector2(W / 2, 0);
                DrawRect(new Rect2(o - new Vector2(14, 44), new Vector2(W + 28, H + 86)), new Color(.06f, .07f, .1f, .82f));
                DrawRect(new Rect2(o - new Vector2(14, 44), new Vector2(W + 28, H + 86)), new Color(.75f, .62f, .38f, .9f), false, 2);
                Text(F.Hooked?.Kind == "fish" ? "Something pulls hard..." : "Something heavy...", o + new Vector2(W / 2, -18), 18, new Color(.95f, .9f, .8f));
                DrawRect(new Rect2(o, new Vector2(W, H)), new Color(.12f, .25f, .35f));
                DrawRect(new Rect2(o + new Vector2(F.Zone * W, 0), new Vector2(F.ZoneW * W, H)), new Color(.35f, .85f, .45f, .75f));
                // the fish marker: a little fish shape
                var fp = o + new Vector2(F.FishAt * W, H / 2); var fc = new Color(1f, .78f, .3f);
                DrawColoredPolygon(new[] { fp + new Vector2(-11, 0), fp + new Vector2(0, -7), fp + new Vector2(9, 0), fp + new Vector2(0, 7) }, fc);
                DrawColoredPolygon(new[] { fp + new Vector2(8, 0), fp + new Vector2(15, -7), fp + new Vector2(15, 7) }, fc.Darkened(.2f));
                DrawRect(new Rect2(o + new Vector2(0, H + 8), new Vector2(W, 8)), new Color(.2f, .2f, .22f));
                DrawRect(new Rect2(o + new Vector2(0, H + 8), new Vector2(W * F.Progress, 8)), new Color(.4f, .8f, 1f));
                Text($"hold {atk}: zone right, let go: zone left   keep the fish in the green", o + new Vector2(W / 2, H + 34), 14, new Color(.8f, .85f, .9f));
                break;
            }
            case Fishing.St.Show when F.LastCatch != "" && F.ShowLeft > .9f:
            {
                var c = new Vector2(sz.X / 2, sz.Y * .3f); var box = new Rect2(c - new Vector2(220, 46), new Vector2(440, 92));
                DrawRect(box, new Color(.06f, .07f, .1f, .85f)); DrawRect(box, new Color(.75f, .62f, .38f, .9f), false, 2);
                Text("Caught!", c + new Vector2(30, -12), 22, new Color(1f, .85f, .4f));
                Text(F.LastCatch, c + new Vector2(30, 20), 18, Colors.White);
                Icons.Draw(this, F.LastIcon, c + new Vector2(-170, 0), 58);
                Text($"Fishing level {LifeSkills.Level(FishDb.Skill)}  ({LifeSkills.Xp(Game.D, FishDb.Skill)} xp)", c + new Vector2(0, 64), 14, new Color(.75f, .9f, 1f));
                break;
            }
        }
    }
}
