using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>In-game HUD matching the prototype layout: top-left info, compass, quest tracker, target bar, bottom bars, hotbar, minimap.</summary>
public partial class Hud : CanvasLayer
{
    HudCanvas _c = null!;
    string? _region, _zone; float _regT;
    static Hud? _inst;
    /// <summary>Dev capture: zeroes the hurt flash, low-HP vignette and damage ghosts.</summary>
    public static void ResetFx() { _inst?._c.ResetFx(); }
    public override void _Ready() { _inst = this; Layer = 5; _c = new HudCanvas { MouseFilter = Control.MouseFilterEnum.Ignore }; _c.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); AddChild(_c); }
    public override void _Process(double delta)
    {
        var p = Player.Instance; if (p == null) return;
        if (Dungeon.Active) Dungeon.Reveal(p.GlobalPosition);   // the dungeon map fills in as you explore
        if (!Dungeon.Active) foreach (var poi in World.Pois)
            if (!Game.D.Disc.Contains(poi.Id) && new Vector2(poi.X - p.GlobalPosition.X, poi.Z - p.GlobalPosition.Z).Length() < poi.R && (!poi.Sky || p.GlobalPosition.Y > poi.Y - 25f)) { Game.D.Disc.Add(poi.Id); Wayshrine.Activate(poi.Id); Ui.Banner(poi.Name, "discovered"); Sfx.Play("level"); }
        // region banner when crossing into another land, the sky islands or the open sea
        if (!Dungeon.Active && !Arenas.Active)
        {
            _regT -= (float)delta;
            if (_regT <= 0)
            {
                _regT = .5f; var reg = Lands.RegionAt(p.GlobalPosition);
                // entering a new level zone (data/zones.json) names it with its level range; that banner wins over the land's
                var zn = Zones.At(p.GlobalPosition.X, p.GlobalPosition.Z); string zid = zn?.Id ?? "";
                if (_zone != null && zid != _zone && zn != null) Ui.Banner(zn.Name, Zones.RangeText(zn, World.Realm));
                else if (_region != null && reg != _region) Ui.Banner(reg, "entering");
                _region = reg; _zone = zid;
            }
        }
    }
}

public partial class HudCanvas : Control
{
    static readonly Color Gold = new Color("e3b04b"), Mute = new Color("a095bd");
    float _hurt, _lastHp = -1, _ptsShowT; int _lastPts;
    float _gHp = 1, _gHpH, _gMp = 1, _gMpH, _gSt = 1, _gStH, _lowV;   // damage ghosts (value, hold) and smoothed low-HP vignette
    // Elden Ring style boss bar state (cached strings so nothing allocates per frame)
    Enemy? _bossRef; float _gBoss = 1, _gBossH, _bossLastHp, _bossDmg, _bossDmgT, _bossFade; int _bossDmgShown = -1;
    string _bossLv = "", _bossDmgS = ""; string[] _bossCh = System.Array.Empty<string>(); float[] _bossAdv = System.Array.Empty<float>();
    public void ResetFx() { _bossRef = null; _hurt = 0; _lowV = 0; _lastHp = -1; _gHp = _gMp = _gSt = 1; _gHpH = _gMpH = _gStH = 0; }
    static GradientTexture2D? _vig;
    static GradientTexture2D Vignette
    {
        get
        {
            if (_vig != null) return _vig;
            var g = new Gradient(); g.SetColor(0, new Color(1, 1, 1, 0)); g.SetOffset(0, .45f); g.SetColor(1, new Color(1, 1, 1, 1)); g.SetOffset(1, 1.05f); g.AddPoint(.78f, new Color(1, 1, 1, .45f));
            return _vig = new GradientTexture2D { Gradient = g, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(.5f, .5f), FillTo = new Vector2(1f, .5f), Width = 256, Height = 256 };
        }
    }
    Image? _img;
    Font F => ThemeDB.FallbackFont;
    float _deathFade;
    public override void _Process(double delta) { QueueRedraw(); float tgt = Player.Instance is { Dead: true, Downed: false } ? 1f : 0f; _deathFade = Mathf.MoveToward(_deathFade, tgt, (float)delta / (tgt > 0 ? 2.2f : .8f)); }

    // ---- compass (Skyrim style): reused buffers, nothing allocated per frame
    static readonly Color Parch = new Color("e8dcc0"), Blood = new Color(.62f, .07f, .07f);
    static readonly string[] CardN = { "N", "E", "S", "W" }, InterN = { "NE", "SE", "SW", "NW" };
    readonly Vector2[] _dia = new Vector2[4]; readonly List<Enemy> _hunt = new(); float _huntT;
    string _tStr = "", _pStr = "", _gStr = ""; float _infoT; int _lastGold = -1;
    void Dia(float x, float y, float r, Color c) { _dia[0] = new Vector2(x, y - r); _dia[1] = new Vector2(x + r * .7f, y); _dia[2] = new Vector2(x, y + r); _dia[3] = new Vector2(x - r * .7f, y); DrawColoredPolygon(_dia, c); }
    void Rune(float x, float y, float r, Color c) { DrawLine(new Vector2(x, y - r), new Vector2(x, y + r), c, 1.6f); DrawLine(new Vector2(x, y), new Vector2(x - r * .7f, y - r * .7f), c, 1.4f); DrawLine(new Vector2(x, y), new Vector2(x + r * .7f, y - r * .7f), c, 1.4f); DrawLine(new Vector2(x - r * .5f, y + r * .5f), new Vector2(x + r * .5f, y + r * .5f), c, 1.2f); }

    void DrawCompass(Player p, float yaw, float cx, float top, float w, float h, int bigF, int smallF, float dt)
    {
        float half = w / 2, scale = half / (Mathf.Pi / 2), left = cx - half, mid = top + h / 2, fadeW = 60f;
        // dark translucent fill and thin gold rules, both fading out toward the ends (strips, no allocation)
        const int N = 52; float sw = w / N;
        for (int i = 0; i < N; i++)
        {
            float x0 = left + i * sw, e = Mathf.Clamp((half - Mathf.Abs(x0 + sw / 2 - cx)) / fadeW, 0, 1);
            DrawRect(new Rect2(x0, top, sw + .5f, h), new Color(.04f, .03f, .06f, .62f * e));
            var rc = new Color(.89f, .69f, .29f, .85f * e);
            DrawRect(new Rect2(x0, top, sw + .5f, 1.5f), rc); DrawRect(new Rect2(x0, top + h - 1.5f, sw + .5f, 1.5f), rc);
        }
        var pp = p.GlobalPosition; var tickC = new Color(.89f, .69f, .29f, .5f);
        // markers: heading = clockwise from north (-Z); camera faces -yaw, so +rel is to the right
        if (!Dungeon.Active)
        {
            foreach (var poi in World.Pois)
            {
                float dx = poi.X - pp.X, dz = poi.Z - pp.Z, dist = Mathf.Sqrt(dx * dx + dz * dz); bool disc = Game.D.Disc.Contains(poi.Id);
                if (!disc && dist > 150f) continue;
                float xx = Mark(Mathf.Atan2(dx, -dz), yaw, scale, half, cx, out float edge);
                float al = disc ? Mathf.Clamp(1.1f - dist / 700f, .4f, 1f) : Mathf.Clamp(1f - dist / 170f, .15f, .6f);
                if (edge > 0) al *= .6f;
                if (disc) Rune(xx, mid + h * .2f, Mathf.Max(4f, h * .17f), new Color(.95f, .76f, .3f, al)); else DrawCircle(new Vector2(xx, mid + h * .2f), 2.2f, new Color(.6f, .6f, .62f, al));
            }
        }
        _huntT -= dt;
        if (_huntT <= 0) { _huntT = .3f; _hunt.Clear(); foreach (var n in GetTree().GetNodesInGroup("enemies")) if (n is Enemy en && !en.Dead && en.Active && en.Hunting && en.GlobalPosition.DistanceTo(pp) < 40f) _hunt.Add(en); }
        for (int i = 0; i < _hunt.Count; i++)
        {
            var en = _hunt[i]; if (!IsInstanceValid(en) || en.Dead) continue;
            var ep = en.GlobalPosition; float dx = ep.X - pp.X, dz = ep.Z - pp.Z; float al = Mathf.Clamp(1.1f - Mathf.Sqrt(dx * dx + dz * dz) / 45f, .35f, 1f);
            Dia(Mark(Mathf.Atan2(dx, -dz), yaw, scale, half, cx, out _), mid + h * .2f, 3.6f, Blood with { A = al });
        }
        if (Targeting.Aim != null && IsInstanceValid(Targeting.Aim) && !Targeting.Aim.Dead)
        {
            var ep = Targeting.Aim.GlobalPosition; float tx = Mark(Mathf.Atan2(ep.X - pp.X, -(ep.Z - pp.Z)), yaw, scale, half, cx, out _);
            Dia(tx, mid + h * .2f, 5.6f, new Color(.2f, .01f, .01f, .95f)); Dia(tx, mid + h * .2f, 4.2f, new Color(.85f, .1f, .1f));
        }
        // ticks every 15 degrees, letters at 45-degree steps
        for (int k = 0; k < 24; k++)
        {
            float rel = Mathf.Wrap(k * Mathf.Tau / 24f + yaw, -Mathf.Pi, Mathf.Pi), xx = cx + rel * scale; float e = Mathf.Clamp((half - Mathf.Abs(xx - cx)) / fadeW, 0, 1); if (e <= 0) continue;
            if (k % 3 != 0) { DrawLine(new Vector2(xx, top + h - 3), new Vector2(xx, top + h - 3 - h * .16f), tickC with { A = tickC.A * e }, 1f); continue; }
            bool card = k % 6 == 0; string s = card ? CardN[k / 6] : InterN[k / 6]; int fz = card ? bigF : smallF;
            var col = card ? (k == 0 ? UiTheme.Highlight : Parch) : UiTheme.Dim; col.A *= e;
            var ts = F.GetStringSize(s, HorizontalAlignment.Left, -1, fz); var tp = new Vector2(xx - ts.X / 2, top + h * .5f + ts.Y * .22f - (card ? h * .06f : h * .1f));
            DrawString(F, tp + new Vector2(1, 1), s, HorizontalAlignment.Left, -1, fz, new Color(0, 0, 0, .8f * e)); DrawString(F, tp, s, HorizontalAlignment.Left, -1, fz, col);
        }
        DrawColoredPolygon(Notch(cx, top), UiTheme.Accent);
    }
    Vector2[] Notch(float cx, float top) { _dia[0] = new Vector2(cx - 5, top); _dia[1] = new Vector2(cx + 5, top); _dia[2] = new Vector2(cx, top + 7); _dia[3] = new Vector2(cx, top + 7); return _dia; }
    /// <summary>Screen x of a bearing; clamped to the bar edges (edge&gt;0 when clamped).</summary>
    static float Mark(float bearing, float yaw, float scale, float half, float cx, out float edge)
    {
        float xx = Mathf.Wrap(bearing + yaw, -Mathf.Pi, Mathf.Pi) * scale; edge = Mathf.Abs(xx) > half - 8 ? 1f : 0f;
        return cx + Mathf.Clamp(xx, -half + 8, half - 8);
    }

    void DrawInfoLine(Player p, float cx, float y, int fz, float hrs)
    {
        _infoT -= (float)GetProcessDeltaTime(); int gold = Game.D.Gold;
        if (_infoT <= 0 || gold != _lastGold) { _infoT = .4f; _lastGold = gold; _tStr = $"{(int)hrs:00}:{(int)((hrs - (int)hrs) * 60):00}"; _pStr = Dungeon.Active ? $"Depth {Dungeon.Depth}" : PlaceName(p.GlobalPosition) + (Zones.At(p.GlobalPosition.X, p.GlobalPosition.Z) is { } zn ? "  Lv " + Zones.RangeText(zn, World.Realm).Substring(6) : ""); _gStr = gold.ToString(); }
        bool shadow = World.Realm == 1; var dim = new Color(UiTheme.Dim.Lightened(.15f), .85f);
        float iw = fz, gap = fz * .7f, tw = F.GetStringSize(_tStr, HorizontalAlignment.Left, -1, fz).X, pw = F.GetStringSize(_pStr, HorizontalAlignment.Left, -1, fz).X, gw = F.GetStringSize(_gStr, HorizontalAlignment.Left, -1, fz).X;
        float total = iw + gap + tw + gap + pw + gap + iw + 4 + gw, x = cx - total / 2, r = fz * .36f; var ic = new Vector2(x + iw / 2, y - fz * .32f);
        if (shadow) { var vc = new Color(.78f, .62f, 1f, .9f); DrawCircle(ic, r, new Color(.05f, .02f, .09f)); DrawArc(ic, r + 1f, 0, Mathf.Tau, 16, vc, 1.4f); }
        else { DrawCircle(ic, r * .7f, new Color(.95f, .78f, .35f, .9f)); for (int i = 0; i < 8; i++) { var dv = Vector2.Right.Rotated(i * Mathf.Tau / 8f); DrawLine(ic + dv * r, ic + dv * (r + 2.5f), new Color(.95f, .78f, .35f, .8f), 1f); } }
        x += iw + gap; Txt(new Vector2(x, y), _tStr, fz, dim); x += tw + gap;
        Txt(new Vector2(x, y), _pStr, fz, UiTheme.Accent); x += pw + gap;
        var cc = new Vector2(x + iw / 2, y - fz * .32f); DrawCircle(cc, r, new Color(.9f, .7f, .25f)); DrawArc(cc, r * .6f, 0, Mathf.Tau, 12, new Color(.5f, .35f, .08f), 1f);
        Txt(new Vector2(x + iw + 4, y), _gStr, fz, dim);
    }

    void Txt(Vector2 pos, string s, int size, Color c, HorizontalAlignment al = HorizontalAlignment.Left, float width = -1)
    {
        DrawStringOutline(F, pos, s, al, width, size, 5, new Color(0, 0, 0, .9f)); DrawString(F, pos, s, al, width, size, c);
    }
    void Bar(Rect2 r, float frac, Color a, Color b, bool rtl = false)
    {
        DrawRect(r, new Color(0, 0, 0, .6f)); frac = Mathf.Clamp(frac, 0, 1);
        var f = rtl ? new Rect2(r.Position.X + r.Size.X * (1 - frac), r.Position.Y, r.Size.X * frac, r.Size.Y) : new Rect2(r.Position, new Vector2(r.Size.X * frac, r.Size.Y));
        DrawRect(f, a); DrawRect(new Rect2(f.Position, new Vector2(f.Size.X, f.Size.Y * .45f)), new Color(1, 1, 1, .05f));
        DrawRect(r, new Color(.72f, .58f, .3f, .55f), false, 1f);   // tarnished-gold frame
    }

    public override void _Draw()
    {
        var p = Player.Instance; if (p == null || LairIntro.Active) return;   // the HUD steps aside during the boss intro
        var d = Game.D; var sz = Size; float u = sz.X / 100f; int fs(float k) => Mathf.Max(11, (int)(u * k));
        var pad = u * 1.4f;
        // hurt vignette: red edges flash when hit and pulse slowly below 35% health
        float dtv = (float)GetProcessDeltaTime(); if (p.Hp < _lastHp - .5f) _hurt = Mathf.Min(1f, _hurt + .35f + (_lastHp - p.Hp) / d.MaxHp() * 3f); _lastHp = p.Hp; _hurt = Mathf.Max(0, _hurt - dtv * 1.6f);
        float hpF0 = p.Hp / d.MaxHp();
        float lowT = p.Dead || hpF0 >= .3f ? 0 : Mathf.Clamp((.3f - hpF0) / .3f, 0, 1) * .6f + .4f; _lowV = Mathf.MoveToward(_lowV, lowT, dtv * 1.5f);   // fades in below 30%, out above
        float low = _lowV * (.6f + .4f * Mathf.Sin((float)Time.GetTicksMsec() / 380f));
        if (_hurt > .01f) DrawTextureRect(Vignette, new Rect2(Vector2.Zero, sz), false, new Color(.85f, .05f, .05f, _hurt * .85f));
        if (low > .01f) DrawTextureRect(Vignette, new Rect2(Vector2.Zero, sz), false, new Color(.35f, .01f, .02f, low * .8f));   // dark-red low-HP pulse
        GhostStep(ref _gHp, ref _gHpH, hpF0, dtv); GhostStep(ref _gMp, ref _gMpH, p.Mp / d.MaxMp(), dtv); GhostStep(ref _gSt, ref _gStH, p.St / d.MaxSt(), dtv);
        // ---- status window, top left: name and level, slanted segmented HP bar (green > yellow > red), MP and stamina, XP line, place
        string realm = World.Realm == 1 ? "SHADOW" : "LIGHT"; float hrs = World.TimeOfDay * 24f;
        float pw = Mathf.Max(300, u * 28), ph = fs(1.2f) + 76, px = pad, py = pad;
        Glass(new Rect2(px, py, pw, ph));
        Txt(new Vector2(px + 12, py + fs(1.2f) + 6), d.Name.ToUpper(), fs(1.2f), UiTheme.Title);
        if (Titles.Get(d.Title) is { } title)   // the worn title, in pale gold after the name
            Txt(new Vector2(px + 20 + F.GetStringSize(d.Name.ToUpper(), HorizontalAlignment.Left, -1, fs(1.2f)).X, py + fs(1.2f) + 6), title.Name, fs(.95f), new Color(1f, .93f, .72f));
        Txt(new Vector2(px + 12, py + fs(1.2f) + 6), $"Lv {d.Level}", fs(1.1f), UiTheme.Accent, HorizontalAlignment.Right, pw - 24);
        float hpF = Mathf.Clamp(p.Hp / d.MaxHp(), 0, 1); var hpCol = hpF > .25f ? new Color("8e1c1a") : new Color("c0281f");   // gothic: crimson health, brighter when low
        float bx0 = px + 12, bw0 = pw - 24, by0 = py + fs(1.2f) + 14;
        SlantBar(new Rect2(bx0, by0, bw0, 14), hpF, hpCol, 10, 10, _gHp);
        Txt(new Vector2(bx0, by0 + 30), "HP", fs(.85f), UiTheme.Dim);
        Txt(new Vector2(bx0, by0 + 30), $"{Mathf.CeilToInt(p.Hp)} / {d.MaxHp()}", fs(.95f), Colors.White, HorizontalAlignment.Right, bw0 - 8);
        SlantBar(new Rect2(bx0 + 28, by0 + 36, bw0 * .62f, 7), p.Mp / d.MaxMp(), new Color("3c5c98"), 5, 0, _gMp);
        SlantBar(new Rect2(bx0 + 28, by0 + 47, bw0 * .62f, 5), p.St / d.MaxSt(), new Color("5c7a3c"), 4, 0, _gSt);
        Txt(new Vector2(bx0, by0 + 44), "MP", fs(.75f), UiTheme.Dim);
        DrawRect(new Rect2(px + 1, py + ph - 4, pw - 2, 2), new Color(0, 0, 0, .4f)); DrawRect(new Rect2(px + 1, py + ph - 4, (pw - 2) * Mathf.Clamp((float)d.Xp / d.XpNeed(), 0, 1), 2), UiTheme.Highlight);
        // realm, clock, place and gold sit under the compass (the notes column starts just below the status window)
        float cBarH = fs(1.4f) + 16, infoY = pad + cBarH + fs(.85f) + 8;
        DrawInfoLine(p, sz.X / 2, infoY, fs(.85f), hrs);
        if (Game.Set.ShowFps) Txt(new Vector2(sz.X - pad - 200, sz.Y - 4), $"{Engine.GetFramesPerSecond():0} FPS", 11, UiTheme.Dim, HorizontalAlignment.Right, 200);
        if (Arenas.Active) Txt(new Vector2(sz.X / 2 - u * 20, infoY + fs(1.2f) + 4), Arenas.Status, fs(1.15f), UiTheme.Highlight, HorizontalAlignment.Center, u * 40);
        // quest tracker, top right
        var aq = Quests.Active();
        if (aq.Count > 0)
        {
            // right-aligned text needs a box: draw from (right edge - width) with that width
            DrawTracker(aq, sz, u, pad, fs(.95f), fs(1f), dtv);
        }
        // compass
        DrawCompass(p, p.Rig?.Yaw ?? 0f, sz.X / 2, pad, Mathf.Min(520f, sz.X * .4f), cBarH, fs(1.4f), fs(.8f), dtv);
        // target bar
        Enemy? tgt = Targeting.Aim != null && IsInstanceValid(Targeting.Aim) && !Targeting.Aim.Dead ? Targeting.Aim : Enemy.CurrentBoss != null && GodotObject.IsInstanceValid(Enemy.CurrentBoss) && !Enemy.CurrentBoss.Dead && Enemy.CurrentBoss.Active && Enemy.CurrentBoss.GlobalPosition.DistanceTo(p.GlobalPosition) < (Enemy.CurrentBoss.HpBars > 1 ? 120f : 35f) ? Enemy.CurrentBoss : p.Dead ? null : p.AimPoint(40f).enemy;
        bool eldenBoss = tgt != null && !tgt.Dead && tgt.Def.Boss && tgt.HpBars <= 1 && !tgt.Duelist;
        if (!eldenBoss) _bossRef = null;
        if (eldenBoss) DrawBossBar(tgt!, sz, u, pad, dtv, Mathf.Max(18, fs(1.2f)));
        else if (tgt != null && !tgt.Dead)
        {
            float tw = u * 24, ty = u * 7.6f + (Arenas.Active ? fs(1.4f) : 0); Txt(new Vector2(sz.X / 2 - tw / 2, ty), $"{tgt.Def.N}  Lv {tgt.Level}", fs(1.2f), tgt.Def.Boss ? UiTheme.Highlight : Colors.White, HorizontalAlignment.Center, tw);
            float extra = 0;
            if (tgt.HpBars > 1)
            {
                // a raid boss: her health split into bars; the current one drains over the colour of the next, "x N" bars left
                Color[] bc = { new("ff5a4f"), new("ff9a3a"), new("f2d14b"), new("8fe04f"), new("4fd0c0"), new("5b86ff"), new("b866f5"), new("ff6ad0"), new("ffffff") };
                float seg = tgt.MaxHp / tgt.HpBars; int left = Mathf.Clamp(Mathf.CeilToInt(tgt.Hp / seg - .0001f), 0, tgt.HpBars); float fr = left <= 0 ? 0 : (tgt.Hp - (left - 1) * seg) / seg;
                var br = new Rect2(sz.X / 2 - tw / 2, ty + 6, tw, Mathf.Max(9, u * .95f));
                if (left > 1) SlantBar(br, 1f, bc[(left - 2) % bc.Length].Darkened(.25f), 6, 0);
                SlantBar(br, fr, bc[(left - 1 + bc.Length) % bc.Length], 6, left > 1 ? 0 : 10);
                Txt(new Vector2(br.End.X + 8, br.Position.Y + br.Size.Y), "x" + left, fs(1.2f), UiTheme.Highlight);
                extra += br.Size.Y - Mathf.Max(7, u * .75f);
                if (tgt.BloomFrac >= 0)
                {
                    float by = br.End.Y + 6, bh = Mathf.Max(6, u * .6f);
                    SlantBar(new Rect2(sz.X / 2 - tw / 2, by, tw, bh), tgt.BloomFrac, new Color("8fe04f"), 4, 6);
                    Txt(new Vector2(sz.X / 2 - tw / 2, by + bh + fs(1f)), $"THE BLOOM: break it in {Mathf.CeilToInt(tgt.BloomLeft)} s or the rot spreads!", fs(1f), new Color("b8ff8a"), HorizontalAlignment.Center, tw);
                    extra += bh + fs(1.2f) + 8;
                }
                if (tgt.ScarBroken) Txt(new Vector2(sz.X / 2 + tw / 2 - 120, ty), "FALTERING", fs(1f), UiTheme.Highlight, HorizontalAlignment.Right, 120);
            }
            else SlantBar(new Rect2(sz.X / 2 - tw / 2, ty + 6, tw, Mathf.Max(7, u * .75f)), tgt.Hp / tgt.MaxHp, tgt.Def.Flee ? new Color("9ad06a") : new Color("ff5a4f"), 6, 10);
            if (tgt.Duelist)
            {
                // the Shadow Duel boss: stance (breaks when full), the Eclipse barrier while it lasts, and the phase
                float by = ty + 10 + Mathf.Max(7, u * .75f), bh = Mathf.Max(4, u * .4f);
                SlantBar(new Rect2(sz.X / 2 - tw / 2, by, tw, bh), tgt.DuelStance, tgt.DuelBroken ? new Color("ffd75a") : new Color("e8b04a"), 4, 6);
                extra = bh + 6;
                if (tgt.DuelBarrier >= 0) { SlantBar(new Rect2(sz.X / 2 - tw / 2, by + extra, tw, bh * 1.6f), tgt.DuelBarrier, new Color("a46bff"), 4, 6); Txt(new Vector2(sz.X / 2 - tw / 2, by + extra + bh * 1.6f + fs(1f)), "ECLIPSE: break the barrier!", fs(1f), new Color("cfa8ff"), HorizontalAlignment.Center, tw); extra += bh * 1.6f + fs(1.2f) + 6; }
                Txt(new Vector2(sz.X / 2 + tw / 2 - 120, ty), $"Phase {tgt.DuelPhase}" + (tgt.DuelBroken ? "  BROKEN" : ""), fs(1f), tgt.DuelBroken ? UiTheme.Highlight : UiTheme.Dim, HorizontalAlignment.Right, 120);
            }
            float tx = sz.X / 2 - tw / 2, ic = Mathf.Max(18, u * 1.6f);
            foreach (var s in tgt.Status.List) { StatusChip(new Vector2(tx, ty + 14 + Mathf.Max(7, u * .75f) + extra), ic, s.Def.Col, s.Def.Name[..1], s.T, s.Stacks); tx += ic + 3; }
        }
        if (Net.Active) DrawLobby(sz, u, pad, fs(1f));
        // crosshair
        DrawCircle(sz / 2, 3f, new Color(1, 1, 1, .75f));
        // charging a spell: a bar under the crosshair, golden when full
        if (p.ChargeFrac >= 0)
        {
            float bw = u * 12, bh = Mathf.Max(6, u * .6f); var br = new Rect2(sz.X / 2 - bw / 2, sz.Y / 2 - u * 4.5f, bw, bh);   // above the crosshair, clear of the hero
            bool full = p.ChargeFrac >= .999f;
            SlantBar(br, p.ChargeFrac, full ? UiTheme.Highlight : UiTheme.Accent, 4, 6);
            Txt(new Vector2(br.Position.X, br.Position.Y - 4), full ? $"{p.ChargeName}: empowered" : $"{p.ChargeName}: charging", fs(.95f), full ? UiTheme.Highlight : Colors.White, HorizontalAlignment.Center, bw);
        }
        // prompt
        if (p.Prompt != "" && !Ui.AnyOpen)
        {
            // interaction prompt in a glass pill: [E] key chip + action
            string ek = Inputs.KeyOf(Inputs.Use), pre = "[" + ek + "] ";
            string act = p.Prompt.StartsWith(pre) ? p.Prompt[pre.Length..] : p.Prompt; int pf = fs(1.25f);
            bool glyph = Inputs.UsingPad && Inputs.PadCode(Inputs.Use) != ""; float gh = pf * 1.2f;
            float aw = F.GetStringSize(act, HorizontalAlignment.Left, -1, pf).X, kw = glyph ? PadGlyphs.Width(ek, gh) + 6 : Mathf.Max(pf * 1.6f, F.GetStringSize(ek, HorizontalAlignment.Left, -1, pf).X + pf * .6f), tot = kw + 10 + aw + 24, x0 = sz.X / 2 - tot / 2, y0 = sz.Y * .62f - pf;
            Glass(new Rect2(x0, y0, tot, pf * 1.7f));
            if (glyph) PadGlyphs.Draw(this, new Rect2(x0 + 9, y0 + (pf * 1.7f - gh) / 2, kw, gh), ek, Inputs.PadStyle);
            else
            {
                DrawRect(new Rect2(x0 + 6, y0 + 5, kw, pf * 1.7f - 10), UiTheme.Accent with { A = .85f });
                Txt(new Vector2(x0 + 6, y0 + pf * 1.2f), ek, pf, new Color("04111f"), HorizontalAlignment.Center, kw);
            }
            Txt(new Vector2(x0 + kw + 16, y0 + pf * 1.2f), act, pf, Colors.White);
        }
        // hotbar bottom-left: 6 skills (keys 1-6, set in the Skills menu, L) + 2 potions
        float ss = Mathf.Max(36, u * 4.4f), hx = pad, hy = sz.Y - pad - ss;
        // unspent points: shown for a few seconds when new points arrive (level up), not all game; the pause menu always lists them
        int unspent = d.AttrPts + d.SkillPts + d.RankPts;
        if (unspent > _lastPts) _ptsShowT = 10f; _lastPts = unspent; _ptsShowT -= (float)GetProcessDeltaTime();
        if (unspent > 0 && _ptsShowT > 0)
        {
            float ux = hx + 8 * (ss + u * .5f) + u;
            Txt(new Vector2(ux, hy + ss * .62f),$"Unspent: {d.AttrPts} attribute ({(Inputs.UsingPad ? Inputs.KeyOf(Inputs.Pause) : "Esc")} > Character), {d.RankPts} skill ({Inputs.KeyOf(Inputs.SkillsUi)}), {d.SkillPts} skill tree ({Inputs.KeyOf(Inputs.Tree)})", fs(1f), UiTheme.Highlight, HorizontalAlignment.Left, Mathf.Max(100, sz.X - ux - pad));
        }
        for (int i = 0; i < 8; i++)
        {
            var r = new Rect2(hx + i * (ss + u * .5f), hy, ss, ss); Glass(r, false);
            string icon, key; float cd = 0, cdMax = 1; bool dim = false; string cnt = "";
            if (i < 6)
            {
                var id = i < d.Bar.Length ? d.Bar[i] : null; var sk = string.IsNullOrEmpty(id) ? null : Skills.Get(id!); icon = sk != null ? Icons.ForSkill(sk) : ""; key = Inputs.KeyOf("spell" + (i + 1));
                if (sk != null) { var sc = Skills.Compute(id!); cd = p.CdOf(id); cdMax = p.CdMaxOf(id); dim = p.Mp < sc.Mp || p.St < sc.St; cnt = sc.Mp > 0 ? $"{sc.Mp:0}" : $"{sc.St:0}"; }
            }
            else { bool hp = i == 6; icon = hp ? "hp" : "mp"; key = Inputs.KeyOf(hp ? Inputs.PotHp : Inputs.PotMp); cnt = (hp ? d.HpPot : d.MpPot).ToString(); dim = (hp ? d.HpPot : d.MpPot) <= 0; }
            // vector icon (scripts/Icons.cs), dimmed when unaffordable; radial cooldown sweep with seconds left
            if (icon != "") Icons.Draw(this, icon, r.GetCenter() + new Vector2(0, -ss * .02f), ss * .68f, dim ? .35f : 1f);
            if (cd > 0)
            {
                float f = Mathf.Clamp(cd / cdMax, 0, 1); var cc = r.GetCenter(); var pts = new System.Collections.Generic.List<Vector2> { cc }; int segs = 24;
                for (int k = 0; k <= segs; k++) { float a = -Mathf.Pi / 2 + Mathf.Tau * f * k / segs; pts.Add(cc + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * ss * .72f); }
                var clip = pts.ConvertAll(v => new Vector2(Mathf.Clamp(v.X, r.Position.X, r.End.X), Mathf.Clamp(v.Y, r.Position.Y, r.End.Y)));
                for (int k = 1; k + 1 < clip.Count; k++) { var a = clip[k] - cc; var b = clip[k + 1] - cc; if (Mathf.Abs(a.Cross(b)) > .5f) DrawPrimitive(new[] { cc, clip[k], clip[k + 1] }, new[] { new Color(0, 0, 0, .62f), new Color(0, 0, 0, .62f), new Color(0, 0, 0, .62f) }, null); }
                Txt(r.Position + new Vector2(0, ss * .62f), cd >= 1 ? $"{Mathf.CeilToInt(cd)}" : $"{cd:0.0}", fs(1.05f), Colors.White, HorizontalAlignment.Center, ss);
            }
            else if (!dim && i < 6 && icon != "") DrawRect(r, UiTheme.Accent with { A = .35f }, false, 1f);
            string kact = i < 6 ? "spell" + (i + 1) : i == 6 ? Inputs.PotHp : Inputs.PotMp;
            if (Inputs.UsingPad && Inputs.PadCode(kact) != "") PadGlyphs.Draw(this, new Rect2(r.Position + new Vector2(3, 3), new Vector2(ss - 6, Mathf.Max(12, ss * .3f))), key, Inputs.PadStyle);
            else Txt(r.Position + new Vector2(4, fs(.95f) + 1), key, fs(.95f), UiTheme.Accent);
            Txt(r.Position + new Vector2(0, ss - 4), cnt, fs(.85f), i == 6 ? new Color("ff8f86") : i == 7 ? new Color("8fb8ff") : UiTheme.Dim.Lightened(.3f), HorizontalAlignment.Right, ss - 4);
        }
        // hero statuses and skill buffs: icons with seconds left, just above the hotbar
        float isz = Mathf.Max(22, u * 2.1f);
        DrawBuffRow(p, new Vector2(px, py + ph + 6), isz);   // buff/debuff row under the player bars
        // a running macro (PlayerMacros.cs), above the hotbar
        if (p.MacroStatus is { Length: > 0 } ms) Txt(new Vector2(hx, hy - isz - 16), "▶ " + ms, fs(1f), UiTheme.Highlight);
        // minimap bottom-right
        DrawMini(p, new Vector2(sz.X - pad - u * 7, sz.Y - pad - u * 7), u * 7);
        if (p.Downed)
        {
            Txt(new Vector2(0, sz.Y / 2), "DOWNED", fs(5f), new Color("ff9a6f"), HorizontalAlignment.Center, sz.X);
            Txt(new Vector2(0, sz.Y / 2 + fs(2.4f)), $"A party member can revive you.  [{Inputs.KeyOf(Inputs.Use)}] give up and wake outside", fs(1.3f), Colors.White, HorizontalAlignment.Center, sz.X);
        }
        if (_deathFade > .01f)
        {
            float k = Mathf.SmoothStep(0, 1, _deathFade), bh = fs(9f), by = sz.Y / 2 - bh / 2;
            DrawRect(new Rect2(0, 0, sz.X, sz.Y), new Color(.02f, 0, 0, .35f * k));
            DrawRect(new Rect2(0, by, sz.X, bh), new Color(0, 0, 0, .78f * k));
            var gold = new Color(.72f, .58f, .3f, .5f * k);
            DrawRect(new Rect2(sz.X * .2f, by, sz.X * .6f, 1), gold); DrawRect(new Rect2(sz.X * .2f, by + bh, sz.X * .6f, 1), gold);
            Txt(new Vector2(0, sz.Y / 2 - fs(4.2f)), "F  A  L  L  E  N", fs(6f), new Color(.55f, .06f, .07f, k), HorizontalAlignment.Center, sz.X);
        }
        // channelling (Resurrection, reviving a friend): a bar under the crosshair
        if (p.ChannelFrac >= 0)
        {
            float bw = u * 16, bh = Mathf.Max(7, u * .7f); var br = new Rect2(sz.X / 2 - bw / 2, sz.Y / 2 + u * 5f, bw, bh);
            SlantBar(br, p.ChannelFrac, new Color("9fffb0"), 4, 15);
            Txt(new Vector2(br.Position.X, br.Position.Y - 4), $"{p.ChannelName}  {p.ChannelT:0.0} s  (moving breaks it)", fs(1f), Colors.White, HorizontalAlignment.Center, bw);
        }
    }

    /// <summary>Status chip: tinted square, the status initial, stacks in the corner and a draining bar for time left.</summary>
    void StatusChip(Vector2 at, float s, Color c, string letter, float t, int stacks)
    {
        var r = new Rect2(at, new Vector2(s, s)); DrawRect(r, new Color(.02f, .05f, .1f, .8f)); DrawRect(r.Grow(-2), c with { A = .45f }); DrawRect(r, c, false, 1.5f);
        Txt(at + new Vector2(0, s * .72f), letter, (int)(s * .6f), Colors.White, HorizontalAlignment.Center, s);
        if (stacks > 1) Txt(at + new Vector2(0, s - 2), stacks.ToString(), (int)(s * .38f), Colors.White, HorizontalAlignment.Right, s - 2);
        DrawRect(new Rect2(at + new Vector2(0, s + 1), new Vector2(s * Mathf.Clamp(t / 10f, 0, 1), 2)), c);
    }

    static readonly Color BuffFrame = new("c9a24a"), DebuffFrame = new("7a1a1a");   // (Parch is declared above, L48)
    readonly System.Collections.Generic.Dictionary<string, float> _maxT = new();   // longest time seen per buff/status id, for the sweep
    float Span(string id, float t, float def) { if (!_maxT.TryGetValue(id, out var m) || t > m) { m = Mathf.Max(t, def); _maxT[id] = m; } return Mathf.Max(.1f, m); }

    /// <summary>Buff/debuff row: gold-framed buff squares, dark-red debuffs, a draining bottom bar, stack count. Max 8, no per-frame allocation.</summary>
    void DrawBuffRow(Player p, Vector2 at, float s)
    {
        int n = 0; float x = at.X;
        for (int i = 0; i < p.Buffs.Count && n < 8; i++, n++)
        {
            var b = p.Buffs[i]; var nm = Skills.Get(b.id)?.Name;
            BuffIcon(new Vector2(x, at.Y), s, new Color(1f, .82f, .4f), nm is { Length: > 0 } ? nm[0] : '+', b.t / Span(b.id, b.t, 10f), 0, BuffFrame, b.t); x += s + 4;
        }
        for (int i = 0; i < p.Statuses.List.Count && n < 8; i++, n++)
        {
            var st = p.Statuses.List[i]; var nm = st.Def.Name;
            bool good = st.Def.Kind is "buff" or "regen" or "haste" or "shield" or "ward";
            BuffIcon(new Vector2(x, at.Y), s, st.Def.Col, nm.Length > 0 ? nm[0] : '?', st.T / Span(st.Def.Id, st.T, st.Def.Dur), st.Stacks, good ? BuffFrame : DebuffFrame, st.T); x += s + 4;
        }
    }
    void BuffIcon(Vector2 at, float s, Color c, char letter, float frac, int stacks, Color frame, float t)
    {
        var r = new Rect2(at, new Vector2(s, s)); DrawRect(r, new Color(.04f, .03f, .05f, .85f)); DrawRect(r.Grow(-2), c with { A = .3f });
        Txt(at + new Vector2(0, s * .72f), letter.ToString(), (int)(s * .6f), Parch, HorizontalAlignment.Center, s);
        frac = Mathf.Clamp(frac, 0, 1);
        DrawRect(new Rect2(at.X + 1, at.Y + s - 3, s - 2, 2), new Color(0, 0, 0, .6f)); DrawRect(new Rect2(at.X + 1, at.Y + s - 3, (s - 2) * frac, 2), frame.Lightened(.2f));
        DrawRect(r, frame, false, 1f);
        if (stacks > 1) Txt(at + new Vector2(0, s - 5), stacks.ToString(), (int)(s * .4f), Colors.White, HorizontalAlignment.Right, s - 2);
        if (t < 3f && ((int)(Time.GetTicksMsec() / 250) & 1) == 0) DrawRect(r, new Color(1, 1, 1, .12f));   // about to expire
    }

    int _qSig; float _qAge = 99, _qA = 1;
    /// <summary>Quest tracker, top right: gold caps name, parchment objective lines with progress, thin gold rule on the left. Dims after 10 s, pulses on progress.</summary>
    void DrawTracker(System.Collections.Generic.List<QuestDef> aq, Vector2 sz, float u, float pad, int nameSz, int lineSz, float dt)
    {
        int sig = 17; for (int i = 0; i < aq.Count && i < 3; i++) { var qi = aq[i]; var si = Game.Q(qi.Id); sig = sig * 31 + qi.Id.GetHashCode(); sig = sig * 31 + si.N * 7 + si.S; }
        if (sig != _qSig) { _qSig = sig; _qAge = 0; } else _qAge += dt;
        float target = _qAge < 10f ? 1f : .6f; _qA = Mathf.MoveToward(_qA, target, dt * (target > _qA ? 6f : .5f));
        float pulse = _qAge < .7f ? 1f - _qAge / .7f : 0f;
        float qw = u * 24, x = sz.X - pad - qw, y = pad + 2; var q0 = aq[0];
        int rows = Mathf.Min(3, aq.Count); float h = nameSz + 6 + rows * (lineSz + 4);
        if (pulse > 0) DrawRect(new Rect2(x - 8, y - 2, qw + 8, h + 4), new Color(Gold, pulse * .18f));
        var gold = Gold.Lightened(pulse * .4f) with { A = _qA }; var par = Parch with { A = _qA };
        DrawRect(new Rect2(x - 7, y, 2, h), gold);
        Txt(new Vector2(x, y + nameSz), q0.Name.ToUpper(), nameSz, gold);
        for (int i = 0; i < rows; i++)
        {
            var q = aq[i]; var s = Game.Q(q.Id);
            string line = s.S == 2 ? "Return to " + Quests.GiverName(q.Giver) : q.Goal + (q.Need > 1 ? $" {s.N}/{q.Need}" : "");
            if (i > 0) line = q.Name + ": " + line;
            Txt(new Vector2(x, y + nameSz + 6 + (i + 1) * (lineSz + 4) - 4), line, lineSz, par, HorizontalAlignment.Left, qw);
        }
    }

    /// <summary>Glass box like the menus: translucent navy, cyan edge, bright corner ticks.</summary>
    /// <summary>Multiplayer: party member frames (or everyone online when not in a party) on the left, and the duel status.</summary>
    void DrawLobby(Vector2 sz, float u, float pad, int f)
    {
        var net = Net.I!; var party = net.MyParty;
        var ids = new System.Collections.Generic.List<long>(); if (party != null) ids.AddRange(party.Members); else ids.AddRange(net.Players.Keys);
        ids.Remove(Net.MyId);
        float w = Mathf.Max(200, u * 15), x = pad, y = sz.Y * .42f;
        Txt(new Vector2(x, y), party != null ? "PARTY  ·  " + Net.LootModeName(party.Loot).Split(' ')[0].ToUpper() + " LOOT" : $"ONLINE {net.Players.Count}  ·  {(Net.IsHost ? "HOSTING" : Game.Online ? "TWIN REALMS ONLINE" : "IN " + net.NameOf(1).ToUpper() + "'S WORLD")}", Mathf.Max(11, f - 2), UiTheme.Accent);
        y += 8;
        foreach (var id in ids)
        {
            if (!net.Players.TryGetValue(id, out var p)) continue;
            Glass(new Rect2(x, y, w, f * 2.6f), false);
            bool lead = party != null && party.Leader == id;
            Txt(new Vector2(x + 8, y + f + 3), (lead ? "* " : "") + p.Name, f, party != null ? new Color(.6f, 1f, .65f) : Colors.White);
            Txt(new Vector2(x + 8, y + f + 3), Net.InDungeonZone(p.Zone) ? "dungeon" : p.Zone == 2 ? "arena" : $"Lv {p.Level}", Mathf.Max(10, f - 3), UiTheme.Dim, HorizontalAlignment.Right, w - 16);
            float hf = Mathf.Clamp(p.Hp / Mathf.Max(1, p.MaxHp), 0, 1);
            SlantBar(new Rect2(x + 8, y + f + 9, w - 16, 6), hf, hf > .5f ? new Color("5be37d") : hf > .25f ? new Color("f2d14b") : new Color("ff5a4f"), 4, 0);
            y += f * 2.6f + 5;
        }
        if (net.Duel is { } d && Net.InDuel)
        {
            string s = d.Live ? $"DUEL  vs  {net.NameOf(net.Opponent).ToUpper()}" : $"DUEL STARTS IN {Mathf.CeilToInt(d.Countdown)}";
            Txt(new Vector2(sz.X / 2 - u * 15, sz.Y * .2f), s, Mathf.Max(14, (int)(f * 1.3f)), new Color(1f, .6f, .4f), HorizontalAlignment.Center, u * 30);
        }
    }

    void Glass(Rect2 r, bool corners = true)
    {
        DrawRect(r, UiTheme.Panel with { A = .62f }); DrawRect(r, UiTheme.AccentSoft, false, 1f);
        if (!corners) return; float L = Mathf.Min(12, r.Size.X * .2f); var a = UiTheme.Accent;
        foreach (var (c, dx, dy) in new[] { (r.Position, 1f, 1f), (new Vector2(r.End.X, r.Position.Y), -1f, 1f), (new Vector2(r.Position.X, r.End.Y), 1f, -1f), (r.End, -1f, -1f) })
        { DrawLine(c, c + new Vector2(dx * L, 0), a, 2f); DrawLine(c, c + new Vector2(0, dy * L), a, 2f); }
    }

    /// <summary>Slanted bar with segment ticks: track, fill and outline are parallelograms leaning right.</summary>
    void SlantBar(Rect2 r, float frac, Color col, float skew, int segs, float ghost = -1f)
    {
        frac = Mathf.Clamp(frac, 0, 1); float x = r.Position.X, y = r.Position.Y, w = r.Size.X, h = r.Size.Y;
        Vector2[] Para(float x0, float x1) => new[] { new Vector2(x0 + skew, y), new Vector2(x1 + skew, y), new Vector2(x1, y + h), new Vector2(x0, y + h) };
        DrawColoredPolygon(Para(x, x + w - skew), new Color(.02f, .015f, .02f, .8f));   // dark inset background
        if (ghost > frac + .002f) DrawColoredPolygon(Para(x + (w - skew) * frac, x + (w - skew) * Mathf.Min(ghost, 1f)), new Color(.9f, .72f, .4f, .75f));   // pale amber chunk of lost value
        if (frac > 0.001f)
        {
            float fw = (w - skew) * frac;
            DrawColoredPolygon(Para(x, x + fw), col);
            DrawColoredPolygon(new[] { new Vector2(x + skew, y), new Vector2(x + skew + fw, y), new Vector2(x + skew * .55f + fw, y + h * .45f), new Vector2(x + skew * .55f, y + h * .45f) }, new Color(1, 1, 1, .18f));   // lighter top
            DrawColoredPolygon(new[] { new Vector2(x + skew * .3f, y + h * .7f), new Vector2(x + skew * .3f + fw, y + h * .7f), new Vector2(x + fw, y + h), new Vector2(x, y + h) }, new Color(0, 0, 0, .28f));   // darker bottom
            DrawLine(new Vector2(x + skew, y + .5f), new Vector2(x + skew + fw, y + .5f), new Color(1, 1, 1, .22f), 1f);   // bright top edge
        }
        for (int i = 1; i < segs; i++) { float sx = x + (w - skew) * i / segs; DrawLine(new Vector2(sx + skew, y), new Vector2(sx, y + h), new Color(0, 0, 0, .45f), 1f); }
        var o = Para(x, x + w - skew); DrawPolyline(new[] { o[0], o[1], o[2], o[3], o[0] }, new Color(0, 0, 0, .8f), 3f);   // dark outer edge
        DrawPolyline(new[] { o[0], o[1], o[2], o[3], o[0] }, new Color(.72f, .58f, .3f, .9f), 1f);   // thin tarnished-gold frame
    }

    /// <summary>Elden Ring style boss bar: bottom centre above the hotbar, name in spaced caps, crimson fill, amber ghost, gold frame, summed damage counter, fade-in on a new boss.</summary>
    void DrawBossBar(Enemy t, Vector2 sz, float u, float pad, float dt, int nameSz)
    {
        if (_bossRef != t)
        {
            _bossRef = t; _gBoss = _bossLastHp = t.Hp / t.MaxHp; _gBossH = 0; _bossDmg = 0; _bossDmgT = 0; _bossFade = 0; _bossDmgShown = -1;
            string nm = t.Def.N.ToUpper(); _bossCh = new string[nm.Length]; _bossAdv = new float[nm.Length];
            for (int i = 0; i < nm.Length; i++) { _bossCh[i] = nm[i].ToString(); _bossAdv[i] = F.GetStringSize(_bossCh[i], HorizontalAlignment.Left, -1, nameSz).X; }
            _bossLv = $"Lv {t.Level}";
        }
        float frac = Mathf.Clamp(t.Hp / t.MaxHp, 0, 1);
        if (t.Hp < _bossLastHp - .01f) { _bossDmg += _bossLastHp - t.Hp; _bossDmgT = 2.5f; }
        _bossLastHp = t.Hp; if (_bossDmgT > 0) { _bossDmgT -= dt; if (_bossDmgT <= 0) _bossDmg = 0; }
        GhostStep(ref _gBoss, ref _gBossH, frac, dt);
        _bossFade = Mathf.Min(1f, _bossFade + dt / .6f); float fa = _bossFade * _bossFade * (3 - 2 * _bossFade);
        float bw = sz.X * .6f * (.88f + .12f * fa), bh = 10f, ss = Mathf.Max(36, u * 4.4f);
        float x0 = sz.X / 2 - bw / 2, by = sz.Y - pad - ss - 110f;   // clear of the hotbar, its buff row and macro line
        var gold = new Color(.72f, .58f, .3f, fa); var parch = new Color(.93f, .87f, .7f, fa);
        // name, letter-spaced
        float cx = x0, ny = by - 8f, sp = 3f;
        for (int i = 0; i < _bossCh.Length; i++) { DrawString(F, new Vector2(cx + 1, ny + 1), _bossCh[i], HorizontalAlignment.Left, -1, nameSz, new Color(0, 0, 0, .8f * fa)); DrawString(F, new Vector2(cx, ny), _bossCh[i], HorizontalAlignment.Left, -1, nameSz, parch); cx += _bossAdv[i] + sp; }
        // level at the right, muted gold
        Txt(new Vector2(x0, ny), _bossLv, fs18(nameSz), new Color(.62f, .5f, .3f, fa), HorizontalAlignment.Right, bw);
        // cumulative damage counter, fades out over the last .5 s
        if (_bossDmgT > 0)
        {
            int shown = Mathf.RoundToInt(_bossDmg); if (shown != _bossDmgShown) { _bossDmgShown = shown; _bossDmgS = shown.ToString("N0"); }
            float da = Mathf.Clamp(_bossDmgT / .5f, 0, 1) * fa;
            Txt(new Vector2(cx + 18, ny), _bossDmgS, nameSz + 2, new Color(1f, .86f, .5f, da));
        }
        // bar: crimson fill, amber ghost via SlantBar, then a gold double rule
        var r = new Rect2(x0, by, bw, bh);
        DrawRect(r.Grow(3), new Color(.03f, .02f, .02f, .85f * fa));
        SlantBar(r, frac * fa, new Color("7a0f14"), 0, 0, _gBoss * fa);
        DrawRect(r.Grow(3), gold, false, 1f);
        // status chips under the bar
        float tx = x0, ic = Mathf.Max(18, u * 1.6f);
        foreach (var s in t.Status.List) { StatusChip(new Vector2(tx, by + bh + 8), ic, s.Def.Col, s.Def.Name[..1], s.T, s.Stacks); tx += ic + 3; }
    }
    static int fs18(int nameSz) => Mathf.Max(11, nameSz - 2);

    /// <summary>Damage ghost: g trails the current fraction, holding ~0.5 s after a drop and then draining.</summary>
    static void GhostStep(ref float g, ref float hold, float cur, float dt)
    {
        cur = Mathf.Clamp(cur, 0, 1);
        if (cur >= g) { g = cur; hold = 0; return; }
        if (hold == 0) hold = .5f;   // fresh drop: wait, then drain (hold < 0 = draining)
        if (hold > 0) { hold -= dt; if (hold <= 0) hold = -1f; return; }
        g = Mathf.Max(cur, g - dt * .35f); if (g <= cur) hold = 0;
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
        DrawCircle(c, rad + 4, UiTheme.Glow); DrawCircle(c, rad, new Color("040b16"));
        // a rotated frame (camera-up) shared by the land, the dungeon and the markers
        float yaw = p.Rig?.Yaw ?? 0f; var f = new Vector2(-Mathf.Sin(yaw), -Mathf.Cos(yaw)); var r = new Vector2(-f.Y, f.X);
        var me = new Vector2(p.GlobalPosition.X, p.GlobalPosition.Z);
        // a raid lair's cells are 40 m: the minimap pulls back to show it
        float span = Dungeon.Active ? Mathf.Min(Dungeon.Tile * 18f, 420f) : 150f; int n = 28; float step = rad * 2 / n;
        Vector2 To(float x, float z) { var dv = new Vector2(x, z) - me; return c + new Vector2(dv.Dot(r), -dv.Dot(f)) / (span / 2) * rad; }
        if (Dungeon.Active)
        {
            // only what you have explored (Hud._Process uncovers it): floor in stone grey, walls darker, the exit and the way down marked
            float half = Dungeon.Tile * .5f, cellPx = Dungeon.Tile / (span / 2) * rad;
            for (int z = 0; z < Dungeon.GH; z++) for (int x = 0; x < Dungeon.GW; x++)
            {
                if (!Dungeon.Seen(x, z)) continue; bool fl = Dungeon.Floor(x, z); if (!fl && !Dungeon.Wall(x, z)) continue;
                var wc = Dungeon.CellCentre(x, z); if ((To(wc.X, wc.Y) - c).Length() > rad - cellPx * .75f) continue;
                DrawColoredPolygon(new[] { To(wc.X - half, wc.Y - half), To(wc.X + half, wc.Y - half), To(wc.X + half, wc.Y + half), To(wc.X - half, wc.Y + half) }, fl ? new Color(.42f, .45f, .5f) : new Color(.16f, .17f, .22f));
            }
            foreach (var (pos, down) in Dungeon.Pads)
            {
                int px = Mathf.FloorToInt((pos.X - Dungeon.Origin.X) / Dungeon.Tile), pz = Mathf.FloorToInt((pos.Z - Dungeon.Origin.Z) / Dungeon.Tile);
                var o = To(pos.X, pos.Z); if (Dungeon.Seen(px, pz) && (o - c).Length() < rad - 4) DrawCircle(o, 4f, down ? new Color(1f, .75f, .38f) : new Color(.5f, .88f, 1f));
            }
        }
        else if (World.MapTex != null)
        {
            _img ??= World.MapTex.GetImage(); var img = _img;
            for (int iy = 0; iy < n; iy++) for (int ix = 0; ix < n; ix++)
            {
                var o = new Vector2((ix + .5f) * step - rad, (iy + .5f) * step - rad); if (o.Length() > rad - 1) continue;
                var w = o / rad * span / 2; var wp = new Vector2(p.GlobalPosition.X, p.GlobalPosition.Z) + r * w.X - f * w.Y;
                var muv = World.MapUV(wp.X, wp.Y); int S = img.GetWidth(); int px = Mathf.Clamp((int)(muv.X * S), 0, S - 1), pz = Mathf.Clamp((int)(muv.Y * S), 0, S - 1);
                DrawRect(new Rect2(c + o - new Vector2(step, step) / 2, new Vector2(step + .5f, step + .5f)), img.GetPixel(px, pz));
            }
            foreach (var poi in World.Pois)
            {
                var o = To(poi.X, poi.Z);
                if ((o - c).Length() < rad - 4) DrawCircle(o, 3.5f, Gold);
            }
        }
        // party members: on the map when close, pinned to the rim (pointing their way) when farther
        foreach (var fr in MapMarks.Friends())
        {
            var o = To(fr.Pos.X, fr.Pos.Z) - c; float l = o.Length(); var col = fr.Party ? MapMarks.PartyCol : MapMarks.OtherCol;
            if (l < rad - 6) MapMarks.Dot(this, c + o, 4.5f, col);
            else MapMarks.Dot(this, c + o / Mathf.Max(.01f, l) * (rad - 6), 3.2f, col with { A = .85f });
        }
        DrawColoredPolygon(new[] { c + new Vector2(0, -8), c + new Vector2(-5, 5), c + new Vector2(5, 5) }, new Color("ff4a3a"));
        DrawArc(c, rad, 0, Mathf.Tau, 48, UiTheme.Accent, 2f); DrawArc(c, rad + 5, -0.5f, 0.5f, 12, UiTheme.Accent, 2f); DrawArc(c, rad + 5, Mathf.Pi - 0.5f, Mathf.Pi + 0.5f, 12, UiTheme.Accent, 2f);
    }

    public static string PlaceName(Vector3 p) { Poi? best = null; float bd = 1e9f; foreach (var poi in World.Pois) { float dd = new Vector2(poi.X - p.X, poi.Z - p.Z).Length(); if (dd < poi.R && dd < bd) { bd = dd; best = poi; } } return best?.Name ?? (Mathf.Abs(p.X) < World.Half && Mathf.Abs(p.Z) < World.Half ? "The Wilds" : Lands.RegionAt(p)); }
}
