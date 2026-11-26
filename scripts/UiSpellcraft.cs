using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TwinRealms;

/// <summary>
/// Spellcraft menu (B): build skills from spell cards, as with the amulet in Two Worlds II. A fixed-size window (fits 1280x720; nothing
/// grows or shifts, the lists scroll). Top: the spell being built as up to three sections (I, II, III) side by side, each with a Carrier
/// slot, an Effect slot and a row of Modifier slots, linked by "then" arrows (each section goes off where the one before it ends).
/// Bottom: the card library (Carriers / Effects / Modifiers tabs and a search box), filtered for the selected slot: cards that fit are
/// gold, the others grey with the reason on hover. Click a slot, then a card: it goes in and the selection moves to the next empty slot.
/// Right: saved spells, the name, live numbers as bars per section, Forge / Clear / Take apart. Every slot and card is a focusable button
/// (controller). Rules and numbers in Spellcraft.cs and data/spellcards.json.
/// </summary>
public partial class Ui
{
    static CraftedSpell _scWork = new();
    static string _scEdit = "", _scTab = "carrier", _scSearch = "";
    static int _scSec = 0, _scSlot = 0; static bool _scConfirm;
    static readonly string[] ScTypes = { "carrier", "effect", "modifier" };
    const float ScW = 1220, ScH = 660, ScLeftW = 848, ScArrowW = 40, ScBuildH = 246, ScRightW = 312;
    static readonly Color ScErr = new(1f, .62f, .5f);

    static string ScR(int i) => Spellcraft.Roman(i);

    static void ScLoad(string id)
    {
        var s = id == "" ? null : Game.D.Spells.FirstOrDefault(x => x.Id == id);
        _scWork = s == null ? new CraftedSpell() : s.Copy();
        _scWork.Sec(Spellcraft.MaxSections - 1);
        _scEdit = s?.Id ?? ""; _scSec = 0; _scConfirm = false; ScAdvance();
    }
    /// <summary>Moves the selection to the next empty slot of the selected section (carrier, then effect, then modifiers).</summary>
    static void ScAdvance()
    {
        var l = _scWork.Sec(_scSec);
        _scSlot = !l.Any(x => Spellcraft.Card(x)?.Type == "carrier") ? 0 : !l.Any(x => Spellcraft.Card(x)?.Type == "effect") ? 1 : 2;
        _scTab = ScTypes[_scSlot];
    }
    static int ScFree(PlayerStats d, string id) => Spellcraft.Free(d, id, _scEdit) - _scWork.AllCards.Count(x => x == id);

    /// <summary>Why card c cannot go into section sec of the spell being built, or null when it can.</summary>
    static string? ScWhy(PlayerStats d, CardDef c, int sec)
    {
        int own = d.Cards.GetValueOrDefault(c.Id);
        if (own == 0) return "Not found yet: cards drop from monsters, chests and bosses.";
        if (d.Level < c.Req) return $"Needs level {c.Req}.";
        int open = Spellcraft.SectionsOpen(d);
        if (sec >= open) return $"Section {ScR(sec)} opens at level {Spellcraft.SectionLevel(sec)}.";
        for (int i = 0; i < sec; i++) if (_scWork.Sec(i).Count == 0) return $"Fill section {ScR(i)} first.";
        var list = _scWork.Sec(sec);
        if (c.Type == "carrier" && sec > 0 && !Spellcraft.SecondCarriers.Contains(c.Id))
            return "Cannot be a chained section. Use " + string.Join(", ", Spellcraft.SecondCarriers.Select(x => Spellcraft.Card(x)?.Name ?? x)) + ".";
        if (ScFree(d, c.Id) <= 0) return $"All your {c.Name} cards are in use.";
        bool swap = c.Type is "carrier" or "effect" && list.Any(x => x != c.Id && Spellcraft.Card(x)?.Type == c.Type);
        if (swap) return null;
        int mc = Spellcraft.MaxCopies(d);
        if (list.Count(x => x == c.Id) >= mc) return $"At most {mc} copies of one card per section at your level.";
        bool noCarrier = !_scWork.Sec(0).Any(x => Spellcraft.Card(x)?.Type == "carrier");
        string fam = sec == 0 && c.Type == "carrier" && noCarrier ? c.Family : Spellcraft.FamilyOf(_scWork);
        if (_scWork.Count >= Spellcraft.MaxCards(d, fam)) return "No free card slot: take a card out first.";
        return null;
    }
    void ScPlace(PlayerStats d, CardDef c)
    {
        var why = ScWhy(d, c, _scSec); if (why != null) { Note(why, ScErr); return; }
        var list = _scWork.Sec(_scSec);
        if (c.Type is "carrier" or "effect") list.RemoveAll(x => x != c.Id && Spellcraft.Card(x)?.Type == c.Type);   // a different carrier / effect swaps in
        list.Add(c.Id); ScAdvance(); OpenSpellcraft(null);
    }

    /// <summary>A card tile wrapped in a flat button, so a controller can focus it (A presses it); x = take it out.</summary>
    Button ScTile(Control parent, SpellCardTile t, Vector2 size, Action click, Action? remove, string title, Color tc, List<(string, Color, int)> lines)
    {
        var b = new Button { CustomMinimumSize = size, FocusMode = Control.FocusModeEnum.All, MouseFilter = Control.MouseFilterEnum.Ignore };
        var flat = new StyleBoxEmpty();
        foreach (var sn in new[] { "normal", "hover", "pressed", "disabled" }) b.AddThemeStyleboxOverride(sn, flat);
        b.AddThemeStyleboxOverride("focus", PadFocusBox());
        t.CustomMinimumSize = size; t.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); t.Click = click;
        t.Hover = (on, at) => { if (on) Info(at, title, tc, lines); else HideInfo(); };
        if (remove != null) t.RightClick = remove;
        b.AddChild(t);
        b.Pressed += () => { Sfx.Play("click"); click(); };
        b.FocusEntered += () => { if (IsInstanceValid(t)) Info(t.GetGlobalRect().Position + new Vector2(t.Size.X + 8, 0), title, tc, lines); };
        b.FocusExited += HideInfo;
        if (remove != null)
        {
            var x = Btn("x", remove, true); x.TooltipText = "Take it out"; x.AddThemeFontSizeOverride("font_size", 11);
            x.AddThemeStyleboxOverride("normal", UiTheme.Box(new Color(.08f, .03f, .02f, .92f), ScErr with { A = .7f }, 1, 4, 0));
            x.AddThemeStyleboxOverride("hover", UiTheme.Box(new Color(.25f, .06f, .04f, .95f), ScErr, 1, 4, 0));
            x.AddThemeStyleboxOverride("pressed", UiTheme.Box(new Color(.25f, .06f, .04f, .95f), ScErr, 1, 4, 0));
            x.AddThemeStyleboxOverride("focus", PadFocusBox());
            x.AnchorLeft = 1; x.AnchorRight = 1; x.OffsetLeft = -18; x.OffsetRight = 0; x.OffsetTop = 0; x.OffsetBottom = 18; x.CustomMinimumSize = new Vector2(18, 18);
            b.AddChild(x);
        }
        parent.AddChild(b); return b;
    }

    /// <summary>Opens the menu; edit = a crafted spell's id to load it, "" for a new spell, null to keep what was being built.</summary>
    public void OpenSpellcraft(string? edit)
    {
        var d = Game.D; Skills.GrantStarting(d);
        if (_scTree) { OpenSpellTree(); return; }
        if (edit != null) ScLoad(edit);
        if (_scEdit != "" && !d.Spells.Exists(x => x.Id == _scEdit)) ScLoad("");
        int nSec = Spellcraft.MaxSections, open = Spellcraft.SectionsOpen(d); _scWork.Sec(nSec - 1);
        _scSec = Mathf.Clamp(_scSec, 0, Math.Min(nSec, open) - 1); _scSlot = Mathf.Clamp(_scSlot, 0, 2);
        // fixed-size window: every zone has a fixed footprint, the library and the preview scroll
        var v = Frame("spellcraft", "Spellcraft", new Vector2(ScW, ScH), () => OpenSpellcraft(null)); HideInfo();
        string fam = _scWork.Sec(0).Count > 0 ? Spellcraft.FamilyOf(_scWork) : "spell";
        int maxCards = Spellcraft.MaxCards(d, fam), maxCopies = Spellcraft.MaxCopies(d), used = _scWork.Count;

        // ---- top bar
        var top = new HBoxContainer { CustomMinimumSize = new Vector2(0, 32) }; v.AddChild(top); top.AddThemeConstantOverride("separation", 8);
        Chip(top, $"Spells  {d.Spells.Count}/{Spellcraft.SpellCap(d)}", false);
        Chip(top, $"Cards  {used}/{maxCards}", used > maxCards);
        Chip(top, $"Copies per section  {maxCopies}", false);
        Chip(top, $"Sections  {open}/{nSec}", false);
        var help = Lbl("Pick a slot, then click a card below. Each section goes off where the one before it ends.", 13, Dim, top);
        help.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; help.ClipText = true; help.VerticalAlignment = VerticalAlignment.Center;
        int tfree = Spellcraft.TreeFree(d);
        var tb = Btn(tfree > 0 ? $"Spell tree  ({tfree} to spend)" : "Spell tree", () => { _scTree = true; OpenSpellTree(); }, true);
        if (tfree > 0) tb.AddThemeColorOverride("font_color", UiTheme.Highlight);
        top.AddChild(tb);
        top.AddChild(Btn($"Skills ({Inputs.KeyOf(Inputs.SkillsUi)})", OpenSkills, true));

        var body = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; body.AddThemeConstantOverride("separation", 12); v.AddChild(body);
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(ScLeftW, 0) }; left.AddThemeConstantOverride("separation", 8); body.AddChild(left);

        // ---- the spell being built: sections side by side, linked by arrows
        var build = new HBoxContainer { CustomMinimumSize = new Vector2(ScLeftW, ScBuildH) }; build.AddThemeConstantOverride("separation", 0); left.AddChild(build);
        float secW = Mathf.Floor((ScLeftW - ScArrowW * (nSec - 1)) / nSec);
        for (int s = 0; s < nSec; s++)
        {
            if (s > 0) build.AddChild(new SpellArrow { CustomMinimumSize = new Vector2(ScArrowW, ScBuildH), Lit = _scWork.Sec(s).Count > 0 && _scWork.Sec(s - 1).Count > 0, Locked = s >= open });
            ScSection(build, d, s, secW, open);
        }

        // ---- the card library
        ScLibrary(left, d);

        // ---- right: saved spells, name, preview, forge
        ScRight(body, d, fam);
    }

    void ScSection(Control parent, PlayerStats d, int sec, float w, int open)
    {
        bool locked = sec >= open, sel = _scSec == sec && !locked; var cards = _scWork.Sec(sec);
        var pc = new PanelContainer { CustomMinimumSize = new Vector2(w, ScBuildH), ClipContents = true }; parent.AddChild(pc);
        pc.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(.035f, .03f, .025f, locked ? .55f : .85f), sel ? UiTheme.Highlight : UiTheme.AccentSoft, sel ? 2 : 1, 8, 6, 0));
        var sv = new VBoxContainer(); sv.AddThemeConstantOverride("separation", 4); pc.AddChild(sv);
        var hr = new HBoxContainer { CustomMinimumSize = new Vector2(0, 26) }; hr.AddThemeConstantOverride("separation", 4); sv.AddChild(hr);
        var hb = Btn($"SECTION {ScR(sec)}", () => { _scSec = sec; ScAdvance(); OpenSpellcraft(null); }, true); hr.AddChild(hb);
        var flat = new StyleBoxEmpty();
        foreach (var sn in new[] { "normal", "hover", "pressed", "disabled" }) hb.AddThemeStyleboxOverride(sn, flat);
        hb.AddThemeStyleboxOverride("focus", PadFocusBox());
        var hc = locked ? UiTheme.Dim : sel ? UiTheme.Highlight : UiTheme.Accent.Lerp(UiTheme.Dim, .25f);
        foreach (var cn in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_disabled_color" }) hb.AddThemeColorOverride(cn, hc);
        hb.AddThemeFontSizeOverride("font_size", 15); hb.Disabled = locked;
        var sub = Lbl(sec == 0 ? "cast" : $"where {ScR(sec - 1)} ends", 11, Dim, hr); sub.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; sub.ClipText = true; sub.VerticalAlignment = VerticalAlignment.Center;
        if (!locked && sec > 0 && cards.Count > 0)
        {
            var xb = Btn("x", () => { cards.Clear(); _scSec = sec; ScAdvance(); OpenSpellcraft(null); }, true); xb.TooltipText = "Empty this section"; xb.CustomMinimumSize = new Vector2(24, 22); hr.AddChild(xb);
        }
        if (locked)
        {
            var lc = new CenterContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; sv.AddChild(lc);
            var lv = new VBoxContainer(); lv.AddThemeConstantOverride("separation", 4); lc.AddChild(lv);
            foreach (var (t, sz, col) in new[] { ("LOCKED", 16, Amber), ($"Opens at level {Spellcraft.SectionLevel(sec)}", 14, Parch), ($"You are level {d.Level}", 12, UiTheme.Dim) })
            { var l = Lbl(t, sz, col, lv); l.HorizontalAlignment = HorizontalAlignment.Center; }
            var hint = Lbl($"One more link in the chain: goes off where section {ScR(sec - 1)} ends.", 11, UiTheme.Dim, lv);
            hint.HorizontalAlignment = HorizontalAlignment.Center; hint.AutowrapMode = TextServer.AutowrapMode.WordSmart; hint.CustomMinimumSize = new Vector2(w - 30, 0);
            return;
        }
        // carrier + effect slots, and the section's state beside them
        var sr = new HBoxContainer(); sr.AddThemeConstantOverride("separation", 8); sv.AddChild(sr);
        ScSlot(sr, sec, 0, cards); ScSlot(sr, sec, 1, cards);
        var sb = new Control { CustomMinimumSize = new Vector2(0, 100), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipContents = true }; sr.AddChild(sb);
        string? err = cards.Count == 0 ? null : Spellcraft.CheckSection(cards, sec);
        string st = cards.Count == 0 ? (sec == 0 ? "Start here: a carrier (how), then an effect (what)." : $"Optional: goes off where section {ScR(sec - 1)} ends.") : err ?? $"Ready.  {cards.Count} card{(cards.Count == 1 ? "" : "s")}.";
        var sl = Lbl(st, 11, cards.Count == 0 ? UiTheme.Dim : err != null ? ScErr : UiTheme.Highlight, sb);
        sl.AutowrapMode = TextServer.AutowrapMode.WordSmart; sl.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); sl.OffsetTop = 16;
        // modifiers
        var mods = cards.Where(x => Spellcraft.Card(x)?.Type == "modifier").ToList();
        bool msel = sel && _scSlot == 2;
        Lbl($"MODIFIERS  {mods.Count}", 11, msel ? UiTheme.Highlight : UiTheme.Accent.Lerp(UiTheme.Dim, .3f), sv);
        var ms = new ScrollContainer { CustomMinimumSize = new Vector2(0, 70), VerticalScrollMode = ScrollContainer.ScrollMode.Disabled, HorizontalScrollMode = ScrollContainer.ScrollMode.Auto }; sv.AddChild(ms);
        var mr = new HBoxContainer(); mr.AddThemeConstantOverride("separation", 4); ms.AddChild(mr);
        var msz = new Vector2(44, 58);
        Action pick = () => { _scSec = sec; _scSlot = 2; _scTab = "modifier"; OpenSpellcraft(null); };
        foreach (var id in mods)
        {
            var c = Spellcraft.Card(id)!; string mid = id;
            var t = CardTile(c, ""); t.Selected = msel;
            ScTile(mr, t, msz, pick, () => { cards.Remove(mid); _scSec = sec; _scSlot = 2; _scTab = "modifier"; OpenSpellcraft(null); }, c.Name, Spellcraft.CardColor(c), CardLines(c, "x (or right-click) takes it out."));
        }
        var et = new SpellCardTile { Empty = true, Known = false, MaxRank = 0, Tint = UiTheme.Accent, Label = "add\nmodifier", Selected = msel };
        ScTile(mr, et, msz, pick, null, "Modifier slot", UiTheme.Dim, new() { ("Drop a modifier here: pick this slot, then click a modifier card below. Any number, as card slots allow.", Colors.White, 13) });
    }

    void ScSlot(Control parent, int sec, int slot, List<string> cards)
    {
        string type = ScTypes[slot]; bool sel = _scSec == sec && _scSlot == slot;
        var v = new VBoxContainer(); v.AddThemeConstantOverride("separation", 2); parent.AddChild(v);
        Lbl(slot == 0 ? "CARRIER" : "EFFECT", 11, sel ? UiTheme.Highlight : UiTheme.Accent.Lerp(UiTheme.Dim, .3f), v);
        var size = new Vector2(64, 84);
        Action select = () => { _scSec = sec; _scSlot = slot; _scTab = type; OpenSpellcraft(null); };
        var id = cards.FirstOrDefault(x => Spellcraft.Card(x)?.Type == type);
        if (id == null)
        {
            var t = new SpellCardTile { Empty = true, Known = false, MaxRank = 0, Tint = UiTheme.Accent, Label = $"Drop a\n{type}\nhere", Selected = sel };
            string what = slot == 0 ? (sec > 0 ? "How it is delivered. Chained sections take: " + string.Join(", ", Spellcraft.SecondCarriers.Select(x => Spellcraft.Card(x)?.Name ?? x)) + "." : "How the spell is delivered.") : "What it does: damage type, statuses, buffs.";
            ScTile(v, t, size, select, null, $"Empty {type} slot", UiTheme.Dim, new() { ($"Drop a {type} here: pick this slot, then click a {type} card below.", Colors.White, 13), (what, UiTheme.Dim, 12) });
            return;
        }
        var c = Spellcraft.Card(id)!; int n = cards.Count(x => x == id); string cid = id;
        var ct = CardTile(c, n > 1 ? $"x{n}" : ""); ct.Selected = sel;
        ScTile(v, ct, size, select, () => { cards.Remove(cid); _scSec = sec; _scSlot = slot; _scTab = type; OpenSpellcraft(null); }, c.Name, Spellcraft.CardColor(c),
            CardLines(c, n > 1 ? $"{n} copies. x takes one out; click a {c.Name} card below for another copy." : $"x takes it out. Click another {type} below to swap it."));
    }

    void ScLibrary(Control parent, PlayerStats d)
    {
        var lib = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; parent.AddChild(lib);
        lib.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(.03f, .025f, .02f, .7f), UiTheme.AccentSoft, 1, 10, 8, 0));
        var lv = new VBoxContainer(); lv.AddThemeConstantOverride("separation", 6); lib.AddChild(lv);
        var tr = new HBoxContainer(); tr.AddThemeConstantOverride("separation", 4); lv.AddChild(tr);
        for (int i = 0; i < ScTypes.Length; i++)
        {
            string tid = ScTypes[i]; int idx = i; string label = new[] { "Carriers", "Effects", "Modifiers" }[i];
            int have = Spellcraft.All.Where(c => c.Type == tid).Sum(c => d.Cards.GetValueOrDefault(c.Id));
            var b = Btn($"{label} ({have})", () => { _scTab = tid; _scSlot = idx; OpenSpellcraft(null); }, true); Mark(b, _scTab == tid); b.CustomMinimumSize = new Vector2(132, 30); tr.AddChild(b);
        }
        var se = new LineEdit { Text = _scSearch, PlaceholderText = "Search cards", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 30), ClearButtonEnabled = true, MaxLength = 24 };
        tr.AddChild(se);
        string slotName = _scTab == "carrier" ? "carrier" : _scTab == "effect" ? "effect" : "modifiers";
        var hl = Lbl($"Click a card to put it in section {ScR(_scSec)} ({slotName}).  Gold: it fits.  Grey: it cannot (hover for why).", 12, Dim, lv); hl.ClipText = true;
        var cs = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; lv.AddChild(cs);
        var grid = new GridContainer { Columns = 13 }; grid.AddThemeConstantOverride("h_separation", 5); grid.AddThemeConstantOverride("v_separation", 5); cs.AddChild(grid);
        void Fill()
        {
            HideInfo();
            foreach (var ch in grid.GetChildren()) { grid.RemoveChild(ch); ch.QueueFree(); }
            string q = _scSearch.Trim().ToLowerInvariant();
            var pool = Spellcraft.All.Where(x => x.Type == _scTab && (q == "" || x.Name.ToLowerInvariant().Contains(q) || x.Desc.ToLowerInvariant().Contains(q) || x.Family.Contains(q) || x.EType.Contains(q)))
                .Select(x => (c: x, why: ScWhy(d, x, _scSec))).OrderBy(x => x.why == null ? 0 : d.Cards.GetValueOrDefault(x.c.Id) > 0 ? 1 : 2).ToList();
            foreach (var (c, why) in pool)
            {
                var cd = c; int own = d.Cards.GetValueOrDefault(c.Id), free = Math.Max(0, ScFree(d, c.Id));
                var t = CardTile(c, own == 0 ? "" : $"x{free}");
                t.Known = own > 0; t.Locked = own > 0 && why != null; t.CanLearn = why == null;
                var lines = CardLines(c, why == null ? $"Click to put it in section {ScR(_scSec)}. You have {own}, {free} free." : why);
                if (why != null) lines[^1] = (why, ScErr, 12);
                ScTile(grid, t, new Vector2(56, 72), () => ScPlace(d, cd), null, c.Name, Spellcraft.CardColor(c), lines);
            }
            if (pool.Count == 0) Lbl(q == "" ? "No cards of this kind." : "No card matches that search.", 12, Dim, grid);
        }
        se.TextChanged += t => { _scSearch = t; Fill(); };
        Fill();
    }

    void ScRight(Control parent, PlayerStats d, string fam)
    {
        var right = new VBoxContainer { CustomMinimumSize = new Vector2(ScRightW, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; right.AddThemeConstantOverride("separation", 6); parent.AddChild(right);
        // saved spells
        Lbl("LOAD SAVED SPELL", 11, UiTheme.Accent, right);
        var ob = new OptionButton { CustomMinimumSize = new Vector2(0, 30), ClipText = true };
        ob.AddItem("+  New spell", 0); int pick = 0;
        for (int i = 0; i < d.Spells.Count; i++)
        {
            var s = d.Spells[i]; int key = Array.IndexOf(d.Bar, s.Id);
            ob.AddItem((Skills.Get(s.Id)?.Name ?? Spellcraft.AutoName(s)) + (key >= 0 ? $"   [{key + 1}]" : ""), i + 1);
            if (s.Id == _scEdit) pick = i + 1;
        }
        ob.Selected = pick;
        ob.ItemSelected += i => { ScLoad(i <= 0 || i > d.Spells.Count ? "" : d.Spells[(int)i - 1].Id); OpenSpellcraft(null); };
        right.AddChild(ob);
        // name
        var nr = new HBoxContainer(); nr.AddThemeConstantOverride("separation", 6); right.AddChild(nr);
        Lbl("NAME", 12, UiTheme.Accent, nr);
        var name = new LineEdit { Text = _scWork.Name, PlaceholderText = Spellcraft.AutoName(_scWork), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 30), MaxLength = 32 };
        name.TextChanged += t => _scWork.Name = t; nr.AddChild(name);
        {   // the spell's icon: a preview of it and the button that opens the icon window (library | editor | import)
            string cur = _scWork.Icon != "" ? _scWork.Icon : _scWork.Sec(0).Count > 0 && Spellcraft.Preview(_scWork) is { } pv ? SpellIcon.Auto(pv) : "";
            var ib = new IconBox { Icon = cur, CustomMinimumSize = new Vector2(30, 30), MouseFilter = Control.MouseFilterEnum.Ignore }; nr.AddChild(ib);
            var icb = Btn("Icon", () => OpenSpellIcon(null), true); icb.TooltipText = "Pick, draw or import this spell's icon"; icb.CustomMinimumSize = new Vector2(52, 30); nr.AddChild(icb);
        }

        // live preview (scrolls inside a fixed box)
        var card = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; card.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(.03f, .025f, .02f, .75f), UiTheme.AccentSoft, 1, 10, 8, 0)); right.AddChild(card);
        var ps = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; card.AddChild(ps);
        var cv = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; cv.AddThemeConstantOverride("separation", 3); ps.AddChild(cv);
        var probe = _scWork.Copy(); probe.Id = _scEdit;
        string? err = _scWork.Sec(0).Count == 0 ? "Put a carrier and an effect card in section I." : Spellcraft.Check(d, probe);
        var pk = _scWork.Sec(0).Count > 0 ? Spellcraft.Preview(_scWork) : null;
        string? stOf = _scEdit == "" ? null : _scEdit;
        if (pk != null)
        {
            var c = Skills.Compute(pk.Id, stOf);
            var tl = Lbl(_scWork.Name.Trim() != "" ? _scWork.Name.Trim() : Spellcraft.AutoName(_scWork), 17, Dmg.TypeColor(c.Type).Lerp(Colors.White, .5f), cv); tl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            Row(cv, $"{Cap(fam)} spell  ·  {c.Type}  ·  {pk.Kind}" + (pk.Scale == "weapon" ? "  ·  weapon" : "  ·  spell power") + (pk.Tags.Contains("bow") ? "  ·  needs a bow" : ""), Dim, 11);
            // the chain of sections
            var secs = new List<SkillDef> { pk }; var cur = pk;
            while (cur.Then != "" && Skills.Get(cur.Then) is { } nx && secs.Count < 6 && !secs.Contains(nx)) { secs.Add(nx); cur = nx; }
            var calcs = secs.Select(k => Skills.Compute(k.Id, stOf)).ToList();
            ScHead(cv, "WHOLE SPELL");
            bool stam = c.St > 0 && c.Mp <= 0;
            float pool = stam ? d.MaxSt() : d.MaxMp();
            ScBar(cv, stam ? "Stamina" : "Mana", stam ? c.St : c.Mp, Math.Max(1, pool), $"{(stam ? c.St : c.Mp):0}", stam ? new Color(.85f, .75f, .35f) : new Color(.4f, .6f, 1f));
            ScBar(cv, "Cooldown", c.Cd, 30, $"{c.Cd:0.#} s", new Color(.75f, .7f, .6f));
            ScBar(cv, "Cast time", c.Cast, 2, $"{c.Cast:0.##} s", new Color(.75f, .7f, .6f));
            float dref = Math.Max(1, calcs.Max(x => x.Dmg)) * 1.15f;
            for (int i = 0; i < calcs.Count; i++)
            {
                var f = calcs[i]; var fk = secs[i]; int cut = fk.Name.IndexOf(" (section");
                ScHead(cv, $"SECTION {ScR(i)}  ·  {(cut > 0 ? fk.Name[..cut] : fk.Name)}", Dmg.TypeColor(f.Type).Lerp(Colors.White, .45f));
                bool hits = fk.Kind is not ("enchant" or "buff" or "ward" or "heal");
                if (hits) ScBar(cv, "Damage", f.Dmg, dref, $"{f.Dmg:0}" + (f.Count > 1 ? $" x{f.Count}" : ""), Dmg.TypeColor(f.Type));
                if (i > 0)
                {
                    float cp = Spellcraft.SecNum("costP", i, 60) / 100f, cdp = Spellcraft.SecNum("cdP", i, 30) / 100f;
                    float add = (f.Mp > 0 ? f.Mp : f.St) * cp;
                    ScBar(cv, "Adds cost", add, Math.Max(1, pool), $"+{add:0}", new Color(.4f, .6f, 1f, .75f));
                    ScBar(cv, "Adds cd", f.Cd * cdp, 30, $"+{f.Cd * cdp:0.#} s", new Color(.75f, .7f, .6f, .75f));
                }
                var extra = PreviewNums(f).Where(x => !x.Contains("damage") && !x.Contains("cooldown") && !x.Contains("mana") && !x.Contains("stamina")).ToList();
                if (extra.Count > 0) Row(cv, string.Join("  ·  ", extra), Dim, 11);
            }
            Row(cv, pk.Desc, Parch, 12);
        }
        else if (_scWork.Sec(0).Count > 0) Row(cv, "Not a spell yet: every section needs one carrier and one effect.", Dim, 12);
        else Row(cv, "Section I needs a carrier (how it is delivered) and an effect (what it does). Modifiers change it; extra copies of a card make it stronger. Sections II and III go off where the one before ends.", Dim, 12);

        // why it cannot be forged (fixed box)
        var eb = new Control { CustomMinimumSize = new Vector2(0, 38), ClipContents = true }; right.AddChild(eb);
        string msg = err ?? (_scEdit != "" && d.Skills.TryGetValue(_scEdit, out var st) && (st.Rank > 1 || st.Sup.Any(x => x != "")) ? $"Keeps its rank ({st.Rank}) and socketed modifiers." : "Ready to forge.");
        var el = Lbl(msg, 12, err != null ? ScErr : UiTheme.Highlight, eb); el.AutowrapMode = TextServer.AutowrapMode.WordSmart; el.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var bot = new HBoxContainer(); bot.AddThemeConstantOverride("separation", 6); right.AddChild(bot);
        var forge = Btn(_scEdit == "" ? "Forge spell" : "Save changes", () =>
        {
            bool fresh = _scEdit == ""; var e = Spellcraft.Forge(d, _scWork, _scEdit);
            if (e != null) { Note(e, ScErr); return; }
            _scEdit = _scWork.Id; var k = Skills.Get(_scEdit);
            int key = Array.IndexOf(d.Bar, _scEdit);
            Note((fresh ? "Forged " : "Re-forged ") + (k?.Name ?? "spell") + (key >= 0 ? $": key {Inputs.KeyOf("spell" + (key + 1))}" : fresh ? $" (hotbar full: set a key in the skills menu, {Inputs.KeyOf(Inputs.SkillsUi)})" : ""), UiTheme.Highlight);
            Sfx.Play("level"); Game.Notify(); Game.Save(); OpenSpellcraft(null);
        });
        forge.Disabled = err != null; forge.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; bot.AddChild(forge);
        forge.CustomMinimumSize = new Vector2(0, 38); forge.AddThemeFontSizeOverride("font_size", 17);
        forge.AddThemeStyleboxOverride("normal", UiTheme.Box(new Color(.16f, .12f, .05f, .95f), UiTheme.Highlight, 2, 20, 8, 0));
        forge.AddThemeStyleboxOverride("hover", UiTheme.Box(new Color(.26f, .19f, .07f, .98f), UiTheme.Highlight.Lerp(Colors.White, .3f), 2, 20, 8, 0));
        forge.AddThemeStyleboxOverride("pressed", UiTheme.Box(new Color(.1f, .07f, .03f, .98f), UiTheme.Highlight, 2, 20, 8, 0));
        forge.AddThemeStyleboxOverride("disabled", UiTheme.Box(new Color(.08f, .07f, .05f, .9f), UiTheme.AccentSoft, 1, 20, 8, 0));
        forge.AddThemeColorOverride("font_color", UiTheme.Highlight); forge.AddThemeColorOverride("font_hover_color", Colors.White);
        var clr = Btn("Clear", () => { foreach (var s in _scWork.Secs) s.Clear(); _scSec = 0; ScAdvance(); OpenSpellcraft(null); }); clr.CustomMinimumSize = new Vector2(80, 38); bot.AddChild(clr);
        if (_scEdit != "")
        {
            string id = _scEdit;
            var ta = Btn(_scConfirm ? "Click again to take it apart" : "Take apart (cards back)", () =>
            {
                if (!_scConfirm) { _scConfirm = true; OpenSpellcraft(null); return; }
                var nm = Skills.Get(id)?.Name ?? "spell"; Spellcraft.Dismantle(d, id); ScLoad("");
                Note($"Took {nm} apart: its cards are back in your collection", UiTheme.Highlight); Game.Notify(); Game.Save(); OpenSpellcraft(null);
            }, true);
            right.AddChild(ta);
        }
    }

    void ScHead(Control parent, string text, Color? col = null)
    {
        var l = Lbl(text, 11, col ?? UiTheme.Accent, parent); l.ClipText = true;
        parent.AddChild(new ColorRect { Color = (col ?? UiTheme.Accent) with { A = .35f }, CustomMinimumSize = new Vector2(0, 1) });
    }
    /// <summary>A small labelled bar: value against max (full width), the number on the right.</summary>
    void ScBar(Control parent, string label, float value, float max, string text, Color col)
    {
        var r = new HBoxContainer(); r.AddThemeConstantOverride("separation", 6); parent.AddChild(r);
        var l = Lbl(label, 11, Dim, r); l.CustomMinimumSize = new Vector2(66, 0); l.ClipText = true;
        r.AddChild(new ScBarFill { Frac = max > 0 ? Mathf.Clamp(value / max, 0, 1) : 0, Col = col, CustomMinimumSize = new Vector2(0, 14), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var vl = Lbl(text, 11, Parch, r); vl.CustomMinimumSize = new Vector2(62, 0); vl.HorizontalAlignment = HorizontalAlignment.Right; vl.ClipText = true;
    }

    /// <summary>Dev capture "spellcraft_3" (via <see cref="UiSpellcraft.DebugPreload3"/>): opens the menu on a three-section spell.</summary>
    internal static void ScDebugPreload3()
    {
        var d = Game.D; Skills.GrantStarting(d);
        int need = Spellcraft.SectionLevel(Math.Min(2, Spellcraft.MaxSections - 1)); if (d.Level < need) d.Level = need;   // capture only: open section III
        var secs = new List<List<string>> { new() { "missile", "fire", "damage" }, new() { "area", "frost" }, new() { "strike", "storm" } };
        foreach (var id in secs.SelectMany(x => x)) if (Spellcraft.Card(id) != null) d.Cards[id] = d.Cards.GetValueOrDefault(id) + 1;
        _scWork = new CraftedSpell { Secs = secs.Take(Spellcraft.MaxSections).ToList() }; _scWork.Sec(Spellcraft.MaxSections - 1);
        _scEdit = ""; _scTree = false; _scConfirm = false; _scSec = Math.Min(2, Spellcraft.MaxSections - 1); _scSearch = ""; ScAdvance();
        I?.OpenSpellcraft(null);
    }
    /// <summary>Test only: forges the preloaded 3-section spell and returns its skill id (null + the reason logged when it fails).</summary>
    internal static string? ScDebugForge3()
    {
        ScDebugPreload3(); I?.Close(); var d = Game.D; int before = d.Spells.Count;
        var err = Spellcraft.Forge(d, _scWork, "");
        if (err != null) { GD.PrintErr("[DEVCAP] forge failed: " + err); return null; }
        return d.Spells.LastOrDefault()?.Id;
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

/// <summary>Spellcraft hooks for dev tools.</summary>
public static class UiSpellcraft
{
    /// <summary>Opens Spellcraft on a three-section spell (Flame Missile -> Frost Area -> Storm Strike): gives its cards and, if needed,
    /// raises the level to open section III. For the "spellcraft_3" capture only.</summary>
    public static void DebugPreload3() => Ui.ScDebugPreload3();
    public static string? DebugForge3() => Ui.ScDebugForge3();
    /// <summary>The "icon_editor" capture: the spell icon window on its Editor tab.</summary>
    public static void DebugIconEditor() => Ui.IcDebugOpen();
}

/// <summary>The "then" arrow between two spell sections: lit when both hold cards.</summary>
public partial class SpellArrow : Control
{
    public bool Lit, Locked;
    public override void _Ready() { MouseFilter = MouseFilterEnum.Ignore; }
    public override void _Draw()
    {
        var f = ThemeDB.FallbackFont; float y = Size.Y * .42f, x0 = 5, x1 = Size.X - 5;
        var col = Locked ? UiTheme.AccentSoft with { A = .35f } : Lit ? UiTheme.Highlight : UiTheme.AccentSoft;
        if (Lit) DrawLine(new Vector2(x0, y), new Vector2(x1 - 6, y), col, 2f);
        else DrawDashedLine(new Vector2(x0, y), new Vector2(x1 - 6, y), col, 1.5f, 4);
        DrawColoredPolygon(new[] { new Vector2(x1, y), new Vector2(x1 - 9, y - 6), new Vector2(x1 - 9, y + 6) }, col);
        DrawString(f, new Vector2(0, y - 10), "then", HorizontalAlignment.Center, Size.X, 10, col);
        DrawString(f, new Vector2(0, y + 20), "at end", HorizontalAlignment.Center, Size.X, 9, col with { A = col.A * .8f });
    }
}

/// <summary>A small horizontal value bar (Spellcraft preview).</summary>
public partial class ScBarFill : Control
{
    public float Frac; public Color Col = UiTheme.Accent;
    public override void _Ready() { MouseFilter = MouseFilterEnum.Ignore; }
    public override void _Draw()
    {
        float h = 8, y = (Size.Y - h) / 2;
        DrawRect(new Rect2(0, y, Size.X, h), new Color(0, 0, 0, .55f));
        if (Frac > 0) DrawRect(new Rect2(0, y, Mathf.Max(2, Size.X * Frac), h), Col);
        DrawRect(new Rect2(0, y, Size.X, h), UiTheme.AccentSoft with { A = .5f }, false, 1f);
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
            DrawRect(r, new Color(0, 0, 0, _hov ? .28f : Selected ? .24f : .16f));
            var col = Selected ? UiTheme.Highlight : (_hov ? UiTheme.Accent : UiTheme.AccentSoft) with { A = _hov ? .9f : .55f };
            float lw = Selected ? 2f : 1.2f;
            var a = new Vector2(1, 1); var b = new Vector2(Size.X - 1, 1); var c = Size - new Vector2(1, 1); var d = new Vector2(1, Size.Y - 1);
            DrawDashedLine(a, b, col, lw, 4); DrawDashedLine(b, c, col, lw, 4); DrawDashedLine(c, d, col, lw, 4); DrawDashedLine(d, a, col, lw, 4);
            var lines = Label == "" ? Array.Empty<string>() : Label.Split('\n');
            var cx = Size.X / 2; var cy = lines.Length > 1 ? Size.Y * .26f : Size.Y * .42f;
            DrawLine(new Vector2(cx - 7, cy), new Vector2(cx + 7, cy), col, 2f); DrawLine(new Vector2(cx, cy - 7), new Vector2(cx, cy + 7), col, 2f);
            for (int i = 0; i < lines.Length; i++)
                DrawString(F, new Vector2(0, Size.Y - 7 - (lines.Length - 1 - i) * 11), lines[i], HorizontalAlignment.Center, Size.X, 10, Selected ? UiTheme.Highlight : UiTheme.Dim);
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
        if (Icon != "") Icons.Draw(this, Icon, new Vector2(Size.X / 2, 14 + (Size.Y - 28) / 2), Mathf.Min(32, Size.X - 14), dim);
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
