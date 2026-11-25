using System;
using System.Linq;
using Godot;

namespace TwinRealms;

/// <summary>Skills > Macros: build the two skill chains for the Macro 1 / Macro 2 keys (PlayerMacros.cs).</summary>
public partial class Ui
{
    static int _macroTab;
    const int MacroMaxSteps = 12;

    public void OpenMacros()
    {
        var d = Game.D; var mac = d.MacroAt(_macroTab); HideInfo();
        var v = Frame("macros", "Macros", new Vector2(1232, 672), OpenMacros);

        // ---- which macro, its key, its name
        var top = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; top.AddThemeConstantOverride("h_separation", 8); top.AddThemeConstantOverride("v_separation", 6); v.AddChild(top);
        for (int i = 0; i < 2; i++)
        {
            int m = i; var mm = d.MacroAt(i);
            var b = Btn($"Macro {i + 1}  [{Inputs.KeyOf(i == 0 ? Inputs.Macro1 : Inputs.Macro2)}]" + (mm.Name != "" ? $"  {mm.Name}" : ""), () => { _macroTab = m; OpenMacros(); }, true);
            b.CustomMinimumSize = new Vector2(0, 32); Mark(b, i == _macroTab); top.AddChild(b);
        }
        var name = new LineEdit { Text = mac.Name, PlaceholderText = "Name (optional)", MaxLength = 24, CustomMinimumSize = new Vector2(170, 32) }; top.AddChild(name);
        name.TextChanged += s => { mac.Name = s.Trim(); Game.Save(); };
        top.AddChild(Btn("Change keys", () => OpenControls(), true));
        top.AddChild(Btn($"Back to skills ({Inputs.KeyOf(Inputs.SkillsUi)})", OpenSkills, true));

        var cols = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; cols.AddThemeConstantOverride("separation", 18); v.AddChild(cols);

        // ---- left: settings and the chain
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(280, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.4f }; left.AddThemeConstantOverride("separation", 8); cols.AddChild(left);
        var lscroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; left.AddChild(lscroll);
        var inner = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; inner.AddThemeConstantOverride("separation", 8); lscroll.AddChild(inner);
        Choice(inner, "Looping", "Hold: tap plays the chain once, holding the key keeps it looping and skips skills on cooldown. Toggle: tap starts looping, tap again stops. Once: always plays the chain one time.",
            new[] { "Hold to loop", "Tap to toggle", "Once" }, Math.Clamp(mac.Loop, 0, 2), i => { mac.Loop = i; Game.Save(); });
        SliderRow(inner, "Time between skills", "A pause after each skill finishes, before the next one starts.", mac.Gap, 0f, 2f, .05f, x => $"{x:0.00} s", x => { mac.Gap = x; Game.Save(); });
        SliderRow(inner, "Wait for cooldowns", "On a single pass, how long to wait for a skill still on cooldown (or short on mana) before skipping it. Looping never waits.", mac.MaxWait, 0f, 6f, .25f, x => x <= 0 ? "Skip" : $"{x:0.00} s", x => { mac.MaxWait = x; Game.Save(); });

        Header($"Chain  {mac.Steps.Count}/{MacroMaxSteps}", inner);
        Lbl("Plays left to right. The slider adds an extra pause after a step.", 13, Dim, inner).AutowrapMode = TextServer.AutowrapMode.WordSmart;
                var list = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; list.AddThemeConstantOverride("h_separation", 4); list.AddThemeConstantOverride("v_separation", 8); inner.AddChild(list);
        if (mac.Steps.Count == 0) Lbl("Empty. Click learned skills on the right to add them.", 14, UiTheme.Dim, list);
        for (int i = 0; i < mac.Steps.Count; i++)
        {
            int idx = i; var st = mac.Steps[i]; var k = Skills.Get(st.Skill); bool known = k != null && d.Skills.ContainsKey(st.Skill);
            var tint = k != null ? Dmg.TypeColor(k.Type) : UiTheme.AccentSoft;
            var pc = new PanelContainer { CustomMinimumSize = new Vector2(132, 0) };
            pc.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(.05f, .045f, .04f, .92f), known ? tint with { A = .75f } : new Color(1, .55f, .5f, .6f), 1, 8, 6, 0)); list.AddChild(pc);
            var cv = new VBoxContainer(); cv.AddThemeConstantOverride("separation", 3); pc.AddChild(cv);
            cv.AddChild(new ColorRect { Color = tint with { A = known ? .85f : .4f }, CustomMinimumSize = new Vector2(0, 4) });
            var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 6); cv.AddChild(head);
            Lbl($"{i + 1}", 15, UiTheme.Accent, head);
            head.AddChild(new IconBox { Icon = k != null ? Icons.ForSkill(k) : "", CustomMinimumSize = new Vector2(34, 34) });
            var nl = Lbl(k?.Name ?? st.Skill, 14, known ? Colors.White : new Color(1, .55f, .5f), cv); nl.ClipText = true; nl.CustomMinimumSize = new Vector2(116, 0);
            if (!known) nl.TooltipText = "Not learned: skipped";
            var sl = new HSlider { MinValue = 0, MaxValue = 3, Step = .05, Value = st.Delay, CustomMinimumSize = new Vector2(116, 20), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, FocusMode = Control.FocusModeEnum.None };
            cv.AddChild(sl);
            var dl = Lbl(st.Delay > 0 ? $"+{st.Delay:0.00} s" : "no pause", 14, UiTheme.Accent, cv);
            sl.ValueChanged += x => { st.Delay = (float)x; dl.Text = x > 0 ? $"+{x:0.00} s" : "no pause"; Game.Save(); };
            var bb = new HBoxContainer(); bb.AddThemeConstantOverride("separation", 2); cv.AddChild(bb);
            bb.AddChild(Btn("◄", () => { if (idx > 0) { (mac.Steps[idx - 1], mac.Steps[idx]) = (mac.Steps[idx], mac.Steps[idx - 1]); Game.Save(); OpenMacros(); } }, true));
            bb.AddChild(Btn("►", () => { if (idx < mac.Steps.Count - 1) { (mac.Steps[idx + 1], mac.Steps[idx]) = (mac.Steps[idx], mac.Steps[idx + 1]); Game.Save(); OpenMacros(); } }, true));
            bb.AddChild(Btn("✕", () => { mac.Steps.RemoveAt(idx); Game.Save(); OpenMacros(); }, true));
            if (i < mac.Steps.Count - 1) { var ar = Lbl("➜", 20, UiTheme.Accent, list); ar.VerticalAlignment = VerticalAlignment.Center; ar.CustomMinimumSize = new Vector2(0, 100); }
        }
        var bot = new HBoxContainer(); bot.AddThemeConstantOverride("separation", 8); left.AddChild(bot);
        bot.AddChild(Btn("Clear chain", () => { mac.Steps.Clear(); Game.Save(); OpenMacros(); }, true));
        bot.AddChild(Btn("Copy hotbar", () =>
        {
            mac.Steps.Clear(); foreach (var id in d.Bar) if (!string.IsNullOrEmpty(id) && d.Skills.ContainsKey(id)) mac.Steps.Add(new MacroStep { Skill = id });
            Game.Save(); OpenMacros();
        }, true));

        // ---- right: learned skills to add (hotbar or not)
        var right = new VBoxContainer { CustomMinimumSize = new Vector2(240, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1f }; right.AddThemeConstantOverride("separation", 6); cols.AddChild(right);
        Header("Learned skills", right);
        Lbl("Click to add to the end of the chain.", 14, Dim, right);
        var gs = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; right.AddChild(gs);
        var grid = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; grid.AddThemeConstantOverride("h_separation", 6); grid.AddThemeConstantOverride("v_separation", 6); gs.AddChild(grid);
        var learned = Skills.All.Where(k => d.Skills.ContainsKey(k.Id)).ToList();
        if (learned.Count == 0) Lbl("No skills learned yet.", 14, UiTheme.Dim, right);
        foreach (var k in learned)
        {
            string id = k.Id; int key = Array.IndexOf(d.Bar, id);
            var t = new SkillTile { Icon = Icons.ForSkill(k), Known = true, Rank = d.Skills[id].Rank, MaxRank = Skills.MaxRank, Key = key, Tint = Dmg.TypeColor(k.Type), CustomMinimumSize = new Vector2(58, 58), Selected = mac.Steps.Any(s => s.Skill == id) };
            t.Click = () =>
            {
                if (mac.Steps.Count >= MacroMaxSteps) { Note($"A macro holds up to {MacroMaxSteps} skills", new Color(1, .6f, .5f)); return; }
                mac.Steps.Add(new MacroStep { Skill = id }); Game.Save(); OpenMacros();
            };
            t.Hover = (on, at) => { if (on) Info(at, k.Name, Dmg.TypeColor(k.Type).Lerp(Colors.White, .4f), SkillTipLines(k)); else HideInfo(); };
            grid.AddChild(t);
        }
    }
}
