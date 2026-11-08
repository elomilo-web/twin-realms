using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>In-game HUD: bars, spell hotbar, potions, boss bar, compass and place name, minimap, interact prompt, status text.</summary>
public partial class Hud : CanvasLayer
{
    ColorRect _hpBg = null!, _hp = null!, _mpBg = null!, _mp = null!, _stBg = null!, _st = null!, _xpBg = null!, _xp = null!, _bossBg = null!, _boss = null!;
    Label _info = null!, _prompt = null!, _dead = null!, _place = null!, _status = null!, _bossName = null!, _pots = null!, _clock = null!;
    readonly Label[] _spell = new Label[4]; readonly ColorRect[] _spellBg = new ColorRect[4], _spellCd = new ColorRect[4];
    MiniMap _mini = null!;
    const float W = 300f;

    public override void _Ready()
    {
        Layer = 5;
        _hpBg = Bar(new Color(0, 0, 0, .6f)); _hp = Bar(new Color(.8f, .15f, .15f)); _mpBg = Bar(new Color(0, 0, 0, .6f)); _mp = Bar(new Color(.25f, .45f, .95f));
        _stBg = Bar(new Color(0, 0, 0, .6f)); _st = Bar(new Color(.3f, .75f, .3f)); _xpBg = Bar(new Color(0, 0, 0, .6f)); _xp = Bar(new Color(.9f, .75f, .3f));
        _bossBg = Bar(new Color(0, 0, 0, .65f)); _boss = Bar(new Color(.75f, .1f, .12f));
        _info = Text(18); _prompt = Text(22); _dead = Text(56); _place = Text(18); _status = Text(22); _bossName = Text(20); _pots = Text(16); _clock = Text(16);
        _dead.Text = "YOU DIED"; _dead.Modulate = new Color(.8f, .1f, .1f); _dead.Visible = false;
        for (int i = 0; i < 4; i++) { _spellBg[i] = Bar(new Color(0, 0, 0, .65f)); _spellCd[i] = Bar(new Color(.1f, .1f, .1f, .8f)); _spell[i] = Text(13); }
        _mini = new MiniMap { MouseFilter = Control.MouseFilterEnum.Ignore }; AddChild(_mini);
    }

    ColorRect Bar(Color c) { var r = new ColorRect { Color = c, MouseFilter = Control.MouseFilterEnum.Ignore }; AddChild(r); return r; }
    Label Text(int size)
    {
        var l = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size); l.AddThemeColorOverride("font_outline_color", Colors.Black); l.AddThemeConstantOverride("outline_size", 6);
        AddChild(l); return l;
    }

    public override void _Process(double delta)
    {
        var p = Player.Instance; if (p == null) return; var d = Game.D;
        var sz = GetViewport().GetVisibleRect().Size; float x = 24, y = sz.Y - 130;
        Place(_hpBg, _hp, x, y, W, 18, p.Hp / d.MaxHp()); Place(_mpBg, _mp, x, y + 22, W, 12, p.Mp / d.MaxMp()); Place(_stBg, _st, x, y + 38, W, 10, p.St / d.MaxSt()); Place(_xpBg, _xp, x, y + 52, W, 6, (float)d.Xp / d.XpNeed());
        _info.Position = new Vector2(x, y - 30); _info.Text = $"{d.Name}   Lv {d.Level}   HP {Mathf.CeilToInt(p.Hp)}/{d.MaxHp()}   MP {Mathf.CeilToInt(p.Mp)}   Gold {d.Gold}";
        _pots.Position = new Vector2(x, y + 64); _pots.Text = $"[H] Health x{d.HpPot}    [G] Mana x{d.MpPot}" + (d.AttrPts + d.SkillPts > 0 ? $"    Points: {d.AttrPts} attr / {d.SkillPts} skill" : "");
        // spell hotbar
        float hx = sz.X / 2 - 4 * 64 / 2f, hy = sz.Y - 84;
        for (int i = 0; i < 4; i++)
        {
            _spellBg[i].Position = new Vector2(hx + i * 64, hy); _spellBg[i].Size = new Vector2(58, 58);
            var sp = i < Player.Spells.Count ? Player.Spells[i] : null; float cdFrac = sp != null && sp.Cd > 0 ? Mathf.Clamp(p.SpellCd[i] / sp.Cd, 0, 1) : 0;
            _spellCd[i].Position = new Vector2(hx + i * 64, hy + 58 * (1 - cdFrac)); _spellCd[i].Size = new Vector2(58, 58 * cdFrac);
            _spell[i].Position = new Vector2(hx + i * 64 + 4, hy + 4); _spell[i].Text = sp == null ? "" : $"{i + 1}\n{sp.Name}\n{sp.Mp:0}mp";
            _spell[i].Modulate = p.Mp >= (sp?.Mp ?? 0) ? Colors.White : new Color(.6f, .6f, .8f);
        }
        // prompt, status, place, boss
        _prompt.Text = p.Prompt; _prompt.Visible = p.Prompt != "" && !Ui.AnyOpen; _prompt.Position = new Vector2(sz.X / 2 - 120, sz.Y / 2 + 90);
        _status.Text = Arenas.Active ? Arenas.Status : ""; _status.Position = new Vector2(sz.X / 2 - 140, 16);
        string place = Dungeon.Active ? "Depth " + Dungeon.Depth : PlaceName(p.GlobalPosition) + (World.Realm == 1 ? "  (Shadow)" : "");
        _place.Text = place; _place.Position = new Vector2(sz.X - 330, sz.Y - 36);
        float hrs = World.TimeOfDay * 24f; _clock.Text = Dungeon.Active ? "" : $"{(int)hrs:00}:{(int)((hrs - (int)hrs) * 60):00}"; _clock.Position = new Vector2(sz.X - 330, sz.Y - 60);
        var b = Enemy.CurrentBoss; bool showBoss = b != null && GodotObject.IsInstanceValid(b) && !b.Dead && b.Active;
        _bossBg.Visible = _boss.Visible = _bossName.Visible = showBoss;
        if (showBoss) { Place(_bossBg, _boss, sz.X / 2 - 260, 60, 520, 16, b!.Hp / b.MaxHp); _bossName.Text = b.Def.N; _bossName.Position = new Vector2(sz.X / 2 - 90, 30); }
        _dead.Visible = p.Dead; if (p.Dead) _dead.Position = sz / 2 - new Vector2(150, 40);
        _mini.Position = new Vector2(sz.X - 176, 14); _mini.Visible = !Dungeon.Active;
        // POI discovery
        if (!Dungeon.Active) foreach (var poi in World.Pois) if (!d.Disc.Contains(poi.Id) && new Vector2(poi.X - p.GlobalPosition.X, poi.Z - p.GlobalPosition.Z).Length() < poi.R) { d.Disc.Add(poi.Id); Ui.Banner(poi.Name, "discovered"); Sfx.Play("level"); }
    }

    static string PlaceName(Vector3 p) { Poi? best = null; float bd = 1e9f; foreach (var poi in World.Pois) { float dd = new Vector2(poi.X - p.X, poi.Z - p.Z).Length(); if (dd < poi.R && dd < bd) { bd = dd; best = poi; } } return best?.Name ?? "The Wilds"; }
    static void Place(ColorRect bg, ColorRect fill, float x, float y, float w, float h, float frac) { bg.Position = new Vector2(x, y); bg.Size = new Vector2(w, h); fill.Position = new Vector2(x, y); fill.Size = new Vector2(w * Mathf.Clamp(frac, 0, 1), h); }
}

/// <summary>Small circular-ish minimap around the player.</summary>
public partial class MiniMap : Control
{
    const float S = 160f, Span = 160f;
    public override void _Process(double delta) { QueueRedraw(); }
    public override void _Draw()
    {
        var p = Player.Instance; if (p == null) return; DrawRect(new Rect2(-3, -3, S + 6, S + 6), new Color(0, 0, 0, .7f));
        if (World.MapTex != null)
        {
            float tx = (p.GlobalPosition.X + 256f) / 512f * 256f, tz = (p.GlobalPosition.Z + 256f) / 512f * 256f, half = Span / 512f * 256f / 2f;
            DrawTextureRectRegion(World.MapTex, new Rect2(0, 0, S, S), new Rect2(tx - half, tz - half, half * 2, half * 2));
        }
        foreach (var poi in World.Pois) { var o = new Vector2(poi.X - p.GlobalPosition.X, poi.Z - p.GlobalPosition.Z) / Span * S + new Vector2(S / 2, S / 2); if (o.X > 4 && o.Y > 4 && o.X < S - 4 && o.Y < S - 4) DrawCircle(o, 4, new Color(1f, .82f, .29f)); }
        float yaw = p.Rig?.Yaw ?? 0f; var c = new Vector2(S / 2, S / 2); var f = new Vector2(-Mathf.Sin(yaw), -Mathf.Cos(yaw)); var r = new Vector2(-f.Y, f.X);
        DrawColoredPolygon(new[] { c + f * 8, c - f * 5 + r * 5, c - f * 5 - r * 5 }, new Color(1, .2f, .2f));
    }
}
