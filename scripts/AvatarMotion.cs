using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>
/// Partial: how the model's clips are timed and joined (data/avatar.json "gait", "blend", "rates").
/// - Locomotion: each loop's own stride speed is measured from its foot tracks when the library loads (the planted foot slides back at
///   exactly the body's speed), logged as "[AVATAR] strides"; playback = ground speed / stride, kept in a natural band; outside the band
///   the gait changes (walk, jog, sprint) instead of the legs speeding up, with a phase-matched crossfade so there is no pop.
/// - One-shots blend out before their last frame (Busy ends blendOut seconds early), so nothing ends and snaps to idle.
/// - Attacks follow a time warp: a natural-rate wind-up, a held top of the swing when it has to be slower (the telegraph), a full-speed
///   strike landing exactly at the attack's hit time, a follow-through at natural speed; the caller then blends into the _Rec clip.
/// - Root motion: the travel of the _RM clips (Roll_RM, Sword_Dash_RM, Shield_Dash_RM, Hit_Knockback_RM) is kept as a curve on the clip
///   when the library is built (meta "rm_curve"), so callers move the body with the clip's own timing.
/// </summary>
public partial class Avatar
{
    // ------------------------------------------------------------------ data
    static float Gait(string k, float d) => Cfg.TryGetProperty("gait", out var g) ? JsonUtil.F(g, k, d) : d;
    /// <summary>A blend time (seconds) from data/avatar.json "blend".</summary>
    public static float Blend(string k, float d) => Cfg.TryGetProperty("blend", out var g) ? JsonUtil.F(g, k, d) : d;
    /// <summary>A playback-rate rule from data/avatar.json "rates".</summary>
    public static float Rate(string k, float d) => Cfg.TryGetProperty("rates", out var g) ? JsonUtil.F(g, k, d) : d;
    /// <summary>The playback band for a fitted one-shot (Play with fit): rates.fit.&lt;key&gt; [min, max], else rates.fitMin / fitMax.</summary>
    static (float lo, float hi) FitBand(string key)
    {
        if (Cfg.TryGetProperty("rates", out var r) && r.TryGetProperty("fit", out var f) && f.TryGetProperty(key, out var b) && b.ValueKind == JsonValueKind.Array && b.GetArrayLength() >= 2)
            return (b[0].GetSingle(), b[1].GetSingle());
        return (Rate("fitMin", .67f), Rate("fitMax", 1.5f));
    }
    /// <summary>The clip for this action key exists on this body (e.g. the crouch clips before a re-bake).</summary>
    public bool HasClip(string key) => _ap != null && _ap.HasAnimation(Clip(key));

    // ------------------------------------------------------------------ strides (measured once per body library)
    static readonly Dictionary<string, Dictionary<string, float>> _strides = new();
    static readonly string[] GaitKeys = { "walk", "run", "sprint", "crouchMove" };
    static void MeasureStrides(string key, AnimationLibrary lib, Skeleton3D sk)
    {
        var d = new Dictionary<string, float>(); var log = new List<string>();
        foreach (var g in GaitKeys)
        {
            string n = Clip(g); float s = lib.HasAnimation(n) ? MeasureStride(lib.GetAnimation(n), sk) : 0f;
            if (s > 0) d[g] = s; log.Add($"{g}({n})={(s > 0 ? s.ToString("0.00") : "-")}");
        }
        _strides[key] = d;
        GD.Print($"[AVATAR] strides {key} (m/s at rate 1, skeleton space): " + string.Join(" ", log));
    }
    /// <summary>
    /// A loop's ground speed at rate 1: while a foot is planted (within 3 cm of its lowest point) it moves back under the hips at the body's
    /// speed, so the mean horizontal foot speed over the planted samples is the clip's stride speed. Forward kinematics on the track data.
    /// </summary>
    static float MeasureStride(Animation a, Skeleton3D sk)
    {
        float len = (float)a.Length; if (len < .2f) return 0;
        int n = Mathf.Clamp((int)(len * 60), 24, 240); float best = 0;
        foreach (var foot in new[] { "foot_l", "foot_r" })
        {
            int b = sk.FindBone(foot); if (b < 0) continue;
            var p = new Vector3[n + 1]; float minY = float.MaxValue;
            for (int i = 0; i <= n; i++) { p[i] = BoneAt(a, sk, b, len * i / n); minY = Mathf.Min(minY, p[i].Y); }
            float sum = 0, time = 0, step = len / n, tol = Gait("plantTol", .03f);
            for (int i = 1; i <= n; i++)
                if (p[i].Y < minY + tol && p[i - 1].Y < minY + tol) { sum += new Vector2(p[i].X - p[i - 1].X, p[i].Z - p[i - 1].Z).Length(); time += step; }
            if (time > len * .12f) best = Mathf.Max(best, sum / time);
        }
        return best;
    }
    /// <summary>A bone's skeleton-space position at time t of a clip (rest pose where the clip has no track).</summary>
    static Vector3 BoneAt(Animation a, Skeleton3D sk, int bone, float t)
    {
        var chain = new List<int>(); for (int b = bone; b >= 0; b = sk.GetBoneParent(b)) chain.Add(b);
        var x = Transform3D.Identity;
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            int b = chain[i]; var local = sk.GetBoneRest(b); var path = new NodePath("Armature/Skeleton3D:" + sk.GetBoneName(b));
            int rt = a.FindTrack(path, Animation.TrackType.Rotation3D); if (rt >= 0) local.Basis = new Basis(a.RotationTrackInterpolate(rt, t));
            int pt = a.FindTrack(path, Animation.TrackType.Position3D); if (pt >= 0) local.Origin = a.PositionTrackInterpolate(pt, t);
            x *= local;
        }
        return x.Origin;
    }
    /// <summary>World ground speed (m/s) of a gait at rate 1 on this body: the measured stride x the skeleton's world scale, else the data value.</summary>
    public float StrideOf(string gait)
    {
        float ws = _sk != null && IsInstanceValid(_sk) && _sk.IsInsideTree() ? _sk.GlobalTransform.Basis.Scale.Y : 1f;
        if (Gait("measure", 1) > .5f && _strides.TryGetValue(_sex, out var d) && d.TryGetValue(gait, out var s) && s * ws > .25f && s * ws < 12f) return s * ws * Gait("measureMul", 1f);
        return Gait(gait, gait == "walk" ? .83f : gait == "sprint" ? 3.48f : gait == "crouchMove" ? .9f : 2.68f);
    }

    // ------------------------------------------------------------------ locomotion
    /// <summary>Sneaking: the real crouch clips (Crouch_Idle / Crouch_Fwd) when this body has them.</summary>
    public bool Crouching;
    /// <summary>The crouch clips are what is playing now (HeroPose then leaves the legs alone).</summary>
    public bool CrouchClip => Cur == Clip("crouchIdle") || Cur == Clip("crouchMove");
    string _gait = "";
    /// <summary>Position in the current loop (0..1): the stride phase, for gait crossfades and the arm swing layer.</summary>
    public float GaitPhase => _ap != null && _ap.IsPlaying() && _ap.HasAnimation(Cur) ? (float)(_ap.CurrentAnimationPosition / Mathf.Max(.01, _ap.CurrentAnimationLength)) : 0f;
    /// <summary>How much faster the body moves than the clip at its fastest natural rate (0 = the legs keep up): HeroPose adds arm swing for it.</summary>
    public float OverSpeed { get; private set; }

    /// <summary>
    /// Walking / jogging / sprinting / idle from ground speed (m/s). back = backpedal (the loop plays in reverse). sprint (A-02): the mover
    /// says whether it is sprinting (null = by speed); with gait.sprintNeedsKey the sprint clip needs it, otherwise a fast hero gets the
    /// longer sprint stride rather than a jog at double speed.
    /// </summary>
    public void Move(float speed, bool grounded, bool armed, bool shield, bool back = false, bool? sprint = null)
    {
        if (_ap == null) return;
        if (!grounded) { Play("fall", Blend("air", .2f)); _gait = ""; return; }
        bool moving = speed > Gait("moveMin", .35f);
        if (Busy && Cur != Clip("land") && !moving) return;   // a one-shot runs on while standing (it ends blendOut early, see Busy)
        bool crouch = Crouching && HasClip("crouchIdle") && HasClip("crouchMove");
        float lo = Gait("minBand", .7f), hi = Gait("maxBand", 1.3f), hy = Gait("hysteresis", .08f);
        string g = "";
        if (moving)
        {
            if (crouch) g = "crouchMove";
            else
            {
                bool sprintOk = !back && !(Gait("sprintNeedsKey", 0) > .5f && sprint == false);
                var order = sprintOk ? new[] { "walk", "run", "sprint" } : new[] { "walk", "run" };
                g = order[^1];
                foreach (var k in order) if (speed <= StrideOf(k) * hi) { g = k; break; }
                // hysteresis: stay in the current gait while the speed is still (nearly) inside its band
                if (_gait != g && Array.IndexOf(order, _gait) >= 0 && speed >= StrideOf(_gait) * lo * (1 - hy) && speed <= StrideOf(_gait) * hi * (1 + hy)) g = _gait;
            }
        }
        string idleKey = crouch ? "crouchIdle" : shield ? "idleShield" : armed ? "idleArmed" : "idle";
        string want = Clip(g != "" ? g : idleKey);
        bool wasLoco = _gait != "" && Cur == Clip(_gait), wasOneShot = _ap.HasAnimation(Cur) && _ap.GetAnimation(Cur).LoopMode == Animation.LoopModeEnum.None;
        if (want != Cur || (back && moving) != _rev)
        {
            float blend = wasOneShot ? Blend("fromAction", .25f) : g != "" && !wasLoco ? Blend("start", .22f) : g == "" ? Blend("stop", .28f) : Blend("gait", .3f);
            float phase = wasLoco && g != "" && (back && moving) == _rev ? GaitPhase : -1f;
            Play(g != "" ? g : idleKey, blend, 0, false, 0, back && moving);
            if (phase >= 0 && _ap.HasAnimation(want)) _ap.Seek(phase * _ap.GetAnimation(want).Length, false);   // the new gait starts on the same foot
        }
        _gait = g;
        // the feet keep pace with the ground inside the band; the top gait may stretch to gait.topMax (beyond that the body glides)
        if (g == "") { _ap.SpeedScale = 1f; OverSpeed = 0; return; }
        float stride = StrideOf(g), top = g is "sprint" || (g == "run" && back) || g == "crouchMove" ? Gait("topMax", 1.5f) : hi;
        float rate = speed / Mathf.Max(.1f, stride);
        _ap.SpeedScale = Mathf.Clamp(rate, lo, top); OverSpeed = Mathf.Max(0, rate / top - 1f);
    }

    // ------------------------------------------------------------------ one-shots end early into the next clip
    /// <summary>Seconds before its end at which a one-shot hands over (the next clip blends in over them): rates.blendOut.&lt;clip&gt; or blend.out.</summary>
    float BlendOutOf(string clip) => clip.StartsWith("atk_rec") ? Blend("outRec", .25f) : clip == Clip("die") ? 0f : Blend("out", .22f);
    float Remaining()
    {
        if (_ap == null || !_ap.HasAnimation(Cur)) return 0;
        float sp = Mathf.Abs(_ap.SpeedScale * _ap.GetPlayingSpeed()); if (sp < .05f) sp = .05f;
        return (float)(_rev ? _ap.CurrentAnimationPosition : _ap.CurrentAnimationLength - _ap.CurrentAnimationPosition) / sp;
    }
    /// <summary>A one-shot clip (attack, cast, hit) is still running and not yet in its blend-out.</summary>
    public bool Busy => _ap != null && _ap.IsPlaying() && _ap.HasAnimation(Cur) && _ap.GetAnimation(Cur).LoopMode == Animation.LoopModeEnum.None && (_wOn || Remaining() > BlendOutOf(Cur));

    // ------------------------------------------------------------------ attack time warp
    bool _wOn; string _wName = ""; float _wEl, _wH, _wT, _wLen, _wPause, _wOff, _hpT, _hpMul = 1f;
    /// <summary>The attack clip has played its follow-through (the caller blends into the recovery clip).</summary>
    public bool AttackDone => !_wOn || Cur != _wName;
    /// <summary>On contact: the attack animation slows to mul for secs (the weapon bites; works online, unlike the global hit-stop).</summary>
    public void HitPause(float secs, float mul) { _hpT = Mathf.Max(_hpT, secs); _hpMul = Mathf.Clamp(mul, 0, 1); }
    /// <summary>The attack's clip time (seconds) at t seconds after the start: see the class summary.</summary>
    float WarpC(float t)
    {
        float H = _wH, T = Mathf.Max(.05f, _wT), r = H / T, rMin = Rate("windMin", .67f), hold = Rate("holdAt", .8f);
        if (t >= T) return H + (t - T) * Mathf.Max(Rate("follow", 1f), Mathf.Min(r, Rate("followMax", 2.5f)));
        if (_wPause <= 0 && r >= rMin) return r * t;
        float strikeLen = H * (1 - hold), strikeT = strikeLen / Mathf.Max(1f, r), upRate = Mathf.Max(rMin, r), upT = hold * H / upRate, holdT = T - strikeT - upT;
        if (holdT <= .01f) return r * t;
        float drift = Rate("holdDrift", .03f) * H;
        if (t < upT) return upRate * t;
        if (t < upT + holdT) return hold * H + drift * (t - upT) / holdT;
        return hold * H + drift + (t - upT - holdT) * (strikeLen - drift) / Mathf.Max(.01f, strikeT);
    }
    /// <summary>Runs every frame (AvClock): steers the attack clip along its warp and the hit pause.</summary>
    void MotionTick(float dt)
    {
        if (_ap == null) return;
        float mul = 1f; if (_hpT > 0) { _hpT -= dt; mul = _hpMul; }
        if (!_wOn) return;
        if (Cur != _wName || !_ap.IsPlaying()) { _wOn = false; return; }
        _wEl += dt * mul;
        float c = _wOff + WarpC(_wEl), rate = (WarpC(_wEl + .02f) + _wOff - c) / .02f, pos = (float)_ap.CurrentAnimationPosition;
        _ap.SpeedScale = Mathf.Clamp((rate + (c - pos) * 8f) * mul, 0f, 4f);
        if (c >= _wLen - BlendOutOf(Cur) * Mathf.Max(.3f, rate)) _wOn = false;   // the follow-through is (nearly) over: hand over to the recovery
    }
    /// <summary>Drives MotionTick from the frame loop.</summary>
    sealed partial class AvClock : Node { public Avatar Av = null!; public override void _Process(double delta) { if (IsInstanceValid(Av)) Av.MotionTick((float)delta); } }
    void AddClock() => AddChild(new AvClock { Name = "AvClock", Av = this });

    // ------------------------------------------------------------------ root motion
    /// <summary>The clip's root travel at a fraction of it (0..1 of the distance) and the whole distance (m, this body's size); false without data.</summary>
    public bool RootAt(string clipName, float frac, out float travel, out float dist)
    {
        travel = frac; dist = 0;
        if (_ap == null || !_ap.HasAnimation(clipName)) return false;
        var a = _ap.GetAnimation(clipName); if (!a.HasMeta("rm_curve")) return false;
        var c = a.GetMeta("rm_curve").AsFloat32Array(); if (c.Length < 2) return false;
        float x = Mathf.Clamp(frac, 0, 1) * (c.Length - 1); int i = Mathf.Min((int)x, c.Length - 2);
        travel = Mathf.Lerp(c[i], c[i + 1], x - i);
        float ws = _sk != null && IsInstanceValid(_sk) && _sk.IsInsideTree() ? _sk.GlobalTransform.Basis.Scale.Y : 1f;
        dist = (float)a.GetMeta("rm_dist", 0f).AsDouble() * ws; return true;
    }
    /// <summary>Bake / merge: keeps the root bone's travel of a root-motion clip as a 30 fps curve (0..1 along its main direction) and its length.</summary>
    static void KeepRootCurve(Animation a, int track, float scale)
    {
        float len = (float)a.Length; int n = Mathf.Max(2, Mathf.CeilToInt(len * 30f));
        var p = new Vector3[n + 1]; for (int i = 0; i <= n; i++) p[i] = a.PositionTrackInterpolate(track, len * i / n) * scale;
        var dir = p[n] - p[0]; if (dir.Length() < .05f) { int far = 0; for (int i = 1; i <= n; i++) if ((p[i] - p[0]).LengthSquared() > (p[far] - p[0]).LengthSquared()) far = i; dir = p[far] - p[0]; }
        float tot = dir.Length(); if (tot < .05f) return; var u = dir / tot;
        float end = (p[n] - p[0]).Dot(u); if (end < .05f) end = tot;
        var c = new float[n + 1]; for (int i = 0; i <= n; i++) c[i] = Mathf.Clamp((p[i] - p[0]).Dot(u) / end, -.2f, 1.2f);
        a.SetMeta("rm_curve", c); a.SetMeta("rm_dist", end);
    }
    /// <summary>The root curve of a slice t0..t1 (seconds) of a source clip, maybe reversed, onto the cut animation.</summary>
    static void SliceRootCurve(Animation src, Animation dst, float t0, float t1, bool reverse)
    {
        if (!src.HasMeta("rm_curve")) return; var c = src.GetMeta("rm_curve").AsFloat32Array(); if (c.Length < 2) return;
        float len = (float)src.Length; float At(float t) { float x = Mathf.Clamp(t / Mathf.Max(.01f, len), 0, 1) * (c.Length - 1); int i = Mathf.Min((int)x, c.Length - 2); return Mathf.Lerp(c[i], c[i + 1], x - i); }
        float v0 = At(t0), v1 = At(t1), span = v1 - v0; if (Mathf.Abs(span) < .02f) return;
        int n = Mathf.Max(2, Mathf.CeilToInt((t1 - t0) * 30f)); var o = new float[n + 1];
        for (int i = 0; i <= n; i++) { float t = reverse ? t1 - (t1 - t0) * i / n : t0 + (t1 - t0) * i / n; o[i] = reverse ? (v1 - At(t)) / span : (At(t) - v0) / span; }
        dst.SetMeta("rm_curve", o); dst.SetMeta("rm_dist", (float)src.GetMeta("rm_dist", 0f).AsDouble() * Mathf.Abs(span));
    }

    /// <summary>The attack animation a humanoid monster swings with (data/avatar.json enemies.&lt;type&gt;.attacks, else enemyAttacks by its weapon, slam or default).</summary>
    public static string FoeAttack(string type, bool slam, int i)
    {
        List<string>? l = null;
        if (Foe(type) is { } fc && fc.TryGetProperty("attacks", out _)) l = L(fc, "attacks");
        if ((l == null || l.Count == 0) && Cfg.TryGetProperty("enemyAttacks", out var ea))
        {
            string wt = Foe(type) is { } f2 ? S(f2, "weapon", "sword") : "sword";
            l = slam && ea.TryGetProperty("slam", out _) ? L(ea, "slam") : ea.TryGetProperty(wt, out _) ? L(ea, wt) : L(ea, "default");
        }
        if (l == null || l.Count == 0) return "slashA";
        return l[((i % l.Count) + l.Count) % l.Count];
    }

    // ------------------------------------------------------------------ clips wanted but not baked
    /// <summary>Every source clip the game plays: data/avatar.json "clips" values and the clips the attack animations are cut from.</summary>
    static HashSet<string> WantedClips()
    {
        var used = new HashSet<string>();
        foreach (var p in Cfg.GetProperty("clips").EnumerateObject())
            if (p.Name.StartsWith("_")) continue;   // notes, not clips (they were counted as missing clips)
            else if (p.Value.ValueKind == JsonValueKind.String) used.Add(p.Value.GetString()!); else if (p.Value.ValueKind == JsonValueKind.Array) foreach (var x in p.Value.EnumerateArray()) used.Add(x.GetString()!);
        foreach (var (_, d) in AttackDefs()) used.Add(d.Clip);
        used.Remove(""); return used;
    }
}
