using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TwinRealms;

/// <summary>
/// Skills menu (L). Left: every skill as an icon tile (rank pips, hotbar key, lock), with filters and a search box, and the hotbar
/// below it (click a key to put the selected skill there). Right: the selected skill's card: numbers, learn / rank up, and its
/// modifier sockets (click a socket, pick a modifier). Modifiers themselves are unlocked in the modifier tree (U).
/// Built to hold many more skills than it has: the grid scrolls and filters instead of listing everything as rows of text.
/// </summary>
public partial class Ui
{
    static string _selSkill = "", _skillFilter = "all", _skillSearch = "";
    static int _openSocket = -1;
    InfoTip? _info;

    void Info(Vector2 at, string title, Color tc, IEnumerable<(string, Color, int)> lines, float width = 300)
    {
        if (_info == null || !IsInstanceValid(_info)) { _info = new InfoTip(); AddChild(_info); }
        _info.Show(at, title, tc, lines, width);
    }
    void HideInfo() { if (_info != null && IsInstanceValid(_info)) _info.Visible = false; }

    static Label Chip(Node parent, string text, bool hot)
    {
        var pc = new PanelContainer(); pc.AddThemeStyleboxOverride("panel", UiTheme.Box(hot ? UiTheme.Highlight with { A = .16f } : UiTheme.Button, hot ? UiTheme.Highlight : UiTheme.AccentSoft, 1, 10, 3, 0)); parent.AddChild(pc);
        var l = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore }; l.AddThemeFontSizeOverride("font_size", 15); l.AddThemeColorOverride("font_color", hot ? UiTheme.Highlight : UiTheme.Text); pc.AddChild(l); return l;
    }

    static bool SkillShown(SkillDef k, string filter, string search)
    {
        var d = Game.D; bool known = d.Skills.ContainsKey(k.Id);
        bool f = filter switch
        {
            "spell" => k.Tags.Contains("spell"), "attack" => !k.Tags.Contains("spell"), "learned" => known,
            "learn" => !known && d.Level >= k.Req, _ => true,
        };
        if (!f) return false;
        if (search == "") return true;
        var s = search.ToLowerInvariant();
        return k.Name.ToLowerInvariant().Contains(s) || k.Desc.ToLowerInvariant().Contains(s) || k.Tags.Exists(t => t.Contains(s)) || k.Type.Contains(s);
    }

    public void OpenSkills()
    {
        var d = Game.D; Skills.GrantStarting(d);
        if (_selSkill == "" || Skills.Get(_selSkill) == null) { _selSkill = d.Bar.Length > 0 && !string.IsNullOrEmpty(d.Bar[0]) ? d.Bar[0] : "fire"; _openSocket = -1; }
        var v = Frame("skills", "Skills", new Vector2(1100, 640), OpenSkills); HideInfo();

        // ---- top bar: points, filters, search, the other skill menus
        var top = new HBoxContainer(); v.AddChild(top); top.AddThemeConstantOverride("separation", 8);
        Chip(top, $"Skill points  {d.RankPts}", d.RankPts > 0);
        top.AddChild(new Control { CustomMinimumSize = new Vector2(8, 0) });
        foreach (var (id, label) in new[] { ("all", "All"), ("spell", "Spells"), ("attack", "Weapon skills"), ("learned", "Learned"), ("learn", "Can learn") })
        {
            string fid = id; var b = Btn(label, () => { _skillFilter = fid; OpenSkills(); }, true); b.CustomMinimumSize = new Vector2(0, 30);
            if (_skillFilter == id) { b.AddThemeColorOverride("font_color", UiTheme.Highlight); b.AddThemeStyleboxOverride("normal", UiTheme.Box(UiTheme.ButtonHover, UiTheme.Highlight, 1, 10, 4, 0)); }
            top.AddChild(b);
        }
        var search = new LineEdit { Text = _skillSearch, PlaceholderText = "Search skills...", CustomMinimumSize = new Vector2(170, 30), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; top.AddChild(search);
        top.AddChild(Btn($"Modifier tree ({Inputs.KeyOf(Inputs.ModsUi)})", OpenMods, true));
        top.AddChild(Btn($"Basic attacks ({Inputs.KeyOf(Inputs.AttacksUi)})", () => OpenAttacks(null), true));

        var cols = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; cols.AddThemeConstantOverride("separation", 16); v.AddChild(cols);

        // ---- left: the skill grid and the hotbar
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(468, 0) }; left.AddThemeConstantOverride("separation", 6); cols.AddChild(left);
        var gs = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(468, 200), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; left.AddChild(gs);
        var grid = new GridContainer { Columns = 7 }; grid.AddThemeConstantOverride("h_separation", 6); grid.AddThemeConstantOverride("v_separation", 6); gs.AddChild(grid);
        var tiles = new List<(SkillTile t, SkillDef k)>();
        foreach (var k in Skills.All)
        {
            bool known = d.Skills.TryGetValue(k.Id, out var st); string id = k.Id; int key = Array.IndexOf(d.Bar, id);
            var t = new SkillTile
            {
                Icon = Icons.ForSkill(k), Known = known, Rank = st?.Rank ?? 0, MaxRank = Skills.MaxRank, Key = known ? key : -1, Selected = id == _selSkill, Tint = Dmg.TypeColor(k.Type),
                Locked = !known && d.Level < k.Req, CanLearn = !known && d.Level >= k.Req && d.RankPts >= k.Cost, Corner = known ? "" : d.Level < k.Req ? $"Lv {k.Req}" : $"{k.Cost} pt",
                CustomMinimumSize = new Vector2(60, 60), Visible = SkillShown(k, _skillFilter, _skillSearch),
            };
            t.Click = () => { _selSkill = id; _openSocket = -1; OpenSkills(); };
            t.Hover = (on, at) => { if (on) Info(at, k.Name, Dmg.TypeColor(k.Type).Lerp(Colors.White, .4f), SkillTipLines(k)); else HideInfo(); };
            grid.AddChild(t); tiles.Add((t, k));
        }
        // the search filters live (rebuilding the menu would take the focus off the search box)
        search.TextChanged += s => { _skillSearch = s; foreach (var (t, k) in tiles) t.Visible = SkillShown(k, _skillFilter, s); };

        Lbl("HOTBAR   click a key to put the selected skill on it", 13, UiTheme.Accent, left);
        var bar = new HBoxContainer(); bar.AddThemeConstantOverride("separation", 6); left.AddChild(bar);
        bool selKnown = d.Skills.ContainsKey(_selSkill);
        for (int i = 0; i < 6; i++)
        {
            int slot = i; var bk = Skills.Get(d.Bar[i] ?? "");
            var t = new SkillTile { Icon = bk != null ? Icons.ForSkill(bk) : "", Known = bk != null, Rank = bk != null && d.Skills.TryGetValue(bk.Id, out var bs) ? bs.Rank : 0, MaxRank = Skills.MaxRank, Key = i, Selected = d.Bar[i] == _selSkill && selKnown, CustomMinimumSize = new Vector2(70, 58), Tint = bk != null ? Dmg.TypeColor(bk.Type) : UiTheme.Accent };
            t.Click = () =>
            {
                if (!d.Skills.ContainsKey(_selSkill)) { Note("Learn the skill first", new Color(1, .6f, .5f)); return; }
                int was = Array.IndexOf(d.Bar, _selSkill); if (was >= 0) d.Bar[was] = d.Bar[slot]; d.Bar[slot] = _selSkill; Game.Notify(); OpenSkills();
            };
            t.Hover = (on, at) => { if (on && bk != null) Info(at, $"Key {slot + 1}: {bk.Name}", Colors.White, new[] { ("Click to put " + (Skills.Get(_selSkill)?.Name ?? "the selected skill") + " here instead.", UiTheme.Dim, 13) }); else HideInfo(); };
            bar.AddChild(t);
        }

        // ---- right: the selected skill
        var right = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; right.AddThemeConstantOverride("separation", 6); cols.AddChild(right);
        SkillCard(right);
    }

    static List<(string, Color, int)> SkillTipLines(SkillDef k)
    {
        var d = Game.D; var l = new List<(string, Color, int)>();
        l.Add((string.Join(" · ", k.Tags) + "  ·  " + k.Type, Dmg.TypeColor(k.Type).Lerp(UiTheme.Dim, .3f), 12));
        l.Add((k.Desc, Colors.White, 14));
        var c = Skills.Compute(k.Id);
        var nums = new List<string>();
        if (k.Kind != "heal" && k.Kind != "buff") nums.Add($"{c.Dmg:0} damage" + (c.Count > 1 ? $" x{c.Count}" : ""));
        if (c.Mp > 0) nums.Add($"{c.Mp:0} mana"); if (c.St > 0) nums.Add($"{c.St:0} stamina"); nums.Add($"{c.Cd:0.#} s cooldown");
        l.Add((string.Join("   ", nums), UiTheme.Highlight, 13));
        if (d.Skills.TryGetValue(k.Id, out var st)) l.Add(($"Rank {st.Rank}/{Skills.MaxRank}" + (st.Sup.Exists(s => s != "") ? "   ·   " + string.Join(", ", st.Sup.Where(s => s != "").Select(s => Skills.Sup(s)?.Name ?? s)) : ""), UiTheme.Accent, 12));
        else l.Add((d.Level < k.Req ? $"Unlocks at level {k.Req}" : $"Click, then Learn ({k.Cost} skill point)", d.Level < k.Req ? new Color(1f, .6f, .5f) : UiTheme.Highlight, 12));
        return l;
    }

    void SkillCard(VBoxContainer box)
    {
        var d = Game.D; var sk = Skills.Get(_selSkill)!; bool learned = d.Skills.TryGetValue(sk.Id, out var ss);
        var card = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; card.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(.02f, .06f, .12f, .7f), UiTheme.AccentSoft, 1, 14, 10, 0)); box.AddChild(card);
        var v = new VBoxContainer(); v.AddThemeConstantOverride("separation", 7); card.AddChild(v);

        // header: icon, name, tags; learn / rank up on the right
        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 12); v.AddChild(head);
        head.AddChild(new IconBox { Icon = Icons.ForSkill(sk), CustomMinimumSize = new Vector2(58, 58) });
        var hv = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; hv.AddThemeConstantOverride("separation", 0); head.AddChild(hv);
        Lbl(sk.Name.ToUpper(), 22, UiTheme.Title, hv);
        Lbl(string.Join("  ·  ", sk.Tags) + "     " + sk.Type + " damage", 13, Dmg.TypeColor(sk.Type).Lerp(UiTheme.Dim, .25f), hv);
        var act = new VBoxContainer(); act.AddThemeConstantOverride("separation", 2); head.AddChild(act);
        if (!learned)
        {
            var lb = Btn(d.Level < sk.Req ? $"Unlocks at level {sk.Req}" : $"Learn  ({sk.Cost} pt)", () => Do(Skills.Unlock(sk.Id), "Learned " + sk.Name)); lb.Disabled = d.Level < sk.Req || d.RankPts < sk.Cost; lb.CustomMinimumSize = new Vector2(170, 36); act.AddChild(lb);
        }
        else
        {
            var rb = Btn(ss!.Rank >= Skills.MaxRank ? "Max rank" : $"Rank up  (1 pt)", () => Do(Skills.RankUp(sk.Id), sk.Name + " ranked up")); rb.Disabled = ss.Rank >= Skills.MaxRank || d.RankPts < 1; rb.CustomMinimumSize = new Vector2(170, 36); act.AddChild(rb);
            Lbl($"Rank {ss.Rank}/{Skills.MaxRank}   +{Skills.RankP:0}% damage per rank", 12, UiTheme.Highlight, act);
        }
        var desc = Row(v, sk.Desc, Colors.White, 15); desc.CustomMinimumSize = new Vector2(400, 0);

        // numbers: three label/value pairs per row
        var c = Skills.Compute(sk.Id);
        var grid = new GridContainer { Columns = 6 }; grid.AddThemeConstantOverride("h_separation", 12); grid.AddThemeConstantOverride("v_separation", 2); v.AddChild(grid);
        void Stat(string n, string val) { Lbl(n, 12, Dim, grid); Lbl(val, 14, Colors.White, grid).CustomMinimumSize = new Vector2(78, 0); }
        if (sk.Kind is not ("heal" or "buff" or "enchant" or "ward" or "resurrect")) Stat("Damage", $"{c.Dmg:0}" + (sk.Kind == "zone" ? "/tick" : c.Count > 1 ? $" x{c.Count}" : ""));
        if (sk.Kind == "heal") Stat("Heals", $"{c.Heal * 100:0}% hp");
        if (c.Mp > 0) Stat("Mana", $"{c.Mp:0}"); if (c.St > 0) Stat("Stamina", $"{c.St:0}");
        Stat("Cooldown", $"{c.Cd:0.0} s"); Stat("Use time", $"{c.Cast:0.00} s");
        if (c.Radius > 0) Stat("Area", $"{c.Radius:0.0} m");
        if (sk.Kind == "projectile") Stat("Speed", $"{c.Speed:0} m/s");
        if (c.Pierce > 0) Stat("Pierce", c.Pierce.ToString()); if (c.Chain > 0) Stat("Chains", c.Chain.ToString());
        if (c.Homing) Stat("Homing", "yes"); if (c.Stun > 0) Stat("Stun", $"{c.Stun:0.0} s"); if (c.Up > 0) Stat("Knock-up", $"{c.Up:0}");
        if (c.Echo > 0) Stat("Echo", c.Echo > 1 ? $"repeats x{Mathf.Min(c.Echo, 3)}" : "repeats"); if (c.Leech > 0) Stat("Leech", $"{c.Leech:0}%");
        if (c.CritC > 0) Stat("Crit chance", $"+{c.CritC:0}%"); if (c.Execute > 0) Stat("Execute", $"+{c.Execute:0}%");
        if (c.SelfHeal > 0) Stat("Heals on use", $"{c.SelfHeal:0.#}% hp"); if (c.ManaBack > 0) Stat("Cost refund", $"{c.ManaBack:0}%");
        if (c.Dur > 0 && sk.Kind is not ("projectile" or "chain" or "nova" or "strike" or "melee" or "spin" or "leap" or "dash" or "rush" or "hook" or "blinkto" or "resurrect")) Stat("Duration", $"{c.Dur:0.#} s");
        if (sk.Kind == "enchant") Row(v, "Party aura: every ally in the area gets it" + (sk.X.ContainsKey("ward") ? $"; shield {sk.X["ward"] * 100:0}% hp" : "") + (sk.X.ContainsKey("hot") ? $"; heals {sk.X["hot"]:0.#}% hp/s" : "") + (sk.X.ContainsKey("cleanse") ? "; cleanses" : ""), new Color(1f, .9f, .6f), 13);
        if (sk.Buff.Count > 0) Row(v, string.Join(", ", sk.Buff.Select(kv => TreeDb.StatText(kv.Key, kv.Value))), new Color(.75f, .92f, 1f), 13);
        if (c.Auto != "") Stat("Auto-cast", c.Auto == "timer" ? $"every {c.AutoCd:0} s" : c.Auto == "hit" ? "when hit" : "on crit");
        if (c.Status.Count > 0) Row(v, "Effects: " + string.Join(", ", c.Status.Select(s => $"{Dmg.Status(s.id)?.Name ?? s.id} {s.chance * 100:0}%")), new Color(.8f, .9f, 1f), 13);

        // modifier sockets
        var sh = new HBoxContainer(); v.AddChild(sh);
        var mt = Lbl("MODIFIERS", 15, UiTheme.Accent, sh); mt.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        if (!learned) { Row(v, "Learn the skill to socket modifiers into it (1 socket, 2 at rank 2, 3 at rank 4).", Dim, 13); return; }
        int open = Skills.Sockets(ss!.Rank); while (ss.Sup.Count < open) ss.Sup.Add("");
        Lbl($"{ss.Sup.Count(s => s != "")}/{open} used", 13, Dim, sh);
        var sockets = new HBoxContainer(); sockets.AddThemeConstantOverride("separation", 8); v.AddChild(sockets);
        for (int i = 0; i < 3; i++)
        {
            int sock = i; bool locked = i >= open; var sp = !locked && i < ss.Sup.Count ? Skills.Sup(ss.Sup[i]) : null;
            var mn = sp != null ? ModTree.Get(ModTree.KeyOf(sp.Id, false)) : null;
            string label = locked ? $"Opens at rank {(i == 1 ? 2 : 4)}" : sp != null ? sp.Name : "+  Empty socket";
            var b = Btn(label, () => { _openSocket = _openSocket == sock ? -1 : sock; OpenSkills(); }, true);
            b.CustomMinimumSize = new Vector2(0, 40); b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; b.Disabled = locked; b.ClipText = true;
            var col = mn?.Branch.Col ?? UiTheme.Accent;
            b.AddThemeStyleboxOverride("normal", UiTheme.Box(sp != null ? col with { A = .14f } : UiTheme.Button, _openSocket == i ? UiTheme.Highlight : sp != null ? col : UiTheme.AccentSoft, _openSocket == i ? 2 : 1, 10, 4, 0));
            if (sp != null) b.AddThemeColorOverride("font_color", col.Lightened(.35f));
            if (sp != null) { var spc = sp; b.MouseEntered += () => Info(GetViewport().GetMousePosition(), spc.Name, col.Lightened(.3f), new[] { (spc.Desc, Colors.White, 14), ("Click to change", UiTheme.Dim, 12) }); b.MouseExited += HideInfo; }
            sockets.AddChild(b);
        }
        if (_openSocket >= 0 && _openSocket < open)
        {
            // the picker: every unlocked modifier that fits this skill
            int sock = _openSocket;
            // in its own scroll box (there are many modifiers), grouped by their lane in the modifier tree
            var pickScroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 230), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; v.AddChild(pickScroll);
            var pick = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; pick.AddThemeConstantOverride("h_separation", 6); pick.AddThemeConstantOverride("v_separation", 6); pickScroll.AddChild(pick);
            int shown = 0;
            var order = Skills.AllSupports.OrderBy(sp => ModTree.Get(ModTree.KeyOf(sp.Id, false)) is { } n ? ModTree.Branches.IndexOf(n.Branch) * 100 + n.Index : 9999).ToList();
            foreach (var sp in order)
            {
                if (!d.Supports.Contains(sp.Id) || !Skills.Fits(sp, sk)) continue;
                bool here = ss.Sup[sock] == sp.Id, elsewhere = !here && ss.Sup.Contains(sp.Id); string sid = sp.Id; var mn = ModTree.Get(ModTree.KeyOf(sp.Id, false)); var col = mn?.Branch.Col ?? UiTheme.Accent;
                var b = Btn(sp.Name, () => { _openSocket = -1; Do(Skills.Socket(sk.Id, sock, sid), ""); }, true);
                b.Disabled = elsewhere || here; b.CustomMinimumSize = new Vector2(0, 32);
                b.AddThemeStyleboxOverride("normal", UiTheme.Box(col with { A = .12f }, col with { A = .8f }, 1, 10, 3, 0)); b.AddThemeColorOverride("font_color", col.Lightened(.4f));
                var spc = sp; string on = Skills.OnSkill(sp, sk);
                b.MouseEntered += () => Info(GetViewport().GetMousePosition(), spc.Name, col.Lightened(.3f), new[] { (spc.Desc, Colors.White, 14), (on != "" ? $"On {sk.Name}: {on}." : "", UiTheme.Highlight, 13), (elsewhere ? "Already in another socket" : here ? "In this socket" : "Click to socket", UiTheme.Dim, 12) }); b.MouseExited += HideInfo;
                pick.AddChild(b); shown++;
            }
            if (ss.Sup[sock] != "") pick.AddChild(Btn("Empty the socket", () => { _openSocket = -1; Do(Skills.Socket(sk.Id, sock, ""), ""); }, true));
            if (shown == 0) Row(v, $"None of your unlocked modifiers fit {sk.Name} ({string.Join(", ", sk.Tags)}). Unlock more in the modifier tree ({Inputs.KeyOf(Inputs.ModsUi)}).", Dim, 13);
        }
        else
        {
            foreach (var sid in ss.Sup) if (Skills.Sup(sid) is { } sp) { string on = Skills.OnSkill(sp, sk); Row(v, $"{sp.Name}:  {sp.Desc}" + (on != "" ? $"  On {sk.Name}: {on}." : ""), UiTheme.Text, 13); }
            int fits = Skills.AllSupports.Count(sp => Skills.Fits(sp, sk)), have = Skills.AllSupports.Count(sp => Skills.Fits(sp, sk) && d.Supports.Contains(sp.Id));
            Row(v, $"{have} of the {fits} modifiers that fit this skill are unlocked. Click a socket to change it; unlock more in the modifier tree ({Inputs.KeyOf(Inputs.ModsUi)}).", Dim, 13);
        }
    }

    void Do(string? err, string ok)
    {
        if (err != null) { Note(err, new Color(1, .6f, .5f)); return; }
        if (ok != "") { Note(ok, Gold); Sfx.Play("level"); } else Sfx.Play("click");
        Game.Notify(); OpenSkills();
    }
}

/// <summary>Draws one vector icon inside a framed box.</summary>
public partial class IconBox : Control
{
    public string Icon = "";
    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size); DrawRect(r, new Color(.02f, .05f, .1f, .8f)); DrawRect(r, UiTheme.Accent with { A = .6f }, false, 1.5f);
        Icons.Draw(this, Icon, r.GetCenter(), Size.Y * .7f);
    }
}
