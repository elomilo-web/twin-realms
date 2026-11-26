using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>Framed recipe/ingredient cell: dark well, rarity-coloured frame, vector icon or a small coloured block cube, optional count badge.</summary>
public partial class RecipeSlot : Control
{
    public string Icon = ""; public Color Frame = new(.55f, .46f, .3f); public Color? Cube; public int Tier; public int Badge; public bool Dimmed;
    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size); float a = Dimmed ? .55f : 1f; var F = ThemeDB.FallbackFont;
        DrawRect(r, new Color(.035f, .03f, .03f, .92f));
        for (int i = 0; i < 4; i++) DrawRect(r.Grow(-i * 2f), new Color(0, 0, 0, .22f - i * .05f), false, 2f);
        if (Tier >= 2) for (int i = 0; i < 3; i++) DrawRect(r.Grow(-1 - i * 2), Frame with { A = (.07f + .04f * Tier) * (1f - i * .3f) * a }, false, 2f);
        var fr = Tier == 0 && Cube == null ? new Color(.5f, .48f, .45f, .7f * a) : Frame.Lerp(Colors.White, .1f) with { A = .75f * a };
        DrawRect(r.Grow(-.5f), fr, false, 1f);
        float l = Mathf.Min(6, r.Size.Y * .2f); var ca = Frame.Lightened(.2f) with { A = .85f * a };
        foreach (var (p, dx, dy) in new[] { (r.Position, 1, 1), (new Vector2(r.End.X, r.Position.Y), -1, 1), (new Vector2(r.Position.X, r.End.Y), 1, -1), (r.End, -1, -1) })
        { var q = p + new Vector2(dx * .5f, dy * .5f); DrawLine(q, q + new Vector2(dx * l, 0), ca, 1f); DrawLine(q, q + new Vector2(0, dy * l), ca, 1f); }
        var c = r.GetCenter();
        if (Cube is Color cc)
        {
            float s = Mathf.Min(r.Size.X, r.Size.Y) * .3f; cc = cc with { A = a };
            Vector2 P(float x, float y) => c + new Vector2(x, y) * s;
            var p0 = P(0, -1); var p1 = P(.866f, -.5f); var p2 = P(.866f, .5f); var p3 = P(0, 1); var p4 = P(-.866f, .5f); var p5 = P(-.866f, -.5f); var m = P(0, 0);
            DrawColoredPolygon(new[] { p0, p1, m, p5 }, cc.Lightened(.25f)); DrawColoredPolygon(new[] { p5, m, p3, p4 }, cc.Darkened(.3f)); DrawColoredPolygon(new[] { m, p1, p2, p3 }, cc.Darkened(.12f));
            var ol = new Color(0, 0, 0, .6f * a); DrawPolyline(new[] { p0, p1, p2, p3, p4, p5, p0 }, ol, 1f); DrawLine(m, p5, ol, 1f); DrawLine(m, p1, ol, 1f); DrawLine(m, p3, ol, 1f);
        }
        else if (Icon != "") Icons.Draw(this, Icon, c, r.Size.Y * .6f, a);
        if (Badge > 1) { var t = "x" + Badge; DrawStringOutline(F, new Vector2(0, r.Size.Y - 4), t, HorizontalAlignment.Right, r.Size.X - 4, 12, 3, Colors.Black); DrawString(F, new Vector2(0, r.Size.Y - 4), t, HorizontalAlignment.Right, r.Size.X - 4, 12, Colors.White); }
    }
}

/// <summary>One crafting recipe as the Crafting window lists it.</summary>
sealed class CraftRec
{
    public string Group = "", Title = "", Sub = "", Btn = "Craft", Icon = ""; public Color Col = Colors.White; public Dictionary<string, int> Cost = new();
    public Color? Cube; public int Tier, Out = 1; public string? Blocked; public bool Repeat; public Func<bool> Do = () => false; public List<string> Extra = new();
}

/// <summary>Pack extras: the pickup filter and crafting with disassembly scraps (recipes in data/loot.json "crafting").</summary>
public partial class Ui
{
    static readonly Color Good = new("5be37d"), Bad = new("ff6a5f");
    static FontVariation? _italic;
    /// <summary>Short italic hint line for empty states.</summary>
    Label CraftHint(Node parent, string text, int size = 14)
    {
        _italic ??= new FontVariation { BaseFont = ThemeDB.FallbackFont, VariationTransform = new Transform2D(new Vector2(1, 0), new Vector2(.22f, 1), Vector2.Zero) };
        var l = Row(parent, text, Dim, size); l.AddThemeFontOverride("font", _italic); return l;
    }
    /// <summary>Gold text tabs: the current one is bright with a gold underline.</summary>
    void CraftTabs(Node parent, string cur, params (string id, string label, Action open)[] tabs)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 16); parent.AddChild(row);
        PadTabs(tabs.Select(t => (t.open, t.id == cur)));   // LB / RB switch them (UiPad.cs)
        foreach (var (id, label, open) in tabs)
        {
            bool on = id == cur; var col = new VBoxContainer(); col.AddThemeConstantOverride("separation", 0); row.AddChild(col);
            var b = new Button { Text = label, Flat = true, FocusMode = Control.FocusModeEnum.None }; b.AddThemeFontSizeOverride("font_size", 17);
            foreach (var n in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" }) b.AddThemeColorOverride(n, on ? UiTheme.Highlight : n == "font_hover_color" ? Colors.White : Dim);
            foreach (var n in new[] { "normal", "hover", "pressed", "focus" }) b.AddThemeStyleboxOverride(n, new StyleBoxEmpty());
            b.Pressed += () => { Sfx.Play("click"); open(); }; col.AddChild(b);
            col.AddChild(new ColorRect { Color = on ? UiTheme.Highlight : UiTheme.Accent with { A = 0 }, CustomMinimumSize = new Vector2(0, 2), MouseFilter = Control.MouseFilterEnum.Ignore });
        }
    }

    // ---------------------------------------------------------------- pickup filter
    static readonly string[] FilterSlots = { "weapon", "shield", "head", "armor", "hands", "feet", "cape", "trinket" };
    public void OpenPickFilter()
    {
        var d = Game.D; var v = Frame("pickfilter", "Pickup Filter", new Vector2(640, 470), OpenPickFilter);
        CraftHint(v, "Choose which items you pick up when you walk over them. Gold, potions, named uniques and set pieces are always picked up.");
        Header("Pick up items of at least", v);
        var rr = new HBoxContainer(); rr.AddThemeConstantOverride("separation", 4); v.AddChild(rr);
        for (int r = 0; r < 5; r++)
        {
            int k = r; var b = Btn(Items.RarityName(r), () => { d.PickRar = k; OpenPickFilter(); }, true);
            b.AddThemeColorOverride("font_color", Items.RarityColor(r)); b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; Mark(b, d.PickRar == r); rr.AddChild(b);
        }
        Header("Slots to pick up", v);
        var sg = new GridContainer { Columns = 4 }; sg.AddThemeConstantOverride("h_separation", 6); sg.AddThemeConstantOverride("v_separation", 6); v.AddChild(sg);
        foreach (var s in FilterSlots)
        {
            string sl = s; bool on = !d.PickSkip.Contains(s);
            var b = Btn((on ? "[x] " : "[  ] ") + Cap(s), () => { if (!d.PickSkip.Remove(sl)) d.PickSkip.Add(sl); OpenPickFilter(); }, true);
            b.CustomMinimumSize = new Vector2(140, 32); Mark(b, on); sg.AddChild(b);
        }
        Header("Items that do not pass", v);
        var mr = new HBoxContainer(); mr.AddThemeConstantOverride("separation", 4); v.AddChild(mr);
        var b0 = Btn("Leave them on the ground", () => { d.PickScrap = false; OpenPickFilter(); }, true); Mark(b0, !d.PickScrap); b0.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; mr.AddChild(b0);
        var b1 = Btn("Disassemble them into scraps", () => { d.PickScrap = true; OpenPickFilter(); }, true); Mark(b1, d.PickScrap); b1.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; mr.AddChild(b1);
        var bot = new HBoxContainer(); bot.AddThemeConstantOverride("separation", 10); v.AddChild(bot);
        bot.AddChild(Btn("Pick up everything", () => { d.PickRar = 0; d.PickSkip.Clear(); d.PickScrap = false; OpenPickFilter(); }));
        bot.AddChild(Btn("Back to the pack", OpenPack));
    }

    // ---------------------------------------------------------------- crafting
    static string _craftSlot = "weapon", _craftElem = "fire", _craftSelTab = ""; static int _craftSel;
    static Dictionary<string, int> Cost(JsonElement e)
    {
        var c = new Dictionary<string, int>(); if (e.TryGetProperty("cost", out var k)) foreach (var p in k.EnumerateObject()) c[p.Name] = p.Value.GetInt32(); return c;
    }
    static bool CanPay(Dictionary<string, int> cost) { foreach (var (id, n) in cost) if (Game.D.Mats.GetValueOrDefault(id) < n) return false; return true; }
    static void Pay(Dictionary<string, int> cost) { var m = Game.D.Mats; foreach (var (id, n) in cost) { m[id] = m.GetValueOrDefault(id) - n; if (m[id] <= 0) m.Remove(id); } }
    static string CostText(Dictionary<string, int> cost) => string.Join(",  ", cost.Select(kv => $"{kv.Value} {GatherDb.Mat(kv.Key).Name} ({Game.D.Mats.GetValueOrDefault(kv.Key)})"));
    static int Missing(Dictionary<string, int> cost) => cost.Sum(kv => Math.Max(0, kv.Value - Game.D.Mats.GetValueOrDefault(kv.Key)));
    static RecipeSlot IngSlot(string id, float size)
    {
        if (id.StartsWith("blk_") && Blocks.Exists(id[4..])) return new RecipeSlot { Cube = Blocks.Tint(Blocks.Def(Blocks.Id(id[4..]))), Frame = new Color(.55f, .46f, .3f), CustomMinimumSize = new Vector2(size, size), MouseFilter = Control.MouseFilterEnum.Ignore };
        var m = GatherDb.Mat(id); return new RecipeSlot { Icon = m.Name, Frame = m.Col, CustomMinimumSize = new Vector2(size, size), MouseFilter = Control.MouseFilterEnum.Ignore };
    }

    public void OpenCraft(string tab)
    {
        var d = Game.D; var v = Frame("craft", "Crafting", new Vector2(860, 640), () => OpenCraft(tab));
        var top = new HBoxContainer(); top.AddThemeConstantOverride("separation", 4); v.AddChild(top);
        var tabsHost = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; top.AddChild(tabsHost);
        CraftTabs(tabsHost, tab, ("gear", "Gear", () => OpenCraft("gear")), ("elem", "Elemental", () => OpenCraft("elem")), ("sets", "Set pieces", () => OpenCraft("sets")), ("pots", "Potions", () => OpenCraft("pots")), ("blocks", "Blocks", () => OpenCraft("blocks")));
        top.AddChild(Btn("Back to the pack", OpenPack, true));
        // scraps on hand
        var have = new HBoxContainer(); have.AddThemeConstantOverride("separation", 6); v.AddChild(have); int shown = 0;
        foreach (var kv in d.Mats.OrderBy(k => k.Key))
        {
            var m = GatherDb.Mat(kv.Key); if (!m.Scrap || kv.Value <= 0) continue;
            have.AddChild(new ItemSlot { IconId = m.Name, Count = kv.Value, Tint = m.Col, CustomMinimumSize = new Vector2(40, 40), TooltipText = $"{m.Name} x{kv.Value}" }); shown++;
        }
        if (shown == 0) CraftHint(v, "No scraps yet. Disassemble items in your pack (the Disassemble button) or set the pickup filter to scrap what you do not want.", 13);
        var c = LootTables.Root.GetProperty("crafting");
        bool Room() { if (d.Inv.Count >= 28) { Note("Pack is full", new Color(1, .5f, .5f)); return false; } return true; }
        void Give(Item it) { d.Inv.Add(it); Note("Crafted " + it.Name, it.Color); Sfx.Play("level"); Game.Notify(); }
        void SlotPicker(Node parent)
        {
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 4); parent.AddChild(row);
            foreach (var s in FilterSlots) { string sl = s; var b = Btn(Cap(s), () => { _craftSlot = sl; OpenCraft(tab); }, true); Mark(b, _craftSlot == s); b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(b); }
        }
        string NoMats() { Note("Not enough materials", new Color(1, .5f, .5f)); return ""; }
        string intro = ""; var recs = new List<CraftRec>(); string empty = "Nothing can be crafted here yet.";
        switch (tab)
        {
            case "blocks":
            {
                // block world (data/blocks.json "recipes"): costs are block ids or gather materials
                intro = "Turn blocks into other blocks. Dig blocks in build mode (" + Inputs.KeyOf(Inputs.BuildMode) + ") to collect them.";
                foreach (var p in Blocks.Root.GetProperty("recipes").EnumerateObject())
                {
                    if (p.Value.ValueKind != JsonValueKind.Object || !Blocks.Exists(p.Name)) continue;
                    string outId = p.Name; int n = (int)JsonUtil.F(p.Value, "out", 1);
                    var cost = new Dictionary<string, int>();
                    foreach (var k in p.Value.GetProperty("cost").EnumerateObject()) cost[Blocks.DropItem(k.Name)] = k.Value.GetInt32();
                    string bn = Blocks.Def(Blocks.Id(outId)).Name;
                    recs.Add(new CraftRec
                    {
                        Title = bn, Sub = $"Makes {n} {bn}", Cost = cost, Out = n, Cube = Blocks.Tint(Blocks.Def(Blocks.Id(outId))), Col = Colors.White, Repeat = true,
                        Do = () =>
                        {
                            if (!CanPay(cost)) { Note("Not enough blocks", new Color(1, .5f, .5f)); return false; }
                            Pay(cost); var m = d.Mats; string item = Blocks.Item(outId); m[item] = m.GetValueOrDefault(item) + n;
                            Note($"Crafted {n} {bn}", UiTheme.Highlight); Sfx.Play("level"); Game.Notify(); return true;
                        },
                    });
                }
                // the pickaxe (data/blocks.json dig.pickaxes): each tier is made from the one before; the next tier only, as alternatives
                if (Blocks.Node("dig.pickaxes.tiers") is { ValueKind: JsonValueKind.Array } tiers)
                    for (int ti = 2; ti <= tiers.GetArrayLength(); ti++)
                    {
                        if (ti != d.Pick + 1 || !tiers[ti - 1].TryGetProperty("costs", out var alts) || alts.ValueKind != JsonValueKind.Array) continue;
                        int tier = ti; string pn = Blocks.PickName(tier); var tj = tiers[ti - 1];
                        foreach (var alt in alts.EnumerateArray())
                        {
                            var cost = new Dictionary<string, int>(); foreach (var k in alt.EnumerateObject()) cost[Blocks.DropItem(k.Name)] = k.Value.GetInt32();
                            recs.Add(new CraftRec
                            {
                                Group = "Pickaxe   held in build mode: digs faster, breaks harder stone", Title = pn,
                                Sub = $"Upgrades your {Blocks.PickName(d.Pick)}: digs x{JsonUtil.F(tj, "speed", 2):0.#}, breaks hardness up to {JsonUtil.F(tj, "maxHardness", 2.5f):0.#}",
                                Cost = cost, Col = UiTheme.Highlight, Btn = "Forge", Icon = Icons.ForItem("weapon", "axe"),
                                Do = () =>
                                {
                                    if (d.Pick >= tier) return false; if (!CanPay(cost)) { Note("Not enough materials", new Color(1, .5f, .5f)); return false; }
                                    Pay(cost); d.Pick = tier; Note($"Forged a {pn}", UiTheme.Highlight); Sfx.Play("level"); Game.Notify(); return true;
                                },
                            });
                        }
                    }
                empty = "No block recipes yet.";
                break;
            }
            case "elem":
            {
                var e = c.GetProperty("elemental"); int rar = (int)JsonUtil.F(e, "rar", 3), lb = (int)JsonUtil.F(e, "levelBonus", 1), cores = (int)JsonUtil.F(e, "cores", 4);
                intro = $"Forge {Items.RarityName(rar).ToLower()} gear of the element you choose (level {d.Level + lb}). Needs {cores} cores of that element.";
                var elemCores = LootTables.Root.GetProperty("scraps").GetProperty("element");
                var cost = Cost(e); string core = elemCores.TryGetProperty(_craftElem, out var cj) ? cj.GetString() ?? "" : ""; if (core != "") cost[core] = cost.GetValueOrDefault(core) + cores;
                bool trinket = _craftSlot == "trinket"; string ew = GearFx.Get(_craftElem)?.Word ?? _craftElem;
                recs.Add(new CraftRec
                {
                    Title = $"{Items.RarityName(rar)} {ew} {_craftSlot}", Sub = $"Level {d.Level + lb}, element: {ew}", Cost = cost, Col = Items.RarityColor(rar), Tier = rar, Repeat = !trinket,
                    Icon = Icons.ForItem(_craftSlot, _craftSlot == "weapon" ? "sword" : ""), Blocked = trinket ? "Trinkets carry no element: pick another slot" : null,
                    Do = () =>
                    {
                        if (trinket || !Room()) return false; if (!CanPay(cost)) { NoMats(); return false; }
                        Pay(cost); Give(Items.Roll(d.Level + lb, 0, _craftSlot, rar, _craftElem)); return true;
                    },
                });
                break;
            }
            case "sets":
            {
                var e = c.GetProperty("sets"); var cost = Cost(e); int lb = (int)JsonUtil.F(e, "levelBonus", 0);
                // the five forged sets: they never drop; choose the piece you want
                foreach (var s in LootTables.Sets.Values.Where(x => x.Craft))
                {
                    var sd = s; var ex = new List<string>(); foreach (var (bn, mods) in s.Bonuses) ex.Add($"{bn} pieces: " + string.Join(", ", mods.Select(kv => TreeDb.StatText(kv.Key, kv.Value))));
                    foreach (var piece in s.Pieces)
                    {
                        string pc = piece;
                        recs.Add(new CraftRec
                        {
                            Group = "Forged sets   only made here, never found", Title = $"{s.Name}: {Cap(piece)}", Sub = $"{Cap(s.Elem)} set", Btn = "Forge", Cost = sd.CraftCost, Col = s.Col, Tier = 3, Icon = Icons.ForItem(pc, pc == "weapon" ? "sword" : ""), Extra = ex,
                            Do = () => { if (!Room()) return false; if (!CanPay(sd.CraftCost)) { NoMats(); return false; } Pay(sd.CraftCost); Give(LootTables.MakeSetPiece(sd, d.Level + lb, pc)); return true; },
                        });
                    }
                }
                foreach (var s in LootTables.Sets.Values.Where(x => !x.Craft))
                {
                    bool owns = d.Eq.Values.Any(i => i?.Set == s.Id) || d.Inv.Any(i => i.Set == s.Id) || d.Stash.Any(i => i.Set == s.Id);
                    var sd = s; var ex = new List<string>(); foreach (var (bn, mods) in s.Bonuses) ex.Add($"{bn} pieces: " + string.Join(", ", mods.Select(kv => TreeDb.StatText(kv.Key, kv.Value))));
                    recs.Add(new CraftRec
                    {
                        Group = "Dungeon sets   forge a random piece", Title = s.Name, Sub = $"A random piece: {string.Join(", ", s.Pieces)}. You must own a piece of the set already (worn, in your pack or in storage). Set pieces drop from floor 10 and deeper of their dungeon.",
                        Cost = cost, Col = owns ? s.Col : Dim, Tier = owns ? 3 : 0, Icon = "armor", Blocked = owns ? null : "Find a piece of this set first", Extra = ex,
                        Do = () =>
                        {
                            if (!owns) { Note("Find a piece of this set first", new Color(1, .6f, .5f)); return false; }
                            if (!Room()) return false; if (!CanPay(cost)) { NoMats(); return false; }
                            Pay(cost); Give(LootTables.MakeSetPiece(sd, d.Level + lb)); return true;
                        },
                    });
                }
                break;
            }
            case "pots":
                foreach (var e in c.GetProperty("potions").EnumerateArray())
                {
                    string pot = JsonUtil.S(e, "pot", "hp"); int n = (int)JsonUtil.F(e, "n", 1); var cost = Cost(e); bool hp = pot == "hp";
                    recs.Add(new CraftRec
                    {
                        Title = $"{n} {(hp ? "health" : "mana")} potions", Sub = $"You carry {(hp ? d.HpPot : d.MpPot)}", Btn = "Brew", Cost = cost, Out = n, Icon = hp ? "hp" : "mp", Col = hp ? new Color("ff8f86") : new Color("8fb8ff"), Tier = 1, Repeat = true,
                        Do = () =>
                        {
                            if (!CanPay(cost)) { NoMats(); return false; }
                            Pay(cost); if (hp) d.HpPot += n; else d.MpPot += n; Sfx.Play("pickup"); Game.Notify(); return true;
                        },
                    });
                }
                break;
            default:
                intro = "Forge gear for the slot you choose at your level (weapons get a random weapon type).";
                foreach (var e in c.GetProperty("gear").EnumerateArray())
                {
                    int rar = (int)JsonUtil.F(e, "rar", 1), lb = (int)JsonUtil.F(e, "levelBonus", 0); var cost = Cost(e);
                    recs.Add(new CraftRec
                    {
                        Title = $"{Items.RarityName(rar)} {_craftSlot}", Sub = $"Level {d.Level + lb}", Cost = cost, Col = Items.RarityColor(rar), Tier = rar, Repeat = true, Icon = Icons.ForItem(_craftSlot, _craftSlot == "weapon" ? "sword" : ""),
                        Do = () => { if (!Room()) return false; if (!CanPay(cost)) { NoMats(); return false; } Pay(cost); Give(Items.Roll(d.Level + lb, 0, _craftSlot, rar)); return true; },
                    });
                }
                break;
        }
        if (intro != "") Row(v, intro, UiTheme.Text, 14);
        if (tab == "gear" || tab == "elem") SlotPicker(v);
        if (tab == "elem")
        {
            var er = new HBoxContainer(); er.AddThemeConstantOverride("separation", 4); v.AddChild(er);
            foreach (var p in LootTables.Root.GetProperty("scraps").GetProperty("element").EnumerateObject()) { string el = p.Name; var b = Btn(GearFx.Get(el)?.Word ?? el, () => { _craftElem = el; OpenCraft(tab); }, true); Mark(b, _craftElem == el); b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; er.AddChild(b); }
        }
        if (_craftSelTab != tab) { _craftSelTab = tab; _craftSel = 0; }
        _craftSel = Math.Clamp(_craftSel, 0, Math.Max(0, recs.Count - 1));
        if (recs.Count == 0) { CraftHint(v, empty); return; }

        var cols = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; cols.AddThemeConstantOverride("separation", 14); v.AddChild(cols);
        var ls = Scroll(cols, 260); ls.CustomMinimumSize = new Vector2(350, 260); ls.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled; var list = Inner(ls); list.AddThemeConstantOverride("separation", 5);
        var ds = Scroll(cols, 260); ds.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; ds.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled; var detail = Inner(ds); detail.AddThemeConstantOverride("separation", 8);
        var rows = new List<PanelContainer>();
        StyleBoxFlat RowStyle(CraftRec r, bool sel)
        {
            bool ok = r.Blocked == null && CanPay(r.Cost);
            var bg = ok ? new Color(.2f, .16f, .1f, .92f) : new Color(.07f, .06f, .06f, .85f);
            var bd = sel ? UiTheme.Highlight : ok ? new Color(.55f, .46f, .3f, .8f) : new Color(.3f, .26f, .22f, .6f);
            return UiTheme.Box(bg, bd, sel ? 2 : 1, 8, 5);
        }
        void Detail(CraftRec r)
        {
            foreach (var ch in detail.GetChildren()) { detail.RemoveChild(ch); ch.QueueFree(); }
            bool ok = r.Blocked == null && CanPay(r.Cost);
            var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 12); detail.AddChild(head);
            head.AddChild(new RecipeSlot { Icon = r.Icon, Cube = r.Cube, Frame = r.Col, Tier = r.Tier, Badge = r.Out, Dimmed = !ok, CustomMinimumSize = new Vector2(92, 92), MouseFilter = Control.MouseFilterEnum.Ignore });
            var hv = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; hv.AddThemeConstantOverride("separation", 2); head.AddChild(hv);
            Row(hv, r.Title, r.Col.Lightened(.1f), 20); if (r.Sub != "") Row(hv, r.Sub, Dim, 13).CustomMinimumSize = new Vector2(200, 0);
            Header("Ingredients", detail);
            if (r.Cost.Count == 0) CraftHint(detail, "No ingredients needed.");
            else
            {
                var fl = new HFlowContainer(); fl.AddThemeConstantOverride("h_separation", 8); fl.AddThemeConstantOverride("v_separation", 6); detail.AddChild(fl);
                foreach (var (id, need) in r.Cost)
                {
                    int got = d.Mats.GetValueOrDefault(id); var cell = new VBoxContainer { CustomMinimumSize = new Vector2(84, 0) }; cell.AddThemeConstantOverride("separation", 1); fl.AddChild(cell);
                    var sl = IngSlot(id, 56); var wrap = new CenterContainer(); wrap.AddChild(sl); cell.AddChild(wrap);
                    var cnt = Lbl($"{got} / {need}", 14, got >= need ? Good : Bad, cell); cnt.HorizontalAlignment = HorizontalAlignment.Center;
                    var nm = Lbl(GatherDb.Mat(id).Name, 11, Dim, cell); nm.HorizontalAlignment = HorizontalAlignment.Center; nm.ClipText = true;
                }
            }
            if (r.Blocked != null) CraftHint(detail, r.Blocked, 13).AddThemeColorOverride("font_color", new Color(1f, .6f, .5f));
            foreach (var x in r.Extra) Row(detail, x, Dim, 12);
            var br = new HBoxContainer(); br.AddThemeConstantOverride("separation", 8); detail.AddChild(br);
            void Go(int times)
            {
                bool any = false; for (int i = 0; i < times; i++) { if (!r.Do()) break; any = true; }
                if (any) { Sfx.UiConfirm(); OpenCraft(tab); } else Sfx.UiError();
            }
            var cb = Btn(r.Btn, () => Go(1)); cb.CustomMinimumSize = new Vector2(120, 38); cb.AddThemeColorOverride("font_color", ok ? UiTheme.Highlight : Dim); br.AddChild(cb);
            if (r.Repeat) { var c5 = Btn(r.Btn + " x5", () => Go(5)); c5.CustomMinimumSize = new Vector2(120, 38); c5.AddThemeColorOverride("font_color", ok ? UiTheme.Highlight : Dim); br.AddChild(c5); }
        }
        string lastGroup = ""; int idx = 0;
        foreach (var r0 in recs)
        {
            var r = r0; int my = idx++;
            if (r.Group != "" && r.Group != lastGroup) { lastGroup = r.Group; Header(r.Group, list); }
            bool ok = r.Blocked == null && CanPay(r.Cost);
            var pc = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, MouseDefaultCursorShape = Control.CursorShape.PointingHand }; pc.AddThemeStyleboxOverride("panel", RowStyle(r, my == _craftSel)); list.AddChild(pc); rows.Add(pc);
            var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 8); pc.AddChild(h);
            h.AddChild(new RecipeSlot { Icon = r.Icon, Cube = r.Cube, Frame = r.Col, Tier = r.Tier, Badge = r.Out, Dimmed = !ok, CustomMinimumSize = new Vector2(46, 46), MouseFilter = Control.MouseFilterEnum.Ignore });
            var tv = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; tv.AddThemeConstantOverride("separation", 0); h.AddChild(tv);
            var nl = Lbl(r.Title, 15, ok ? r.Col.Lightened(.2f) : Dim, tv); nl.ClipText = true; nl.CustomMinimumSize = new Vector2(100, 0);
            var sum = Lbl(r.Cost.Count == 0 ? "no ingredients" : string.Join(", ", r.Cost.Select(kv => $"{kv.Value} {GatherDb.Mat(kv.Key).Name}")), 11, ok ? new Color(.82f, .74f, .55f) : new Color(.5f, .48f, .46f), tv); sum.ClipText = true; sum.CustomMinimumSize = new Vector2(100, 0);
            int miss = Missing(r.Cost); if (!ok && miss > 0) Lbl($"missing {miss}", 11, Bad, tv);
            pc.GuiInput += e =>
            {
                if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                {
                    Sfx.Play("click"); _craftSel = my; for (int i = 0; i < rows.Count; i++) rows[i].AddThemeStyleboxOverride("panel", RowStyle(recs[i], i == my)); Detail(r);
                }
            };
        }
        Detail(recs[_craftSel]);
    }
}
