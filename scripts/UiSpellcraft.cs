using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TwinRealms;

/// <summary>
/// Spellcraft menu (B): build skills from spell cards, as with the amulet in Two Worlds II. Left: your crafted spells. Middle: the
/// spell being built, its two sections as rows of card slots (click a card to take it out), a live preview of what it does and the
/// Forge button. Right: your cards by type (click to put one in the selected section). Rules and numbers in Spellcraft.cs and
/// data/spellcards.json; a forged spell is a skill like any other (rank it up and socket modifiers in the skills menu).
/// </summary>
public partial class Ui
{
    static CraftedSpell _scWork = new();
    static string _scEdit = "", _scTab = "carrier";
    static int _scSec = 0; static bool _scConfirm;

    static void ScLoad(string id)
    {
        var s = id == "" ? null : Game.D.Spells.FirstOrDefault(x => x.Id == id);
        _scWork = s == null ? new CraftedSpell() : new CraftedSpell { Id = s.Id, Name = s.Name, A = s.A.ToList(), B = s.B.ToList() };
        _scEdit = s?.Id ?? ""; _scSec = 0; _scConfirm = false;
    }

    /// <summary>Opens the menu; edit = a crafted spell's id to load it, "" for a new spell, null to keep what was being built.</summary>
    public void OpenSpellcraft(string? edit)
    {
        var d = Game.D; Skills.GrantStarting(d);
        if (_scTree) { OpenSpellTree(); return; }
        if (edit != null) ScLoad(edit);
        if (_scEdit != "" && !d.Spells.Exists(x => x.Id == _scEdit)) ScLoad("");
        var v = Frame("spellcraft", "Spellcraft", new Vector2(1180, 680), () => OpenSpellcraft(null)); HideInfo();
        string fam = _scWork.A.Count > 0 ? Spellcraft.FamilyOf(_scWork) : "spell";
        int maxCards = Spellcraft.MaxCards(d, fam), maxCopies = Spellcraft.MaxCopies(d), used = _scWork.A.Count + _scWork.B.Count;

        // ---- top bar
        var top = new HBoxContainer(); v.AddChild(top); top.AddThemeConstantOverride("separation", 8);
        Chip(top, $"Spells  {d.Spells.Count}/{Spellcraft.SpellCap(d)}", false);
        Chip(top, $"Cards per {fam} spell  {maxCards}", false);
        Chip(top, $"Copies of one card  {maxCopies}", false);
        var help = Lbl("A spell needs a carrier (how) and an effect (what). Modifiers change it; extra copies of a card make it stronger.", 13, Dim, top);
        help.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; help.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        int tfree = Spellcraft.TreeFree(d);
        var tb = Btn(tfree > 0 ? $"Spell tree  ({tfree} to spend)" : "Spell tree", () => { _scTree = true; OpenSpellTree(); }, true);
        if (tfree > 0) tb.AddThemeColorOverride("font_color", UiTheme.Highlight);
        top.AddChild(tb);
        top.AddChild(Btn($"Skills ({Inputs.KeyOf(Inputs.SkillsUi)})", OpenSkills, true));

        var cols = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; cols.AddThemeConstantOverride("separation", 14); v.AddChild(cols);

        // ---- left: crafted spells
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(220, 0) }; left.AddThemeConstantOverride("separation", 5); cols.AddChild(left);
        Header("Your spells", left);
        var nb = Btn("+  New spell", () => { ScLoad(""); OpenSpellcraft(null); }, true); Mark(nb, _scEdit == ""); left.AddChild(nb);
        var ls = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; left.AddChild(ls);
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; list.AddThemeConstantOverride("separation", 4); ls.AddChild(list);
        foreach (var s in d.Spells)
        {
            string sid = s.Id; var k = Skills.Get(sid); int key = Array.IndexOf(d.Bar, sid);
            var b = Btn((k?.Name ?? Spellcraft.AutoName(s)) + (key >= 0 ? $"   [{key + 1}]" : ""), () => { ScLoad(sid); OpenSpellcraft(null); }, true);
            b.Alignment = HorizontalAlignment.Left; Mark(b, sid == _scEdit); if (k != null) b.AddThemeColorOverride("font_color", Dmg.TypeColor(k.Type).Lerp(Colors.White, .45f));
            list.AddChild(b);
        }
        if (d.Spells.Count == 0) Row(list, "No spells yet. Put a carrier and an effect card in the first section, then forge it.", Dim, 13);
        Row(left, "Forged spells are skills: put them on the hotbar, rank them up and socket modifiers in the skills menu.", Dim, 12);

        // ---- middle: the spell being built
        var mid = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; mid.AddThemeConstantOverride("separation", 6); cols.AddChild(mid);
        var nr = new HBoxContainer(); nr.AddThemeConstantOverride("separation", 6); mid.AddChild(nr);
        Lbl("NAME", 13, UiTheme.Accent, nr);
        var name = new LineEdit { Text = _scWork.Name, PlaceholderText = Spellcraft.AutoName(_scWork), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 30), MaxLength = 32 };
        name.TextChanged += t => _scWork.Name = t; nr.AddChild(name);
        Lbl($"Cards {used}/{maxCards}", 15, used > maxCards ? new Color(1, .55f, .5f) : UiTheme.Highlight, nr);

        void Section(int sec, string title, string sub)
        {
            var cards = sec == 0 ? _scWork.A : _scWork.B;
            var hr = new HBoxContainer(); hr.AddThemeConstantOverride("separation", 8); mid.AddChild(hr);
            var hb = Btn(title, () => { _scSec = sec; OpenSpellcraft(null); }, true); hr.AddChild(hb);
            var flat = new StyleBoxEmpty();
            foreach (var sn in new[] { "normal", "hover", "pressed", "focus", "disabled" }) hb.AddThemeStyleboxOverride(sn, flat);
            var hc = _scSec == sec ? UiTheme.Highlight : UiTheme.Accent.Lerp(UiTheme.Dim, .25f);
            foreach (var cn in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" }) hb.AddThemeColorOverride(cn, hc);
            hb.AddThemeFontSizeOverride("font_size", 15);
            var sl = Lbl(sub, 12, Dim, hr); sl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; sl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            if (sec == 1 && cards.Count > 0) hr.AddChild(Btn("Remove section", () => { _scWork.B.Clear(); _scSec = 0; OpenSpellcraft(null); }, true));
            mid.AddChild(new ColorRect { Color = (_scSec == sec ? UiTheme.Highlight : UiTheme.Accent) with { A = .45f }, CustomMinimumSize = new Vector2(0, 1) });
            var row = new HFlowContainer(); row.AddThemeConstantOverride("h_separation", 5); row.AddThemeConstantOverride("v_separation", 5); mid.AddChild(row);
            // carrier first, then the effect, then modifiers: the order they are read in
            var order = cards.Select((id, i) => (id, i)).OrderBy(x => Spellcraft.Card(x.id)?.Type switch { "carrier" => 0, "effect" => 1, _ => 2 }).ThenBy(x => x.i).ToList();
            foreach (var (id, i) in order)
            {
                var c = Spellcraft.Card(id); if (c == null) continue; int idx = i;
                var t = CardTile(c, ""); t.Selected = _scSec == sec;
                t.Click = () => { cards.RemoveAt(idx); _scSec = sec; OpenSpellcraft(null); };
                t.Hover = (on, at) => { if (on) Info(at, c.Name, Spellcraft.CardColor(c), CardLines(c, "Click to take it out.")); else HideInfo(); };
                row.AddChild(t);
            }
            int empty = Mathf.Clamp(maxCards - used, 0, 10); if (sec == 1 && cards.Count == 0) empty = Math.Min(empty, 3);
            for (int i = 0; i < (sec == _scSec ? empty : Math.Min(empty, 1)); i++)
            {
                var t = new SpellCardTile { Empty = true, Known = false, MaxRank = 0, CustomMinimumSize = new Vector2(56, 72), Tint = UiTheme.Accent, Label = i == 0 && cards.Count == 0 ? (sec == 0 ? "carrier" : "optional") : "" };
                t.Click = () => { _scSec = sec; OpenSpellcraft(null); };
                t.Hover = (on, at) => { if (on) Info(at, "Empty slot", UiTheme.Dim, new[] { ($"Click a card on the right to put it in the {(sec == 0 ? "first" : "second")} section.", Colors.White, 13) }); else HideInfo(); };
                row.AddChild(t);
            }
        }
        Section(0, "FIRST SECTION", "One carrier, one effect, any modifiers.");
        Section(1, "SECOND SECTION", "Optional: goes off where the first one ends, so a Missile into an Area makes a fireball. Its carrier must be " + string.Join(", ", Spellcraft.SecondCarriers.Select(x => Spellcraft.Card(x)?.Name ?? x)) + ".");

        // ---- the preview card
        var card = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; card.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(.02f, .06f, .12f, .7f), UiTheme.AccentSoft, 1, 14, 10, 0)); mid.AddChild(card);
        var cv = new VBoxContainer(); cv.AddThemeConstantOverride("separation", 4); card.AddChild(cv);
        var probe = new CraftedSpell { Id = _scEdit, Name = _scWork.Name, A = _scWork.A, B = _scWork.B };
        string? err = _scWork.A.Count == 0 ? "Put a carrier and an effect card in the first section." : Spellcraft.Check(d, probe);
        var pk = _scWork.A.Count > 0 ? Spellcraft.Preview(_scWork) : null;
        if (pk != null)
        {
            var c = Skills.Compute(pk.Id, _scEdit == "" ? null : _scEdit);
            Lbl(_scWork.Name.Trim() != "" ? _scWork.Name.Trim() : Spellcraft.AutoName(_scWork), 22, Dmg.TypeColor(c.Type).Lerp(Colors.White, .5f), cv);
            Row(cv, $"{Cap(fam)} spell  ·  {c.Type}  ·  {pk.Kind}" + (pk.Scale == "weapon" ? "  ·  scales with your weapon" : "  ·  scales with spell power") + (pk.Tags.Contains("bow") ? "  ·  needs a bow" : ""), Dim, 13);
            Row(cv, pk.Desc, Colors.White, 14);
            Row(cv, string.Join("   ", PreviewNums(c)), UiTheme.Highlight, 14);
            if (pk.Then != "" && Skills.Get(pk.Then) != null)
            {
                var f = Skills.Compute(pk.Then, _scEdit == "" ? null : _scEdit);
                Row(cv, "Second section:  " + string.Join("   ", PreviewNums(f).Where(x => !x.Contains("cooldown") && !x.Contains("mana") && !x.Contains("stamina"))), Dmg.TypeColor(f.Type).Lerp(Colors.White, .5f), 13);
            }
        }
        if (err != null) Row(cv, err, new Color(1f, .62f, .5f), 14);
        else if (_scEdit != "" && d.Skills.TryGetValue(_scEdit, out var st) && (st.Rank > 1 || st.Sup.Any(x => x != ""))) Row(cv, $"Keeps its rank ({st.Rank}) and socketed modifiers.", Dim, 12);

        var bot = new HBoxContainer(); bot.AddThemeConstantOverride("separation", 8); mid.AddChild(bot);
        var forge = Btn(_scEdit == "" ? "Forge spell" : "Save changes", () =>
        {
            bool fresh = _scEdit == ""; var e = Spellcraft.Forge(d, _scWork, _scEdit);
            if (e != null) { Note(e, new Color(1, .6f, .5f)); return; }
            _scEdit = _scWork.Id; var k = Skills.Get(_scEdit);
            int key = Array.IndexOf(d.Bar, _scEdit);
            Note((fresh ? "Forged " : "Re-forged ") + (k?.Name ?? "spell") + (key >= 0 ? $": key {Inputs.KeyOf("spell" + (key + 1))}" : fresh ? $" (hotbar full: set a key in the skills menu, {Inputs.KeyOf(Inputs.SkillsUi)})" : ""), UiTheme.Highlight);
            Sfx.Play("level"); Game.Notify(); Game.Save(); OpenSpellcraft(null);
        });
        forge.Disabled = err != null; bot.AddChild(forge);
        forge.CustomMinimumSize = new Vector2(150, 38); forge.AddThemeFontSizeOverride("font_size", 17);
        forge.AddThemeStyleboxOverride("normal", UiTheme.Box(new Color(.16f, .12f, .05f, .95f), UiTheme.Highlight, 2, 20, 8, 0));
        forge.AddThemeStyleboxOverride("hover", UiTheme.Box(new Color(.26f, .19f, .07f, .98f), UiTheme.Highlight.Lerp(Colors.White, .3f), 2, 20, 8, 0));
        forge.AddThemeStyleboxOverride("pressed", UiTheme.Box(new Color(.1f, .07f, .03f, .98f), UiTheme.Highlight, 2, 20, 8, 0));
        forge.AddThemeStyleboxOverride("disabled", UiTheme.Box(new Color(.08f, .07f, .05f, .9f), UiTheme.AccentSoft, 1, 20, 8, 0));
        forge.AddThemeColorOverride("font_color", UiTheme.Highlight); forge.AddThemeColorOverride("font_hover_color", Colors.White);
        bot.AddChild(Btn("Clear", () => { _scWork.A.Clear(); _scWork.B.Clear(); _scSec = 0; OpenSpellcraft(null); }));
        if (_scEdit != "")
        {
            string id = _scEdit;
            bot.AddChild(Btn(_scConfirm ? "Click again to take it apart" : "Take apart", () =>
            {
                if (!_scConfirm) { _scConfirm = true; OpenSpellcraft(null); return; }
                var nm = Skills.Get(id)?.Name ?? "spell"; Spellcraft.Dismantle(d, id); ScLoad("");
                Note($"Took {nm} apart: its cards are back in your collection", UiTheme.Highlight); Game.Notify(); Game.Save(); OpenSpellcraft(null);
            }));
        }

        // ---- right: the card collection
        var right = new VBoxContainer { CustomMinimumSize = new Vector2(390, 0) }; right.AddThemeConstantOverride("separation", 6); cols.AddChild(right);
        var tabs = new HBoxContainer(); tabs.AddThemeConstantOverride("separation", 4); right.AddChild(tabs);
        foreach (var (id, label) in new[] { ("carrier", "Carriers"), ("effect", "Effects"), ("modifier", "Modifiers") })
        {
            string tid = id; int have = Spellcraft.All.Where(c => c.Type == id).Sum(c => d.Cards.GetValueOrDefault(c.Id));
            var b = Btn($"{label} ({have})", () => { _scTab = tid; OpenSpellcraft(null); }, true); Mark(b, _scTab == id); b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; tabs.AddChild(b);
        }
        Lbl($"Click a card to add it to the {(_scSec == 0 ? "first" : "second")} section", 12, Dim, right);
        var cs = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; right.AddChild(cs);
        var cl = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; cl.AddThemeConstantOverride("separation", 6); cs.AddChild(cl);
        var groups = _scTab == "carrier" ? new[] { ("spell", "SPELLS   mana, spell power"), ("martial", "WEAPON ARTS   stamina, weapon damage"), ("archery", "ARCHERY   stamina, needs a bow") } : new[] { ("", "") };
        foreach (var (g, gl) in groups)
        {
            if (gl != "") Header(gl, cl);
            var grid = new GridContainer { Columns = 6 }; grid.AddThemeConstantOverride("h_separation", 5); grid.AddThemeConstantOverride("v_separation", 5); cl.AddChild(grid);
            foreach (var c in Spellcraft.All.Where(x => x.Type == _scTab && (g == "" || x.Family == g)))
            {
                var cd = c; int free = Spellcraft.Free(d, c.Id, _scEdit) - _scWork.A.Count(x => x == c.Id) - _scWork.B.Count(x => x == c.Id), own = d.Cards.GetValueOrDefault(c.Id);
                var t = CardTile(c, own == 0 ? "" : $"x{Math.Max(0, free)}");
                t.Known = own > 0; t.Locked = own > 0 && d.Level < c.Req; t.CanLearn = free > 0 && d.Level >= c.Req;
                t.Click = () =>
                {
                    if (own == 0) { Note($"You have no {cd.Name} cards yet. Cards drop from monsters, chests and bosses.", Dim); return; }
                    if (d.Level < cd.Req) { Note($"{cd.Name} needs level {cd.Req}", new Color(1, .6f, .5f)); return; }
                    if (free <= 0) { Note($"All your {cd.Name} cards are in use", new Color(1, .6f, .5f)); return; }
                    var list2 = _scSec == 0 ? _scWork.A : _scWork.B;
                    if (_scWork.A.Count + _scWork.B.Count >= Spellcraft.MaxCards(d, _scWork.A.Count > 0 || cd.Type != "carrier" ? Spellcraft.FamilyOf(_scWork) : cd.Family)) { Note("No free card slot: take a card out first", new Color(1, .6f, .5f)); return; }
                    if (list2.Count(x => x == cd.Id) >= Spellcraft.MaxCopies(d)) { Note($"At most {Spellcraft.MaxCopies(d)} copies of one card per section at your level", new Color(1, .6f, .5f)); return; }
                    list2.Add(cd.Id); OpenSpellcraft(null);
                };
                t.Hover = (on, at) => { if (on) Info(at, cd.Name, Spellcraft.CardColor(cd), CardLines(cd, own == 0 ? "Not found yet." : $"You have {own}, {Math.Max(0, free)} free.")); else HideInfo(); };
                grid.AddChild(t);
            }
        }
    }

    // ---------------------------------------------------------------- the spell tree (its own points; only crafted spells feel it)
    static bool _scTree, _scTreeReset;
    void OpenSpellTree()
    {
        var d = Game.D;
        var v = Frame("spellcraft", "Spell tree", new Vector2(1180, 680), OpenSpellTree); HideInfo();
        var top = new HBoxContainer(); v.AddChild(top); top.AddThemeConstantOverride("separation", 8);
        top.AddChild(Btn("<  Back to Spellcraft", () => { _scTree = false; _scTreeReset = false; OpenSpellcraft(null); }, true));
        int free = Spellcraft.TreeFree(d);
        Chip(top, $"Points  {free} / {Spellcraft.TreePoints(d)}", free > 0);
        var help = Lbl("Spell tree points come with your level. Every rank only strengthens the spells you forge in Spellcraft.", 13, Dim, top);
        help.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; help.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        var rs = Btn(_scTreeReset ? "Click again to reset" : "Reset (free)", () =>
        {
            if (!_scTreeReset) { _scTreeReset = true; OpenSpellTree(); return; }
            _scTreeReset = false; Spellcraft.ResetTree(d); Note("Spell tree reset: all points are back", UiTheme.Highlight); Game.Notify(); Game.Save(); OpenSpellTree();
        }, true);
        rs.Disabled = Spellcraft.TreeSpent(d) == 0; top.AddChild(rs);

        var cols = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; cols.AddThemeConstantOverride("separation", 16); v.AddChild(cols);
        foreach (var (bid, bname) in Spellcraft.TreeBranches)
        {
            var col = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            col.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(.02f, .06f, .12f, .7f), UiTheme.AccentSoft, 1, 14, 10, 0)); cols.AddChild(col);
            var cv = new VBoxContainer(); cv.AddThemeConstantOverride("separation", 8); col.AddChild(cv);
            var hr = new HBoxContainer(); cv.AddChild(hr);
            var hl = Lbl(bname, 18, UiTheme.Accent, hr); hl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            Lbl($"{Spellcraft.TreeSpentIn(d, bid)} spent", 13, Dim, hr);
            foreach (var n in Spellcraft.TreeNodes.Where(x => x.Branch == bid))
            {
                var node = n; int r = Spellcraft.Rank(d, n.Id); string? why = Spellcraft.CanBuy(d, n);
                bool full = r >= n.Max, open = why == null;
                var box = new PanelContainer();
                box.AddThemeStyleboxOverride("panel", UiTheme.Box(r > 0 ? UiTheme.ButtonHover : UiTheme.Button, full ? UiTheme.Highlight : open ? UiTheme.Accent : UiTheme.AccentSoft, full || open ? 2 : 1, 12, 8, 0));
                cv.AddChild(box);
                var bv = new VBoxContainer(); bv.AddThemeConstantOverride("separation", 2); box.AddChild(bv);
                var nr = new HBoxContainer(); bv.AddChild(nr);
                var nl = Lbl(n.Name, 16, r > 0 ? Colors.White : Dim.Lerp(Colors.White, .4f), nr); nl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                Lbl($"{r}/{n.Max}", 15, full ? UiTheme.Highlight : r > 0 ? UiTheme.Accent : Dim, nr);
                Row(bv, r > 0 ? n.Text(r) : n.Text(1), r > 0 ? Colors.White : Dim, 13);
                if (r > 0 && !full) Row(bv, "Next rank: " + n.Text(r + 1), UiTheme.Dim, 12);
                var br = new HBoxContainer(); bv.AddChild(br);
                if (!full)
                {
                    var b = Btn(r == 0 ? "Learn" : "Rank up", () =>
                    {
                        var e = Spellcraft.Buy(d, node);
                        if (e != null) { Note(e, new Color(1, .6f, .5f)); return; }
                        Sfx.Play("level"); Game.Notify(); Game.Save(); OpenSpellTree();
                    }, true);
                    b.Disabled = !open; br.AddChild(b);
                    if (!open) { var wl = Lbl(why!, 12, new Color(1f, .7f, .55f), br); wl.AutowrapMode = TextServer.AutowrapMode.WordSmart; wl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; }
                }
            }
        }
        var foot = Row(v, $"One point every {Spellcraft.TreeEvery} levels. Changes apply to your forged spells at once; resetting is free.", Dim, 12);
        foot.HorizontalAlignment = HorizontalAlignment.Center;
    }

    static SpellCardTile CardTile(CardDef c, string corner) => new()
    {
        Icon = Spellcraft.CardIcon(c), Tint = Spellcraft.CardColor(c), Known = true, MaxRank = 0, Corner = corner, CustomMinimumSize = new Vector2(56, 72),
        Band = SpellCardTile.BandFor(c), Label = c.Name,
    };

    /// <summary>Gold small-caps section label with a thin divider line under it.</summary>
    Label Header(string text, Control parent)
    {
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 2); parent.AddChild(box);
        var l = Lbl(text.ToUpperInvariant(), 14, UiTheme.Accent, box);
        box.AddChild(new ColorRect { Color = UiTheme.Accent with { A = .45f }, CustomMinimumSize = new Vector2(0, 1) });
        return l;
    }
    static List<(string, Color, int)> CardLines(CardDef c, string foot)
    {
        var l = new List<(string, Color, int)>();
        string kind = c.Type == "carrier" ? $"Carrier  ·  {c.Family}" : c.Type == "effect" ? $"Effect  ·  {c.EType}" : "Modifier";
        l.Add((kind + (c.Req > 1 ? $"  ·  level {c.Req}" : ""), Spellcraft.CardColor(c).Lerp(UiTheme.Dim, .3f), 12));
        l.Add((c.Desc, Colors.White, 14));
        if (c.Hint != "") l.Add((c.Hint, UiTheme.Dim, 12));
        else if (c.Type == "effect") l.Add(("Each extra copy: more damage, statuses last longer.", UiTheme.Dim, 12));
        else if (c.Type == "modifier") l.Add(("Extra copies stack.", UiTheme.Dim, 12));
        if (c.Type == "effect" && c.Status.Count > 0) l.Add(("Inflicts: " + string.Join(", ", c.Status.Keys.Select(s => Dmg.Status(s)?.Name ?? s)), UiTheme.Dim, 12));
        l.Add((foot, UiTheme.Highlight, 12));
        return l;
    }
    static List<string> PreviewNums(SkillCalc c)
    {
        var k = c.Def; var n = new List<string>();
        bool hits = k.Kind is not ("enchant" or "buff" or "ward" or "heal");
        if (hits) n.Add($"{c.Dmg:0} damage" + (c.Count > 1 ? $" x{c.Count}" : ""));
        if (k.Kind == "ward") n.Add($"shield {c.Heal * 100:0}% health");
        if (k.Kind == "enchant" && c.Heal > 0) n.Add($"heals {c.Heal * 100:0}%");
        if (c.Radius > 0) n.Add($"{c.Radius:0.#} m radius");
        if (c.Dur > 0) n.Add($"{c.Dur:0.#} s");
        if (c.Chain > 0) n.Add($"chains {c.Chain}"); if (c.Pierce > 0) n.Add($"pierces {c.Pierce}"); if (c.Homing) n.Add("seeks");
        if (c.Leech > 0) n.Add($"{c.Leech:0}% life steal");
        foreach (var s in c.Status.Select(x => x.id).Distinct()) n.Add(Dmg.Status(s)?.Name ?? s);
        foreach (var (b, val) in k.Buff) n.Add(TreeDb.StatText(b, val));
        if (c.Mp > 0) n.Add($"{c.Mp:0} mana"); if (c.St > 0) n.Add($"{c.St:0} stamina"); n.Add($"{c.Cd:0.#} s cooldown");
        return n;
    }
}

/// <summary>A spellcraft card: portrait frame, family-coloured top band, centred icon, short name, optional count badge; or a dashed empty slot.</summary>
public partial class SpellCardTile : SkillTile
{
    public Color Band = UiTheme.Accent; public string Label = ""; public bool Empty;
    bool _hov;
    static Font F => ThemeDB.FallbackFont;

    public static Color BandFor(CardDef c)
    {
        if (c.Type == "carrier") return c.Family == "martial" ? Color.FromHtml("#7d8590") : c.Family == "archery" ? Color.FromHtml("#5b7d34") : Color.FromHtml("#26408f");
        if (c.Type == "effect")
        {
            if (c.Id == "life") return Color.FromHtml("#3f9a4a");
            if (c.Id == "stone") return Color.FromHtml("#7a5a3a");
            return c.EType switch
            {
                "fire" => Color.FromHtml("#c8501e"), "frost" => Color.FromHtml("#9fe0ee"), "lightning" => Color.FromHtml("#b8a8ff"),
                "poison" => Color.FromHtml("#7aa628"), "holy" => Color.FromHtml("#e8d68a"), "shadow" => Color.FromHtml("#4a2a6a"),
                _ => Color.FromHtml("#8a8f96"),
            };
        }
        return Color.FromHtml("#b89a4a");
    }

    public override void _Ready()
    {
        base._Ready();
        MouseEntered += () => { _hov = true; QueueRedraw(); };
        MouseExited += () => { _hov = false; QueueRedraw(); };
    }

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        if (Empty)
        {
            DrawRect(r, new Color(0, 0, 0, _hov ? .28f : .16f));
            var col = (_hov ? UiTheme.Accent : UiTheme.AccentSoft) with { A = _hov ? .9f : .55f };
            var a = new Vector2(1, 1); var b = new Vector2(Size.X - 1, 1); var c = Size - new Vector2(1, 1); var d = new Vector2(1, Size.Y - 1);
            DrawDashedLine(a, b, col, 1.2f, 4); DrawDashedLine(b, c, col, 1.2f, 4); DrawDashedLine(c, d, col, 1.2f, 4); DrawDashedLine(d, a, col, 1.2f, 4);
            var cx = Size.X / 2; var cy = Size.Y * .42f;
            DrawLine(new Vector2(cx - 8, cy), new Vector2(cx + 8, cy), col, 2f); DrawLine(new Vector2(cx, cy - 8), new Vector2(cx, cy + 8), col, 2f);
            if (Label != "") DrawString(F, new Vector2(0, Size.Y - 7), Label, HorizontalAlignment.Center, Size.X, 10, UiTheme.Dim);
            return;
        }
        float dim = Locked ? .45f : Known ? 1f : .55f;
        var frame = new StyleBoxFlat { BgColor = new Color(.05f, .045f, .04f, .95f), CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4 };
        var bc = Selected ? UiTheme.Highlight : _hov ? UiTheme.Highlight.Lerp(Colors.White, .35f) : CanLearn ? UiTheme.Highlight with { A = .75f } : Known ? UiTheme.Accent with { A = .8f } : UiTheme.AccentSoft with { A = .4f };
        int bw = Selected || _hov ? 2 : 1;
        frame.BorderColor = bc; frame.BorderWidthLeft = frame.BorderWidthRight = frame.BorderWidthTop = frame.BorderWidthBottom = bw;
        DrawStyleBox(frame, r);
        // dark inset and family band
        DrawRect(new Rect2(3, 14, Size.X - 6, Size.Y - 28), new Color(0, 0, 0, .45f));
        var band = Band with { A = dim * (_hov ? 1f : .85f) };
        DrawRect(new Rect2(bw, bw, Size.X - 2 * bw, 12), band);
        DrawRect(new Rect2(bw, 12 + bw, Size.X - 2 * bw, 1), Colors.Black with { A = .5f });
        if (_hov) DrawRect(new Rect2(bw, bw, Size.X - 2 * bw, Size.Y - 2 * bw), Colors.White with { A = .07f });
        if (Icon != "") Icons.Draw(this, Icon, new Vector2(Size.X / 2, 14 + (Size.Y - 28) / 2), 32, dim);
        // short name
        int fs = 10; string s = Label;
        while (s.Length > 2 && F.GetStringSize(s, HorizontalAlignment.Left, -1, fs).X > Size.X - 4) s = s[..^1];
        if (s != Label) s = s.TrimEnd() + ".";
        DrawString(F, new Vector2(0, Size.Y - 5), s, HorizontalAlignment.Center, Size.X, fs, (Known ? new Color(.92f, .87f, .74f) : UiTheme.Dim) with { A = dim });
        // count badge
        if (Corner != "")
        {
            float w = F.GetStringSize(Corner, HorizontalAlignment.Left, -1, 11).X + 8;
            var br = new Rect2(Size.X - w - 2, 15, w, 15);
            var badge = new StyleBoxFlat { BgColor = new Color(0, 0, 0, .8f), BorderColor = (CanLearn ? UiTheme.Highlight : UiTheme.AccentSoft), CornerRadiusTopLeft = 7, CornerRadiusTopRight = 7, CornerRadiusBottomLeft = 7, CornerRadiusBottomRight = 7 };
            badge.BorderWidthLeft = badge.BorderWidthRight = badge.BorderWidthTop = badge.BorderWidthBottom = 1;
            DrawStyleBox(badge, br);
            DrawString(F, new Vector2(br.Position.X, br.Position.Y + 12), Corner, HorizontalAlignment.Center, w, 11, CanLearn ? UiTheme.Highlight : UiTheme.Dim);
        }
    }
}
