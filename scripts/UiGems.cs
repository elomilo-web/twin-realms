using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TwinRealms;

/// <summary>
/// Gems in the UI (focus F-10, Gems.cs): the pack's Gems tab (loose gems + the sockets of worn gear; drag a gem onto a socket or a
/// worn item, or click a gem then a socket for the gamepad; click a filled socket to take its gem back), the gem tooltip, and the
/// Crafting > Gems tab (fuse 3 into the next tier with the result shown first, break gems into Gem Dust, cut gems from dust).
/// </summary>
public partial class Ui
{
    static string _gemSel = "";   // the gem picked for the click flow ("" = none)
    static readonly Color GemPink = new("e07ad0");

    void GemsPane(GridContainer host, int cell, int gap, float width)
    {
        var d = Game.D; var all = new VBoxContainer { CustomMinimumSize = new Vector2(width, 0) }; all.AddThemeConstantOverride("separation", UiTheme.Gap); host.AddChild(all);
        // worn gear's sockets first (always in view), the loose gems under them
        var sv = new VBoxContainer(); sv.AddThemeConstantOverride("separation", UiTheme.RowGap); all.AddChild(sv);
        var v = new VBoxContainer(); v.AddThemeConstantOverride("separation", UiTheme.RowGap); all.AddChild(v);
        var stacks = GemDb.Stacks(d); if (_gemSel != "" && GemDb.Count(d, _gemSel) <= 0) _gemSel = "";
        Lbl($"LOOSE GEMS   {GemDb.Total(d)} gems, {d.Mats.GetValueOrDefault(GemDb.Dust)} Gem Dust", 13, UiTheme.Accent, v);
        if (stacks.Count == 0) { var l = Row(v, "No gems yet. Monsters drop them now and then (more, and finer ones, deep in dungeons); you can also cut them from Gem Dust at Crafting > Gems.", Dim, 13); l.CustomMinimumSize = new Vector2(width, 0); }
        else
        {
            var g = new GridContainer { Columns = 7 }; g.AddThemeConstantOverride("h_separation", gap); g.AddThemeConstantOverride("v_separation", gap); v.AddChild(g);
            var cells = new List<ItemSlot>();
            foreach (var key in stacks)
            {
                string k = key; var c = new ItemSlot { Gem = k, Count = GemDb.Count(d, k), DragGem = k, Selected = k == _gemSel, CustomMinimumSize = new Vector2(cell, cell) }; g.AddChild(c); cells.Add(c);
                c.Hover += on => { if (on) ShowGemTip(k, c); else HideTip(); };
                // no rebuild on click: the cell must live on for a drag that starts with this press
                c.Clicked += right => { if (right) return; _gemSel = _gemSel == k ? "" : k; Sfx.Play("click"); foreach (var x in cells) { x.Selected = x.Gem == _gemSel; x.QueueRedraw(); } };
            }
        }
        Lbl("SOCKETS ON WORN GEAR", 13, UiTheme.Accent, sv); int rows = 0;
        foreach (var slot in PlayerStats.Slots)
        {
            var it = d.Eq.GetValueOrDefault(slot); if (it == null || it.Sockets <= 0) continue; rows++;
            var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 6); sv.AddChild(h);
            var ic = new ItemSlot { Item = it, CustomMinimumSize = new Vector2(36, 36) }; h.AddChild(ic);
            ic.Hover += on => ShowTip(on ? it : null, ic, null); ic.DropGem = k => SocketGem(it, -1, k);
            ic.Clicked += right => { if (!right && _gemSel != "") SocketGem(it, -1, _gemSel); };
            var nm = Lbl(it.Name, 13, it.Color.Lightened(.15f), h); nm.CustomMinimumSize = new Vector2(150, 0); nm.ClipText = true; nm.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; nm.VerticalAlignment = VerticalAlignment.Center;
            for (int i = 0; i < it.Sockets; i++)
            {
                int si = i; string have = GemDb.At(it, i);
                var s = new ItemSlot { Gem = have, Socket = have == "", CustomMinimumSize = new Vector2(36, 36), TooltipText = have == "" ? "Empty socket: drop a gem here" : "" }; h.AddChild(s);
                if (have != "") s.Hover += on => { if (on) ShowGemTip(have, s, true); else HideTip(); };
                s.DropGem = k =>
                {
                    if (have != "") { var e = GemDb.Unsocket(d, it, si); if (e != null) { Note(e, ErrRed); Sfx.UiError(); return; } }
                    SocketGem(it, si, k);
                };
                s.Clicked += right =>
                {
                    HideTip();
                    if (have != "")
                    {
                        int cost = GemDb.RemoveGold(have); var e = GemDb.Unsocket(d, it, si);
                        if (e != null) { Note(e, ErrRed); Sfx.UiError(); return; }
                        Note($"{GemDb.Name(have)} is back in your gem bag" + (cost > 0 ? $" (-{cost} G)" : ""), GemDb.Col(have).Lightened(.2f)); Sfx.Play("click"); Game.Notify(); OpenPack();
                    }
                    else if (_gemSel != "") SocketGem(it, si, _gemSel);
                    else Note("Pick a gem first (click it), then this socket", Dim);
                };
            }
        }
        if (rows == 0) Row(sv, "Nothing you wear has sockets. Gear rolls 1 to 4 sockets when it drops or is crafted.", Dim, 13).CustomMinimumSize = new Vector2(width, 0);
    }

    void SocketGem(Item it, int idx, string key)
    {
        var d = Game.D; HideTip();
        var e = GemDb.Socket(d, it, idx, key);
        if (e != null) { Note(e, ErrRed); Sfx.UiError(); return; }
        if (GemDb.Count(d, key) <= 0) _gemSel = "";
        Note($"{GemDb.Name(key)} set into {it.Name}: {GemDb.StatText(key)}", GemDb.Col(key).Lightened(.2f)); Sfx.Play("pickup"); Game.Notify(); OpenPack();
    }

    void ShowGemTip(string key, Control anchor, bool socketed = false)
    {
        if (_tip == null || !IsInstanceValid(_tip)) { _tip = new ItemTip(); AddChild(_tip); }
        _tip.ShowGem(key, socketed ? 0 : GemDb.Count(Game.D, key), anchor.GetGlobalRect(), GetViewport().GetVisibleRect().Size);
    }

    /// <summary>Opens the pack on a tab: "items", "materials", "gems" or "filter" (dev capture views).</summary>
    public void OpenPackTab(string tab) { _packTab = tab == "filter" ? 3 : tab == "gems" ? 2 : tab == "materials" ? 1 : 0; OpenPack(); }
    /// <summary>Dev capture: the tooltip of the open window's cell showing this item.</summary>
    public void DevHoverItem(Item? it)
    {
        if (it == null) return;
        ItemSlot? Find(Node n) { if (n is ItemSlot s && s.Item == it && s.IsVisibleInTree()) return s; foreach (var c in n.GetChildren()) if (Find(c) is { } f) return f; return null; }
        if (Find(this) is { } cell) cell.SimulateHover();
    }

    /// <summary>Crafting > Gems: fuse (result shown in the detail pane before the Fuse button), break into dust, cut from dust.</summary>
    void GemRecipes(List<CraftRec> recs)
    {
        var d = Game.D; var stacks = GemDb.Stacks(d);
        foreach (var key in stacks.Where(k => GemDb.Tier(k) < GemDb.MaxTier).OrderByDescending(k => GemDb.Count(d, k) >= 3))
        {
            string k = key, next = GemDb.Next(key); int have = GemDb.Count(d, k);
            var cost = new Dictionary<string, int>(); if (GemDb.FuseDust(k) > 0) cost[GemDb.Dust] = GemDb.FuseDust(k);
            recs.Add(new CraftRec
            {
                Group = "Fuse   3 of a kind make 1 of the next tier", Title = $"Fuse 3 {GemDb.Name(k)}", Sub = $"Result: {GemDb.Name(next)}\n{GemDb.StatText(next)}", Btn = "Fuse",
                Cost = cost, Icon = "gem:" + next, Col = GemDb.Col(next), Tier = GemDb.Tier(next), Repeat = true, Blocked = have < 3 ? $"You have {have} of the 3 needed" : null,
                Extra = { $"Uses: 3 x {GemDb.Name(k)} ({GemDb.StatText(k)}), you have {have}", $"Makes: 1 x {GemDb.Name(next)} ({GemDb.StatText(next)})" },
                Do = () =>
                {
                    var e = GemDb.Fuse(d, k); if (e != null) { Note(e, ErrRed); return false; }
                    Note($"Fused a {GemDb.Name(next)}: {GemDb.StatText(next)}", GemDb.Col(next).Lightened(.2f)); Sfx.Play("level"); Game.Notify(); return true;
                },
            });
        }
        foreach (var key in stacks)
        {
            string k = key; int have = GemDb.Count(d, k);
            recs.Add(new CraftRec
            {
                Group = "Break   a gem into Gem Dust (fusing and crafting use it)", Title = $"Break {GemDb.Name(k)}", Sub = $"Gives {GemDb.DustOf(k)} Gem Dust. You have {have}", Btn = "Break",
                Icon = "gem:" + k, Col = GemDb.Col(k), Tier = GemDb.Tier(k), Repeat = true, Extra = { $"The gem: {GemDb.StatText(k)}" },
                Do = () => { int n = GemDb.Break(d, k); if (n == 0) return false; Note($"Broke {GemDb.Name(k)}: +{n} Gem Dust", GemPink); Sfx.Play("hit"); Game.Notify(); return true; },
            });
        }
        var cc = new Dictionary<string, int> { [GemDb.Dust] = GemDb.CutDust };
        recs.Add(new CraftRec
        {
            Group = "Cut   a new gem from Gem Dust", Title = $"Cut a {GemDb.TierNames[0]} gem", Sub = "A random kind of gem", Btn = "Cut", Cost = cc, Icon = "gem:ruby:0", Col = GemPink, Tier = 1, Repeat = true,
            Do = () =>
            {
                if (!CanPay(cc)) { Note("Not enough Gem Dust", ErrRed); return false; }
                Pay(cc); var g = GemDb.Roll(0); GemDb.Give(d, g); Note($"Cut a {GemDb.Name(g)}: {GemDb.StatText(g)}", GemDb.Col(g).Lightened(.2f)); Sfx.Play("pickup"); Game.Notify(); return true;
            },
        });
    }
}
