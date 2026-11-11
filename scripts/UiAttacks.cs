using System;
using System.Linq;
using Godot;

namespace TwinRealms;

/// <summary>
/// Basic attacks menu (O, or Skills > Basic attacks): build the chain of swings each weapon class runs on the attack button,
/// socket modifiers into it, learn new steps and modifiers, rank it up. Spends skill points (shared with spells).
/// Left: weapon classes. Middle: the chain with live numbers and the sockets. Right: steps and modifiers to learn or add.
/// </summary>
public partial class Ui
{
    static string _atkWt = "";

    public void OpenAttacks(string? wt)
    {
        var d = Game.D;
        if (wt != null) _atkWt = wt; if (_atkWt == "" || !Attacks.HasStyle(_atkWt)) _atkWt = Attacks.HasStyle(d.WType()) ? d.WType() : "sword";
        string w = _atkWt; var b = Attacks.Build(w); var wd = Weapons.Get(w);
        var v = Frame("attacks", "Basic Attacks", new Vector2(1100, 660), () => OpenAttacks(null));
        var top = new HBoxContainer(); v.AddChild(top); top.AddThemeConstantOverride("separation", 20);
        Lbl($"Skill points: {d.RankPts}", 18, d.RankPts > 0 ? Gold : Dim, top);
        Lbl($"The equipped weapon's attack runs on {Inputs.KeyOf(Inputs.Attack)}: taps walk the chain left to right, holding keeps it going. {Inputs.KeyOf(Inputs.Heavy)} is the heavy attack.", 14, Dim, top);
        void Did(string? err) { if (err != null) Note(err, new Color(1, .55f, .5f)); else { Sfx.Play("click"); Game.Notify(); } OpenAttacks(null); }
        var cols = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; cols.AddThemeConstantOverride("separation", 16); v.AddChild(cols);

        // ---- left: weapon classes
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(200, 0) }; cols.AddChild(left);
        Lbl("WEAPONS", 15, UiTheme.Accent, left);
        foreach (var t in Weapons.Types.Where(Attacks.HasStyle))
        {
            string tt = t; var bt = Attacks.Build(t);
            var btn = Btn($"{Attacks.StyleName(t)}  r{bt.Rank}" + (d.WType() == t ? "  (equipped)" : ""), () => OpenAttacks(tt), true);
            btn.Alignment = HorizontalAlignment.Left; btn.CustomMinimumSize = new Vector2(190, 30);
            btn.AddThemeColorOverride("font_color", t == w ? UiTheme.Highlight : d.WType() == t ? Colors.White : Dim); left.AddChild(btn);
        }

        // ---- middle: the chain
        var mid = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; mid.AddThemeConstantOverride("separation", 6); cols.AddChild(mid);
        var hdr = new HBoxContainer(); mid.AddChild(hdr);
        var tl = Lbl($"{Attacks.StyleName(w)}   rank {b.Rank}/{Attacks.MaxRank}", 20, Colors.White, hdr); tl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        if (b.Rank < Attacks.MaxRank) hdr.AddChild(Btn($"Rank up (1 point): chain {Attacks.ChainLen(b.Rank + 1)}, sockets {Attacks.Sockets(b.Rank + 1)}", () => Did(Attacks.RankUp(w)), true));
        Lbl($"CHAIN  {b.Chain.Count}/{Attacks.ChainLen(b.Rank)}", 15, UiTheme.Accent, mid);
        for (int i = 0; i < b.Chain.Count; i++)
        {
            int pos = i; var st = Attacks.Resolve(wd, i, false); var a = st.Def;
            var row = new HBoxContainer(); mid.AddChild(row); row.AddThemeConstantOverride("separation", 8);
            Lbl($"{i + 1}.", 16, UiTheme.Accent, row);
            string extra = (a.Finisher ? "  finisher" : "") + (st.Shots > 1 ? $"  {st.Shots} shots" : "") + (st.Pierce ? "  pierce" : "") + (st.Aoe > 0 ? $"  blast {st.Aoe:0.#}m" : "")
                + (a.Stun > 0 ? $"  stun {a.Stun:0.##}s" : "") + (a.Up > 0 ? "  launch" : "") + string.Concat(st.Status.Select(s => $"  {Dmg.Status(s.id)?.Name ?? s.id} {s.chance * 100:0}%"));
            var l = Lbl($"{st.Name}\n" + $"x{a.Dmg:0.00} dmg  ·  speed x{st.Speed:0.00}" + (wd.Ranged ? "" : $"  ·  arc {a.Arc:0}°  ·  reach {a.Range:0.0} m") + extra.Replace("  ", "  ·  "), 15, Colors.White, row);
            l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            if (pos > 0) row.AddChild(Btn("<", () => { (b.Chain[pos - 1], b.Chain[pos]) = (b.Chain[pos], b.Chain[pos - 1]); Did(null); }, true));
            if (pos < b.Chain.Count - 1) row.AddChild(Btn(">", () => { (b.Chain[pos + 1], b.Chain[pos]) = (b.Chain[pos], b.Chain[pos + 1]); Did(null); }, true));
            row.AddChild(Btn("Remove", () => Did(Attacks.SetChain(w, pos, null)), true));
        }
        var pw = Attacks.Resolve(wd, 0, true);
        Lbl($"Heavy attack ({Inputs.KeyOf(Inputs.Heavy)}): x{pw.Def.Dmg:0.00} dmg" + (pw.Def.Stun > 0 ? $", stun {pw.Def.Stun:0.##}s" : ""), 14, Dim, mid);
        Lbl($"MODIFIERS  {b.Mods.Count}/{Attacks.Sockets(b.Rank)}", 15, UiTheme.Accent, mid);
        if (b.Mods.Count == 0) Lbl("Empty: socket a modifier from the right.", 14, Dim, mid);
        foreach (var mid2 in b.Mods.ToList())
        {
            var m = Attacks.Mod(mid2); if (m == null) continue; string mm = mid2;
            var row = new HBoxContainer(); mid.AddChild(row); var l = Lbl($"{m.Name}: {m.Desc}", 15, Gold, row); l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(Btn("Unsocket", () => Did(Attacks.Socket(w, mm)), true));
        }

        // ---- right: steps and modifiers
        var right = new VBoxContainer { CustomMinimumSize = new Vector2(330, 0) }; cols.AddChild(right);
        var sc = Scroll(right, 540); var list = Inner(sc);
        Lbl("SWING STEPS", 15, UiTheme.Accent, list);
        foreach (var s in Attacks.Steps(w))
        {
            string sid = s.Id; bool known = Attacks.StepKnown(w, sid);
            var row = new HBoxContainer(); list.AddChild(row);
            var l = Lbl($"{s.Name}: {s.Desc}", 14, known ? Colors.White : d.Level < s.Req ? Dim.Darkened(.3f) : Dim, row); l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.CustomMinimumSize = new Vector2(220, 0);
            if (known) row.AddChild(Btn("Add", () => Did(Attacks.SetChain(w, b.Chain.Count, sid)), true));
            else row.AddChild(Btn(d.Level < s.Req ? $"Lv {s.Req}" : $"Learn {s.Cost}", () => Did(Attacks.UnlockStep(w, sid)), true));
        }
        Lbl("MODIFIERS", 15, UiTheme.Accent, list);
        foreach (var m in Attacks.Mods)
        {
            if (!m.Fits(w)) continue; string mmid = m.Id; bool have = d.AtkMods.Contains(mmid), on = b.Mods.Contains(mmid);
            var row = new HBoxContainer(); list.AddChild(row);
            var l = Lbl($"{m.Name}: {m.Desc}", 14, on ? Gold : have ? Colors.White : d.Level < m.Req ? Dim.Darkened(.3f) : Dim, row); l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.CustomMinimumSize = new Vector2(220, 0);
            if (have) row.AddChild(Btn(on ? "Unsocket" : "Socket", () => Did(Attacks.Socket(w, mmid)), true));
            else { var mn = ModTree.Get(ModTree.KeyOf(mmid, true)); var why = mn != null ? ModTree.Why(mn) : null; var ub = Btn(why == null ? $"Unlock {m.Cost}" : d.Level < m.Req ? $"Lv {m.Req}" : "Tree", () => { if (mn != null && ModTree.Why(mn) == null) Did(ModTree.Unlock(mn)); else OpenMods(); }, true); row.AddChild(ub); }
        }
    }
}
