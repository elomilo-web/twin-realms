using Godot;

namespace TwinRealms;

/// <summary>In-game HUD matching the prototype layout: top-left info, compass, quest tracker, target bar, bottom bars, hotbar, minimap.</summary>
public partial class Hud : CanvasLayer
{
    HudCanvas _c = null!;
    public override void _Ready() { Layer = 5; _c = new HudCanvas { MouseFilter = Control.MouseFilterEnum.Ignore }; _c.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); AddChild(_c); }
    public override void _Process(double delta)
    {
        var p = Player.Instance; if (p == null || Dungeon.Active) { if (p == null) return; }
        if (!Dungeon.Active) foreach (var poi in World.Pois)
            if (!Game.D.Disc.Contains(poi.Id) && new Vector2(poi.X - p.GlobalPosition.X, poi.Z - p.GlobalPosition.Z).Length() < poi.R) { Game.D.Disc.Add(poi.Id); Ui.Banner(poi.Name, "discovered"); Sfx.Play("level"); }
    }
}

public partial class HudCanvas : Control
{
    static readonly Color Gold = new Color("e3b04b"), Mute = new Color("a095bd");
    Image? _img;
    Font F => ThemeDB.FallbackFont;
    public override void _Process(double delta) { QueueRedraw(); }

    void Txt(Vector2 pos, string s, int size, Color c, HorizontalAlignment al = HorizontalAlignment.Left, float width = -1)
    {
        DrawStringOutline(F, pos, s, al, width, size, 5, new Color(0, 0, 0, .9f)); DrawString(F, pos, s, al, width, size, c);
    }
    void Bar(Rect2 r, float frac, Color a, Color b, bool rtl = false)
    {
        DrawRect(r, new Color(0, 0, 0, .6f)); frac = Mathf.Clamp(frac, 0, 1);
        var f = rtl ? new Rect2(r.Position.X + r.Size.X * (1 - frac), r.Position.Y, r.Size.X * frac, r.Size.Y) : new Rect2(r.Position, new Vector2(r.Size.X * frac, r.Size.Y));
        DrawRect(f, a); DrawRect(new Rect2(f.Position, new Vector2(f.Size.X, f.Size.Y * .45f)), new Color(1, 1, 1, .12f));
        DrawRect(r, new Color(1, 1, 1, .35f), false, 1f);
    }

    public override void _Draw()
    {
        var p = Player.Instance; if (p == null) return; var d = Game.D; var sz = Size; float u = sz.X / 100f; int fs(float k) => Mathf.Max(11, (int)(u * k));
        var pad = u * 1.4f;
        // top left
        string realm = World.Realm == 1 ? "SHADOW" : "LIGHT"; float hrs = World.TimeOfDay * 24f;
        Txt(new Vector2(pad, pad + fs(1.3f)), $"{d.Name}   Lv {d.Level}   {d.Gold} gold", fs(1.25f), Colors.White);
        Txt(new Vector2(pad, pad + fs(1.3f) * 2.4f), Dungeon.Active ? $"Depth {Dungeon.Depth}" : $"{realm}   {(int)hrs:00}:{(int)((hrs - (int)hrs) * 60):00}   {PlaceName(p.GlobalPosition)}", fs(1.05f), World.Realm == 1 ? new Color(.75f, .6f, 1f) : Mute);
        if (Arenas.Active) Txt(new Vector2(pad, pad + fs(1.3f) * 3.8f), Arenas.Status, fs(1.15f), Gold);
        // quest tracker, top right
        var aq = Quests.Active();
        if (aq.Count > 0)
        {
            // right-aligned text needs a box: draw from (right edge - width) with that width
            var q = aq[0]; var s = Game.Q(q.Id); float qw = u * 30, qx = sz.X - pad - qw;
            Txt(new Vector2(qx, pad + fs(1.5f)), q.Name, fs(1.5f), Gold, HorizontalAlignment.Right, qw);
            Txt(new Vector2(qx, pad + fs(1.5f) * 2.1f), s.S == 2 ? "Return to " + Quests.GiverName(q.Giver) : q.Goal + (q.Need > 1 ? $" ({s.N}/{q.Need})" : ""), fs(1.05f), Colors.White, HorizontalAlignment.Right, qw);
        }
        // compass
        float yaw = p.Rig?.Yaw ?? 0f; float cw = u * 34, cx = sz.X / 2, cy = pad + fs(1.2f);
        DrawRect(new Rect2(cx - cw / 2, cy - fs(1.2f), cw, fs(1.7f)), new Color(0, 0, 0, .35f));
        for (int k = 0; k < 16; k++)
        {
            // a = compass heading clockwise from north (-Z); the camera faces heading -yaw, so +rel is to the right
            float a = k * Mathf.Tau / 16f; float rel = Mathf.Wrap(a + yaw, -Mathf.Pi, Mathf.Pi);
            float xx = cx + rel / (Mathf.Pi / 2) * (cw / 2); if (Mathf.Abs(xx - cx) > cw / 2) continue;
            string[] nm = { "N", "", "NE", "", "E", "", "SE", "", "S", "", "SW", "", "W", "", "NW", "" };
            if (nm[k] != "") Txt(new Vector2(xx - 20, cy), nm[k], fs(1.1f), k % 4 == 0 ? Gold : Colors.White, HorizontalAlignment.Center, 40); else DrawLine(new Vector2(xx, cy - fs(0.9f)), new Vector2(xx, cy - fs(0.4f)), new Color(1, 1, 1, .5f), 1f);
        }
        DrawLine(new Vector2(cx, cy - fs(1.2f)), new Vector2(cx, cy + fs(.5f)), Gold, 2f);
        // target bar
        Enemy? tgt = Enemy.CurrentBoss != null && GodotObject.IsInstanceValid(Enemy.CurrentBoss) && !Enemy.CurrentBoss.Dead && Enemy.CurrentBoss.Active ? Enemy.CurrentBoss : p.Dead ? null : p.AimPoint(40f).enemy;
        if (tgt != null && !tgt.Dead)
        {
            float tw = u * 24, ty = u * 6.2f; Txt(new Vector2(sz.X / 2, ty), $"{tgt.Def.N}  Lv {tgt.Level}", fs(1.2f), tgt.Def.Boss ? Gold : Colors.White, HorizontalAlignment.Center, tw);
            Bar(new Rect2(sz.X / 2 - tw / 2, ty + 6, tw, Mathf.Max(6, u * .7f)), tgt.Hp / tgt.MaxHp, new Color("b8362d"), new Color("8e241d"));
        }
        // crosshair
        DrawCircle(sz / 2, 3f, new Color(1, 1, 1, .75f));
        // prompt
        if (p.Prompt != "" && !Ui.AnyOpen) Txt(new Vector2(sz.X / 2, sz.Y * .62f), p.Prompt, fs(1.4f), Colors.White, HorizontalAlignment.Center, u * 40);
        // bottom bars: HP | MP | ST, xp under
        float bw = u * 38, bx = (sz.X - bw) / 2, by = sz.Y - pad - Mathf.Max(9, u * 1.1f), bh = Mathf.Max(9, u * 1.1f), gap = u * .8f;
        float unit = (bw - gap * 2) / 3.8f; float wH = unit, wM = unit * 1.8f, wS = unit;
        Bar(new Rect2(bx, by, wH, bh), p.Hp / d.MaxHp(), new Color("d4483f"), new Color("8e241d")); Txt(new Vector2(bx, by - 4), $"{Mathf.CeilToInt(p.Hp)}", fs(.95f), Colors.White);
        Bar(new Rect2(bx + wH + gap, by, wM, bh), p.Mp / d.MaxMp(), new Color("5b9be0"), new Color("2b5a96"), true); Txt(new Vector2(bx + wH + gap, by - 4), $"{Mathf.CeilToInt(p.Mp)}", fs(.95f), Colors.White);
        Bar(new Rect2(bx + wH + wM + gap * 2, by, wS, bh), p.St / d.MaxSt(), new Color("6ec283"), new Color("2f7345"));
        DrawRect(new Rect2(bx, by + bh + 3, bw, 3), new Color(0, 0, 0, .5f)); DrawRect(new Rect2(bx, by + bh + 3, bw * Mathf.Clamp((float)d.Xp / d.XpNeed(), 0, 1), 3), Gold);
        if (d.AttrPts + d.SkillPts > 0) Txt(new Vector2(sz.X / 2, by - bh - 18), $"Unspent points: {d.AttrPts} attribute (Esc > Character), {d.SkillPts} skill (K)", fs(1f), Gold, HorizontalAlignment.Center, bw);
        // hotbar bottom-left: 4 spells + 2 potions
        float ss = Mathf.Max(36, u * 4.4f), hx = pad, hy = sz.Y - pad - ss;
        for (int i = 0; i < 6; i++)
        {
            var r = new Rect2(hx + i * (ss + u * .5f), hy, ss, ss); DrawRect(r, new Color(.03f, .02f, .055f, .75f)); DrawRect(r, new Color(1, 1, 1, .35f), false, 1f);
            string name, key; float cd = 0, cdMax = 1; bool dim = false; string cnt = "";
            if (i < 4) { var sp = i < Player.Spells.Count ? Player.Spells[i] : null; name = sp?.Name ?? ""; key = (i + 1).ToString(); if (sp != null) { cd = p.SpellCd[i]; cdMax = Mathf.Max(0.01f, sp.Cd); dim = p.Mp < sp.Mp; cnt = $"{sp.Mp:0}"; } }
            else { bool hp = i == 4; name = hp ? "Health" : "Mana"; key = hp ? "H" : "G"; cnt = (hp ? d.HpPot : d.MpPot).ToString(); dim = (hp ? d.HpPot : d.MpPot) <= 0; }
            if (dim) DrawRect(r, new Color(0, 0, 0, .4f));
            if (cd > 0) { float h = ss * Mathf.Clamp(cd / cdMax, 0, 1); DrawRect(new Rect2(r.Position.X, r.End.Y - h, ss, h), new Color(0, 0, 0, .65f)); }
            SlotName(r, name, fs(.8f), i == 4 ? new Color("ff8f86") : i == 5 ? new Color("8fb8ff") : Colors.White);
            Txt(r.Position + new Vector2(3, fs(.95f)), key, fs(.95f), Gold); Txt(r.Position + new Vector2(ss - 3, ss - 3), cnt, fs(.9f), Colors.White, HorizontalAlignment.Right, 0);
        }
        // minimap bottom-right
        if (!Dungeon.Active) DrawMini(p, new Vector2(sz.X - pad - u * 7, sz.Y - pad - u * 7), u * 7);
        if (p.Dead) Txt(new Vector2(sz.X / 2, sz.Y / 2), "YOU DIED", fs(5f), new Color("ff7b6f"), HorizontalAlignment.Center, sz.X);
    }

    /// <summary>Slot label that always fits its box: one line if it fits, else two lines split at a space, shrinking the font as a last resort.</summary>
    void SlotName(Rect2 r, string name, int size, Color col)
    {
        if (name == "") return;
        float w = r.Size.X - 6; float W(string s, int fsz) => F.GetStringSize(s, HorizontalAlignment.Left, -1, fsz).X;
        string[] lines = { name };
        if (W(name, size) > w && name.Contains(' ')) { int sp = name.IndexOf(' '); lines = new[] { name[..sp], name[(sp + 1)..] }; }
        int fsz = size; while (fsz > 8 && System.Array.Exists(lines, l => W(l, fsz) > w)) fsz--;
        float y0 = r.Position.Y + r.Size.Y / 2 + fsz * 0.35f - (lines.Length - 1) * fsz * 0.5f;
        for (int j = 0; j < lines.Length; j++) Txt(new Vector2(r.Position.X + 3, y0 + j * fsz), lines[j], fsz, col, HorizontalAlignment.Center, w);
    }

    void DrawMini(Player p, Vector2 c, float rad)
    {
        DrawCircle(c, rad + 3, new Color("e3b04b") with { A = .75f }); DrawCircle(c, rad, new Color("07060b"));
        const float span = 150f; int n = 28; float step = rad * 2 / n;
        if (World.MapTex != null)
        {
            // sample the map image in a rotated frame (camera-up)
            float yaw = p.Rig?.Yaw ?? 0f; var f = new Vector2(-Mathf.Sin(yaw), -Mathf.Cos(yaw)); var r = new Vector2(-f.Y, f.X);
            _img ??= World.MapTex.GetImage(); var img = _img;
            for (int iy = 0; iy < n; iy++) for (int ix = 0; ix < n; ix++)
            {
                var o = new Vector2((ix + .5f) * step - rad, (iy + .5f) * step - rad); if (o.Length() > rad - 1) continue;
                var w = o / rad * span / 2; var wp = new Vector2(p.GlobalPosition.X, p.GlobalPosition.Z) + r * w.X - f * w.Y;
                int px = Mathf.Clamp((int)((wp.X + 256f) / 512f * 256f), 0, 255), pz = Mathf.Clamp((int)((wp.Y + 256f) / 512f * 256f), 0, 255);
                DrawRect(new Rect2(c + o - new Vector2(step, step) / 2, new Vector2(step + .5f, step + .5f)), img.GetPixel(px, pz));
            }
            foreach (var poi in World.Pois)
            {
                var dv = new Vector2(poi.X - p.GlobalPosition.X, poi.Z - p.GlobalPosition.Z); var o = new Vector2(dv.Dot(r), -dv.Dot(f)) / (span / 2) * rad;
                if (o.Length() < rad - 4) DrawCircle(c + o, 3.5f, Gold);
            }
        }
        DrawColoredPolygon(new[] { c + new Vector2(0, -8), c + new Vector2(-5, 5), c + new Vector2(5, 5) }, new Color("ff4a3a"));
        DrawArc(c, rad, 0, Mathf.Tau, 48, new Color("e3b04b"), 2f);
    }

    static string PlaceName(Vector3 p) { Poi? best = null; float bd = 1e9f; foreach (var poi in World.Pois) { float dd = new Vector2(poi.X - p.X, poi.Z - p.Z).Length(); if (dd < poi.R && dd < bd) { bd = dd; best = poi; } } return best?.Name ?? "The Wilds"; }
}
