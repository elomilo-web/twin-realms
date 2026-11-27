using System;
using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// The lockpicking minigame (focus F-25), shown in the "lockpick" window (UiLife.cs). Move the pick around the top of the lock
/// (A/D, the arrow keys or the left stick) while the lock is free; hold tension (Space or E, the pad's A or right trigger) to turn the
/// cylinder. Inside the hidden sweet spot it turns all the way and the lock opens; off it, it stops early and the pick strains: hold too
/// long and it snaps (one lockpick gone). The sweet spot is wider with lockpicking levels (lockW) and harder locks (iron) make it narrower;
/// lockStrainP lets a pick hold longer. Success opens the door or chest (BlockUse.Lockpicked) and gives lockpicking XP once per lock
/// per 10 minutes.
/// </summary>
public partial class Lockpick : Control
{
    public const string Item = "lockpick";
    public static Lockpick? Cur;
    public Vector3I Cell; public bool Door; public string LockName = "";
    /// <summary>A-10: a lock that is not a block (a locked treasure chest, Props.cs Chest): its own key for the once-per-10-minutes XP rule; the block lock at Cell is left alone.</summary>
    public string LockKey = "";
    public float Angle = 90, Sweet = 90, Turn, Strain, Diff = .3f;
    public bool Solved; public int Broken;
    /// <summary>DevCapture: pick movement (-1..1) and tension without input.</summary>
    public static float DevAxis; public static bool DevTension;
    public Action? Finished;
    static readonly System.Collections.Generic.Dictionary<string, double> _picked = new();

    /// <summary>The sweet spot's width in degrees for an easy lock (wider with lockpicking levels: the stat key lockW).</summary>
    public static float SweetDeg() => Mathf.Clamp(12f * (1 + StatPipeline.Key("lockW") / 100f), 4f, 45f);
    public float Width => SweetDeg() * (1.15f - Diff * .5f);
    float StrainTime => .9f * (1 + StatPipeline.Key("lockStrainP") / 100f);

    public override void _Ready()
    {
        Cur = this; ProcessMode = ProcessModeEnum.Always; MouseFilter = MouseFilterEnum.Ignore;
        var rng = new RandomNumberGenerator(); rng.Randomize(); Sweet = rng.RandfRange(20, 160);
    }
    public override void _ExitTree() { if (Cur == this) Cur = null; }

    bool Tension() => DevTension || Input.IsActionPressed(Inputs.Jump) || Input.IsActionPressed(Inputs.Use) || Inputs.PadButton(JoyButton.A) || Inputs.PadAxis(JoyAxis.TriggerRight) > .5f;
    float Axis()
    {
        float a = DevAxis + Input.GetAxis(Inputs.Left, Inputs.Right);
        if (InputMap.HasAction("ui_left") && Input.IsActionPressed("ui_left")) a -= 1; if (InputMap.HasAction("ui_right") && Input.IsActionPressed("ui_right")) a += 1;
        float sx = Inputs.PadAxis(JoyAxis.LeftX); if (Mathf.Abs(sx) > .2f) a += sx;
        return Mathf.Clamp(a, -1, 1);
    }
    /// <summary>How far the cylinder may turn at the pick's angle: all the way in the sweet spot, less the further off it is.</summary>
    public float Allowed() { float off = Mathf.Abs(Angle - Sweet) - Width / 2; return off <= 0 ? 1f : Mathf.Clamp(.85f - off / 80f, 0f, .85f); }

    public override void _Process(double delta)
    {
        float dt = (float)delta; if (Solved) { QueueRedraw(); return; }
        bool t = Tension();
        if (!t) { Angle = Mathf.Clamp(Angle + Axis() * 70f * dt, 0, 180); Turn = Mathf.MoveToward(Turn, 0, dt * 3f); Strain = Mathf.Max(0, Strain - dt * 1.5f); }
        else
        {
            float al = Allowed(); Turn = Mathf.MoveToward(Turn, al, dt * 1.4f);
            if (al >= 1 && Turn >= .999f) { Success(); return; }
            if (Turn >= al - .005f && al < 1) { Strain += dt / StrainTime; if (Strain >= 1) Snap(); }
        }
        QueueRedraw();
    }
    void Snap()
    {
        var m = Game.D.Mats; m[Item] = m.GetValueOrDefault(Item) - 1; if (m[Item] <= 0) m.Remove(Item);
        Broken++; Strain = 0; Turn = 0; Sfx.Play("hit", .6f, 1.9f); LifeSkills.Award("lockpicking", .5f);
        int left = m.GetValueOrDefault(Item);
        Ui.Note(left > 0 ? $"The pick snapped ({left} left)" : "Your last pick snapped", new Color(1, .6f, .5f));
        if (left <= 0) { Solved = true; Callable.From(() => Finished?.Invoke()).CallDeferred(); }
    }
    void Success()
    {
        Solved = true; Turn = 1; if (LockKey == "") BlockUse.Lockpicked(Cell); Sfx.Play("lock", .9f, 1.3f);   // (a treasure chest, LockKey set: its own Finished opens it)
        string key = LockKey != "" ? LockKey : BlockWorld.I?.Area is { } a ? BlockUse.Key(a, Cell.X, Cell.Y, Cell.Z) : Cell.ToString(); double now = Time.GetTicksMsec() / 1000.0;
        bool fresh = !_picked.TryGetValue(key, out var when) || now - when > 600; _picked[key] = now;
        if (fresh) LifeSkills.Award("lockpicking", 8 + Diff * 20);   // the same lock again soon after (re-locked by its owner) teaches nothing
        LastResult = $"picked {LockName} (difficulty {Diff:0.00}, sweet spot {Width:0.#} deg), {Broken} picks broken, xp {(fresh ? "given" : "none (picked recently)")}";
        Ui.Note($"Click. The {LockName} opens", UiTheme.Highlight);
        Callable.From(() => Finished?.Invoke()).CallDeferred();
    }
    public static string LastResult = "";

    public override void _Draw()
    {
        var sz = Size; float R = Mathf.Min(sz.X * .26f, sz.Y * .3f); var c = new Vector2(sz.X / 2, R * 1.4f + 4);
        var brass = new Color(.72f, .56f, .28f); var iron = new Color(.2f, .19f, .21f);
        DrawCircle(c, R + 10, new Color(0, 0, 0, .5f)); DrawCircle(c, R, iron); DrawArc(c, R, 0, Mathf.Tau, 64, brass, 3f, true);
        DrawCircle(c, R * .62f, iron.Lightened(.08f)); DrawArc(c, R * .62f, 0, Mathf.Tau, 48, brass.Darkened(.3f), 2f, true);
        // the keyhole turns with the cylinder (up to 90 degrees)
        float rot = Turn * Mathf.Pi / 2; Vector2 Rt(Vector2 v) => c + v.Rotated(rot);
        DrawCircle(Rt(new Vector2(0, -R * .14f)), R * .11f, Colors.Black);
        DrawColoredPolygon(new[] { Rt(new Vector2(-R * .06f, -R * .1f)), Rt(new Vector2(R * .06f, -R * .1f)), Rt(new Vector2(R * .1f, R * .3f)), Rt(new Vector2(-R * .1f, R * .3f)) }, Colors.Black);
        // the tension wrench at the bottom of the keyhole, the pick from the top arc toward the centre
        DrawLine(Rt(new Vector2(0, R * .3f)), Rt(new Vector2(R * .1f, R * 1.05f)), new Color(.6f, .6f, .65f), 5f, true);
        float a = Mathf.DegToRad(180 - Angle); var tip = c + new Vector2(Mathf.Cos(a), -Mathf.Sin(a)) * R * .1f; var tail = c + new Vector2(Mathf.Cos(a), -Mathf.Sin(a)) * (R * 1.22f);
        float shake = Strain > 0 ? Mathf.Sin(Time.GetTicksMsec() * .08f) * Strain * 3f : 0;
        DrawLine(tail + new Vector2(shake, 0), tip + new Vector2(shake, 0), new Color(.82f, .84f, .88f), 3.5f, true);
        DrawLine(tail + new Vector2(shake, 0), tail + (tail - c).Normalized() * R * .16f, brass, 7f, true);
        // a faint arc where the pick can sit
        DrawArc(c, R * 1.12f, Mathf.Pi, Mathf.Tau, 40, new Color(1, 1, 1, .12f), 2f, true);
        // the bars: how far the lock turned, how hard the pick strains
        var font = ThemeDB.FallbackFont; float bw = sz.X * .7f, bx = (sz.X - bw) / 2, by = sz.Y - 46;
        DrawRect(new Rect2(bx, by, bw, 12), new Color(0, 0, 0, .55f)); DrawRect(new Rect2(bx, by, bw * Turn, 12), Turn >= .999f ? new Color(.5f, 1f, .55f) : brass);
        DrawRect(new Rect2(bx, by + 18, bw, 8), new Color(0, 0, 0, .55f)); DrawRect(new Rect2(bx, by + 18, bw * Strain, 8), new Color(1f, .35f, .25f));
        DrawString(font, new Vector2(bx, by - 6), "TENSION", HorizontalAlignment.Left, -1, 12, new Color(.9f, .82f, .6f));
        DrawString(font, new Vector2(bx + bw - 60, by + 40), "STRAIN", HorizontalAlignment.Left, -1, 11, new Color(1f, .6f, .5f));
        if (Solved && Turn >= .999f) DrawString(font, new Vector2(0, 30), "UNLOCKED", HorizontalAlignment.Center, sz.X, 26, new Color(.6f, 1f, .6f));
    }
}
