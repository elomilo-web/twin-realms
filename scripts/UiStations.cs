using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>
/// A-26: the station windows. OpenStation is the crafting grid (2x2 by hand, 3x3 at a crafting table, smithing table or alchemy
/// table): the hero's materials on the left (click puts one on the grid, right-click five), the grid and its result in the middle,
/// the recipe book on the right (click a recipe and the grid fills itself). A recipe is found from what lies on the grid: shaped
/// recipes by their pattern, shapeless ones by exactly their materials. Nothing leaves the pack until Craft is pressed, so closing
/// the window loses nothing. OpenForge is the furnace of one forge block. Also here: potions drunk from the pack, the merchant's seeds.
/// </summary>
public partial class Ui
{
    static string _stn = "", _stnTab = "", _bookSel = ""; static readonly string[] _gMat = { "", "", "", "", "", "", "", "", "" }; static readonly int[] _gN = new int[9]; static int _gSel;
    static void GridClear() { for (int i = 0; i < 9; i++) { _gMat[i] = ""; _gN[i] = 0; } _gSel = 0; }

    public void OpenStation(string station, string? tab = null)
    {
        Stations.Load(); var def = Stations.Get(station); station = def.Id;
        if (def.Tabs.Length == 0) { Note($"Press {Inputs.KeyOf(Inputs.Use)} on the {def.Name} block itself", Dim); return; }
        if (tab == null || !def.Tabs.Contains(tab)) tab = def.Tabs[0];
        if (_stn != station || _stnTab != tab) { GridClear(); _bookSel = ""; _stn = station; _stnTab = tab; }
        string tb = tab; var d = Game.D; int G = Math.Clamp(def.Grid, 2, 3);
        void Re() => OpenStation(station, tb);
        var v = Frame("craft", def.Name, new Vector2(1040, 600), Re);
        var recs = CraftRecs(tb, out string intro, out string empty);

        // ---- the grid's logic
        bool Used(int i) => i % 3 < G && i / 3 < G;
        int Total(string m) { int t = 0; for (int i = 0; i < 9; i++) if (_gMat[i] == m) t += _gN[i]; return t; }
        bool GridEmpty() { for (int i = 0; i < 9; i++) if (_gN[i] > 0) return false; return true; }
        for (int i = 0; i < 9; i++) if (!Used(i) || _gN[i] <= 0) { _gMat[i] = ""; _gN[i] = 0; }
        bool Match(CraftRec r, out int times)
        {
            times = 0; if (r.Cost.Count == 0) return false;
            if (r.Shape != null && r.KeyMap != null)
            {
                int minR = 9, maxR = -1, minC = 9, maxC = -1;
                for (int i = 0; i < 9; i++) if (_gN[i] > 0) { minR = Math.Min(minR, i / 3); maxR = Math.Max(maxR, i / 3); minC = Math.Min(minC, i % 3); maxC = Math.Max(maxC, i % 3); }
                if (maxR < 0) return false; int h = r.Shape.Length, wd = r.Shape.Max(s => s.Length);
                if (maxR - minR + 1 != h || maxC - minC + 1 != wd) return false; int best = int.MaxValue;
                for (int y = 0; y < h; y++) for (int x = 0; x < wd; x++)
                {
                    char ch = x < r.Shape[y].Length ? r.Shape[y][x] : ' '; int i = (minR + y) * 3 + minC + x;
                    string want = r.KeyMap.TryGetValue(ch, out var m) ? m : "";
                    if (want == "") { if (_gN[i] > 0) return false; continue; }
                    if (_gN[i] <= 0 || _gMat[i] != want) return false; best = Math.Min(best, _gN[i]);
                }
                if (best == int.MaxValue) return false; times = best; return true;
            }
            var tot = new Dictionary<string, int>(); for (int i = 0; i < 9; i++) if (_gN[i] > 0) tot[_gMat[i]] = tot.GetValueOrDefault(_gMat[i]) + _gN[i];
            if (tot.Count != r.Cost.Count) return false; int tm = int.MaxValue;
            foreach (var (m, need) in r.Cost) { if (!tot.TryGetValue(m, out int have) || have < need) return false; tm = Math.Min(tm, have / Math.Max(1, need)); }
            times = tm; return true;
        }
        void Fill(CraftRec r)
        {
            GridClear(); _bookSel = r.Title;
            if (r.Shape != null && r.KeyMap != null && r.Shape.Length <= G && r.Shape.Max(s => s.Length) <= G)
            {
                for (int y = 0; y < r.Shape.Length; y++) for (int x = 0; x < r.Shape[y].Length; x++)
                    if (r.KeyMap.TryGetValue(r.Shape[y][x], out var m) && m != "") { _gMat[y * 3 + x] = m; _gN[y * 3 + x] = 1; }
            }
            else if (r.Shape == null && r.Cost.Count <= G * G)
            {
                int k = 0; foreach (var (m, need) in r.Cost) { while (k < 9 && !Used(k)) k++; if (k >= 9) break; _gMat[k] = m; _gN[k] = need; k++; }
            }
        }
        // the recipe the grid shows: the one picked in the book if the grid matches it, else the largest match
        CraftRec? cur = null; int curTimes = 0; bool onGrid = false, direct = false;
        var sel = recs.Find(r => r.Title == _bookSel);
        if (sel != null && Match(sel, out int st0)) { cur = sel; curTimes = st0; onGrid = true; }
        else
        {
            int bestCost = -1;
            foreach (var r in recs) if (Match(r, out int t)) { int cs = r.Cost.Values.Sum(); if (cs > bestCost) { bestCost = cs; cur = r; curTimes = t; onGrid = true; } }
        }
        if (cur == null && sel != null)
        {   // recipes the grid cannot hold (no materials at all: gem work; or too many kinds): made straight from the book
            direct = sel.Cost.Count == 0 || sel.Cost.Count > G * G || (sel.Shape != null && (sel.Shape.Length > G || sel.Shape.Max(s => s.Length) > G));
            if (direct || GridEmpty()) { cur = sel; curTimes = 1; }
        }
        void Craft(int times)
        {
            if (cur == null) return; bool any = false; var r = cur;
            for (int k = 0; k < times; k++)
            {
                if (!r.Do()) break; any = true; CraftedLife(r.Cost);   // F-25: crafting XP and its perk (UiLife.cs)
                if (direct) continue;
                if (r.Shape != null) { for (int i = 0; i < 9; i++) if (_gN[i] > 0 && --_gN[i] == 0) _gMat[i] = ""; }
                else foreach (var (m, need) in r.Cost)
                {
                    int left = need;
                    for (int i = 0; i < 9 && left > 0; i++) if (_gMat[i] == m && _gN[i] > 0) { int take = Math.Min(left, _gN[i]); _gN[i] -= take; left -= take; if (_gN[i] == 0) _gMat[i] = ""; }
                }
            }
            if (any && !direct && GridEmpty() && r.Blocked == null && CanPay(r.Cost)) Fill(r);   // ready for the next one
            if (any) { Sfx.UiConfirm(); Re(); } else Sfx.UiError();
        }
        void AddMat(string m, int n)
        {
            n = Math.Min(n, d.Mats.GetValueOrDefault(m) - Total(m)); if (n <= 0) { Sfx.UiError(); return; }
            int t = -1;
            if (Used(_gSel) && (_gN[_gSel] == 0 || _gMat[_gSel] == m)) t = _gSel;
            if (t < 0) for (int i = 0; i < 9; i++) if (Used(i) && _gN[i] > 0 && _gMat[i] == m) { t = i; break; }
            if (t < 0) for (int i = 0; i < 9; i++) if (Used(i) && _gN[i] == 0) { t = i; break; }
            if (t < 0) { Sfx.UiError(); return; }
            _gMat[t] = m; _gN[t] += n; _gSel = t; Re();
        }

        // ---- top row: the station's recipe lists, the smith's bench, hand crafting, the pack
        var top = new HBoxContainer(); top.AddThemeConstantOverride("separation", 6); v.AddChild(top);
        var tabsHost = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; top.AddChild(tabsHost);
        if (def.Tabs.Length > 1) CraftTabs(tabsHost, tb, def.Tabs.Select(t => (t, Stations.TabName(t), (Action)(() => OpenStation(station, t)))).ToArray());
        else Lbl(Stations.TabName(tb), 15, UiTheme.Highlight, tabsHost);
        if (station == "smith") top.AddChild(Btn("Smith's bench (hone, reroll, repair)", OpenSmith, true));
        if (station != "hand") top.AddChild(Btn("Hand crafting", () => OpenStation("hand"), true));
        top.AddChild(Btn("Pack", OpenPack, true));
        if (intro != "") Row(v, intro, Dim, 13);
        if (tb == "gear" || tb == "elem")
        {
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 4); v.AddChild(row);
            foreach (var s in FilterSlots) { string sl = s; var b = Btn(Cap(s), () => { _craftSlot = sl; _bookSel = ""; Re(); }, true); Mark(b, _craftSlot == s); b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(b); }
        }
        if (tb == "elem")
        {
            var er = new HBoxContainer(); er.AddThemeConstantOverride("separation", 4); v.AddChild(er);
            foreach (var p in LootTables.Root.GetProperty("scraps").GetProperty("element").EnumerateObject()) { string el = p.Name; var b = Btn(GearFx.Get(el)?.Word ?? el, () => { _craftElem = el; _bookSel = ""; GridClear(); Re(); }, true); Mark(b, _craftElem == el); b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; er.AddChild(b); }
        }

        var cols = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; cols.AddThemeConstantOverride("separation", 14); v.AddChild(cols);
        // ---- left: materials
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(292, 0) }; left.AddThemeConstantOverride("separation", 4); cols.AddChild(left);
        Lbl("YOUR MATERIALS", 13, UiTheme.Highlight, left);
        var ms = new ScrollContainer { CustomMinimumSize = new Vector2(292, 400), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; left.AddChild(ms);
        var mg = new GridContainer { Columns = 6 }; mg.AddThemeConstantOverride("h_separation", 4); mg.AddThemeConstantOverride("v_separation", 4); ms.AddChild(mg);
        int shownMats = 0;
        foreach (var kv in d.Mats.OrderBy(k => GatherDb.Mat(k.Key).Name))
        {
            if (kv.Value <= 0) continue; var m = GatherDb.Mat(kv.Key); string id = kv.Key; int onG = Total(id); shownMats++;
            var cell = new ItemSlot { IconId = m.Name, Count = kv.Value - onG, Tint = m.Col, CustomMinimumSize = new Vector2(44, 44), TooltipText = $"{m.Name} x{kv.Value}{(onG > 0 ? $" ({onG} on the grid)" : "")}\nClick: put 1 on the grid   Right-click: 5" };
            cell.Clicked += right => AddMat(id, right ? 5 : 1); mg.AddChild(cell);
        }
        if (shownMats == 0) CraftHint(left, "Your pack holds no materials yet.", 13);

        // ---- middle: the grid and its result
        var mid = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) }; mid.AddThemeConstantOverride("separation", 6); cols.AddChild(mid);
        Lbl(G == 2 ? "HAND GRID 2 x 2" : "CRAFTING GRID 3 x 3", 13, UiTheme.Highlight, mid);
        var gg = new GridContainer { Columns = G }; gg.AddThemeConstantOverride("h_separation", 5); gg.AddThemeConstantOverride("v_separation", 5); mid.AddChild(gg);
        for (int y = 0; y < G; y++) for (int x = 0; x < G; x++)
        {
            int i = y * 3 + x; var m = _gN[i] > 0 ? GatherDb.Mat(_gMat[i]) : null;
            var cell = new ItemSlot { IconId = m?.Name ?? "", Count = _gN[i], Tint = m?.Col ?? Colors.White, Selected = i == _gSel, CustomMinimumSize = new Vector2(62, 62), TooltipText = m != null ? $"{m.Name} x{_gN[i]}\nRight-click: take it off the grid" : "Click to choose this cell, then click a material" };
            cell.Clicked += right => { if (right) { _gMat[i] = ""; _gN[i] = 0; } _gSel = i; Re(); };
            gg.AddChild(cell);
        }
        var gb = new HBoxContainer(); gb.AddThemeConstantOverride("separation", 6); mid.AddChild(gb);
        gb.AddChild(Btn("Clear the grid", () => { GridClear(); _bookSel = ""; Re(); }, true));
        mid.AddChild(new HSeparator());
        if (cur == null) CraftHint(mid, GridEmpty() ? "Lay materials on the grid, or pick a recipe in the book: the grid fills itself." : "No recipe matches what is on the grid.", 13);
        else
        {
            var r = cur; bool ok = r.Blocked == null && CanPay(r.Cost) && (onGrid || direct);
            var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 10); mid.AddChild(head);
            head.AddChild(new RecipeSlot { Icon = r.Icon, Cube = r.Cube, Frame = r.Col, Tier = r.Tier, Badge = r.Out, Dimmed = !ok, CustomMinimumSize = new Vector2(72, 72), MouseFilter = Control.MouseFilterEnum.Ignore });
            var hv = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; hv.AddThemeConstantOverride("separation", 1); head.AddChild(hv);
            Row(hv, r.Title, r.Col.Lightened(.1f), 17); if (r.Sub != "") Row(hv, r.Sub, Dim, 12);
            foreach (var (id, need) in r.Cost) { int got = d.Mats.GetValueOrDefault(id); Lbl($"{GatherDb.Mat(id).Name}  {got} / {need}", 13, got >= need ? Good : Bad, mid); }
            if (r.Blocked != null) Row(mid, r.Blocked, new Color(1f, .6f, .5f), 13);
            foreach (var x in r.Extra) Row(mid, x, Dim, 12);
            if (!onGrid && !direct) CraftHint(mid, "Click the recipe in the book to lay it on the grid.", 12);
            var br = new HBoxContainer(); br.AddThemeConstantOverride("separation", 8); mid.AddChild(br);
            var cb = Btn(r.Btn, () => Craft(1)); cb.CustomMinimumSize = new Vector2(110, 34); cb.Disabled = !ok; cb.AddThemeColorOverride("font_color", ok ? UiTheme.Highlight : Dim); br.AddChild(cb);
            if (r.Repeat)
            {
                int many = direct ? 5 : Math.Max(curTimes, 5); var c5 = Btn($"{r.Btn} x{(direct || curTimes <= 1 ? 5 : curTimes)}", () => { if (direct || curTimes > 1) Craft(direct ? 5 : curTimes); else { for (int k = 0; k < 5; k++) { if (cur == null || !CanPay(cur.Cost)) break; if (GridEmpty()) Fill(cur); if (!cur.Do()) break; CraftedLife(cur.Cost); } GridClear(); if (cur != null && CanPay(cur.Cost)) Fill(cur); Sfx.UiConfirm(); Re(); } });
                c5.CustomMinimumSize = new Vector2(110, 34); c5.Disabled = !ok; c5.AddThemeColorOverride("font_color", ok ? UiTheme.Highlight : Dim); br.AddChild(c5); _ = many;
            }
        }

        // ---- right: the recipe book
        var book = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; book.AddThemeConstantOverride("separation", 4); cols.AddChild(book);
        Lbl($"RECIPE BOOK ({recs.Count})", 13, UiTheme.Highlight, book);
        var ls = Scroll(book, 400); ls.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled; var list = Inner(ls); list.AddThemeConstantOverride("separation", 4);
        if (recs.Count == 0) CraftHint(list, empty);
        string lastGroup = "";
        foreach (var r0 in recs)
        {
            var r = r0; if (r.Group != "" && r.Group != lastGroup) { lastGroup = r.Group; Lbl(r.Group, 12, UiTheme.Highlight, list); }
            bool ok = r.Blocked == null && CanPay(r.Cost), on = r == cur;
            var pc = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
            pc.AddThemeStyleboxOverride("panel", UiTheme.Box(ok ? new Color(.2f, .16f, .1f, .92f) : new Color(.07f, .06f, .06f, .85f), on ? UiTheme.Highlight : ok ? new Color(.55f, .46f, .3f, .8f) : new Color(.3f, .26f, .22f, .6f), on ? 2 : 1, 6, 4));
            list.AddChild(pc);
            var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 8); pc.AddChild(h);
            h.AddChild(new RecipeSlot { Icon = r.Icon, Cube = r.Cube, Frame = r.Col, Tier = r.Tier, Badge = r.Out, Dimmed = !ok, CustomMinimumSize = new Vector2(38, 38), MouseFilter = Control.MouseFilterEnum.Ignore });
            var tv = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; tv.AddThemeConstantOverride("separation", 0); h.AddChild(tv);
            var nl = Lbl(r.Title + (r.Shape != null ? "   (shaped)" : ""), 14, ok ? r.Col.Lightened(.2f) : Dim, tv); nl.ClipText = true; nl.CustomMinimumSize = new Vector2(100, 0); nl.MouseFilter = Control.MouseFilterEnum.Ignore;
            var sum = Lbl(r.Cost.Count == 0 ? "no ingredients" : string.Join(", ", r.Cost.Select(kv => $"{kv.Value} {GatherDb.Mat(kv.Key).Name}")), 11, ok ? new Color(.82f, .74f, .55f) : new Color(.5f, .48f, .46f), tv); sum.ClipText = true; sum.CustomMinimumSize = new Vector2(100, 0); sum.MouseFilter = Control.MouseFilterEnum.Ignore;
            pc.GuiInput += e => { if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left) { Sfx.Play("click"); Fill(r); Re(); } };
        }
    }

    // ---------------------------------------------------------------- the forge: a furnace per forge block
    public void OpenForge(string key)
    {
        Stations.Load(); var d = Game.D; var f = Stations.ForgeAt(key); Stations.ForgeRun(f);
        void Re() => OpenForge(key);
        var v = Frame("forge", "Forge", new Vector2(900, 560), Re);
        var top = new HBoxContainer(); top.AddThemeConstantOverride("separation", 8); v.AddChild(top);
        var il = Lbl("Put ore or scraps in the input and fuel in the fire: the forge smelts while you are away.", 13, Dim, top); il.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; il.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        top.AddChild(Btn("Refresh", Re, true)); top.AddChild(Btn("Smith's bench", OpenSmith, true));
        void Back(string m, int n) { if (n > 0 && m != "") d.Mats[m] = d.Mats.GetValueOrDefault(m) + n; }
        void Put(string m, int n)
        {
            n = Math.Min(n, d.Mats.GetValueOrDefault(m)); if (n <= 0) return;
            if (Stations.Smelts.ContainsKey(m) && (f.InN == 0 || f.In == m)) { n = Math.Min(n, Stations.Slot - f.InN); if (n <= 0) return; f.In = m; f.InN += n; }
            else if (Stations.Fuel.ContainsKey(m) && (f.FuelN == 0 || f.Fuel == m)) { n = Math.Min(n, Stations.Slot - f.FuelN); if (n <= 0) return; f.Fuel = m; f.FuelN += n; }
            else { Note("That slot holds something else: take it out first", new Color(1, .6f, .5f)); return; }
            d.Mats[m] -= n; if (d.Mats[m] <= 0) d.Mats.Remove(m); Game.Notify(); Re();
        }
        var cols = new HBoxContainer(); cols.AddThemeConstantOverride("separation", 24); v.AddChild(cols);
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(330, 0) }; cols.AddChild(left);
        Lbl("ORE, SCRAPS AND FUEL IN YOUR PACK", 13, UiTheme.Highlight, left);
        var mg = new GridContainer { Columns = 6 }; mg.AddThemeConstantOverride("h_separation", 4); mg.AddThemeConstantOverride("v_separation", 4); left.AddChild(mg); int shown = 0;
        foreach (var kv in d.Mats.OrderBy(k => GatherDb.Mat(k.Key).Name))
        {
            if (kv.Value <= 0) continue; bool sm = Stations.Smelts.TryGetValue(kv.Key, out var s), fu = Stations.Fuel.TryGetValue(kv.Key, out float secs); if (!sm && !fu) continue;
            var m = GatherDb.Mat(kv.Key); string id = kv.Key; int all = kv.Value; shown++;
            var cell = new ItemSlot { IconId = m.Name, Count = kv.Value, Tint = m.Col, CustomMinimumSize = new Vector2(48, 48), TooltipText = $"{m.Name} x{kv.Value}\n" + (sm ? $"Smelts: {s!.Need} -> {s.N} {GatherDb.Mat(s.Out).Name} in {s.Time:0} s" : $"Fuel: burns {secs:0} s") + "\nClick: put 1 in   Right-click: all" };
            cell.Clicked += right => Put(id, right ? all : 1); mg.AddChild(cell);
        }
        if (shown == 0) CraftHint(left, "Nothing to smelt or burn in your pack. Iron ore, iron scrap, gold ore and sand smelt; coal, logs and planks burn.", 13);
        Header("What smelts", left);
        foreach (var s in Stations.Smelts.Values) Lbl($"{s.Need} {GatherDb.Mat(s.In).Name}  ->  {s.N} {GatherDb.Mat(s.Out).Name}   ({s.Time:0} s)", 12, Parch, left);
        var right2 = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; right2.AddThemeConstantOverride("separation", 8); cols.AddChild(right2);
        var slots = new HBoxContainer(); slots.AddThemeConstantOverride("separation", 22); right2.AddChild(slots);
        void SlotBox(string title, string mat, int n, string hint, Action take)
        {
            var b = new VBoxContainer(); b.AddThemeConstantOverride("separation", 3); slots.AddChild(b); Lbl(title, 13, UiTheme.Highlight, b);
            var m = n > 0 ? GatherDb.Mat(mat) : null;
            var s = new ItemSlot { IconId = m?.Name ?? "", Count = n, Tint = m?.Col ?? Colors.White, CustomMinimumSize = new Vector2(76, 76), TooltipText = m != null ? $"{m.Name} x{n}\nClick: take it back" : "Empty" };
            s.Clicked += _ => take(); b.AddChild(s); var hl = Lbl(hint, 12, Dim, b); hl.CustomMinimumSize = new Vector2(120, 0); hl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        }
        Stations.Smelts.TryGetValue(f.In, out var cs);
        SlotBox("INPUT", f.In, f.InN, cs != null && f.InN > 0 ? $"{f.Prog:0.0} / {cs.Time:0} s" : "ore or scraps", () => { Back(f.In, f.InN); f.In = ""; f.InN = 0; f.Prog = 0; Game.Notify(); Re(); });
        SlotBox("FUEL", f.Fuel, f.FuelN, f.Burn > 0 ? $"burning: {f.Burn:0} s left" : "the fire is out", () => { Back(f.Fuel, f.FuelN); f.Fuel = ""; f.FuelN = 0; Game.Notify(); Re(); });
        SlotBox("OUTPUT", f.Out, f.OutN, f.OutN > 0 ? "click to take" : "nothing yet", () =>
        {
            if (f.OutN <= 0) return; Back(f.Out, f.OutN); Note($"+{f.OutN} {GatherDb.Mat(f.Out).Name}", GatherDb.Mat(f.Out).Col.Lightened(.3f));
            if (f.Xp > 0) { LifeSkills.Award("smithing", f.Xp); f.Xp = 0; }
            f.Out = ""; f.OutN = 0; Sfx.Play("pickup"); Game.Notify(); Re();
        });
        if (cs != null && f.InN >= cs.Need)
        {
            var bar = new ProgressBar { MinValue = 0, MaxValue = cs.Time, Value = f.Prog, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 10), MouseFilter = Control.MouseFilterEnum.Ignore }; right2.AddChild(bar);
            if (f.Burn <= 0 && f.FuelN <= 0) Row(right2, "The forge needs fuel.", new Color(1f, .6f, .5f), 13);
            else if (f.OutN > 0 && (f.Out != cs.Out || f.OutN + cs.N > Stations.Slot)) Row(right2, "Take the output out first.", new Color(1f, .6f, .5f), 13);
        }
        Header("What burns", right2);
        Row(right2, string.Join(",   ", Stations.Fuel.Select(kv => $"{GatherDb.Mat(kv.Key).Name} {kv.Value:0} s")), Parch, 12);
    }

    // ---------------------------------------------------------------- the pack's Materials tab: potions are drunk with a click
    void SteadPackCell(ItemSlot cell, string mat)
    {
        Stations.Load(); if (!Stations.Potions.TryGetValue(mat, out var po)) return;
        cell.TooltipText += $"\nClick: drink ({Stations.PotionText(po)})";
        cell.Clicked += _ => { if (Stations.Drink(mat)) OpenPack(); };
    }

    // ---------------------------------------------------------------- the merchant: seeds (data/farm.json "shop")
    void FarmShopRows(VBoxContainer list, string tab)
    {
        if (Farm.Node("shop") is not { ValueKind: JsonValueKind.Object } sh) return; var d = Game.D;
        foreach (var p in sh.EnumerateObject())
        {
            if (!Blocks.Exists(p.Name) || p.Value.ValueKind != JsonValueKind.Number) continue;
            string item = Blocks.Item(p.Name), nm = Blocks.Def(Blocks.Id(p.Name)).Name; int price = Cost(p.Value.GetInt32());
            AddRow(list, $"{nm}  (have {d.Mats.GetValueOrDefault(item)})", new Color(.7f, .9f, .6f), $"Buy {price}g", () =>
            {
                if (d.Gold < price) { Sfx.UiError(); Note("Not enough gold", new Color(1, .5f, .5f)); return; }
                d.Gold -= price; d.Mats[item] = d.Mats.GetValueOrDefault(item) + 1; Traded(); Sfx.Play("coin"); Game.Notify(); OpenShop(tab);
            }, d.Gold >= price);
        }
    }
}
