using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;

namespace TwinRealms;

/// <summary>
/// AnimeTool: a solid white animation studio for the real hero (the game's own Player, gear, trail and cape, nothing faked).
/// The world is built as usual, then the hero is moved into a white room floating high above it: a white floor with a faint metre
/// grid (foot sliding shows against it) inside a white backdrop, with its own orbit camera.
///
/// Run:   tools/run.ps1 animetool                    (interactive)
///        tools/run.ps1 animetool-shots [a,b,...]    (batch: contact sheets + numbers to bot_out/animetool/, then quits)
///        godot --path . -- --animetool [--animetool-shots=all|light5,heavy,...]
///
/// Interactive keys: ` (backquote) switches between TOOL mode (free mouse: buttons, right-drag orbits, wheel zooms) and PLAY mode
/// (mouse captured: play with the game's keys, the mouse orbits). F1 full body, F2 side, F3 front, F4 sword hand, F6 face, F7 feet.
/// [ ] slower / faster, \ pause, = one frame while paused. The buttons on the left run the same scripted moves as the batch.
/// Batch actions: idle run sprint light5 heavy roll rollmove jump jumpmove block parry blockhit hit hitheavy. Each is filmed from the
/// side and from the front at a fifth of real speed; per sample the report lists the clip, the blade's largest turn in one 1/60 s
/// step (a swing reads as motion below ~25 deg, a teleport is far above), the blade against the forearm, hip and head height,
/// how far each upper arm is raised (a T-pose is ~90) and where the cape hangs.
/// </summary>
public partial class AnimeTool : Node
{
    public static bool Wanted() => OS.GetCmdlineUserArgs().Contains("--animetool");
    static string? ShotsArg => OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--animetool-shots"));
    /// <summary>--animetool-clips=A,B: raw library clips (any clip the hero can play, by name) posed at 12 even points on a second body,
    /// side view: bot_out/animetool/clip_&lt;name&gt;.png. For choosing clips before wiring them in.</summary>
    static string? ClipsArg => OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--animetool-clips="));
    /// <summary>--animetool-grips[=class,class]: every kit model of each melee class held in the guard pose: a close-up of the hand and the whole weapon, bot_out/animetool/grip_&lt;class&gt;.png.</summary>
    static string? GripsArg => OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--animetool-grips"));
    static bool Batching => ShotsArg != null || ClipsArg != null || GripsArg != null;
    public static readonly string[] Actions = { "idle", "run", "sprint", "light5", "light5foe", "heavy", "heavyfoe", "roll", "rollmove", "jump", "jumpmove", "block", "parry", "blockhit", "hit", "hitheavy" };

    /// <summary>Where the studio floats (above the village, well clear of any terrain).</summary>
    public static readonly Vector3 S = new(0, 640, 9);
    static Player P => Player.Instance!;

    Camera3D _cam = null!; float _yaw = .6f, _pitch = .12f, _dist = 4.2f, _height = 1.0f; Vector3 _pan;
    bool _play, _drag, _trace = true, _bones, _busy; float _speed = 1f; bool _paused;
    double _gt; Label _info = null!; Control _panel = null!;
    MeshInstance3D _traceMi = null!, _boneMi = null!; readonly ImmediateMesh _traceIm = new(), _boneIm = new();
    readonly List<(Vector3 a, Vector3 b, double t)> _blade = new();
    Vector3 _lastDir; bool _hasDir; float _maxStep, _stepAcc; Enemy? _foe, _spar;
    /// <summary>Recording: the rig's yaw is held here (the hero faces -Z) and the camera is placed by the view.</summary>
    float? _fixedYaw; string _view = ""; bool _camLock;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always; HeroPose.Debug = true;
        BuildStudio();
        _ = Begin();
    }

    // ================================================================ studio
    void BuildStudio()
    {
        var root = new Node3D { Name = "Studio", Position = S }; AddChild(root);
        var floorSh = new Shader { Code = @"shader_type spatial;
render_mode fog_disabled;
varying vec3 wp;
void vertex(){ wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
float line(vec2 p, float w){ vec2 g = abs(fract(p - 0.5) - 0.5) / (fwidth(p) * w); return 1.0 - clamp(min(g.x, g.y), 0.0, 1.0); }
void fragment(){
  float l1 = line(wp.xz, 1.0), l5 = line(wp.xz / 5.0, 1.5);
  float axis = 1.0 - clamp(min(abs(wp.x) , abs(wp.z - 9.0)) / (fwidth(wp.x) * 2.0), 0.0, 1.0);
  ALBEDO = vec3(1.0) * (1.0 - l1 * 0.07 - l5 * 0.12) - vec3(0.0, 0.08, 0.12) * axis;
  ROUGHNESS = 0.95; SPECULAR = 0.1;
}" };
        var floor = new StaticBody3D { Name = "Floor", CollisionLayer = 1, CollisionMask = 0 }; root.AddChild(floor);
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(70, 1, 70) }, Position = new Vector3(0, -.5f, 0) });
        floor.AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(70, 70) }, MaterialOverride = new ShaderMaterial { Shader = floorSh } });
        var backSh = new Shader { Code = "shader_type spatial;\nrender_mode unshaded, cull_front, fog_disabled;\nvoid fragment(){ ALBEDO = vec3(1.0); }\n" };
        root.AddChild(new MeshInstance3D { Name = "Backdrop", Mesh = new SphereMesh { Radius = 34, Height = 68, RadialSegments = 48, Rings = 24 }, MaterialOverride = new ShaderMaterial { Shader = backSh }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        // a soft studio fill (no shadows) on top of the world's sun, so the hero reads well from every side
        root.AddChild(new OmniLight3D { Position = new Vector3(3, 4, 4), OmniRange = 16, LightEnergy = .7f, ShadowEnabled = false });
        root.AddChild(new OmniLight3D { Position = new Vector3(-4, 3, -3), OmniRange = 16, LightEnergy = .45f, ShadowEnabled = false });
        var lineMat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, NoDepthTest = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha };
        _traceMi = new MeshInstance3D { Mesh = _traceIm, TopLevel = true, MaterialOverride = lineMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off }; AddChild(_traceMi);
        _boneMi = new MeshInstance3D { Mesh = _boneIm, TopLevel = true, MaterialOverride = lineMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off }; AddChild(_boneMi);
        _cam = new Camera3D { Name = "AnimeCam", Fov = 50, Near = .03f, Far = 200 }; AddChild(_cam);
    }

    async Task Begin()
    {
        for (int i = 0; i < 6 && Player.Instance == null; i++) await Frames(1);
        if (Player.Instance == null) { GD.PrintErr("[ANIMETOOL] no player"); GetTree().Quit(1); return; }
        // the hero: the warrior's sword, plus a cape so the cloth can be judged
        Game.D.Eq["cape"] = new Item { Name = "Ember Cape", Slot = "cape" };
        // --animetool-weapon=greatsword[:Name]: another weapon class in the hand (default: the warrior's sword)
        if (OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--animetool-weapon=")) is string wa)
        {
            var parts = wa.Split('=', 2)[1].Split(':', 2); string wt = parts[0];
            Game.D.Eq["weapon"] = new Item { Name = parts.Length > 1 ? parts[1] : char.ToUpper(wt[0]) + wt[1..], Slot = "weapon", Wt = wt, Dmg = 10 };
            if (Weapons.Get(wt).Hands > 1 || OS.GetCmdlineUserArgs().Contains("--animetool-noshield")) Game.D.Eq["shield"] = null;
        }
        Game.Notify();
        if (P.Rig != null) P.Rig.ProcessMode = ProcessModeEnum.Disabled;   // our camera drives the view and the rig's yaw
        foreach (var n in GetParent().GetChildren()) if (n is CanvasLayer cl && (n.Name == "Hud" || (Batching && n is Ui))) cl.Visible = false;   // no HUD; no banners over the batch pictures
        Reset();
        await Secs(1f, true);
        _cam.Current = true;
        if (GripsArg is string ga) { await GripSheets(ga.Contains('=') ? ga.Split('=', 2)[1] : "all"); GetTree().Quit(); return; }
        if (ClipsArg is string ca) { await ClipSheets(ca.Split('=', 2)[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)); GetTree().Quit(); return; }
        if (ShotsArg is string arg)
        {
            string list = arg.Contains('=') ? arg.Split('=', 2)[1] : "all";
            var want = list == "all" || list == "" ? Actions : list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            await Batch(want); GetTree().Quit(); return;
        }
        BuildUi(); SetPlay(false);
    }

    void Reset()
    {
        _foe?.QueueFree(); _foe = null;
        foreach (var a in new[] { Inputs.Forward, Inputs.Back, Inputs.Left, Inputs.Right, Inputs.Sprint, Inputs.Attack, Inputs.Heavy, Inputs.Guard, Inputs.Roll, Inputs.Jump }) Input.ActionRelease(a);
        P.Teleport(S + new Vector3(0, .05f, 0), 0f);
        Heal();
    }
    void Heal() { if (Player.Instance == null) return; P.Hp = Game.D.MaxHp(); P.St = Game.D.MaxSt(); P.Mp = Game.D.MaxMp(); }

    // ================================================================ frame loop
    public override void _Process(double delta)
    {
        if (Player.Instance == null || !IsInstanceValid(P)) return;
        _gt += delta; Heal();
        Engine.TimeScale = _paused ? 0 : _speed;
        _cam.Current = true;
        var hero = P.GlobalPosition;
        if (_camLock) { }
        else if (_fixedYaw is float fy)
        {
            if (P.Rig != null) P.Rig.Rotation = new Vector3(0, fy, 0);
            var at = hero + new Vector3(0, .95f, 0);
            var off = _view == "front" ? new Vector3(-3.0f, .35f, -2.0f) : new Vector3(3.6f, .15f, 0);   // front: three-quarter, clear of a sparring foe 1.7 m ahead
            if (OS.GetCmdlineUserArgs().Contains("--animetool-close")) { off = _view == "front" ? new Vector3(-1.5f, .45f, -1.0f) : new Vector3(1.9f, .35f, .5f); at += new Vector3(0, .25f, 0); }   // upper body: hands and grip
            _cam.GlobalPosition = at + off; _cam.LookAt(at, Vector3.Up);
        }
        else
        {
            if (P.Rig != null) P.Rig.Rotation = new Vector3(0, _yaw, 0);   // WASD goes where the camera looks
            var at = hero + new Vector3(0, _height, 0) + _pan;
            var off = new Vector3(Mathf.Sin(_yaw) * Mathf.Cos(_pitch), Mathf.Sin(_pitch), Mathf.Cos(_yaw) * Mathf.Cos(_pitch)) * _dist;
            _cam.GlobalPosition = at + off; _cam.LookAt(at, Vector3.Up);
        }
        Measure((float)delta);
        DrawTrace(); DrawBones();
        if (_info != null) _info.Text = Info();
    }

    void Measure(float dt)
    {
        var av = P.Av; if (av?.BladeBase == null || av.BladeTip == null || !IsInstanceValid(av.BladeTip)) { _hasDir = false; return; }
        Vector3 a = av.BladeBase.GlobalPosition, b = av.BladeTip.GlobalPosition, d = (b - a).Normalized();
        if (dt > 0)
        {
            // the skeleton may only move on physics ticks (several drawn frames apart in slow motion): measure per pose change, over the time it took
            _stepAcc += dt;
            if (_hasDir) { float a0 = Mathf.RadToDeg(_lastDir.AngleTo(d)); if (a0 > .05f) { _maxStep = Mathf.Max(_maxStep, a0 / Mathf.Max(.05f, _stepAcc * 60f)); _stepAcc = 0; _lastDir = d; } }
            else { _lastDir = d; _hasDir = true; _stepAcc = 0; }
            _blade.Add((a, b, _gt));
        }
        while (_blade.Count > 0 && _gt - _blade[0].t > .35) _blade.RemoveAt(0);
    }

    void DrawTrace()
    {
        _traceIm.ClearSurfaces(); if (!_trace || _blade.Count < 2) return;
        _traceIm.SurfaceBegin(Mesh.PrimitiveType.Lines);
        for (int i = 0; i < _blade.Count; i++)
        {
            float age = (float)((_gt - _blade[i].t) / .35);
            _traceIm.SurfaceSetColor(new Color(.9f, .25f, .1f, 1f - age)); _traceIm.SurfaceAddVertex(_blade[i].a);
            _traceIm.SurfaceSetColor(new Color(.15f, .35f, 1f, 1f - age)); _traceIm.SurfaceAddVertex(_blade[i].b);
            if (i > 0) { _traceIm.SurfaceSetColor(new Color(.1f, .1f, .1f, .8f - age * .8f)); _traceIm.SurfaceAddVertex(_blade[i - 1].b); _traceIm.SurfaceAddVertex(_blade[i].b); }
        }
        _traceIm.SurfaceEnd();
    }

    void DrawBones()
    {
        _boneIm.ClearSurfaces(); var sk = P.Av?.Skel; if (!_bones || sk == null || !IsInstanceValid(sk)) return;
        _boneIm.SurfaceBegin(Mesh.PrimitiveType.Lines); var g = sk.GlobalTransform;
        for (int b = 0; b < sk.GetBoneCount(); b++)
        {
            int p = sk.GetBoneParent(b); if (p < 0) continue; string n = sk.GetBoneName(b);
            if (n.Contains("thumb") || n.Contains("index") || n.Contains("middle") || n.Contains("ring") || n.Contains("pinky")) continue;
            _boneIm.SurfaceSetColor(n.EndsWith("_r") ? new Color(1, .2f, .2f) : n.EndsWith("_l") ? new Color(.2f, .4f, 1) : new Color(.1f, .1f, .1f));
            _boneIm.SurfaceAddVertex(g * sk.GetBoneGlobalPose(p).Origin); _boneIm.SurfaceAddVertex(g * sk.GetBoneGlobalPose(b).Origin);
        }
        _boneIm.SurfaceEnd();
    }

    // ================================================================ numbers
    Vector3 Bone(Skeleton3D sk, string n) { int b = sk.FindBone(n); return b < 0 ? Vector3.Zero : sk.GlobalTransform * sk.GetBoneGlobalPose(b).Origin; }
    string Metrics()
    {
        var av = P.Av; var sk = av?.Skel; if (av == null || sk == null) return "no model";
        float floorY = S.Y; var fw = P.Facing();
        float pel = Bone(sk, "pelvis").Y - floorY, head = Bone(sk, "Head").Y - floorY;
        float ArmUp(string side) { var u = Bone(sk, "upperarm_" + side); var l = Bone(sk, "lowerarm_" + side); return Mathf.RadToDeg((l - u).Normalized().AngleTo(Vector3.Down)); }
        string blade = "-";
        if (av.BladeBase != null && av.BladeTip != null)
        {
            var d = (av.BladeTip.GlobalPosition - av.BladeBase.GlobalPosition).Normalized(); var fore = (Bone(sk, "hand_r") - Bone(sk, "lowerarm_r")).Normalized();
            blade = $"vsForearm {Mathf.RadToDeg(d.AngleTo(fore)),3:0} up {Mathf.RadToDeg(d.AngleTo(Vector3.Up)),3:0} fwd {d.Dot(fw),5:0.00}";
        }
        string cape = "-";
        if (av.Cape != null && IsInstanceValid(av.Cape)) { var hang = -av.Cape.GlobalTransform.Basis.Y.Normalized(); cape = $"tilt {Mathf.RadToDeg(hang.AngleTo(Vector3.Down)),3:0} front {hang.Dot(fw),5:0.00}"; }
        string tgt = "";
        if (_foe != null && IsInstanceValid(_foe) && av.BladeTip != null && av.HandPos(false) is Vector3 hp)
        {   // the point of the weapon nearest the foe's body (a vertical line through it, radius R): u = 0 at the fist, 1 at the tip
            var tp = av.BladeTip.GlobalPosition; var fp = _foe.GlobalPosition; float R = _foe.Def.R * _foe.ScaleF(), hgt = _foe.HeightM();
            float best = 1e9f, bu = 0;
            for (int i = 0; i <= 40; i++)
            {
                float u = i / 40f; var p = hp.Lerp(tp, u); float y = Mathf.Clamp(p.Y, fp.Y, fp.Y + hgt);
                float dd = new Vector2(p.X - fp.X, p.Z - fp.Z).Length() - R + Mathf.Abs(p.Y - y);
                if (dd < best) { best = dd; bu = u; }
            }
            float edge = new Vector2(fp.X - P.GlobalPosition.X, fp.Z - P.GlobalPosition.Z).Length() - R;
            tgt = $"  foeEdge {edge:0.00} near u {bu:0.00} gap {best:0.00} fist {new Vector2(hp.X - fp.X, hp.Z - fp.Z).Length() - R:0.00} len {hp.DistanceTo(tp):0.00}";
        }
        if (sk.GetNodeOrNull<HeroPose>("HeroPose") is { } hpz) tgt += "  two[" + hpz.TwoDebug + "]";
        string s = $"clip {av.Cur,-22} bladeStep {_maxStep,5:0.0}  blade {blade}  pelvis {pel:0.00} head {head:0.00}  armUp R {ArmUp("r"),3:0} L {ArmUp("l"),3:0}  cape {cape}  floor {P.IsOnFloor()} at {P.GlobalPosition - S:0.00} v {P.Velocity.Length():0.0}" + tgt;
        _maxStep = 0; return s;
    }
    string Info() => $"{(_play ? "PLAY (` for tool)" : "TOOL (` to play)")}   speed {(_paused ? "paused" : _speed.ToString("0.00") + "x")}   " + (P.Av != null ? "clip " + P.Av.Cur : "");

    // ================================================================ scripted moves
    async Task Frames(int n) { for (int i = 0; i < Math.Max(1, n); i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    /// <summary>Waits game seconds (time scale applies) unless real.</summary>
    async Task Secs(float s, bool real = false) { double end = (real ? Time.GetTicksMsec() / 1000.0 : _gt) + s; while ((real ? Time.GetTicksMsec() / 1000.0 : _gt) < end) await Frames(1); }
    async Task Tap(string a, float hold = .06f) { Input.ActionPress(a); await Secs(hold); Input.ActionRelease(a); }

    Enemy Foe(bool frozen)
    {
        _foe?.QueueFree();
        var e = new Enemy { Type = "orcWarrior", Level = 1, AnyRealm = true, NoLeash = true, Position = P.GlobalPosition + new Vector3(0, .1f, -1.7f) };
        GetParent().AddChild(e); _foe = e;
        if (frozen) GetTree().CreateTimer(.25, true).Timeout += () => { if (IsInstanceValid(e)) { e.ProcessMode = ProcessModeEnum.Disabled; e.Rotation = Vector3.Zero; } };   // still, once its idle has started (frozen at once it stood in its T-pose)
        return e;
    }
    void HitHero(float knock, bool stagger, Enemy? by) =>
        P.TakeHit(new HitInfo { Damage = 1, From = P.GlobalPosition + new Vector3(0, 0, -1.5f), Knock = knock, Stagger = stagger, Attacker = by });

    /// <summary>One move: how long to film it (game s) and the inputs.</summary>
    (float dur, Func<Task> run) Move(string id) => id switch
    {
        "idle" => (1.6f, async () => await Secs(1.6f)),
        "run" => (1.6f, async () => { Input.ActionPress(Inputs.Forward); await Secs(1.6f); Input.ActionRelease(Inputs.Forward); }),
        "sprint" => (1.6f, async () => { Input.ActionPress(Inputs.Forward); Input.ActionPress(Inputs.Sprint); await Secs(1.6f); Input.ActionRelease(Inputs.Sprint); Input.ActionRelease(Inputs.Forward); }),
        "light5" => (3.6f, async () => { for (int i = 0; i < 5; i++) { await Tap(Inputs.Attack); await Secs(.5f); } }),
        // the same chain at a foe standing 2.6 m ahead (the step-in toward a target)
        "light5foe" => (3.6f, async () => { var f = Foe(true); f.GlobalPosition = P.GlobalPosition + new Vector3(0, .1f, -2.6f); await Secs(.3f); for (int i = 0; i < 5; i++) { await Tap(Inputs.Attack); await Secs(.5f); } }),
        "heavyfoe" => (2.4f, async () => { var f = Foe(true); f.GlobalPosition = P.GlobalPosition + new Vector3(0, .1f, -2.6f); await Secs(.3f); await Tap(Inputs.Heavy, .12f); }),
        "heavy" => (2.2f, async () => await Tap(Inputs.Heavy, .12f)),
        "roll" => (1.3f, async () => await Tap(Inputs.Roll)),
        "rollmove" => (1.5f, async () => { Input.ActionPress(Inputs.Forward); await Secs(.35f); await Tap(Inputs.Roll); await Secs(1f); Input.ActionRelease(Inputs.Forward); }),
        "jump" => (1.4f, async () => await Tap(Inputs.Jump, .1f)),
        "jumpmove" => (1.6f, async () => { Input.ActionPress(Inputs.Forward); await Secs(.35f); await Tap(Inputs.Jump, .1f); await Secs(1.1f); Input.ActionRelease(Inputs.Forward); }),
        "block" => (1.5f, async () => { Input.ActionPress(Inputs.Guard); await Secs(1.4f); Input.ActionRelease(Inputs.Guard); }),
        "parry" => (1.4f, async () => { var f = Foe(true); Input.ActionPress(Inputs.Guard); await Secs(.04f); HitHero(2, false, f); await Secs(1.2f); Input.ActionRelease(Inputs.Guard); }),
        "blockhit" => (1.6f, async () => { var f = Foe(true); Input.ActionPress(Inputs.Guard); await Secs(.6f); HitHero(2, false, f); await Secs(.9f); Input.ActionRelease(Inputs.Guard); }),
        "hit" => (1.2f, async () => { await Secs(.1f); HitHero(2, true, null); await Secs(1f); }),
        "hitheavy" => (1.6f, async () => { await Secs(.1f); HitHero(10, true, null); await Secs(1.4f); }),
        _ => (.5f, async () => await Secs(.5f)),
    };

    async Task RunMove(string id)
    {
        if (_busy) return; _busy = true;
        try { Reset(); await Secs(.5f); bool was = _play; Input.MouseMode = Input.MouseModeEnum.Captured; await Move(id).run(); Input.MouseMode = was ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible; }
        finally { _busy = false; }
    }

    // ================================================================ batch: contact sheets
    async Task Batch(IEnumerable<string> ids)
    {
        string dir = ProjectSettings.GlobalizePath("res://") + "bot_out/animetool/"; DirAccess.MakeDirRecursiveAbsolute(dir);
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        _trace = OS.GetCmdlineUserArgs().Contains("--animetool-trace");
        var rep = new StringBuilder(); rep.AppendLine($"AnimeTool report {DateTime.Now:yyyy-MM-dd HH:mm}  (samples every {Every * 1000:0} ms of game time, filmed at {Slow}x)");
        Input.MouseMode = Input.MouseModeEnum.Captured; _speed = Slow;
        foreach (var id in ids)
            foreach (var view in new[] { "side", "front" })
            {
                try { await Film(id, view, dir, rep); }
                catch (Exception e) { rep.AppendLine($"{id}/{view}: failed {e.Message}"); GD.PrintErr("[ANIMETOOL] " + e); }
                System.IO.File.WriteAllText(dir + "report.txt", rep.ToString());
            }
        _speed = 1; GD.Print("[ANIMETOOL] done: " + dir);
    }
    static float Every => Arg("--animetool-every=", .05f); static float Slow => Arg("--animetool-slow=", .2f);
    static float Arg(string k, float d) { var a = OS.GetCmdlineUserArgs().FirstOrDefault(x => x.StartsWith(k)); return a != null && float.TryParse(a[k.Length..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : d; }

    async Task Film(string id, string view, string dir, StringBuilder rep)
    {
        _fixedYaw = 0; _view = view; Reset(); await Secs(.7f);
        var (dur, run) = Move(id); var tiles = new List<Image>();
        rep.AppendLine($"== {id} / {view}");
        _gt = 0; _maxStep = 0; var task = run(); double next = 0; int k = 0;
        while (_gt < dur + .1)
        {
            await Frames(1);
            if (_gt < next) continue;
            next += Every;
            string m = Metrics(); var img = await Grab(); tiles.Add(Crop(img));
            rep.AppendLine($"  {k++,2} t {_gt,5:0.00}  {m}");
        }
        await task;
        Sheet(tiles).SavePng(dir + $"{id}_{view}.png");
        GD.Print($"[ANIMETOOL] {id}/{view}: {tiles.Count} frames");
        _fixedYaw = null;
    }
    async Task<Image> Grab() { await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); return GetViewport().GetTexture().GetImage(); }
    Image Crop(Image full)
    {
        var sz = full.GetSize(); int h = sz.Y, w = Mathf.Min(sz.X, (int)(h * .82f));
        var c = _cam.UnprojectPosition(P.GlobalPosition + new Vector3(0, .95f, 0));
        int x = Mathf.Clamp((int)c.X - w / 2, 0, sz.X - w);
        var r = full.GetRegion(new Rect2I(x, 0, w, h)); r.Convert(Image.Format.Rgb8); r.Resize(w * 2 / 5, h * 2 / 5, Image.Interpolation.Bilinear); return r;
    }
    static Image Sheet(List<Image> tiles)
    {
        if (tiles.Count == 0) return Image.CreateEmpty(8, 8, false, Image.Format.Rgb8);
        int cols = 8, tw = tiles[0].GetWidth(), th = tiles[0].GetHeight(), rows = (tiles.Count + cols - 1) / cols, gap = 3;
        var img = Image.CreateEmpty(cols * (tw + gap), rows * (th + gap), false, Image.Format.Rgb8); img.Fill(new Color(.35f, .35f, .38f));
        for (int i = 0; i < tiles.Count; i++)
        {
            int px = (i % cols) * (tw + gap), py = (i / cols) * (th + gap);
            img.BlitRect(tiles[i], new Rect2I(0, 0, tw, th), new Vector2I(px, py));
            // the sample number as little ticks in the corner (5 = one long tick): matches the report lines
            for (int t = 0; t < i / 5; t++) img.FillRect(new Rect2I(px + 3 + t * 5, py + 3, 3, 9), new Color(.1f, .1f, .1f));
            for (int t = 0; t < i % 5; t++) img.FillRect(new Rect2I(px + 3 + (i / 5) * 5 + 3 + t * 4, py + 3, 2, 5), new Color(.9f, .2f, .1f));
        }
        return img;
    }

    async Task ClipSheets(string[] clips)
    {
        string dir = ProjectSettings.GlobalizePath("res://") + "bot_out/animetool/"; DirAccess.MakeDirRecursiveAbsolute(dir);
        DisplayServer.WindowSetSize(new Vector2I(1280, 720)); _trace = false;
        P.Visible = false; _camLock = true;
        var av = new Avatar { Name = "ClipBody", Position = S + new Vector3(0, 0, -3) }; GetParent().AddChild(av);
        av.Build(Races.Look(Game.D), "clips"); av.Dress(s => Game.D.Eq.GetValueOrDefault(s), Game.D.WType(), Classes.Get(Game.D.Class).Tunic);
        await Frames(4);
        var names = new HashSet<string>(av.ClipNames);
        foreach (var c in clips)
        {
            if (!names.Contains(c)) { GD.Print($"[ANIMETOOL] no clip {c}"); continue; }
            // the blade's turn between 120 even poses of the raw clip (deg per 1/60 s at the clip's own speed): snaps in the source itself
            if (av.BladeBase != null && av.BladeTip != null)
            {
                float len = av.ClipLength(c); Vector3? last = null; var peaks = new List<string>();
                for (int i = 0; i <= 120; i++)
                {
                    av.PoseAt(c, i / 120f); await Frames(1);
                    var d = (av.BladeTip.GlobalPosition - av.BladeBase.GlobalPosition).Normalized();
                    if (last is Vector3 l) { float deg = Mathf.RadToDeg(l.AngleTo(d)) / Mathf.Max(.001f, len / 120f * 60f); if (deg > 25) peaks.Add($"{i / 120f:0.00}:{deg:0}"); }
                    last = d;
                }
                GD.Print($"[ANIMETOOL] scan {c} ({len:0.00}s) fast spots (frac:deg/frame): " + string.Join(" ", peaks));
            }
            var tiles = new List<Image>(); var log = new StringBuilder();
            for (int i = 0; i < 12; i++)
            {
                av.PoseAt(c, i / 11f); await Frames(2);
                var at = av.GlobalPosition + new Vector3(0, .95f, 0); _cam.GlobalPosition = at + new Vector3(3.6f, .15f, 0); _cam.LookAt(at, Vector3.Up);
                var img = await Grab(); var sz = img.GetSize(); int w = (int)(sz.Y * .82f), x = (sz.X - w) / 2;
                var r = img.GetRegion(new Rect2I(x, 0, w, sz.Y)); r.Convert(Image.Format.Rgb8); r.Resize(w * 2 / 5, sz.Y * 2 / 5, Image.Interpolation.Bilinear); tiles.Add(r);
            }
            Sheet(tiles).SavePng(dir + $"clip_{c}.png"); GD.Print($"[ANIMETOOL] clip {c}: {av.ClipLength(c):0.00}s");
        }
        av.QueueFree(); P.Visible = true;
    }

    async Task GripSheets(string which)
    {
        string dir = ProjectSettings.GlobalizePath("res://") + "bot_out/animetool/"; DirAccess.MakeDirRecursiveAbsolute(dir);
        DisplayServer.WindowSetSize(new Vector2I(1280, 720)); _trace = false; P.Visible = false; _camLock = true;
        var classes = new List<string>();
        using (var doc = JsonUtil.Load("res://data/weapons.json"))
            foreach (var t in doc.RootElement.GetProperty("types").EnumerateObject()) if (!Weapons.Get(t.Name).Ranged && t.Name != "unarmed" && t.Name != "wand" && t.Name != "staff") classes.Add(t.Name);
        if (which != "all") classes = classes.Where(c => which.Split(',').Contains(c)).ToList();
        var rep = new StringBuilder();
        foreach (var wt in classes)
        {
            var models = Weapons.KitModels(wt).ToList(); if (models.Count == 0) models.Add("");
            var tiles = new List<Image>();
            foreach (var m in models.Take(8))
            {
                var it = new Item { Name = char.ToUpper(wt[0]) + wt[1..], Slot = "weapon", Wt = wt, Dmg = 10, Model = m == "" ? null : m };
                var av = new Avatar { Name = "GripBody", Position = S + new Vector3(0, 0, -3) }; GetParent().AddChild(av);
                av.Build(Races.Look(Game.D), "grips"); av.Dress(s => s == "weapon" ? it : null, wt, Classes.Get(Game.D.Class).Tunic);
                await Frames(3);
                av.PoseAt(Weapons.Get(wt).Hands > 1 ? "idleArmed" : "idleArmed", .3f); await Frames(2);
                var sk = av.Skel!; var hand = sk.GlobalTransform * sk.GetBoneGlobalPose(sk.FindBone("hand_r")).Origin;
                // close-up of the grip, from the hero's right and a little in front
                _cam.GlobalPosition = hand + new Vector3(.75f, .12f, .45f); _cam.LookAt(hand, Vector3.Up); var a = await Grab();
                // the whole weapon
                var mid = av.BladeTip != null ? (hand + av.BladeTip.GlobalPosition) / 2 : hand;
                float len = av.BladeTip != null ? hand.DistanceTo(av.BladeTip.GlobalPosition) : 1f;
                _cam.GlobalPosition = mid + new Vector3(1.2f + len, .3f, .9f + len * .5f); _cam.LookAt(mid, Vector3.Up); var b = await Grab();
                foreach (var img in new[] { a, b }) { var sz = img.GetSize(); int w = sz.Y; var r = img.GetRegion(new Rect2I((sz.X - w) / 2, 0, w, sz.Y)); r.Convert(Image.Format.Rgb8); r.Resize(288, 288, Image.Interpolation.Bilinear); tiles.Add(r); }
                string tipInfo = av.BladeTip != null && av.BladeBase != null ? $"hand->tip {hand.DistanceTo(av.BladeTip.GlobalPosition):0.00} m" : "no marks";
                rep.AppendLine($"{wt,-14} {(m == "" ? "(built-in)" : m),-32} {tipInfo}");
                av.QueueFree(); await Frames(1);
            }
            Sheet(tiles).SavePng(dir + $"grip_{wt}.png"); GD.Print($"[ANIMETOOL] grips {wt}: {tiles.Count / 2} models");
        }
        System.IO.File.WriteAllText(dir + "grips.txt", rep.ToString());
        P.Visible = true;
    }

    // ================================================================ interactive
    void SetPlay(bool on) { _play = on; Input.MouseMode = on ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible; if (_panel != null) _panel.Modulate = new Color(1, 1, 1, on ? .45f : 1f); }

    void Cam(string preset)
    {
        _pan = Vector3.Zero;
        switch (preset)
        {
            case "full": _dist = 4.2f; _height = 1f; _pitch = .12f; break;
            case "side": _dist = 3.6f; _height = 1f; _pitch = .05f; _yaw = P.ModelYaw + Mathf.Pi / 2; break;
            case "front": _dist = 3.4f; _height = 1.05f; _pitch = .1f; _yaw = P.ModelYaw + Mathf.Pi; break;
            case "hand": _dist = 1.1f; _height = 1.1f; _pitch = .2f; _yaw = P.ModelYaw + Mathf.Pi * .7f; break;
            case "face": _dist = .75f; _height = 1.62f; _pitch = .02f; _yaw = P.ModelYaw + Mathf.Pi; break;
            case "feet": _dist = 1.6f; _height = .2f; _pitch = .25f; _yaw = P.ModelYaw + Mathf.Pi * .6f; break;
        }
    }

    public override void _Input(InputEvent e)
    {
        if (Batching || Player.Instance == null) return;
        if (e is InputEventKey { Pressed: true, Echo: false } k)
        {
            switch (k.PhysicalKeycode)
            {
                case Key.Quoteleft: SetPlay(!_play); break;
                case Key.F1: Cam("full"); break; case Key.F2: Cam("side"); break; case Key.F3: Cam("front"); break;
                case Key.F4: Cam("hand"); break; case Key.F6: Cam("face"); break; case Key.F7: Cam("feet"); break;
                case Key.Bracketleft: _speed = Mathf.Max(.05f, _speed * .5f); break;
                case Key.Bracketright: _speed = Mathf.Min(1f, _speed * 2f); break;
                case Key.Backslash: _paused = !_paused; break;
                case Key.Equal: _ = Step(); break;
            }
        }
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed) _dist = Mathf.Max(.3f, _dist * .9f);
            if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed) _dist = Mathf.Min(14f, _dist * 1.1f);
            if (mb.ButtonIndex == MouseButton.Right && !_play) _drag = mb.Pressed;
        }
        if (e is InputEventMouseMotion mm && (_play || _drag))
        {
            _yaw -= mm.Relative.X * .006f; _pitch = Mathf.Clamp(_pitch + mm.Relative.Y * .005f, -1.2f, 1.4f);
        }
    }
    async Task Step() { if (!_paused) return; _paused = false; await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); _paused = true; }

    void BuildUi()
    {
        var layer = new CanvasLayer { Layer = 50 }; AddChild(layer);
        var panel = new PanelContainer { Position = new Vector2(10, 10) }; layer.AddChild(panel); _panel = panel;
        var box = new VBoxContainer(); panel.AddChild(box);
        _info = new Label(); box.AddChild(_info);
        void Row(string title, IEnumerable<(string, Action)> items)
        {
            box.AddChild(new Label { Text = title });
            var g = new GridContainer { Columns = 4 }; box.AddChild(g);
            foreach (var (n, a) in items) { var b = new Button { Text = n, FocusMode = Control.FocusModeEnum.None }; b.Pressed += a; g.AddChild(b); }
        }
        Row("Moves", Actions.Select(id => (id, (Action)(() => _ = RunMove(id)))));
        Row("Camera", new (string, Action)[] { ("full", () => Cam("full")), ("side", () => Cam("side")), ("front", () => Cam("front")), ("hand", () => Cam("hand")), ("face", () => Cam("face")), ("feet", () => Cam("feet")) });
        Row("Time", new (string, Action)[] { ("1x", () => _speed = 1), ("0.5x", () => _speed = .5f), ("0.25x", () => _speed = .25f), ("0.1x", () => _speed = .1f), ("pause", () => _paused = !_paused), ("step", () => _ = Step()) });
        Row("Show", new (string, Action)[] {
            ("blade trace", () => _trace = !_trace), ("bones", () => _bones = !_bones),
            ("sparring foe", () => { if (_spar != null && IsInstanceValid(_spar)) { _spar.QueueFree(); _spar = null; } else { _spar = Foe(false); _foe = null; } }),
            ("reset", Reset), ("film all", () => _ = Batch(Actions)) });
        box.AddChild(new Label { Text = "` play/tool   F1 F2 F3 F4 F6 F7 cameras   [ ] speed   \\ pause   = step" });
    }
}
