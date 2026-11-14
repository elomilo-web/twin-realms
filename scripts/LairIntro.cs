using Godot;

namespace TwinRealms;

/// <summary>
/// The boss room intro (Scarletta's Rotting Hollow): when the first hero steps into the great chamber, every hero on the floor is brought
/// to the edge of the fighting ground and watches the same eight seconds: a wide shot across the chamber, a slow orbit in to her as she
/// rises, her name, then back to the hero. Heroes cannot move meanwhile and the HUD steps aside; she wakes when it ends.
/// </summary>
public partial class LairIntro : CanvasLayer
{
    public static bool Active => _i != null && IsInstanceValid(_i);
    static LairIntro? _i;
    const float Len = 8f;
    float _t; Camera3D _cam = null!; Camera3D? _prev; ColorRect _top = null!, _bot = null!, _fade = null!; Label _name = null!, _sub = null!; bool _posed;

    public static void Play()
    {
        if (Active || Game.Root == null) return;
        var p = Player.Instance; if (p != null && !p.Dead) p.Teleport(Dungeon.IntroSpot(), 0f);   // the party is brought into the boss room
        _i = new LairIntro { Name = "LairIntro" }; Game.Root.AddChild(_i);
    }

    public override void _Ready()
    {
        Layer = 60;
        _top = new ColorRect { Color = Colors.Black }; _bot = new ColorRect { Color = Colors.Black }; _fade = new ColorRect { Color = Colors.Black };
        foreach (var r in new[] { _top, _bot, _fade }) { r.MouseFilter = Control.MouseFilterEnum.Ignore; AddChild(r); }
        _fade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        Label L(int size, Color c) { var l = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, 0) }; l.AddThemeFontSizeOverride("font_size", size); l.AddThemeColorOverride("font_color", c); l.AddThemeColorOverride("font_outline_color", Colors.Black); l.AddThemeConstantOverride("outline_size", 10); AddChild(l); return l; }
        _name = L(64, new Color(1f, .82f, .45f)); _name.Text = "SCARLETTA";
        _sub = L(28, new Color(.75f, 1f, .6f)); _sub.Text = "The Rotting Blade";
        _prev = GetViewport().GetCamera3D();
        _cam = new Camera3D { Fov = 55f, Far = 4000f }; Game.Root!.AddChild(_cam);
        _cam.Current = true;
        Sfx.Play("roar", .4f);
    }

    public override void _Process(double delta)
    {
        _t += (float)delta; var sz = GetViewport().GetVisibleRect().Size; float bar = sz.Y * .12f * Mathf.Clamp(Mathf.Min(_t / .6f, (Len - _t) / .6f), 0, 1);
        _top.Position = Vector2.Zero; _top.Size = new Vector2(sz.X, bar); _bot.Position = new Vector2(0, sz.Y - bar); _bot.Size = new Vector2(sz.X, bar);
        _fade.Color = new Color(0, 0, 0, Mathf.Clamp(1 - _t / .7f, 0, 1) + Mathf.Clamp((_t - (Len - .35f)) / .35f, 0, 1));
        _name.Position = new Vector2(0, sz.Y * .66f); _name.Size = new Vector2(sz.X, 80); _sub.Position = new Vector2(0, sz.Y * .66f + 76); _sub.Size = new Vector2(sz.X, 40);
        float ta = Mathf.Clamp((_t - 3.4f) / .8f, 0, 1) * Mathf.Clamp((Len - .6f - _t) / .8f, 0, 1); _name.Modulate = new Color(1, 1, 1, ta); _sub.Modulate = new Color(1, 1, 1, Mathf.Clamp(ta * 1.2f - .2f, 0, 1));

        var boss = Dungeon.Boss; var focus = (boss?.GlobalPosition ?? Dungeon.BossRoomCentre) + Vector3.Up * 6f;
        if (!_posed && _t > 3.1f) { _posed = true; boss?.ScarIntroPose(); }
        Vector3 pos;
        if (_t < 3f)
        {
            // a wide, slow drift across the chamber toward her
            float k = Ease(_t / 3f), a = Mathf.Lerp(-.7f, .25f, k), r = Mathf.Lerp(170f, 60f, k), h = Mathf.Lerp(70f, 20f, k);
            pos = focus + new Vector3(Mathf.Sin(a) * r, h, Mathf.Cos(a) * r);
        }
        else if (_t < 6.2f)
        {
            // closing in and circling her as she rises
            float k = Ease((_t - 3f) / 3.2f), a = Mathf.Lerp(.25f, 1.35f, k), r = Mathf.Lerp(30f, 15f, k), h = Mathf.Lerp(6f, 2.5f, k);
            pos = focus + new Vector3(Mathf.Sin(a) * r, h, Mathf.Cos(a) * r);
        }
        else
        {
            // back to the hero's view
            float k = Ease((_t - 6.2f) / (Len - 6.2f)); var from = focus + new Vector3(Mathf.Sin(1.35f) * 15f, 2.5f, Mathf.Cos(1.35f) * 15f);
            var to = _prev != null && IsInstanceValid(_prev) ? _prev.GlobalPosition : from;
            pos = from.Lerp(to, k);
            if (k > .98f && _prev != null && IsInstanceValid(_prev)) { _cam.GlobalTransform = _prev.GlobalTransform; }
        }
        if (_t < Len - .2f || _prev == null) _cam.LookAtFromPosition(pos, _t < 6.2f ? focus : focus.Lerp(Player.Instance?.GlobalPosition + Vector3.Up * 1.6f ?? focus, Ease((_t - 6.2f) / (Len - 6.2f))), Vector3.Up);
        if (_t >= Len) End();
    }
    static float Ease(float k) { k = Mathf.Clamp(k, 0, 1); return k * k * (3 - 2 * k); }
    void End()
    {
        if (_prev != null && IsInstanceValid(_prev)) _prev.Current = true;
        _cam.QueueFree(); _i = null; QueueFree();
        Ui.Banner("Scarletta, The Rotting Blade", "she has judged you");
    }
}
