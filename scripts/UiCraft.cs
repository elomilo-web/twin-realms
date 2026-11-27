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
    // A-26 (the crafting grid, UiStations.cs): Hand = also on the 2x2 hand grid; Shape + KeyMap = a shaped recipe (rows of letters, letter -> material id)
    public bool Hand; public string[]? Shape; public Dictionary<char, string>? KeyMap;
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
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); parent.AddChild(row);
        PadTabs(tabs.Select(t => (t.open, t.id == cur)));   // LB / RB switch them (UiPad.cs)
        foreach (var (id, label, open) in tabs)
        {
            bool on = id == cur; var col = new VBoxContainer(); col.AddThemeConstantOverride("separation", 0); row.AddChild(col);
            var b = new Button { Text = label, Flat = true, FocusMode = Control.FocusModeEnum.None }; b.AddThemeFontSizeOverride("font_size", UiTheme.TabSize);
            foreach (var n in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" }) b.AddThemeColorOverride(n, on ? UiTheme.Highlight : n == "font_hover_color" ? Colors.White : Dim);
            foreach (var n in new[] { "normal", "hover", "pressed", "focus" }) b.AddThemeStyleboxOverride(n, new StyleBoxEmpty());
            b.Pressed += () => { Sfx.Play("click"); open(); }; col.AddChild(b);
            col.AddChild(new ColorRect { Color = on ? UiTheme.Highlight : UiTheme.Accent with { A = 0 }, CustomMinimumSize = new Vector2(0, 2), MouseFilter = Control.MouseFilterEnum.Ignore });
        }
    }

    // ---------------------------------------------------------------- pickup filter
    static readonly string[] FilterSlots = { "weapon", "shield", "head", "armor", "hands", "feet", "cape", "trinket" };
    /// <summary>F-41: the pickup filter is the pack's Filter tab now (it used to be a window of its own, 640 x 470).</summary>
    public void OpenPickFilter() { _packTab = 3; OpenPack(); }
    /// <summary>The Filter tab of the pack (Ui.OpenPack): lowest rarity, slots, what happens to the rest. Every click rebuilds the pack.</summary>
    void PickFilterPane(GridContainer host, float width)
    {
        var d = Game.D; var v = new VBoxContainer { CustomMinimumSize = new Vector2(width, 0) }; v.AddThemeConstantOverride("separation", UiTheme.RowGap); host.AddChild(v);
        string about = "Which items you pick up when you walk over them. Gold, potions, named uniques and set pieces are always picked up.";
        var hl = Header("Pick up items of at least  [?]", v); hl.TooltipText = about; hl.MouseFilter = Control.MouseFilterEnum.Stop;
        var rr = new HBoxContainer { TooltipText = about }; rr.AddThemeConstantOverride("separation", 4); v.AddChild(rr);
        for (int r = 0; r < 5; r++)
        {
            int k = r; var b = Btn(Items.RarityName(r), () => { d.PickRar = k; OpenPack(); }, true);
            b.AddThemeColorOverride("font_color", Items.RarityColor(r)); b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; b.ClipText = true; b.CustomMinimumSize = new Vector2(60, UiTheme.CtlH); Mark(b, d.PickRar == r); rr.AddChild(b);
        }
        Header("Slots to pick up", v);
        var sg = new GridContainer { Columns = 4 }; sg.AddThemeConstantOverride("h_separation", 4); sg.AddThemeConstantOverride("v_separation", 4); v.AddChild(sg);
        foreach (var s in FilterSlots)
        {
            string sl = s; bool on = !d.PickSkip.Contains(s);
            var b = Btn((on ? "[x] " : "[  ] ") + Cap(s), () => { if (!d.PickSkip.Remove(sl)) d.PickSkip.Add(sl); OpenPack(); }, true);
            b.CustomMinimumSize = new Vector2(60, UiTheme.CtlH); b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; b.ClipText = true; Mark(b, on); sg.AddChild(b);
        }
        Header("Items that do not pass", v);
        var mr = new HBoxContainer(); mr.AddThemeConstantOverride("separation", 4); v.AddChild(mr);
        var b0 = Btn("Stay on the ground", () => { d.PickScrap = false; OpenPack(); }, true); Mark(b0, !d.PickScrap); b0.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; b0.CustomMinimumSize = new Vector2(0, UiTheme.CtlH); mr.AddChild(b0);
        var b1 = Btn("Are scrapped", () => { d.PickScrap = true; OpenPack(); }, true); Mark(b1, d.PickScrap); b1.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; b1.CustomMinimumSize = new Vector2(0, UiTheme.CtlH); b1.TooltipText = "Items that do not pass are disassembled into scraps as you walk over them"; mr.AddChild(b1);
        var all = Btn("Pick up everything", () => { d.PickRar = 0; d.PickSkip.Clear(); d.PickScrap = false; OpenPack(); }, true); all.TooltipText = "Clears the filter"; v.AddChild(all);
    }

    // ---------------------------------------------------------------- crafting
    static string _craftSlot = "weapon", _craftElem = "fire", _craftSelTab = ""; static int _craftSel;
    static Dictionary<string, int> Cost(JsonElement e)
    {
        var c = new Dictionary<string, int>(); if (e.TryGetProperty("cost", out var k)) foreach (var p in k.EnumerateObject()) c[p.Name] = p.Value.GetInt32(); return c;
    }
    static bool CanPay(Dictionary<string, int> cost) { foreach (var (id, n) in cost) if (Game.D.Mats.GetValueOrDefault(id) < n) return false; return true; }
    static void Pay(Dictionary<string, int> cost) { var m = Game.D.Mats; foreach (var (id, n) in cost) { m[id] = m.GetValueOrDefault(id) - n; if (m[id] <= 0) m.Remove(id); } }
    static int Missing(Dictionary<string, int> cost) => cost.Sum(kv => Math.Max(0, kv.Value - Game.D.Mats.GetValueOrDefault(kv.Key)));
    static RecipeSlot IngSlot(string id, float size)
    {
        if (id.StartsWith("blk_") && Blocks.Exists(id[4..])) return new RecipeSlot { Cube = Blocks.Tint(Blocks.Def(Blocks.Id(id[4..]))), Frame = new Color(.55f, .46f, .3f), CustomMinimumSize = new Vector2(size, size), MouseFilter = Control.MouseFilterEnum.Ignore };
        var m = GatherDb.Mat(id); return new RecipeSlot { Icon = m.Name, Frame = m.Col, CustomMinimumSize = new Vector2(size, size), MouseFilter = Control.MouseFilterEnum.Ignore };
    }

    /// <summary>
    /// A-26: the old "open crafting" entries (the pack's Crafting / Fuse gems buttons, dev tools). Crafting now happens at a station:
    /// with the right station block within reach this opens it on that recipe list, otherwise it says which station is needed and
    /// opens hand crafting (the 2x2 grid: planks, torches, a crafting table...).
    /// </summary>
    public void OpenCraft(string tab)
    {
        string st = Stations.For(tab);
        if (st != "hand" && !Stations.Near(st))
        {
            Note($"{Stations.TabName(tab)}: you need a {Stations.Get(st).Name} within reach. Hand crafting makes a Crafting Table.", new Color(1f, .8f, .5f));
            OpenStation("hand"); return;
        }
        OpenStation(st, tab);
    }

    /// <summary>The recipes of one list (tab): blocks, gear, elem, sets, gems, pots; "hand" = the hand subset of blocks.</summary>
    List<CraftRec> CraftRecs(string tab, out string intro, out string empty)
    {
        var d = Game.D;
        if (tab == "hand")
        {
            var all = CraftRecs("blocks", out _, out empty); intro = "Hand crafting: the basics. Everything else needs a station: a Crafting Table (blocks, tools), a Smithing Table (gear), a Forge (smelting), an Alchemy Table (potions).";
            return all.Where(r => r.Hand).ToList();
        }
        var c = LootTables.Root.GetProperty("crafting");
        bool Room() { if (d.Inv.Count >= 28) { Note("Pack is full", new Color(1, .5f, .5f)); return false; } return true; }
        void Give(Item it) { d.Inv.Add(it); Note("Crafted " + it.Name, it.Color); Sfx.Play("level"); Game.Notify(); }
        string NoMats() { Note("Not enough materials", new Color(1, .5f, .5f)); return ""; }
        intro = ""; var recs = new List<CraftRec>(); empty = "Nothing can be crafted here yet.";
        switch (tab)
        {
            case "blocks":
            {
                // block world (data/blocks.json "recipes"): costs are block ids or gather materials
                intro = "Blocks, furniture and tools. Dig blocks in build mode (" + Inputs.KeyOf(Inputs.BuildMode) + ") to collect them.";
                foreach (var p in Blocks.Root.GetProperty("recipes").EnumerateObject())
                {
                    if (p.Value.ValueKind != JsonValueKind.Object) continue;
                    string outId = JsonUtil.S(p.Value, "block", p.Name); if (!Blocks.Exists(outId)) continue;
                    int n = (int)JsonUtil.F(p.Value, "out", 1);
                    var cost = new Dictionary<string, int>(); string[]? shape = null; Dictionary<char, string>? keyMap = null;
                    if (p.Value.TryGetProperty("shape", out var shj) && shj.ValueKind == JsonValueKind.Array && p.Value.TryGetProperty("key", out var kj) && kj.ValueKind == JsonValueKind.Object)
                    {   // shaped: the cost is counted from the pattern
                        shape = shj.EnumerateArray().Select(x => x.GetString() ?? "").ToArray(); keyMap = new();
                        foreach (var k in kj.EnumerateObject()) if (k.Name.Length > 0) keyMap[k.Name[0]] = Blocks.DropItem(k.Value.GetString() ?? "");
                        foreach (var row in shape) foreach (char ch in row) if (keyMap.TryGetValue(ch, out var mid)) cost[mid] = cost.GetValueOrDefault(mid) + 1;
                    }
                    else if (p.Value.TryGetProperty("cost", out var cj)) foreach (var k in cj.EnumerateObject()) cost[Blocks.DropItem(k.Name)] = k.Value.GetInt32();
                    if (cost.Count == 0) continue;
                    string bn = Blocks.Def(Blocks.Id(outId)).Name;
                    recs.Add(new CraftRec
                    {
                        Hand = p.Value.TryGetProperty("hand", out var hj) && hj.ValueKind == JsonValueKind.True, Shape = shape, KeyMap = keyMap,
                        Title = bn, Sub = $"Makes {n} {bn}", Cost = cost, Out = n, Cube = Blocks.Tint(Blocks.Def(Blocks.Id(outId))), Col = Colors.White, Repeat = true,
                        Do = () =>
                        {
                            if (!CanPay(cost)) { Note("Not enough blocks", new Color(1, .5f, .5f)); return false; }
                            Pay(cost); var m = d.Mats; string item = Blocks.Item(outId); m[item] = m.GetValueOrDefault(item) + n;
                            Note($"Crafted {n} {bn}", UiTheme.Highlight); Sfx.Play("level"); Game.Notify(); return true;
                        },
                    });
                }
                // things made from blocks that are not blocks (data/blocks.json "items"): the boat (F-21, Boat.cs), kept in the materials bag
                if (Blocks.Root.TryGetProperty("items", out var bItems))
                    foreach (var p in bItems.EnumerateObject())
                    {
                        if (p.Value.ValueKind != JsonValueKind.Object) continue;
                        string key = p.Name, nm = p.Value.TryGetProperty("n", out var nmE) ? nmE.GetString() ?? key : key; int n = (int)JsonUtil.F(p.Value, "out", 1);
                        var cost = new Dictionary<string, int>();
                        foreach (var k in p.Value.GetProperty("cost").EnumerateObject()) cost[Blocks.DropItem(k.Name)] = k.Value.GetInt32();
                        recs.Add(new CraftRec
                        {
                            Title = nm, Sub = p.Value.TryGetProperty("sub", out var sb) ? sb.GetString() ?? "" : $"Makes {n} {nm}", Cost = cost, Out = n, Col = Colors.White, Repeat = true,
                            Cube = Blocks.Exists("planks") ? (Color?)Blocks.Tint(Blocks.Def(Blocks.Id("planks"))) : null,
                            Do = () =>
                            {
                                if (!CanPay(cost)) { Note("Not enough blocks", new Color(1, .5f, .5f)); return false; }
                                Pay(cost); d.Mats[key] = d.Mats.GetValueOrDefault(key) + n;
                                Note($"Crafted {n} {nm} (you have {d.Mats[key]})", UiTheme.Highlight); Sfx.Play("level"); Game.Notify(); return true;
                            },
                        });
                    }
                // the fishing rod (data/fishing.json "rod": timber and leather strips; Fishing.cs)
                if (d.Rod <= 0)
                {
                    var rodCost = new Dictionary<string, int>(); if (FishDb.RodData().TryGetProperty("cost", out var rcj)) foreach (var k in rcj.EnumerateObject()) rodCost[k.Name] = k.Value.GetInt32();
                    recs.Add(new CraftRec
                    {
                        Group = "Tools", Title = "Fishing Rod", Sub = "Take it out with 7; attack casts, strikes and reels", Cost = rodCost, Col = UiTheme.Highlight, Btn = "Craft", Icon = "fishing_rod",
                        Do = () =>
                        {
                            if (d.Rod > 0) return false; if (!CanPay(rodCost)) { Note("Not enough materials", new Color(1, .5f, .5f)); return false; }
                            Pay(rodCost); d.Rod = 1; Note("Crafted a Fishing Rod: press 7 to take it out", UiTheme.Highlight); Sfx.Play("level"); Game.Notify(); return true;
                        },
                    });
                }
                InstrumentRecipes(recs);   // F-40: lute, flute, drum, harp (Instruments.cs, data/instruments.json)
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
            case "gems":   // UiGems.cs: fuse, break, cut (F-10)
                intro = "Fuse 3 gems of the same kind and tier into 1 of the next tier, break gems into Gem Dust, or cut new gems from dust. Socket gems in the pack's Gems tab.";
                GemRecipes(recs); break;
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
                // Soul Cubes and pet gear (data/pets.json "cubes" craft costs, "gear.craft")
                foreach (var cb in PetDb.Cubes)
                {
                    if (cb.Craft.Count == 0) continue; var cdef = cb; var cost = new Dictionary<string, int>(cb.Craft);
                    recs.Add(new CraftRec
                    {
                        Group = "Soul Cubes   bind weakened monsters as pets", Title = cdef.Name, Sub = $"x{cdef.Mul:0.#} catch chance (bosses x{cdef.BossMul:0.#}). You carry {Cubes.Count(cdef.Id)}", Btn = "Bind", Cost = cost, Cube = cdef.Col, Tier = 1, Repeat = true, Col = cdef.Col.Lightened(.2f),
                        Do = () => { if (!CanPay(cost)) { NoMats(); return false; } Pay(cost); Cubes.Give(cdef.Id, 1); Sfx.Play("pickup"); return true; },
                    });
                }
                if (PetDb.Root.GetProperty("gear").TryGetProperty("craft", out var pg))
                    foreach (var e in pg.EnumerateArray())
                    {
                        string ps = JsonUtil.S(e, "slot", "pet_collar"); int rar = (int)JsonUtil.F(e, "rar", 1); var cost = Cost(e);
                        recs.Add(new CraftRec
                        {
                            Group = "Pet gear   collars, barding and charms", Title = $"{Items.RarityName(rar)} {PetDb.GearName(ps).ToLower()}", Sub = $"Level {d.Level}. Equip it in the Pets menu", Cost = cost, Col = Items.RarityColor(rar), Tier = rar, Repeat = true, Icon = ps,
                            Do = () => { if (!Room()) return false; if (!CanPay(cost)) { NoMats(); return false; } Pay(cost); Give(PetDb.RollGear(d.Level, 0, ps, rar)); return true; },
                        });
                    }
                // A-26: the alchemy table's potions (data/stations.json "potions"): materials "pot_<id>", drunk from the pack
                Stations.Load();
                foreach (var po in Stations.Potions.Values)
                {
                    var pd = po; var pm = GatherDb.Mat(pd.Mat); var cost = new Dictionary<string, int>(pd.Cost);
                    recs.Add(new CraftRec
                    {
                        Group = "Alchemy   brewed from herbs and monster parts", Title = pm.Name, Sub = Stations.PotionText(pd) + $". You carry {d.Mats.GetValueOrDefault(pd.Mat)}", Btn = "Brew", Cost = cost, Out = pd.Out, Icon = "hp", Col = pm.Col, Tier = 1, Repeat = true,
                        Do = () =>
                        {
                            if (!CanPay(cost)) { NoMats(); return false; }
                            Pay(cost); d.Mats[pd.Mat] = d.Mats.GetValueOrDefault(pd.Mat) + pd.Out; Sfx.Play("pickup"); Game.Notify(); return true;
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
        return recs;
    }
}
