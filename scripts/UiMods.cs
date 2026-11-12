using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TwinRealms;

/// <summary>
/// Modifier tree menu (U): every skill modifier and basic attack modifier, in lanes by family (data/modtree.json).
/// Click a modifier to see what it does and what it fits; double-click (or Unlock) to learn it with skill points.
/// Each lane unlocks left to right. Unlocked modifiers are socketed in Skills (L) and Basic attacks (O).
/// </summary>
public partial class Ui
{
    static string _modSel = "";

    public void OpenMods()
    {
        ModTree.Load(); var d = Game.D;
        var v = Frame("mods", "Modifier Tree", new Vector2(1120, 640), OpenMods); HideInfo();
        if (_modSel == "" || ModTree.Get(_modSel) == null) _modSel = ModTree.Branches.SelectMany(b => b.Nodes).FirstOrDefault(n => ModTree.Why(n) == null)?.Key ?? ModTree.Branches[0].Nodes.FirstOrDefault()?.Key ?? "";

        var top = new HBoxContainer(); v.AddChild(top); top.AddThemeConstantOverride("separation", 10);
        Chip(top, $"Skill points  {d.RankPts}", d.RankPts > 0);
        Chip(top, $"Unlocked  {ModTree.Count(true)}/{ModTree.Count(false)}", false);
        Lbl("Each lane unlocks left to right.  Click: details   Double-click: unlock", 13, Dim, top).SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        top.AddChild(Btn($"Skills ({Inputs.KeyOf(Inputs.SkillsUi)})", OpenSkills, true));
        top.AddChild(Btn($"Basic attacks ({Inputs.KeyOf(Inputs.AttacksUi)})", () => OpenAttacks(null), true));

        var cols = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; cols.AddThemeConstantOverride("separation", 14); v.AddChild(cols);
        var sc = new ScrollContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(640, 300) }; cols.AddChild(sc);
        var lanes = new ModLanes { Sel = _modSel }; sc.AddChild(lanes);
        lanes.Select = n => { _modSel = n.Key; OpenMods(); };
        lanes.Activate = n => { _modSel = n.Key; ModUnlock(n); };
        lanes.Hover = (n, at) =>
        {
            if (n == null) { HideInfo(); return; }
            string? why = ModTree.Unlocked(n) ? null : ModTree.Why(n);
            Info(at, n.Name, n.Branch.Col.Lightened(.3f), new[]
            {
                (n.Desc, Colors.White, 14),
                (n.Attack ? "Basic attacks: " + string.Join(", ", n.Tags) : "Fits every skill", UiTheme.Dim, 12),
                (ModTree.Unlocked(n) ? "Unlocked" : why == null ? $"Double-click to unlock ({n.Cost} pt)" : why, ModTree.Unlocked(n) ? UiTheme.Accent : why == null ? UiTheme.Highlight : new Color(1f, .6f, .5f), 12),
            }, 260);
        };

        // ---- right: the selected modifier
        var right = new PanelContainer { CustomMinimumSize = new Vector2(300, 0) }; right.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(.02f, .06f, .12f, .7f), UiTheme.AccentSoft, 1, 14, 10, 0)); cols.AddChild(right);
        var rv = new VBoxContainer(); rv.AddThemeConstantOverride("separation", 6); right.AddChild(rv);
        var sel = ModTree.Get(_modSel); if (sel == null) return;
        var b = sel.Branch; bool on = ModTree.Unlocked(sel); var why2 = ModTree.Why(sel);
        Lbl(b.Name.ToUpper() + (sel.Attack ? "   ·   BASIC ATTACKS" : ""), 13, b.Col, rv);
        Lbl(sel.Name, 22, UiTheme.Title, rv);
        Row(rv, sel.Desc, Colors.White, 15).CustomMinimumSize = new Vector2(270, 0);
        Row(rv, sel.Attack ? "Weapons: " + string.Join(", ", sel.Tags) : "Fits every skill and combines with every other modifier.", UiTheme.Dim, 13).CustomMinimumSize = new Vector2(270, 0);
        Row(rv, $"Cost {sel.Cost} skill point" + (sel.Cost > 1 ? "s" : "") + $"   ·   level {sel.Req}" + (sel.Prev != null ? $"   ·   after {sel.Prev.Name}" : ""), UiTheme.Text, 13).CustomMinimumSize = new Vector2(270, 0);
        if (on) Lbl("UNLOCKED", 15, UiTheme.Accent, rv);
        else
        {
            var ub = Btn(why2 == null ? $"Unlock  ({sel.Cost} pt)" : why2, () => ModUnlock(sel)); ub.Disabled = why2 != null; ub.CustomMinimumSize = new Vector2(0, 38); rv.AddChild(ub);
        }
        // where it can go
        Lbl(sel.Attack ? "YOUR WEAPON STYLES" : "YOUR SKILLS IT FITS", 13, UiTheme.Accent, rv);
        var uses = new List<string>();
        if (sel.Attack) { var m = Attacks.Mod(sel.Id); foreach (var wt in Weapons.Types) if (Attacks.HasStyle(wt) && (m?.Fits(wt) ?? false)) uses.Add(Attacks.StyleName(wt) + (Attacks.Build(wt).Mods.Contains(sel.Id) ? "  (socketed)" : "")); }
        else { var sp = Skills.Sup(sel.Id); foreach (var kv in d.Skills) if (Skills.Get(kv.Key) is { } k && sp != null && Skills.Fits(sp, k)) uses.Add(k.Name + (kv.Value.Sup.Contains(sel.Id) ? "  (socketed)" : "")); }
        var us = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 80) }; rv.AddChild(us);
        var ul = new VBoxContainer(); us.AddChild(ul);
        if (uses.Count == 0) Row(ul, sel.Attack ? "No weapon style takes it." : "None of your learned skills fit it yet.", Dim, 13);
        foreach (var u in uses) Lbl("•  " + u, 13, u.EndsWith("(socketed)") ? UiTheme.Highlight : UiTheme.Text, ul);
        if (on) rv.AddChild(Btn(sel.Attack ? $"Socket it: Basic attacks ({Inputs.KeyOf(Inputs.AttacksUi)})" : $"Socket it: Skills ({Inputs.KeyOf(Inputs.SkillsUi)})", () => { if (sel.Attack) OpenAttacks(null); else OpenSkills(); }, true));
    }

    void ModUnlock(ModTree.Node n)
    {
        var err = ModTree.Unlock(n);
        if (err != null) { Note(err, new Color(1, .6f, .5f)); Sfx.Play("click"); }
        else { Note("Unlocked " + n.Name, Gold); Sfx.Play("level"); Game.Notify(); }
        OpenMods();
    }
}
