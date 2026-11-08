using Godot;

namespace TwinRealms;

/// <summary>Health, stamina and XP bars, controls hint and the death screen. Built in code; no scene needed.</summary>
public partial class Hud : CanvasLayer
{
    ColorRect _hpBg = null!, _hp = null!, _stBg = null!, _st = null!, _xpBg = null!, _xp = null!;
    Label _info = null!, _hint = null!, _dead = null!, _dot = null!;
    const float W = 280f;

    public override void _Ready()
    {
        _hpBg = Bar(new Color(0, 0, 0, 0.55f)); _hp = Bar(new Color(0.8f, 0.15f, 0.15f));
        _stBg = Bar(new Color(0, 0, 0, 0.55f)); _st = Bar(new Color(0.25f, 0.75f, 0.3f));
        _xpBg = Bar(new Color(0, 0, 0, 0.55f)); _xp = Bar(new Color(0.4f, 0.6f, 1f));
        _info = Text(18); _hint = Text(14); _dead = Text(56); _dot = Text(18);
        _hint.Text = "WASD move  |  Shift sprint  |  Space jump  |  C roll  |  LMB strike (hold = power)  |  RMB guard (block right as a hit lands to parry)  |  Esc frees the mouse";
        _hint.Modulate = new Color(1, 1, 1, 0.7f);
        _dead.Text = "YOU DIED"; _dead.Modulate = new Color(0.8f, 0.1f, 0.1f); _dead.Visible = false;
        _dot.Text = "·"; 
    }

    ColorRect Bar(Color c) { var r = new ColorRect { Color = c, MouseFilter = Control.MouseFilterEnum.Ignore }; AddChild(r); return r; }
    Label Text(int size)
    {
        var l = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_outline_color", Colors.Black);
        l.AddThemeConstantOverride("outline_size", 6);
        AddChild(l); return l;
    }

    public override void _Process(double delta)
    {
        var p = Player.Instance; if (p == null) return;
        var sz = GetViewport().GetVisibleRect().Size;
        float x = 24, y = sz.Y - 100;
        Place(_hpBg, _hp, x, y, W, 18, p.Hp / p.Stats.MaxHp);
        Place(_stBg, _st, x, y + 24, W, 12, p.St / p.Stats.MaxSt);
        Place(_xpBg, _xp, x, y + 42, W, 6, (float)p.Stats.Xp / p.Stats.XpNeed);
        _info.Position = new Vector2(x, y - 30);
        _info.Text = $"{p.Stats.Name}   Level {p.Stats.Level}   HP {Mathf.CeilToInt(p.Hp)}/{p.Stats.MaxHp}   Kills {p.Stats.Kills}";
        _hint.Position = new Vector2(16, 12);
        _dot.Position = sz / 2 - new Vector2(4, 14);
        _dead.Visible = p.Dead;
        if (p.Dead) _dead.Position = sz / 2 - new Vector2(150, 40);
    }

    static void Place(ColorRect bg, ColorRect fill, float x, float y, float w, float h, float frac)
    {
        bg.Position = new Vector2(x, y); bg.Size = new Vector2(w, h);
        fill.Position = new Vector2(x, y); fill.Size = new Vector2(w * Mathf.Clamp(frac, 0, 1), h);
    }
}
