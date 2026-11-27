using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TwinRealms;

/// <summary>
/// A-14: the data-driven attributes (Wisdom, Agility, Luck, Endurance; data/stats.json "attributes") in the Character window:
/// a compact two-column grid under the four classic attribute rows, each cell with its value, a tooltip (what a point gives) and a
/// +1 button while attribute points are unspent (Shift+click spends up to 5).
/// </summary>
public partial class Ui
{
    /// <summary>What this hero's race and class start an attribute at (5 + their bonuses).</summary>
    static int BaseAttr(PlayerStats d, string id) => StatPipeline.AttrBase + Classes.Get(d.Class).Attr.GetValueOrDefault(id) + Races.Get(d.Race).Attr.GetValueOrDefault(id);

    void ExtraAttrRows(Control mid)
    {
        var d = Game.D; var attrs = StatPipeline.Attrs; if (attrs.Count == 0) return;
        var g = new GridContainer { Columns = 2, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; g.AddThemeConstantOverride("h_separation", 4); g.AddThemeConstantOverride("v_separation", 4); mid.AddChild(g);
        foreach (var a0 in attrs)
        {
            var a = a0; int val = d.AttrOf(a.Id);
            string per = string.Join(", ", a.Keys.Select(kv => TreeDb.StatText(kv.Key, kv.Value)));
            var pc = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = $"{a.Name}: {a.Desc}\nEach point past {StatPipeline.AttrBase}: {per}" + (d.AttrPts > 0 ? "\nShift+click +1 to spend up to 5 points" : "") };
            pc.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(.03f, .025f, .02f, .7f), a.Col with { A = .45f }, 1, 8, 3, 0)); g.AddChild(pc);
            var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 6); pc.AddChild(h);
            var ic = new Control { CustomMinimumSize = new Vector2(22, 22), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore }; string icon = a.Icon;
            ic.Draw += () => Icons.Draw(ic, icon, new Vector2(11, 11), 20); h.AddChild(ic);
            var nl = Lbl(a.Name, 14, a.Col.Lightened(.3f), h); nl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; nl.ClipText = true;
            var vl = Lbl(val.ToString(), 18, Colors.White, h); vl.CustomMinimumSize = new Vector2(26, 0); vl.HorizontalAlignment = HorizontalAlignment.Right;
            if (d.AttrPts < 1) continue;
            var b = Btn("+1", () =>
            {
                int n = Math.Min(Input.IsKeyPressed(Key.Shift) ? 5 : 1, d.AttrPts); if (n <= 0) return;
                d.AttrPts -= n; d.AddAttr(a.Id, n); Game.Notify();
            }, true);
            b.CustomMinimumSize = new Vector2(34, 0); b.AddThemeColorOverride("font_color", UiTheme.Highlight); h.AddChild(b);
        }
    }

    /// <summary>The Character window's Reset: the data attributes go back to what the race and class start with; returns the points freed.</summary>
    static int ResetExtraAttrs(PlayerStats d)
    {
        int back = 0;
        foreach (var a in StatPipeline.Attrs)
        {
            int cur = d.AttrOf(a.Id), b0 = BaseAttr(d, a.Id); if (cur <= b0) continue;
            back += cur - b0; d.AddAttr(a.Id, b0 - cur);
        }
        return back;
    }
}
