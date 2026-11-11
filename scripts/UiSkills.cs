using System;
using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// Skills menu (L): learn skills, rank them up, socket supports that change how they work, and put them on hotbar keys 1-6.
/// Left: every skill (learned, learnable, locked). Middle: the selected skill with its live numbers. Right: supports to unlock.
/// </summary>
public partial class Ui
{
    static string _selSkill = "";

    public void OpenSkills()
    {
        var d = Game.D; Skills.GrantStarting(d);
        if (_selSkill == "" || Skills.Get(_selSkill) == null) _selSkill = d.Bar.Length > 0 && !string.IsNullOrEmpty(d.Bar[0]) ? d.Bar[0] : "fire";
        var v = Frame("skills", "Skills", new Vector2(1060, 640), OpenSkills);
        var top = new HBoxContainer(); v.AddChild(top); top.AddThemeConstantOverride("separation", 20);
        Lbl($"Skill points: {d.RankPts}", 18, d.RankPts > 0 ? Gold : Dim, top); top.AddChild(Btn($"Basic attacks ({Inputs.KeyOf(Inputs.AttacksUi)})", () => OpenAttacks(null), true));
        Lbl("Learn skills, rank them up (+15% damage, more sockets), socket supports, bind to keys 1-6. One rank point per level.", 14, Dim, top);
        var cols = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; cols.AddThemeConstantOverride("separation", 16); v.AddChild(cols);

        // ---- left: skill list
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(250, 0) }; cols.AddChild(left);
        Lbl("SKILLS", 15, UiTheme.Accent, left);
        var ls = Scroll(left, 500); var list = Inner(ls);
        foreach (var k in Skills.All)
        {
            bool known = d.Skills.TryGetValue(k.Id, out var st); string id = k.Id;
            string tag = known ? $"rank {st!.Rank}" + (Array.IndexOf(d.Bar, id) is int bi && bi >= 0 ? $"  [{bi + 1}]" : "") : d.Level < k.Req ? $"Lv {k.Req}" : $"learn {k.Cost}";
            var b = Btn($"{k.Name}   {tag}", () => { _selSkill = id; OpenSkills(); }, true);
            b.Alignment = HorizontalAlignment.Left; b.CustomMinimumSize = new Vector2(230, 30);
            b.AddThemeColorOverride("font_color", id == _selSkill ? UiTheme.Highlight : known ? Colors.White : d.Level < k.Req ? Dim.Darkened(.3f) : Dim);
            list.AddChild(b);
        }

        // ---- middle: selected skill
        var mid = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; mid.AddThemeConstantOverride("separation", 6); cols.AddChild(mid);
        var sk = Skills.Get(_selSkill)!; bool learned = d.Skills.TryGetValue(sk.Id, out var ss);
        var head = new HBoxContainer(); mid.AddChild(head); head.AddThemeConstantOverride("separation", 10);
        head.AddChild(new IconBox { Icon = Icons.ForSkill(sk), CustomMinimumSize = new Vector2(52, 52) });
        var hv = new VBoxContainer(); head.AddChild(hv);
        Lbl(sk.Name.ToUpper(), 22, UiTheme.Title, hv); Lbl(string.Join("  ·  ", sk.Tags) + "   ·   " + sk.Type + " damage", 13, Dmg.TypeColor(sk.Type).Lerp(Dim, .3f), hv);
        var desc = Row(mid, sk.Desc, Colors.White, 15); desc.CustomMinimumSize = new Vector2(420, 0);
        var c = Skills.Compute(sk.Id);
        var grid = new GridContainer { Columns = 4 }; grid.AddThemeConstantOverride("h_separation", 18); mid.AddChild(grid);
        void Stat(string n, string val) { Lbl(n, 13, Dim, grid); Lbl(val, 14, Colors.White, grid); }
        if (sk.Kind != "heal" && sk.Kind != "buff") Stat("Damage", $"{c.Dmg:0}" + (sk.Kind == "zone" ? " / tick" : sk.Kind == "spin" ? $" x{c.Count}" : c.Count > 1 ? $" x{c.Count}" : ""));
        if (sk.Kind == "heal") Stat("Heals", $"{c.Heal * 100:0}% health");
        Stat("Cost", (c.Mp > 0 ? $"{c.Mp:0} mana " : "") + (c.St > 0 ? $"{c.St:0} stamina" : ""));
        Stat("Cooldown", $"{c.Cd:0.0} s"); Stat("Use time", $"{c.Cast:0.00} s");
        if (c.Radius > 0) Stat("Area", $"{c.Radius:0.0} m");
        if (sk.Kind == "projectile") Stat("Speed", $"{c.Speed:0} m/s");
        if (c.Pierce > 0) Stat("Pierce", c.Pierce.ToString());
        if (c.Chain > 0) Stat("Chains", c.Chain.ToString());
        if (c.Homing) Stat("Homing", "yes");
        if (c.Stun > 0) Stat("Stun", $"{c.Stun:0.0} s");
        if (c.Up > 0) Stat("Knock-up", $"{c.Up:0}");
        if (c.Echo > 0) Stat("Echo", "repeats once");
        if (c.Auto != "") Stat("Auto-cast", c.Auto == "timer" ? $"every {c.AutoCd:0} s" : c.Auto == "hit" ? "when hit" : "on crit");
        if (c.Status.Count > 0) { var sl = new List<string>(); foreach (var s in c.Status) sl.Add($"{Dmg.Status(s.id)?.Name ?? s.id} {s.chance * 100:0}%"); Stat("Effects", string.Join(", ", sl)); }
        if (c.Leech > 0) Stat("Leech", $"{c.Leech:0}%");

        var act = new HBoxContainer(); mid.AddChild(act); act.AddThemeConstantOverride("separation", 8);
        if (!learned)
        {
            var lb = Btn(d.Level < sk.Req ? $"Requires level {sk.Req}" : $"Learn ({sk.Cost} rank)", () => Do(Skills.Unlock(sk.Id), "Learned " + sk.Name)); lb.Disabled = d.Level < sk.Req || d.RankPts < sk.Cost; act.AddChild(lb);
        }
        else
        {
            Lbl($"Rank {ss!.Rank}/{Skills.MaxRank}", 16, Gold, act);
            var rb = Btn(ss.Rank >= Skills.MaxRank ? "Max rank" : "Rank up (1)", () => Do(Skills.RankUp(sk.Id), sk.Name + " ranked up")); rb.Disabled = ss.Rank >= Skills.MaxRank || d.RankPts < 1; act.AddChild(rb);
            Lbl("   Key:", 15, Dim, act);
            for (int i = 0; i < 6; i++)
            {
                int slot = i; var kb = Btn((i + 1).ToString(), () =>
                {
                    int was = Array.IndexOf(d.Bar, sk.Id); if (was >= 0) d.Bar[was] = d.Bar[slot]; d.Bar[slot] = sk.Id; Sfx.Play("click"); Game.Notify(); OpenSkills();
                }, true);
                kb.CustomMinimumSize = new Vector2(32, 30); if (d.Bar[i] == sk.Id) kb.AddThemeColorOverride("font_color", UiTheme.Highlight); act.AddChild(kb);
            }
            // sockets
            Lbl("SUPPORTS", 15, UiTheme.Accent, mid);
            while (ss.Sup.Count < Skills.Sockets(ss.Rank)) ss.Sup.Add("");
            for (int i = 0; i < 3; i++)
            {
                var row = new HBoxContainer(); mid.AddChild(row); row.AddThemeConstantOverride("separation", 8);
                Lbl($"Socket {i + 1}", 14, Dim, row).CustomMinimumSize = new Vector2(70, 0);
                if (i >= Skills.Sockets(ss.Rank)) { Lbl($"opens at rank {(i == 1 ? 2 : 4)}", 14, Dim.Darkened(.3f), row); continue; }
                var ob = new OptionButton { CustomMinimumSize = new Vector2(260, 30), FocusMode = Control.FocusModeEnum.None }; row.AddChild(ob);
                ob.AddItem("(empty)"); ob.SetItemMetadata(0, ""); int sel = 0;
                foreach (var sp in Skills.AllSupports)
                {
                    if (!d.Supports.Contains(sp.Id) || !Skills.Fits(sp, sk)) continue;
                    if (ss.Sup.Contains(sp.Id) && ss.Sup[i] != sp.Id) continue;
                    ob.AddItem(sp.Name); int ix = ob.ItemCount - 1; ob.SetItemMetadata(ix, sp.Id); if (ss.Sup[i] == sp.Id) sel = ix;
                }
                ob.Selected = sel; int sock = i;
                ob.ItemSelected += ix => Do(Skills.Socket(sk.Id, sock, ob.GetItemMetadata((int)ix).AsString()), "");
                var cur = Skills.Sup(ss.Sup[i]); if (cur != null) Lbl(cur.Desc, 13, UiTheme.Text, row);
            }
        }

        // ---- right: supports
        var right = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) }; cols.AddChild(right);
        Lbl("SUPPORT MODIFIERS", 15, UiTheme.Accent, right);
        var rs = Scroll(right, 500); var rl = Inner(rs);
        foreach (var sp in Skills.AllSupports)
        {
            bool have = d.Supports.Contains(sp.Id); bool fits = Skills.Fits(sp, sk); string id = sp.Id;
            var box = new VBoxContainer(); rl.AddChild(box);
            var h = new HBoxContainer(); box.AddChild(h);
            var nl = Lbl(sp.Name, 15, have ? (fits ? UiTheme.Highlight : Colors.White) : Dim, h); nl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            if (!have) { var ub = Btn(d.Level < sp.Req ? $"Lv {sp.Req}" : $"Unlock {sp.Cost}", () => Do(Skills.UnlockSupport(id), "Unlocked " + sp.Name), true); ub.Disabled = d.Level < sp.Req || d.RankPts < sp.Cost; h.AddChild(ub); }
            var dl = Row(box, sp.Desc + "  (" + string.Join(", ", sp.Tags) + ")", fits ? UiTheme.Text : Dim, 13); dl.CustomMinimumSize = new Vector2(280, 0);
        }
    }

    void Do(string? err, string ok)
    {
        if (err != null) { Note(err, new Color(1, .6f, .5f)); return; }
        if (ok != "") { Note(ok, Gold); Sfx.Play("level"); } else Sfx.Play("click");
        Game.Notify(); OpenSkills();
    }
}

/// <summary>Draws one vector icon inside a framed box.</summary>
public partial class IconBox : Control
{
    public string Icon = "";
    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size); DrawRect(r, new Color(.02f, .05f, .1f, .8f)); DrawRect(r, UiTheme.Accent with { A = .6f }, false, 1.5f);
        Icons.Draw(this, Icon, r.GetCenter(), Size.Y * .7f);
    }
}
