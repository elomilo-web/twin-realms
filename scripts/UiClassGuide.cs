using Godot;

namespace TwinRealms;

/// <summary>
/// A-17 ("carrier" read as "career"): how each class plays, from data/classes.json "guide" (role, weapon, resource, signature
/// skills, skill tree, tip). Shown as rows on the creation screen under the class's description, and in the Character window
/// behind the "How a (class) plays" button.
/// </summary>
public partial class Ui
{
    /// <summary>The class's guide lines as "Label: text" rows.</summary>
    void ClassGuideRows(Control parent, ClassDef cd, int size)
    {
        foreach (var (label, text) in cd.Guide) Row(parent, label + ": " + text, label == "Tip" ? UiTheme.Highlight : UiTheme.Text, size);
    }

    void ClassGuideButton(Control parent)
    {
        var cd = Classes.Get(Game.D.Class); if (cd.Guide.Count == 0) return;
        var b = Btn($"How a {cd.Name} plays", OpenClassGuide, true);
        b.TooltipText = string.Join("\n", cd.Guide.ConvertAll(g => g.label + ": " + g.text));
        parent.AddChild(b);
    }

    public void OpenClassGuide()
    {
        var d = Game.D; var cd = Classes.Get(d.Class);
        var v = Frame("classguide", $"How a {cd.Name} plays", new Vector2(760, 520), OpenClassGuide); HideInfo();
        var top = new HBoxContainer(); top.AddThemeConstantOverride("separation", 8); v.AddChild(top);
        top.AddChild(Btn("Back to Character", OpenCharacterPanel, true));
        top.AddChild(Btn($"Skills ({Inputs.KeyOf(Inputs.SkillsUi)})", OpenSkills, true));
        top.AddChild(Btn($"Skill tree ({Inputs.KeyOf(Inputs.Tree)})", OpenTree, true));
        var sc = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; v.AddChild(sc);
        var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; box.AddThemeConstantOverride("separation", 8); sc.AddChild(box);
        Row(box, cd.Desc, UiTheme.Title, 16).CustomMinimumSize = new Vector2(680, 0);
        foreach (var (label, text) in cd.Guide)
        {
            Lbl(label.ToUpperInvariant(), 12, UiTheme.Accent, box);
            Row(box, text, label == "Tip" ? UiTheme.Highlight : UiTheme.Text, 15).CustomMinimumSize = new Vector2(680, 0);
        }
        if (cd.Passives.Count > 0 || cd.Mods.Count > 0)
        {
            Lbl("CLASS BONUSES", 12, UiTheme.Accent, box);
            foreach (var kv in cd.Mods) Row(box, TreeDb.StatText(kv.Key, kv.Value), UiTheme.Dim, 14);
            foreach (var p in cd.Passives) Row(box, p.Name + ": " + string.Join(", ", System.Linq.Enumerable.Select(p.Mods, kv => TreeDb.StatText(kv.Key, kv.Value))), UiTheme.Dim, 14);
        }
    }
}
