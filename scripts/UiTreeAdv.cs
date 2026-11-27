using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TwinRealms;

/// <summary>
/// A-15: the tree window's second layer. A row of two tabs (Base tree / Advanced tree) under the toolbar, and under it, while the
/// advanced tree is shown, what it still needs: the points invested in the base tree against the 100 it asks for
/// (data/skilltree_adv.json "unlock"), and which advanced paths this hero can open now. Node cards of the advanced layer get their
/// own headings and, on a locked path, the missing conditions. The rules themselves are in TreeDb (Tree.cs).
/// </summary>
public partial class Ui
{
    void TreeLayerBar(VBoxContainer v)
    {
        var d = Game.D; int inv = TreeDb.Invested(d.Tree, 0), adv = TreeDb.Invested(d.Tree, 1); bool open = TreeDb.AdvOpen(d.Tree);
        if (TreeDb.Paths.Count == 0) { TreeView.Layer = 0; return; }   // no advanced tree in the data: nothing to switch to
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8); v.AddChild(row);
        void Tab(string text, int layer)
        {
            var b = Btn(text, () => { if (TreeView.Layer == layer) return; TreeView.Layer = layer; _treeSearch = ""; OpenTree(); }, true); b.CustomMinimumSize = new Vector2(0, 28);
            if (TreeView.Layer == layer) { b.AddThemeColorOverride("font_color", UiTheme.Highlight); b.AddThemeStyleboxOverride("normal", UiTheme.Box(UiTheme.ButtonHover, UiTheme.Highlight, 1, 10, 4, 0)); }
            row.AddChild(b);
        }
        Tab($"Base tree  ({inv} points)", 0);
        Tab(open ? $"Advanced tree  ({adv} points)" : $"Advanced tree  (locked: {inv}/{TreeDb.AdvPoints})", 1);
        PadTabs(new[] { ((System.Action)(() => { TreeView.Layer = 0; OpenTree(); }), TreeView.Layer == 0), ((System.Action)(() => { TreeView.Layer = 1; OpenTree(); }), TreeView.Layer == 1) });
        string msg; Color col = Dim;
        if (TreeView.Layer == 0)
            msg = open ? "The advanced tree is open: every base path has a stronger form there." : $"Invest {TreeDb.AdvPoints} points here to open the advanced tree ({TreeDb.AdvPoints - inv} to go). +{Progression.TreePerLevel} points per level.";
        else if (!open) { msg = $"LOCKED. Invest {TreeDb.AdvPoints} points in the base tree first: you have {inv}. Each path here then opens with its base path's keystone, or {TreeDb.AdvArmNodes} nodes of that path."; col = new Color(1f, .6f, .5f); }
        else
        {
            var can = TreeDb.Paths.Values.Where(p => d.Tree.Contains(p.Root) || TreeDb.PathOpen(p, d.Tree, out _)).Select(p => p.Name).ToList();
            msg = can.Count > 0 ? "Open to you: " + string.Join(", ", can) + ". Others open with their base path's keystone or " + TreeDb.AdvArmNodes + " of its nodes."
                : $"No path is open yet: take a base path's keystone, or {TreeDb.AdvArmNodes} of its nodes, and its advanced form opens."; col = can.Count > 0 ? Gold : new Color(1f, .75f, .5f);
        }
        var l = Lbl(msg, 13, col, row); l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; l.ClipText = true; l.VerticalAlignment = VerticalAlignment.Center; l.TooltipText = msg;
    }

    /// <summary>Extra lines on a node's hover card: branch and advanced headings, and why a locked advanced path is shut.</summary>
    static void TreeAdvLines(TreeNode n, List<(string, Color, int)> lines)
    {
        if (n.Type == "branch") lines.Insert(0, ("THEMED PATH   open to every class", n.Col.Lightened(.3f), 12));
        if (TreeDb.PathOf(n) is not { } p) return;
        string head = n.Type switch { "gate" => $"ADVANCED PATH   {p.BaseName} > {p.Name}", "key" => $"CAPSTONE   {p.Name}", "notable" => $"{p.Name.ToUpperInvariant()}   notable", _ => p.Name };
        if (n.Type == "key" && lines.Count > 0 && lines[0].Item1.StartsWith("KEYSTONE")) lines.RemoveAt(0);
        else if (n.Type == "notable" && lines.Count > 0 && lines[0].Item1.StartsWith("NOTABLE")) lines.RemoveAt(0);
        lines.Insert(0, (head, n.Col.Lightened(.3f), 12));
        string why = TreeView.LockedWhy(n);
        if (why != "") lines.Add(("Locked. " + why + ".", new Color(1f, .6f, .5f), 12));
    }
}
