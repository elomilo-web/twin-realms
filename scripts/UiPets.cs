using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TwinRealms;

/// <summary>
/// Pets menu (P / pad LT+RT): party + Soul Box on the left, a turning 3D preview with stats in the centre, Skills / Gear / Orders
/// tabs on the right; set active, rename, release. Fixed-size window (Ui.Frame); long lists scroll.
/// </summary>
public partial class Ui
{
    string _petSel = "", _petTab = "skills", _petConfirm = ""; bool _petRenaming;

    public void OpenPets(string? sel, string tab = "")
    {
        var d = Game.D; if (tab != "") _petTab = tab; if (sel != null && sel != _petSel) { _petSel = sel; _petConfirm = ""; _petRenaming = false; }
        if (d.Pets.Find(p => p.Id == _petSel) == null) _petSel = d.ActivePet != "" && d.Pets.Exists(p => p.Id == d.ActivePet) ? d.ActivePet : d.Pets.FirstOrDefault()?.Id ?? "";
        var v = Frame("pets", "Bound Souls", new Vector2(1180, 640), () => OpenPets(null));
        var cur = d.Pets.Find(p => p.Id == _petSel);
        var row = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; row.AddThemeConstantOverride("separation", 14); v.AddChild(row);

        // ---- left: cubes carried, party and Soul Box
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(280, 0) }; left.AddThemeConstantOverride("separation", 6); row.AddChild(left);
        var cubes = new HBoxContainer(); cubes.AddThemeConstantOverride("separation", 4); left.AddChild(cubes);
        foreach (var c in PetDb.Cubes)
        {
            var cd = c; bool on = Cubes.Selected == c.Id;
            var b = Btn($"{Cubes.Count(c.Id)}", () => { Game.D.CubeSel = cd.Id; OpenPets(null); }, true); b.TooltipText = $"{c.Name}: x{c.Mul:0.#} catch (bosses x{c.BossMul:0.#}). Throw with {Inputs.KeyOf(Inputs.ThrowCube)}";
            b.AddThemeColorOverride("font_color", on ? Gold : c.Col.Lightened(.2f)); b.CustomMinimumSize = new Vector2(50, 30); cubes.AddChild(b);
        }
        var duelRow = new HBoxContainer(); duelRow.AddThemeConstantOverride("separation", 4); left.AddChild(duelRow);
        var sdb = Btn("Soul Duels", () => OpenSoulTeam(), true); sdb.TooltipText = $"Turn-based pet duels in the Crimson Arena. Ladder: {DuelDb.RankTitle(d.DuelRank)} next."; sdb.Disabled = d.Pets.Count == 0; duelRow.AddChild(sdb);
        if (PetTrade.Open) duelRow.AddChild(Btn($"Trade: {PetTrade.PartnerName}", OpenPetTrade, true));
        var ls = Scroll(left, 430); ls.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled; var lv = Inner(ls); lv.AddThemeConstantOverride("separation", 4);
        if (d.Pets.Count == 0) Empty(lv, $"No bound souls yet. Weaken a monster and throw a Soul Cube ({Inputs.KeyOf(Inputs.ThrowCube)}). Brynn sells cubes; the craft bench makes them.");
        var act = d.Pets.Find(p => p.Id == d.ActivePet);
        if (act != null) { Header("Companion", lv); PetEntry(lv, act); }
        if (d.Pets.Count > (act != null ? 1 : 0)) { Header($"Soul Box ({d.Pets.Count - (act != null ? 1 : 0)})", lv); foreach (var p in d.Pets) if (p != act) PetEntry(lv, p); }

        // ---- centre: preview, identity, stats, actions
        var mid = new VBoxContainer { CustomMinimumSize = new Vector2(330, 0) }; mid.AddThemeConstantOverride("separation", 4); row.AddChild(mid);
        var right = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; right.AddThemeConstantOverride("separation", 6); row.AddChild(right);
        if (cur == null) { Empty(mid, "Select a pet."); return; }
        var sp = cur.Sp(); var st = cur.Stats(); var nat = PetDb.Nature(cur.Nature);
        var svc = new SubViewportContainer { CustomMinimumSize = new Vector2(330, 230), Stretch = true, MouseFilter = Control.MouseFilterEnum.Ignore }; mid.AddChild(svc);
        var svp = new SubViewport { OwnWorld3D = true, Size = new Vector2I(330, 230), RenderTargetUpdateMode = SubViewport.UpdateMode.Always, Msaa3D = Viewport.Msaa.Msaa2X }; svc.AddChild(svp);
        svp.AddChild(new PetPreview { Species = cur.Species, Level = cur.Level, Down = cur.Ko() });
        var nm = Lbl(cur.Name() + (cur.Nick != "" ? $"  ({sp.Name})" : ""), 20, Gold, mid); nm.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Lbl($"Level {cur.Level}  {PetDb.RarityName(sp.Rarity)} {(sp.Elem == "physical" ? "" : sp.Elem + " ")}{sp.Arch}", 15, PetDb.RarityColor(sp.Rarity), mid);
        Lbl($"Nature: {nat.Name}   Bond {cur.Bond:0}/100   {(sp.Mountable ? "Mountable" : "")}", 14, Parch, mid);
        Bar(mid, "XP", cur.Xp, cur.XpNeed(), new Color(.55f, .75f, 1f));
        Bar(mid, cur.Ko() ? $"In its cube ({cur.KoT:0} s)" : "HP", cur.Ko() ? 0 : cur.HpFrac * 100, 100, new Color(.75f, .25f, .2f));
        var grid = new GridContainer { Columns = 5 }; grid.AddThemeConstantOverride("h_separation", 14); mid.AddChild(grid);
        foreach (var (k, val) in new[] { ("HP", st.Hp), ("ATK", st.Atk), ("DEF", st.Def), ("MAG", st.Mag), ("SPD", st.Spd) })
        { var cell = new VBoxContainer(); grid.AddChild(cell); Lbl(k, 12, Dim, cell); Lbl(val.ToString(), 17, Parch, cell); }
        var acts = new HBoxContainer(); acts.AddThemeConstantOverride("separation", 6); mid.AddChild(acts);
        bool isAct = cur.Id == d.ActivePet;
        acts.AddChild(Btn(isAct ? "To Soul Box" : "Set active", () => { PetWorld.SetActive(isAct ? "" : cur.Id); OpenPets(null); }, true));
        acts.AddChild(Btn("Rename", () => { _petRenaming = !_petRenaming; OpenPets(null); }, true));
        var rel = Btn(_petConfirm == cur.Id ? "Really release?" : "Release", () =>
        {
            if (_petConfirm != cur.Id) { _petConfirm = cur.Id; OpenPets(null); return; }
            foreach (var g in cur.Gear.Values) if (g != null) d.Inv.Add(g);   // its gear comes back to the pack
            d.Pets.Remove(cur); if (d.ActivePet == cur.Id) PetWorld.SetActive(""); _petConfirm = ""; Note($"{cur.Name()} is released", Dim); Game.Notify(); Game.Save(); OpenPets(null);
        }, true);
        rel.AddThemeColorOverride("font_color", _petConfirm == cur.Id ? new Color(1f, .5f, .4f) : Muted); acts.AddChild(rel);
        if (cur.Ko() && d.HpPot > 0) acts.AddChild(Btn("Revive (potion)", () => { d.HpPot--; cur.KoT = 0; cur.HpFrac = 1; Sfx.Play("heal"); Game.Notify(); OpenPets(null); }, true));
        if (_petRenaming)
        {
            var rh = new HBoxContainer(); mid.AddChild(rh);
            var le = new LineEdit { Text = cur.Nick, PlaceholderText = sp.Name, MaxLength = 18, CustomMinimumSize = new Vector2(200, 30) }; rh.AddChild(le);
            void Done() { cur.Nick = le.Text.Trim(); _petRenaming = false; if (PetWorld.Out != null && GodotObject.IsInstanceValid(PetWorld.Out) && PetWorld.Out.S == cur) PetWorld.Out.Refresh(); Game.Notify(); OpenPets(null); }
            le.TextSubmitted += _ => Done(); rh.AddChild(Btn("Save", Done, true));
            Callable.From(() => { if (GodotObject.IsInstanceValid(le)) le.GrabFocus(); }).CallDeferred();
        }

        // ---- right: tabs
        var top = new HBoxContainer(); right.AddChild(top);
        Tabs(top, _petTab, ("skills", "Skills", () => OpenPets(null, "skills")), ("gear", "Gear", () => OpenPets(null, "gear")), ("orders", "Orders", () => OpenPets(null, "orders")), ("tree", PetTree.Free(cur) > 0 ? $"Tree ({PetTree.Free(cur)})" : "Tree", () => OpenPets(null, "tree")));
        var rs = Scroll(right, 470); rs.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled; var rv = Inner(rs); rv.AddThemeConstantOverride("separation", 5);
        if (_petTab == "tree") PetTreeTab(rv, cur);   // A-20 (UiPetTree.cs)
        else if (_petTab == "gear") PetGear(rv, cur);
        else if (_petTab == "orders") PetOrders(rv, cur);
        else PetSkills(rv, cur);
    }

    // ---- Soul Duels: pick up to three pets, see the next Duelist on the ladder, enter the pit (Arenas.Start("souls"))
    readonly List<string> _duelTeam = new();
    public void OpenSoulTeam()
    {
        var d = Game.D; _duelTeam.RemoveAll(id => !d.Pets.Exists(p => p.Id == id));
        if (_duelTeam.Count == 0)
        {   // default: the companion, then the highest levels
            if (d.Pets.Exists(p => p.Id == d.ActivePet)) _duelTeam.Add(d.ActivePet);
            foreach (var p in d.Pets.OrderByDescending(p => p.Level)) if (_duelTeam.Count < 3 && !_duelTeam.Contains(p.Id)) _duelTeam.Add(p.Id);
        }
        var v = Frame("soulteam", "Soul Duels", new Vector2(980, 600), OpenSoulTeam);
        int rk = Math.Clamp(d.DuelRank, 0, DuelDb.RankCount - 1); var r = DuelDb.Rank(rk);
        Row(v, $"Ladder: next up a {DuelDb.RankTitle(rk)} (rank {rk + 1}/{DuelDb.RankCount}) with {JsonUtil.F(r, "size", 1):0} pet(s), about {(JsonUtil.F(r, "lvl") >= 0 ? "+" : "")}{JsonUtil.F(r, "lvl"):0} levels over your team.   Won {d.SoulWins}, lost {d.SoulLosses}.", Parch, 16);
        Row(v, $"Win: {JsonUtil.F(r, "valor"):0} valor, {JsonUtil.F(r, "gold"):0} gold, {JsonUtil.F(r, "petXp"):0} xp for each pet, maybe a {PetDb.Cube(JsonUtil.S(r, "cube", "iron")).Name} or pet gear. Elements: fire > venom > storm > frost > fire; holy and shadow sear each other.", Dim, 14);
        Header($"Your team ({_duelTeam.Count}/3)", v);
        var sc = Scroll(v, 360); sc.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled; var lv = Inner(sc); lv.AddThemeConstantOverride("separation", 4);
        foreach (var p in d.Pets.OrderByDescending(p => _duelTeam.Contains(p.Id)).ThenByDescending(p => p.Level))
        {
            var pp = p; bool on = _duelTeam.Contains(p.Id); var sp = p.Sp(); var st = p.Stats();
            string mv = string.Join(", ", p.Skills.Select(s => PetDb.Skill(s)?.Name ?? s));
            AddRow(lv, $"{(on ? $"[{_duelTeam.IndexOf(p.Id) + 1}]  " : "")}{p.Name()}  Lv{p.Level}  {(sp.Elem == "physical" ? "" : sp.Elem + " ")}{sp.Name}   HP {st.Hp} ATK {st.Atk} DEF {st.Def} MAG {st.Mag} SPD {st.Spd}\n{mv}",
                on ? Gold : sp.Elem == "physical" ? Parch : Dmg.TypeColor(sp.Elem).Lerp(Parch, .4f), on ? "Remove" : "Add", () =>
                {
                    if (_duelTeam.Contains(pp.Id)) _duelTeam.Remove(pp.Id); else if (_duelTeam.Count < 3) _duelTeam.Add(pp.Id); else { Note("Three pets at most", new Color(1, .6f, .5f)); return; }
                    OpenSoulTeam();
                }, on || _duelTeam.Count < 3);
        }
        var bot = new HBoxContainer(); bot.AddThemeConstantOverride("separation", 12); v.AddChild(bot);
        var go = Btn("Enter the pit", () =>
        {
            if (Dungeon.Active) { Note("Leave the dungeon first", new Color(1, .6f, .5f)); return; }
            var team = new List<string>(_duelTeam); Close(); Arenas.Start("souls", team);
        });
        go.Disabled = _duelTeam.Count == 0 || Dungeon.Active; go.CustomMinimumSize = new Vector2(240, 42); bot.AddChild(go);
        var back = Btn("Back to pets", () => OpenPets(null)); back.CustomMinimumSize = new Vector2(200, 42); bot.AddChild(back);
    }

    void PetEntry(Node parent, PetState p)
    {
        var box = CardBox(parent, p.Id == _petSel); var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 8); box.AddChild(h);
        var sp = p.Sp(); h.AddChild(new PetBadge { Col = PetDb.RarityColor(sp.Rarity), Letter = sp.Name.Length > 0 ? sp.Name[..1] : "?", Out = p.Id == Game.D.ActivePet, CustomMinimumSize = new Vector2(34, 34), MouseFilter = Control.MouseFilterEnum.Ignore });
        var col = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; col.AddThemeConstantOverride("separation", 1); h.AddChild(col);
        var b = Btn($"{p.Name()}   Lv{p.Level}", () => OpenPets(p.Id), true); b.Alignment = HorizontalAlignment.Left; b.Flat = true; b.AddThemeColorOverride("font_color", p.Ko() ? Dim : Parch); col.AddChild(b);
        Lbl(sp.Name + (p.Ko() ? $"  (in cube {p.KoT:0} s)" : ""), 12, Dim, col);
        var pb = new ProgressBar { MinValue = 0, MaxValue = 1, Value = p.Ko() ? 0 : p.HpFrac, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 5), MouseFilter = Control.MouseFilterEnum.Ignore };
        pb.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = new Color(.7f, .2f, .18f) }); pb.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(.1f, .08f, .07f) }); col.AddChild(pb);
    }
    void Bar(Node parent, string label, float val, float max, Color c)
    {
        var h = new HBoxContainer(); parent.AddChild(h); var l = Lbl(label, 12, Dim, h); l.CustomMinimumSize = new Vector2(110, 0);
        var pb = new ProgressBar { MinValue = 0, MaxValue = Mathf.Max(1, max), Value = val, ShowPercentage = false, CustomMinimumSize = new Vector2(200, 8), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore };
        pb.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = c }); pb.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(.1f, .08f, .07f) }); h.AddChild(pb);
    }
    Button Opt(Node parent, string text, bool on, Action a)
    {
        var b = Btn(text, a, true); b.AddThemeColorOverride("font_color", on ? Gold : Parch); if (on) b.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = new Color(.18f, .14f, .08f), BorderColor = Gold, BorderWidthBottom = 2, ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 4, ContentMarginBottom = 4 });
        parent.AddChild(b); return b;
    }
    static string SkillLine(PetSkillDef s) => s.Kind switch
    {
        "attack" => $"{s.Elem} {s.Power:0} power, {s.Acc:0}% acc, {s.Cost} focus" + (s.Effect != "" ? $", {s.Chance * 100:0}% {s.Effect}" : "") + (s.Priority > 0 ? ", quick" : ""),
        "heal" => $"heals {s.Power:0}%, {s.Cost} focus", _ => $"{s.Kind}, {s.Cost} focus" + (s.Priority > 0 ? ", quick" : ""),
    };

    void PetSkills(VBoxContainer v, PetState p)
    {
        Header($"Equipped ({p.Skills.Count}/4)", v);
        if (p.Skills.Count == 0) Empty(v, "No skills equipped.");
        foreach (var id in p.Skills.ToList())
        {
            var s = PetDb.Skill(id); if (s == null) continue; string sid = id;
            AddRow(v, $"{s.Name}   {SkillLine(s)}\n{s.Desc}", s.Elem == "physical" ? Parch : Dmg.TypeColor(s.Elem), "Unequip", () => { p.Skills.Remove(sid); Game.Notify(); OpenPets(null); }, p.Skills.Count > 1);
        }
        var rest = p.Learned.Where(x => !p.Skills.Contains(x)).ToList();
        if (rest.Count > 0) Header("Learned", v);
        foreach (var id in rest)
        {
            var s = PetDb.Skill(id); if (s == null) continue; string sid = id;
            AddRow(v, $"{s.Name}   {SkillLine(s)}\n{s.Desc}", Dim.Lightened(.3f), "Equip", () => { if (p.Skills.Count >= 4) { Note("Four skills at most: unequip one first", new Color(1, .6f, .5f)); return; } p.Skills.Add(sid); Game.Notify(); OpenPets(null); }, p.Skills.Count < 4);
        }
        var next = p.Sp().Learn.Where(l => !p.Learned.Contains(l.skill) && PetDb.Skill(l.skill) != null).ToList();
        if (next.Count > 0) { Header("To learn", v); foreach (var (lvl, sk) in next) Row(v, $"Level {lvl}: {PetDb.Skill(sk)!.Name}  ({SkillLine(PetDb.Skill(sk)!)})", Dim, 14); }
    }

    void PetGear(VBoxContainer v, PetState p)
    {
        var d = Game.D;
        foreach (var slot in PetDb.GearSlots)
        {
            string sl = slot; Header(PetDb.GearName(slot), v);
            var on = p.Gear.GetValueOrDefault(slot);
            if (on != null) AddRow(v, $"{on.Name} (L{on.Lvl})  {PetDb.GearText(on)}", on.Color, "Remove", () => { if (d.Inv.Count >= 28) { Note("Pack is full", new Color(1, .5f, .5f)); return; } p.Gear[sl] = null; d.Inv.Add(on); Refit(p); OpenPets(null); });
            else Row(v, "Nothing worn.", Dim, 14);
            for (int i = 0; i < d.Inv.Count; i++)
            {
                var it = d.Inv[i]; if (it.Slot != slot) continue;
                AddRow(v, $"{it.Name} (L{it.Lvl})  {PetDb.GearText(it)}", it.Color, "Equip", () => { var old = p.Gear.GetValueOrDefault(sl); d.Inv.Remove(it); p.Gear[sl] = it; if (old != null) d.Inv.Add(old); Sfx.Play("click"); Refit(p); OpenPets(null); });
            }
        }
        Row(v, "Pet gear drops from monsters now and then and is made at the craft bench (Potions & cubes).", Dim, 13);
    }
    static void Refit(PetState p) { if (PetWorld.Out != null && GodotObject.IsInstanceValid(PetWorld.Out) && PetWorld.Out.S == p) PetWorld.Out.Refresh(); Game.Notify(); }

    void PetOrders(VBoxContainer v, PetState p)
    {
        Header("Movement", v);
        var h1 = new HBoxContainer(); h1.AddThemeConstantOverride("separation", 6); v.AddChild(h1);
        Opt(h1, "Follow", !p.Stay, () => { p.Stay = false; OpenPets(null); }); Opt(h1, "Stay", p.Stay, () => { p.Stay = true; OpenPets(null); });
        Row(v, $"Key {Inputs.KeyOf(Inputs.PetStay)} switches follow / stay in the field.", Dim, 13);
        Header("When to fight", v);
        var h2 = new HBoxContainer(); h2.AddThemeConstantOverride("separation", 6); v.AddChild(h2);
        foreach (var m in PetDb.AggroModes) { string mm = m; Opt(h2, char.ToUpper(m[0]) + m[1..], p.Aggro == m, () => { p.Aggro = mm; Refit(p); OpenPets(null); }); }
        Row(v, p.Aggro switch { "passive" => "Never fights.", "defensive" => "Fights whatever strikes you or it.", "aggressive" => $"Attacks any foe within {PetDb.R("aggroRange", 12):0} m.", _ => "Attacks your target (and defends you)." }, Dim, 13);
        Header("Auto-loot", v);
        var h3 = new HBoxContainer(); h3.AddThemeConstantOverride("separation", 6); v.AddChild(h3);
        for (int i = 0; i < PetDb.LootModes.Length; i++) { int k = i; Opt(h3, PetDb.LootModes[i], p.Loot == i, () => { p.Loot = k; OpenPets(null); }); }
        if (p.Loot == 2)
        {
            var h4 = new HBoxContainer(); h4.AddThemeConstantOverride("separation", 6); v.AddChild(h4);
            for (int r = 0; r <= 4; r++) { int rr = r; var b = Opt(h4, Items.RarityName(r), p.LootRar == r, () => { p.LootRar = rr; OpenPets(null); }); b.AddThemeColorOverride("font_color", p.LootRar == r ? Gold : Items.RarityColor(r)); }
        }
        Row(v, $"Brings drops within {PetDb.R("lootRange", 15):0} m to your pack (your pickup filter still applies).", Dim, 13);
        if (p.Mountable())
        {
            Header("Riding", v);
            bool canNow = p.Id == Game.D.ActivePet && !p.Ko();
            AddRow(v, $"{p.Name()} can carry you ({PetDb.R("mountSpeedBase", 9) + p.Stats().Spd * PetDb.R("mountSpeedPerSpd", .03f):0.0} m/s). Mount key: {Inputs.KeyOf(Inputs.Mount)}.", Parch, "Ride now", () =>
            {
                if (!canNow) { Note("Set it active first", new Color(1, .6f, .5f)); return; }
                Close(); var pl = Player.Instance; if (pl != null) { if (pl.Mounted) pl.ToggleMount(true); pl.ToggleMount(); }
            }, canNow);
        }
    }
}

/// <summary>Round portrait badge: rarity colour, the species' initial, a gold ring on the one out in the world.</summary>
public partial class PetBadge : Control
{
    public Color Col = Colors.Gray; public string Letter = "?"; public bool Out;
    public override void _Draw()
    {
        var c = Size / 2; float r = Mathf.Min(Size.X, Size.Y) / 2 - 1;
        DrawCircle(c, r, new Color(.06f, .05f, .045f)); DrawCircle(c, r - 3, Col.Darkened(.45f));
        DrawArc(c, r - 1, 0, Mathf.Tau, 24, Out ? new Color(.9f, .75f, .35f) : Col, Out ? 2.5f : 1.5f, true);
        var f = ThemeDB.FallbackFont; int fs = (int)(r * 1.1f); var sz = f.GetStringSize(Letter, HorizontalAlignment.Left, -1, fs);
        DrawString(f, c + new Vector2(-sz.X / 2, sz.Y * .32f), Letter, HorizontalAlignment.Left, -1, fs, new Color(.95f, .9f, .8f));
    }
}

/// <summary>The pets menu preview: the species model (an Enemy body) turning slowly under its own light, in its own little world. Runs while paused.</summary>
public partial class PetPreview : Node3D
{
    public string Species = "wolf"; public int Level = 1; public bool Down;
    Node3D _spin = null!;
    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        var env = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(.045f, .04f, .035f), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(.55f, .5f, .45f), AmbientLightEnergy = .6f };
        AddChild(new WorldEnvironment { Environment = env });
        AddChild(new DirectionalLight3D { Rotation = new Vector3(-.7f, .6f, 0), LightEnergy = 1.3f, LightColor = new Color(1f, .93f, .82f) });
        AddChild(new OmniLight3D { Position = new Vector3(-2, 1.5f, -2), LightColor = new Color(.5f, .6f, 1f), LightEnergy = 1.2f, OmniRange = 8 });
        _spin = new Node3D(); AddChild(_spin);
        var body = new Enemy { Type = Species, PetBody = true, Level = Level, AnyRealm = true }; _spin.AddChild(body);
        if (Down) body.PetDown(true);
        float h = Mathf.Max(.3f, body.HeightM()), k = 1.5f / h; _spin.Scale = Vector3.One * k;
        var cam = new Camera3D { Fov = 40, Current = true }; AddChild(cam);
        cam.Position = new Vector3(0, 1.05f, 3.6f); cam.LookAt(new Vector3(0, .7f, 0), Vector3.Up);
        _spin.Rotation = new Vector3(0, Mathf.Pi * .8f, 0);
    }
    public override void _Process(double delta) { _spin.RotateY((float)delta * .5f); }
}
