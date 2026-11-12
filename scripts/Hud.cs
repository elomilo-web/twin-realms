using Godot;

namespace TwinRealms;

/// <summary>In-game HUD matching the prototype layout: top-left info, compass, quest tracker, target bar, bottom bars, hotbar, minimap.</summary>
public partial class Hud : CanvasLayer
{
    HudCanvas _c = null!;
    string? _region; float _regT;
    public override void _Ready() { Layer = 5; _c = new HudCanvas { MouseFilter = Control.MouseFilterEnum.Ignore }; _c.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); AddChild(_c); }
    public override void _Process(double delta)
    {
        var p = Player.Instance; if (p == null || Dungeon.Active) { if (p == null) return; }
        if (!Dungeon.Active) foreach (var poi in World.Pois)
            if (!Game.D.Disc.Contains(poi.Id) && new Vector2(poi.X - p.GlobalPosition.X, poi.Z - p.GlobalPosition.Z).Length() < poi.R && (!poi.Sky || p.GlobalPosition.Y > poi.Y - 25f)) { Game.D.Disc.Add(poi.Id); Ui.Banner(poi.Name, "discovered"); Sfx.Play("level"); }
        // region banner when crossing into another land, the sky islands or the open sea
        if (!Dungeon.Active && !Arenas.Active)
        {
            _regT -= (float)delta;
            if (_regT <= 0) { _regT = .5f; var reg = Lands.RegionAt(p.GlobalPosition); if (_region != null && reg != _region) Ui.Banner(reg, "entering"); _region = reg; }
        }
    }
}

public partial class HudCanvas : Control
{
    static readonly Color Gold = new Color("e3b04b"), Mute = new Color("a095bd");
    float _hurt, _lastHp = -1, _ptsShowT; int _lastPts;
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
        // hurt vignette: red edges flash when hit and pulse slowly below 35% health
        float dtv = (float)GetProcessDeltaTime(); if (p.Hp < _lastHp - .5f) _hurt = Mathf.Min(1f, _hurt + .35f + (_lastHp - p.Hp) / d.MaxHp() * 3f); _lastHp = p.Hp; _hurt = Mathf.Max(0, _hurt - dtv * 1.6f);
        float hpF0 = p.Hp / d.MaxHp(), low = p.Dead ? 0 : Mathf.Clamp((.35f - hpF0) / .35f, 0, 1) * (.55f + .25f * Mathf.Sin((float)Time.GetTicksMsec() / 260f));
        float vig = Mathf.Max(_hurt, low); if (vig > .01f) DrawTextureRect(Vignette, new Rect2(Vector2.Zero, sz), false, new Color(.85f, .05f, .05f, vig * .85f));
        // ---- status window, top left: name and level, slanted segmented HP bar (green > yellow > red), MP and stamina, XP line, place
        string realm = World.Realm == 1 ? "SHADOW" : "LIGHT"; float hrs = World.TimeOfDay * 24f;
        float pw = Mathf.Max(300, u * 28), ph = fs(1.2f) + 76, px = pad, py = pad;
        Glass(new Rect2(px, py, pw, ph));
        Txt(new Vector2(px + 12, py + fs(1.2f) + 6), d.Name.ToUpper(), fs(1.2f), UiTheme.Title);
        Txt(new Vector2(px + 12, py + fs(1.2f) + 6), $"Lv {d.Level}", fs(1.1f), UiTheme.Accent, HorizontalAlignment.Right, pw - 24);
        float hpF = Mathf.Clamp(p.Hp / d.MaxHp(), 0, 1); var hpCol = hpF > .5f ? new Color("5be37d") : hpF > .25f ? new Color("f2d14b") : new Color("ff5a4f");
        float bx0 = px + 12, bw0 = pw - 24, by0 = py + fs(1.2f) + 14;
        SlantBar(new Rect2(bx0, by0, bw0, 14), hpF, hpCol, 10, 10);
        Txt(new Vector2(bx0, by0 + 30), "HP", fs(.85f), UiTheme.Dim);
        Txt(new Vector2(bx0, by0 + 30), $"{Mathf.CeilToInt(p.Hp)} / {d.MaxHp()}", fs(.95f), Colors.White, HorizontalAlignment.Right, bw0 - 8);
        SlantBar(new Rect2(bx0 + 28, by0 + 36, bw0 * .62f, 7), p.Mp / d.MaxMp(), new Color("4fb6ff"), 5, 0);
        SlantBar(new Rect2(bx0 + 28, by0 + 47, bw0 * .62f, 5), p.St / d.MaxSt(), new Color("9ef0b8"), 4, 0);
        Txt(new Vector2(bx0, by0 + 44), "MP", fs(.75f), UiTheme.Dim);
        DrawRect(new Rect2(px + 1, py + ph - 4, pw - 2, 2), new Color(0, 0, 0, .4f)); DrawRect(new Rect2(px + 1, py + ph - 4, (pw - 2) * Mathf.Clamp((float)d.Xp / d.XpNeed(), 0, 1), 2), UiTheme.Highlight);
        // realm, clock, place and gold sit under the compass (the notes column starts just below the status window)
        float infoY = pad + fs(1.2f) + fs(.5f) + fs(1f) + 12;
        Txt(new Vector2(sz.X / 2 - u * 20, infoY), Dungeon.Active ? $"DEPTH {Dungeon.Depth}" : $"{realm}   {(int)hrs:00}:{(int)((hrs - (int)hrs) * 60):00}   {PlaceName(p.GlobalPosition)}   {d.Gold} G", fs(.95f), World.Realm == 1 ? new Color(.78f, .62f, 1f) : UiTheme.Dim.Lightened(.2f), HorizontalAlignment.Center, u * 40);
        if (Game.Set.ShowFps) Txt(new Vector2(sz.X - pad - 200, sz.Y - 4), $"{Engine.GetFramesPerSecond():0} FPS", 11, UiTheme.Dim, HorizontalAlignment.Right, 200);
        if (Arenas.Active) Txt(new Vector2(sz.X / 2 - u * 20, infoY + fs(1.2f) + 4), Arenas.Status, fs(1.15f), UiTheme.Highlight, HorizontalAlignment.Center, u * 40);
        // quest tracker, top right
        var aq = Quests.Active();
        if (aq.Count > 0)
        {
            // right-aligned text needs a box: draw from (right edge - width) with that width
            var q = aq[0]; var s = Game.Q(q.Id); float qw = u * 30, qx = sz.X - pad - qw;
            float tw0 = F.GetStringSize(q.Name, HorizontalAlignment.Left, -1, fs(1.5f)).X; var dm = new Vector2(sz.X - pad - tw0 - 12, pad + fs(1.5f) * .62f);
            DrawColoredPolygon(new[] { dm + new Vector2(0, -5), dm + new Vector2(4, 0), dm + new Vector2(0, 5), dm + new Vector2(-4, 0) }, UiTheme.Accent);
            Txt(new Vector2(qx, pad + fs(1.5f)), q.Name, fs(1.5f), UiTheme.Title, HorizontalAlignment.Right, qw);
            Txt(new Vector2(qx, pad + fs(1.5f) * 2.1f), s.S == 2 ? "Return to " + Quests.GiverName(q.Giver) : q.Goal + (q.Need > 1 ? $" ({s.N}/{q.Need})" : ""), fs(1.05f), Colors.White, HorizontalAlignment.Right, qw);
        }
        // compass
        float yaw = p.Rig?.Yaw ?? 0f; float cw = u * 34, cx = sz.X / 2, cy = pad + fs(1.2f);
        var cr = new Rect2(cx - cw / 2, cy - fs(1.2f), cw, fs(1.7f)); DrawRect(cr, UiTheme.Panel with { A = .45f });
        DrawLine(cr.Position, cr.Position + new Vector2(cw, 0), UiTheme.AccentSoft, 1f); DrawLine(cr.Position + new Vector2(0, cr.Size.Y), cr.End, UiTheme.AccentSoft, 1f);
        for (int k = 0; k < 16; k++)
        {
            // a = compass heading clockwise from north (-Z); the camera faces heading -yaw, so +rel is to the right
            float a = k * Mathf.Tau / 16f; float rel = Mathf.Wrap(a + yaw, -Mathf.Pi, Mathf.Pi);
            float xx = cx + rel / (Mathf.Pi / 2) * (cw / 2); if (Mathf.Abs(xx - cx) > cw / 2) continue;
            string[] nm = { "N", "", "NE", "", "E", "", "SE", "", "S", "", "SW", "", "W", "", "NW", "" };
            if (nm[k] != "") Txt(new Vector2(xx - 20, cy), nm[k], fs(1.1f), k == 0 ? UiTheme.Highlight : k % 4 == 0 ? UiTheme.Title : UiTheme.Dim, HorizontalAlignment.Center, 40); else DrawLine(new Vector2(xx, cy - fs(0.9f)), new Vector2(xx, cy - fs(0.4f)), UiTheme.AccentSoft, 1f);
        }
        DrawColoredPolygon(new[] { new Vector2(cx - 5, cr.End.Y + 6), new Vector2(cx + 5, cr.End.Y + 6), new Vector2(cx, cr.End.Y) }, UiTheme.Accent);
        // target bar
        Enemy? tgt = Targeting.Aim != null && IsInstanceValid(Targeting.Aim) && !Targeting.Aim.Dead ? Targeting.Aim : Enemy.CurrentBoss != null && GodotObject.IsInstanceValid(Enemy.CurrentBoss) && !Enemy.CurrentBoss.Dead && Enemy.CurrentBoss.Active && Enemy.CurrentBoss.GlobalPosition.DistanceTo(p.GlobalPosition) < 35f ? Enemy.CurrentBoss : p.Dead ? null : p.AimPoint(40f).enemy;
        if (tgt != null && !tgt.Dead)
        {
            float tw = u * 24, ty = u * 7.6f + (Arenas.Active ? fs(1.4f) : 0); Txt(new Vector2(sz.X / 2 - tw / 2, ty), $"{tgt.Def.N}  Lv {tgt.Level}", fs(1.2f), tgt.Def.Boss ? UiTheme.Highlight : Colors.White, HorizontalAlignment.Center, tw);
            SlantBar(new Rect2(sz.X / 2 - tw / 2, ty + 6, tw, Mathf.Max(7, u * .75f)), tgt.Hp / tgt.MaxHp, tgt.Def.Flee ? new Color("9ad06a") : new Color("ff5a4f"), 6, 10);
            float extra = 0;
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
            float aw = F.GetStringSize(act, HorizontalAlignment.Left, -1, pf).X, kw = Mathf.Max(pf * 1.6f, F.GetStringSize(ek, HorizontalAlignment.Left, -1, pf).X + pf * .6f), tot = kw + 10 + aw + 24, x0 = sz.X / 2 - tot / 2, y0 = sz.Y * .62f - pf;
            Glass(new Rect2(x0, y0, tot, pf * 1.7f));
            DrawRect(new Rect2(x0 + 6, y0 + 5, kw, pf * 1.7f - 10), UiTheme.Accent with { A = .85f });
            Txt(new Vector2(x0 + 6, y0 + pf * 1.2f), ek, pf, new Color("04111f"), HorizontalAlignment.Center, kw);
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
            Txt(new Vector2(ux, hy + ss * .62f),$"Unspent: {d.AttrPts} attribute (Esc > Character), {d.RankPts} skill (L), {d.SkillPts} skill tree (K)", fs(1f), UiTheme.Highlight, HorizontalAlignment.Left, Mathf.Max(100, sz.X - ux - pad));
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
            Txt(r.Position + new Vector2(4, fs(.95f) + 1), key, fs(.95f), UiTheme.Accent); Txt(r.Position + new Vector2(0, ss - 4), cnt, fs(.85f), i == 6 ? new Color("ff8f86") : i == 7 ? new Color("8fb8ff") : UiTheme.Dim.Lightened(.3f), HorizontalAlignment.Right, ss - 4);
        }
        // hero statuses and skill buffs: icons with seconds left, just above the hotbar
        float sx0 = hx; float isz = Mathf.Max(22, u * 2.1f);
        foreach (var b in p.Buffs) { StatusChip(new Vector2(sx0, hy - isz - 8), isz, new Color(1f, .82f, .4f), Skills.Get(b.id)?.Name[..1] ?? "+", b.t, 0); sx0 += isz + 4; }
        foreach (var s in p.Statuses.List) { StatusChip(new Vector2(sx0, hy - isz - 8), isz, s.Def.Col, s.Def.Name[..1], s.T, s.Stacks); sx0 += isz + 4; }
        // minimap bottom-right
        if (!Dungeon.Active) DrawMini(p, new Vector2(sz.X - pad - u * 7, sz.Y - pad - u * 7), u * 7);
        if (p.Dead) Txt(new Vector2(sz.X / 2, sz.Y / 2), "YOU DIED", fs(5f), new Color("ff7b6f"), HorizontalAlignment.Center, sz.X);
    }

    /// <summary>Status chip: tinted square, the status initial, stacks in the corner and a draining bar for time left.</summary>
    void StatusChip(Vector2 at, float s, Color c, string letter, float t, int stacks)
    {
        var r = new Rect2(at, new Vector2(s, s)); DrawRect(r, new Color(.02f, .05f, .1f, .8f)); DrawRect(r.Grow(-2), c with { A = .45f }); DrawRect(r, c, false, 1.5f);
        Txt(at + new Vector2(0, s * .72f), letter, (int)(s * .6f), Colors.White, HorizontalAlignment.Center, s);
        if (stacks > 1) Txt(at + new Vector2(0, s - 2), stacks.ToString(), (int)(s * .38f), Colors.White, HorizontalAlignment.Right, s - 2);
        DrawRect(new Rect2(at + new Vector2(0, s + 1), new Vector2(s * Mathf.Clamp(t / 10f, 0, 1), 2)), c);
    }

    /// <summary>Glass box like the menus: translucent navy, cyan edge, bright corner ticks.</summary>
    /// <summary>Multiplayer: party member frames (or everyone online when not in a party) on the left, and the duel status.</summary>
    void DrawLobby(Vector2 sz, float u, float pad, int f)
    {
        var net = Net.I!; var party = net.MyParty;
        var ids = new System.Collections.Generic.List<long>(); if (party != null) ids.AddRange(party.Members); else ids.AddRange(net.Players.Keys);
        ids.Remove(Net.MyId);
        float w = Mathf.Max(200, u * 15), x = pad, y = sz.Y * .42f;
        Txt(new Vector2(x, y), party != null ? "PARTY  ·  " + Net.LootModeName(party.Loot).Split(' ')[0].ToUpper() + " LOOT" : $"ONLINE {net.Players.Count}  ·  {(Net.IsHost ? "HOSTING" : "IN " + net.NameOf(1).ToUpper() + "'S WORLD")}", Mathf.Max(11, f - 2), UiTheme.Accent);
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
    void SlantBar(Rect2 r, float frac, Color col, float skew, int segs)
    {
        frac = Mathf.Clamp(frac, 0, 1); float x = r.Position.X, y = r.Position.Y, w = r.Size.X, h = r.Size.Y;
        Vector2[] Para(float x0, float x1) => new[] { new Vector2(x0 + skew, y), new Vector2(x1 + skew, y), new Vector2(x1, y + h), new Vector2(x0, y + h) };
        DrawColoredPolygon(Para(x, x + w - skew), new Color(0, 0, 0, .55f));
        if (frac > 0.001f) { DrawColoredPolygon(Para(x, x + (w - skew) * frac), col); DrawColoredPolygon(new[] { new Vector2(x + skew, y), new Vector2(x + skew + (w - skew) * frac, y), new Vector2(x + skew * .55f + (w - skew) * frac, y + h * .45f), new Vector2(x + skew * .55f, y + h * .45f) }, new Color(1, 1, 1, .18f)); }
        for (int i = 1; i < segs; i++) { float sx = x + (w - skew) * i / segs; DrawLine(new Vector2(sx + skew, y), new Vector2(sx, y + h), new Color(0, 0, 0, .45f), 1f); }
        var o = Para(x, x + w - skew); DrawPolyline(new[] { o[0], o[1], o[2], o[3], o[0] }, UiTheme.AccentSoft, 1f);
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
                var muv = World.MapUV(wp.X, wp.Y); int S = img.GetWidth(); int px = Mathf.Clamp((int)(muv.X * S), 0, S - 1), pz = Mathf.Clamp((int)(muv.Y * S), 0, S - 1);
                DrawRect(new Rect2(c + o - new Vector2(step, step) / 2, new Vector2(step + .5f, step + .5f)), img.GetPixel(px, pz));
            }
            foreach (var poi in World.Pois)
            {
                var dv = new Vector2(poi.X - p.GlobalPosition.X, poi.Z - p.GlobalPosition.Z); var o = new Vector2(dv.Dot(r), -dv.Dot(f)) / (span / 2) * rad;
                if (o.Length() < rad - 4) DrawCircle(c + o, 3.5f, Gold);
            }
        }
        DrawColoredPolygon(new[] { c + new Vector2(0, -8), c + new Vector2(-5, 5), c + new Vector2(5, 5) }, new Color("ff4a3a"));
        DrawArc(c, rad, 0, Mathf.Tau, 48, UiTheme.Accent, 2f); DrawArc(c, rad + 5, -0.5f, 0.5f, 12, UiTheme.Accent, 2f); DrawArc(c, rad + 5, Mathf.Pi - 0.5f, Mathf.Pi + 0.5f, 12, UiTheme.Accent, 2f);
    }

    public static string PlaceName(Vector3 p) { Poi? best = null; float bd = 1e9f; foreach (var poi in World.Pois) { float dd = new Vector2(poi.X - p.X, poi.Z - p.Z).Length(); if (dd < poi.R && dd < bd) { bd = dd; best = poi; } } return best?.Name ?? (Mathf.Abs(p.X) < World.Half && Mathf.Abs(p.Z) < World.Half ? "The Wilds" : Lands.RegionAt(p)); }
}
