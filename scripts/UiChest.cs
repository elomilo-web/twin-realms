using System;
using System.Collections.Generic;
using Godot;

namespace TwinRealms;

public partial class Ui
{
    /// <summary>
    /// A placed chest's window (focus F-15, the Home Storage pattern): pack on the left, the chest's slots on the right, both in fixed-size
    /// scroll boxes (the window never stretches). Click an item to move it across. Contents: PlayerStats.Chests[key] (BlockUse.ChestItems).
    /// </summary>
    public void OpenChest(string key, string title, int capacity)
    {
        var d = Game.D; var box = BlockUse.ChestItems(key);
        void Re() => OpenChest(key, title, capacity);
        var v = Frame("chest", title, new Vector2(1000, 600), Re);
        var top = new HBoxContainer(); v.AddChild(top); top.AddThemeConstantOverride("separation", 16);
        Lbl($"Pack {d.Inv.Count}/28", 16, d.Inv.Count >= 28 ? ErrRed : Gold, top);
        Lbl($"Chest {box.Count}/{capacity}", 16, box.Count >= capacity ? ErrRed : Gold, top);
        Lbl("Click to move between pack and chest.", 14, Dim, top).SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        top.AddChild(Btn("Store all", () => { while (d.Inv.Count > 0 && box.Count < capacity) { box.Add(d.Inv[0]); d.Inv.RemoveAt(0); } Sfx.Play("click"); Game.Notify(); Re(); }, true));
        top.AddChild(Btn("Take all", () => { while (box.Count > 0 && d.Inv.Count < 28) { d.Inv.Add(box[0]); box.RemoveAt(0); } Sfx.Play("click"); Game.Notify(); Re(); }, true));
        var cols = new HBoxContainer(); cols.AddThemeConstantOverride("separation", 24); v.AddChild(cols);
        const float Cell = 58, Gap = 5, BoxH = 7 * Cell + 6 * Gap + 4;
        GridContainer Grid(string head, int columns)
        {
            var b = new VBoxContainer(); cols.AddChild(b); Header(head, b);
            var s = new ScrollContainer { CustomMinimumSize = new Vector2(columns * Cell + (columns - 1) * Gap + 14, BoxH), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; b.AddChild(s);
            var g = new GridContainer { Columns = columns }; g.AddThemeConstantOverride("h_separation", (int)Gap); g.AddThemeConstantOverride("v_separation", (int)Gap); s.AddChild(g); return g;
        }
        var pg = Grid("PACK", 4); var cg = Grid("CHEST", 9);
        for (int i = 0; i < Math.Max(28, (d.Inv.Count + 3) / 4 * 4); i++)
        {
            int k = i; var it = i < d.Inv.Count ? d.Inv[i] : null; var cell = new ItemSlot { Item = it, CustomMinimumSize = new Vector2(Cell, Cell) }; pg.AddChild(cell); if (it == null) continue;
            cell.Hover += on => ShowTip(on ? it : null, cell, d.Eq.GetValueOrDefault(it.Slot));
            cell.Clicked += _ => { HideTip(); if (box.Count >= capacity) { Note("The chest is full", ErrRed); return; } d.Inv.RemoveAt(k); box.Add(it); Sfx.Play("click"); Game.Notify(); Re(); };
        }
        for (int i = 0; i < Math.Max(capacity, box.Count); i++)
        {
            int k = i; var it = i < box.Count ? box[i] : null; var cell = new ItemSlot { Item = it, CustomMinimumSize = new Vector2(Cell, Cell) }; cg.AddChild(cell); if (it == null) continue;
            cell.Hover += on => ShowTip(on ? it : null, cell, d.Eq.GetValueOrDefault(it.Slot));
            cell.Clicked += _ => { HideTip(); if (d.Inv.Count >= 28) { Note("Pack is full", ErrRed); return; } box.RemoveAt(k); d.Inv.Add(it); Sfx.Play("click"); Game.Notify(); Re(); };
        }
    }
}
