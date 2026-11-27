using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TwinRealms;

/// <summary>
/// The character creator (focus F-36): the title screen's "new hero" page. Left: tabs (Origin = race and class, Body, Face, Hair and
/// skin, Marks and voice), the name and Begin. Right: the hero on a turntable (CreatorPreview) with Random / Reset. Every list and
/// range comes from data/creator.json (Creator); the choices are one HeroLook that goes into the new hero's save (Main: Ui.PickedLook).
/// Picking an option restyles its buttons and rebuilds the preview only; the window itself is rebuilt on a tab change, Random or Reset
/// (the preview node is carried over, so the turntable keeps its angle and zoom).
/// </summary>
public partial class Ui
{
    static HeroLook _pickLook = new() { V = 1, Hair = 0 };
    static string _crTab = "origin"; static bool _crGear = true;
    static readonly (string id, string label)[] CrTabs = { ("origin", "Origin"), ("body", "Body"), ("face", "Face"), ("hair", "Hair"), ("marks", "Marks") };
    CreatorPreview? _crPrev;
    /// <summary>The look chosen in the creator (a copy; Main puts it on the new hero).</summary>
    public static HeroLook PickedLook => _pickLook.Copy();

    /// <summary>Dev captures: preselect a race and class on the create screen.</summary>
    public static void DevPick(string race, string cls) { if (race != "") _pickRace = race; if (cls != "") _pickClass = cls; }
    bool CreateOpen => _panel != null && _crPrev != null && IsInstanceValid(_crPrev) && _crPrev.IsInsideTree();
    /// <summary>Dev ("/look set|random|reset"): puts a look into the open creator. False when the creator is not open.</summary>
    public static bool DevLook(HeroLook l) { if (I == null || !I.CreateOpen) return false; _pickLook = l.Copy(); if (_pickLook.V == 0) _pickLook = Creator.Fresh(); I.ShowCreate(); return true; }
    /// <summary>Dev ("/look tab|zoom|yaw|gear"): drives the open creator for pictures.</summary>
    public static bool DevCreate(string what, string val)
    {
        if (I == null || !I.CreateOpen) return false; var pv = I._crPrev!;
        float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f);
        switch (what)
        {
            case "tab": if (!CrTabs.Any(t => t.id == val)) return false; _crTab = val; I.ShowCreate(); return true;
            case "zoom": pv.ZoomTo = pv.Zoom = Mathf.Clamp(f, 0, 1); return true;
            case "yaw": pv.Yaw = Mathf.DegToRad(f); return true;
            case "gear": _crGear = val != "off"; I.ShowCreate(); return true;
        }
        return false;
    }

    static void CrStyle(Button b, bool on)
    {
        var sbn = UiTheme.Box(on ? new Color(.20f, .15f, .06f, .9f) : new Color(.05f, .045f, .04f, .7f), on ? Gold : Line with { A = .5f }, 1, 8, 3, 0);
        b.AddThemeStyleboxOverride("normal", sbn); b.AddThemeStyleboxOverride("pressed", sbn);
        b.AddThemeStyleboxOverride("hover", UiTheme.Box(new Color(.14f, .11f, .06f, .9f), Gold, 1, 8, 3, 0));
        b.AddThemeColorOverride("font_color", on ? Gold : Parch with { A = .75f });
    }
    PlayerStats CrStats()
    {
        var ps = new PlayerStats { Name = _pickName, Race = _pickRace, Class = _pickClass, Body = _pickBody, Look = _pickLook.Copy() };
        ps.Eq = _crGear ? Classes.StartGear(Classes.Get(_pickClass)) : new Dictionary<string, Item?>();
        if (_crTab is "face" or "hair" or "marks") ps.Eq["head"] = null;   // helmets and hoods off while the head is being worked on
        return ps;
    }
    void CrDirty() { if (_crPrev != null && IsInstanceValid(_crPrev)) _crPrev.Request(CrStats()); }

    /// <summary>New hero for the selected slot: race, class, body, face, hair, skin, markings, voice and name, with a live preview.</summary>
    public void ShowCreate()
    {
        InGame = false; Input.MouseMode = Input.MouseModeEnum.Visible; Creator.Init();
        if (_pickLook.V == 0) _pickLook = Creator.Fresh();
        if (!CrTabs.Any(t => t.id == _crTab)) _crTab = "origin";
        // the preview outlives the window rebuild: taken out before Frame frees the old window, put back into the new one
        var keep = _crPrev != null && IsInstanceValid(_crPrev) && !_crPrev.IsQueuedForDeletion() ? _crPrev : null;
        keep?.GetParent()?.RemoveChild(keep);
        var v = Frame("title", $"NEW HERO - SLOT {Game.Slot + 1}", new Vector2(1060, 640), ShowCreate);
        var look = _pickLook; bool female = _pickBody == "female";

        void Cap(Node p, string t) { var l = Lbl(t, 13, Gold, p); l.AddThemeFontOverride("font", new FontVariation { BaseFont = ThemeDB.FallbackFont, SpacingGlyph = 3 }); p.AddChild(new ColorRect { Color = Line with { A = .6f }, CustomMinimumSize = new Vector2(0, 1), MouseFilter = Control.MouseFilterEnum.Ignore }); }
        // a set of buttons where one is chosen: picking restyles them in place and refreshes the preview
        void Group(Node parent, IReadOnlyList<string> labels, int cur, Action<int> set, int columns = 0, int font = 14)
        {
            Container box;
            if (columns > 0) { var g = new GridContainer { Columns = columns, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; g.AddThemeConstantOverride("h_separation", 4); g.AddThemeConstantOverride("v_separation", 3); box = g; }
            else { var f = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; f.AddThemeConstantOverride("h_separation", 4); f.AddThemeConstantOverride("v_separation", 4); box = f; }
            parent.AddChild(box);
            var btns = new List<Button>();
            for (int i = 0; i < labels.Count; i++)
            {
                int k = i; Button b = null!;
                b = Btn(labels[i], () => { set(k); for (int j = 0; j < btns.Count; j++) CrStyle(btns[j], j == k); CrDirty(); });
                b.AddThemeFontSizeOverride("font_size", font); b.CustomMinimumSize = new Vector2(columns > 0 ? 0 : 64, 28); if (columns > 0) b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                b.ClipText = columns > 0; CrStyle(b, i == cur); box.AddChild(b); btns.Add(b);
            }
        }
        void Slider(Node parent, string name, int val, Action<int> set)
        {
            var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 8); parent.AddChild(h);
            var nl = Lbl(name, 15, Parch, h); nl.CustomMinimumSize = new Vector2(96, 0); nl.VerticalAlignment = VerticalAlignment.Center;
            var sl = new HSlider { MinValue = -100, MaxValue = 100, Step = 5, Value = val, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(160, 24), FocusMode = Control.FocusModeEnum.None };
            StyleSlider(sl); h.AddChild(sl);
            var vl = Lbl(val.ToString("+0;-0;0"), 14, GoldC, h); vl.CustomMinimumSize = new Vector2(40, 0); vl.HorizontalAlignment = HorizontalAlignment.Right; vl.VerticalAlignment = VerticalAlignment.Center;
            sl.ValueChanged += x => { int n = (int)Math.Round(x); set(n); vl.Text = n.ToString("+0;-0;0"); CrDirty(); };
        }
        // colour swatches: the race's own colour first ("" in the look), the data swatches, then a free colour picker
        void Colours(Node parent, Color own, IEnumerable<string> swatches, string cur, Action<string> set)
        {
            var f = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; f.AddThemeConstantOverride("h_separation", 4); f.AddThemeConstantOverride("v_separation", 4); parent.AddChild(f);
            var list = new List<(string hex, Color c)> { ("", own) };
            foreach (var s in swatches) if (Creator.TryHex(s, out var c) && !list.Any(x => x.hex == s)) list.Add((s, c));
            var btns = new List<Button>(); ColorPickerButton pick = null!;
            void Mark(string now)
            {
                for (int j = 0; j < btns.Count; j++)
                {
                    bool on = list[j].hex == now; var c = list[j].c;
                    StyleBoxFlat Sb(Color border, int w) => new() { BgColor = c, BorderColor = border, BorderWidthLeft = w, BorderWidthRight = w, BorderWidthTop = w, BorderWidthBottom = w, CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3 };
                    var n = Sb(on ? Colors.White : new Color(0, 0, 0, .7f), on ? 3 : 1);
                    btns[j].AddThemeStyleboxOverride("normal", n); btns[j].AddThemeStyleboxOverride("pressed", n); btns[j].AddThemeStyleboxOverride("hover", Sb(Gold, 2));
                }
            }
            for (int i = 0; i < list.Count; i++)
            {
                int k = i; var b = Btn(i == 0 ? "D" : "", () => { set(list[k].hex); Mark(list[k].hex); pick.Color = list[k].c; CrDirty(); }, true);
                b.CustomMinimumSize = new Vector2(28, 26); b.TooltipText = i == 0 ? "The race's own colour" : "#" + list[i].hex;
                b.AddThemeColorOverride("font_color", list[i].c.Luminance > .5f ? Colors.Black : Colors.White); f.AddChild(b); btns.Add(b);
            }
            pick = new ColorPickerButton { Color = Creator.TryHex(cur, out var cc) ? cc : own, EditAlpha = false, CustomMinimumSize = new Vector2(64, 26), FocusMode = Control.FocusModeEnum.None, TooltipText = "Any colour" };
            pick.ColorChanged += c => { string hex = Creator.ToHex(c); set(hex); Mark(hex); CrDirty(); };
            f.AddChild(pick); Mark(cur);
        }

        // ---- top: back + tabs
        var top = new HBoxContainer(); top.AddThemeConstantOverride("separation", 4); v.AddChild(top);
        var back = Btn("Back", ShowTitle, true); back.TooltipText = "Back to heroes"; top.AddChild(back);
        void Tab(string t) { _crTab = t; if (_crPrev != null && IsInstanceValid(_crPrev)) _crPrev.ZoomTo = t is "face" or "hair" or "marks" ? 1f : 0f; ShowCreate(); }   // head work: the camera goes to the face
        foreach (var (id, label) in CrTabs)
        {
            string t = id; var tb = Btn(label, () => Tab(t), true); tb.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; tb.CustomMinimumSize = new Vector2(0, 30);
            CrStyle(tb, id == _crTab); top.AddChild(tb);
        }
        PadTabs(CrTabs.Select(t => ((Action)(() => Tab(t.id)), t.id == _crTab)));   // LB / RB step through the tabs

        var sc = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true }; v.AddChild(sc);
        var body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; body.AddThemeConstantOverride("separation", 6); sc.AddChild(body);
        Label? head = null;   // the big "RACE CLASS" line over the preview
        var rr = Races.Get(_pickRace);

        switch (_crTab)
        {
            case "origin":
            {
                var info = new VBoxContainer(); info.AddThemeConstantOverride("separation", 4);
                void Info()
                {
                    foreach (var c in info.GetChildren()) { info.RemoveChild(c); c.QueueFree(); }
                    var rd = Races.Get(_pickRace); var cd = Classes.Get(_pickClass);
                    if (head != null) head.Text = $"{rd.Name.ToUpper()}  {cd.Name.ToUpper()}";
                    void Stats(string title, Dictionary<string, int> attr, Dictionary<string, float> mods, string extra)
                    {
                        Cap(info, title);
                        var fl = new HFlowContainer(); fl.AddThemeConstantOverride("h_separation", 12); fl.AddThemeConstantOverride("v_separation", 2); info.AddChild(fl);
                        foreach (var kv in attr) { var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 4); fl.AddChild(h); Lbl((kv.Value > 0 ? "+" : "") + kv.Value, 13, Gold, h); Lbl(kv.Key, 13, Parch, h); }
                        if (extra != "") Lbl(extra, 13, Parch, fl);
                        foreach (var kv in mods) Lbl(TreeDb.StatText(kv.Key, kv.Value), 13, Dim, fl);
                    }
                    Row(info, rd.Desc, Parch, 14);
                    Stats("RACE BONUSES", rd.Attr, rd.Mods, rd.Pts > 0 ? $"+{rd.Pts} skill point" : "");
                    foreach (var p in rd.Passives) Row(info, $"Racial passive - {p.Name}: {string.Join(", ", p.Mods.Select(kv => TreeDb.StatText(kv.Key, kv.Value)))}", Parch, 13);
                    Row(info, cd.Desc, Parch, 14);
                    Stats("CLASS BONUSES", cd.Attr, cd.Mods, "");
                    foreach (var p in cd.Passives) Row(info, $"{p.Name}: {string.Join(", ", p.Mods.Select(kv => TreeDb.StatText(kv.Key, kv.Value)))}", Parch, 13);
                    Row(info, $"Weapon: {(cd.Wt == "unarmed" ? "bare fists" : $"{cd.WName} ({Weapons.Get(cd.Wt).Name}{(cd.Shield ? " and shield" : "")})")}" + (cd.Gear.Count > 0 ? "   Gear: " + string.Join(", ", cd.Gear.Values.Select(g => g.n)) : ""), Parch, 13);
                    Row(info, "Starting skills: " + string.Join(", ", cd.Skills.Select(s => Skills.Get(s)?.Name ?? s)), Parch, 13);
                }
                var races = Races.All.ToList(); var classes = Classes.All.ToList();
                Cap(body, "RACE");
                Group(body, races.Select(r => r.Name).ToList(), races.FindIndex(r => r.Id == _pickRace), i => { _pickRace = races[i].Id; Info(); }, 3);
                Cap(body, "CLASS");
                Group(body, classes.Select(c => c.Name).ToList(), classes.FindIndex(c => c.Id == _pickClass), i => { _pickClass = classes[i].Id; Info(); }, 3);
                body.AddChild(info); Info();
                break;
            }
            case "body":
            {
                Cap(body, "BODY TYPE");
                var ids = new[] { "male", "female" };
                Group(body, new[] { "Male", "Female" }, female ? 1 : 0, i =>
                {
                    _pickBody = ids[i];
                    if (look.Hair != HeroLook.Bald && look.Hair >= Avatar.HairFiles(i == 1).Count) look.Hair = 0;   // the other body has its own hair list
                }, 2, 15);
                Cap(body, "PROPORTIONS");
                Slider(body, "Height", look.Height, x => look.Height = x);
                Slider(body, "Build", look.Build, x => look.Build = x);
                Slider(body, "Shoulders", look.Shoulders, x => look.Shoulders = x);
                Slider(body, "Legs", look.Legs, x => look.Legs = x);
                Slider(body, "Head size", look.Head, x => look.Head = x);
                Row(body, $"0 is the {rr.Name}'s own build. Height and build scale the whole body; shoulders and legs move bones; gear follows all of them.", Dim, 13);
                break;
            }
            case "face":
            {
                Cap(body, "FACE SHAPE"); Group(body, Creator.Face.Select(p => p.Name).ToList(), Math.Clamp(look.Face, 0, Creator.Face.Count - 1), i => look.Face = i);
                Cap(body, "EYES"); Group(body, Creator.Eyes.Select(p => p.Name).ToList(), Math.Clamp(look.Eyes, 0, Creator.Eyes.Count - 1), i => look.Eyes = i);
                Slider(body, "Eye size", look.EyeSize, x => look.EyeSize = x);
                Cap(body, "EYE COLOUR"); Colours(body, rr.Look.Eyes, Creator.Swatches("eyes"), look.EyeCol, s => look.EyeCol = s);
                Cap(body, "BROWS"); Group(body, Creator.Brows.Select(p => p.Name).ToList(), Math.Clamp(look.Brows, 0, Creator.Brows.Count - 1), i => look.Brows = i);
                Cap(body, "MOUTH"); Group(body, Creator.Mouth.Select(p => p.Name).ToList(), Math.Clamp(look.Mouth, 0, Creator.Mouth.Count - 1), i => look.Mouth = i);
                break;
            }
            case "hair":
            {
                var hairs = Creator.HairList(female); var labels = hairs.Select(h => h.label).ToList(); if (Creator.BaldAllowed) labels.Add("Bald");
                int cur = look.Hair >= hairs.Count ? (Creator.BaldAllowed ? hairs.Count : 0) : Math.Max(0, look.Hair);
                Cap(body, "HAIR STYLE");
                if (labels.Count > 0) Group(body, labels, cur, i => look.Hair = i >= hairs.Count ? HeroLook.Bald : i);
                Cap(body, "HAIR COLOUR"); Colours(body, rr.Look.Hair, Creator.Swatches("hair"), look.HairCol, s => look.HairCol = s);
                Cap(body, "SKIN TONE"); Colours(body, rr.Look.Skin, Creator.SkinShades(_pickRace).Concat(Creator.Swatches("skin")), look.Skin, s => look.Skin = s);
                Row(body, "D is the race's own colour. The last button opens a free colour picker.", Dim, 13);
                break;
            }
            default:
            {
                Cap(body, "MARKINGS"); Group(body, Creator.Marks.Select(m => m.Name).ToList(), Math.Clamp(look.Mark, 0, Creator.Marks.Count - 1), i => look.Mark = i);
                Cap(body, "MARKING COLOUR"); Colours(body, Creator.MarkColor(new HeroLook { V = 1, Mark = look.Mark }), Creator.Swatches("mark"), look.MarkCol, s => look.MarkCol = s);
                Cap(body, "VOICE");
                string sample = Creator.Root.TryGetProperty("voiceSample", out var vs) && vs.ValueKind == System.Text.Json.JsonValueKind.String ? vs.GetString() ?? "hurt" : "hurt";
                Group(body, Creator.Voices.Select(x => x.name).ToList(), Math.Clamp(look.Voice, 0, Creator.Voices.Count - 1), i => { look.Voice = i; Sfx.Voice(sample, .8f, Creator.Voices[i].pitch); });
                Row(body, "The voice sets the pitch of your hero's grunts and cries. Picking one plays a sample.", Dim, 13);
                break;
            }
        }

        // ---- name and begin
        var foot = new VBoxContainer(); foot.AddThemeConstantOverride("separation", 6); v.AddChild(foot); Cap(foot, "NAME");
        var name = new LineEdit { Text = _pickName, PlaceholderText = "Name your hero", CustomMinimumSize = new Vector2(0, 34), MaxLength = 24 }; foot.AddChild(name); name.TextChanged += s => _pickName = s;
        name.AddThemeStyleboxOverride("normal", UiTheme.Box(new Color(.02f, .018f, .015f, .95f), Line with { A = .5f }, 1, 10, 4, 0));
        name.AddThemeStyleboxOverride("focus", UiTheme.Box(new Color(.03f, .025f, .02f, .98f), Gold, 2, 10, 4, 0));
        name.AddThemeColorOverride("font_color", Parch);
        var go = Btn("Begin the journey", () => { CloseNoUnpause(); OnNew?.Invoke(_pickClass, name.Text, _pickRace); }); go.CustomMinimumSize = new Vector2(0, 46); go.AddThemeFontSizeOverride("font_size", 20); foot.AddChild(go);
        var gn = UiTheme.Box(new Color(.62f, .48f, .20f, .95f), Gold, 1, 12, 6, 6); var gh = UiTheme.Box(new Color(.80f, .64f, .28f, 1f), new Color(1f, .9f, .6f), 1, 12, 6, 10);
        go.AddThemeStyleboxOverride("normal", gn); go.AddThemeStyleboxOverride("hover", gh); go.AddThemeStyleboxOverride("pressed", gn);
        foreach (var cn in new[] { "font_color", "font_hover_color", "font_pressed_color" }) go.AddThemeColorOverride(cn, new Color(.08f, .05f, .02f));

        // ---- right: the live preview, its title, Random / Reset
        var vp = GetViewport().GetVisibleRect().Size; float leftW = Mathf.Min(460, vp.X - 80);
        var area = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, AnchorRight = 1, AnchorBottom = 1, OffsetLeft = 40 + leftW + 16, OffsetRight = -24, OffsetTop = 24, OffsetBottom = -24 };
        _panel?.AddChild(area);
        var frame = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore }; frame.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        frame.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(.03f, .03f, .04f, .9f), Line with { A = .7f }, 1, 0, 0, 0)); area.AddChild(frame);
        _crPrev = keep ?? new CreatorPreview { Name = "CreatorPreview" };
        _crPrev.AnchorRight = 1; _crPrev.AnchorBottom = 1; _crPrev.OffsetLeft = 2; _crPrev.OffsetTop = 2; _crPrev.OffsetRight = -2; _crPrev.OffsetBottom = -2;
        area.AddChild(_crPrev);
        head = Lbl($"{rr.Name.ToUpper()}  {Classes.Get(_pickClass).Name.ToUpper()}", 22, Gold, area); head.Position = new Vector2(16, 10); head.MouseFilter = Control.MouseFilterEnum.Ignore;
        head.AddThemeFontOverride("font", new FontVariation { BaseFont = ThemeDB.FallbackFont, SpacingGlyph = 4 }); head.AddThemeColorOverride("font_outline_color", new Color(.08f, .04f, .02f)); head.AddThemeConstantOverride("outline_size", 5);
        var bar = new HBoxContainer { AnchorTop = 1, AnchorBottom = 1, AnchorRight = 1, OffsetLeft = 12, OffsetRight = -12, OffsetTop = -44, OffsetBottom = -10 }; bar.AddThemeConstantOverride("separation", 6); area.AddChild(bar);
        var rng = new Random();
        Button Bar(string text, string tip, Action a) { var b = Btn(text, a, true); b.TooltipText = tip; b.CustomMinimumSize = new Vector2(0, 32); bar.AddChild(b); return b; }
        Bar("Random look", "A random body, face, hair, colours, marking and voice (keeps the race and class)", () => { _pickLook = Creator.Random(_pickRace, _pickBody == "female", rng); ShowCreate(); });
        Bar("Random all", "A random race, class and body type too", () =>
        {
            var rs = Races.All.ToList(); var cs = Classes.All.ToList();
            _pickRace = rs[rng.Next(rs.Count)].Id; _pickClass = cs[rng.Next(cs.Count)].Id; _pickBody = rng.Next(2) == 0 ? "male" : "female";
            _pickLook = Creator.Random(_pickRace, _pickBody == "female", rng); ShowCreate();
        });
        Bar("Reset", "Back to the race's own look", () => { _pickLook = Creator.Fresh(); ShowCreate(); });
        Bar(_crGear ? "Gear: class" : "Gear: plain", "Show the class's starting gear or plain clothes", () => { _crGear = !_crGear; ShowCreate(); });
        bar.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        var hint = Lbl(Inputs.UsingPad ? "Right stick: turn   Triggers: zoom" : "Drag: turn   Wheel: zoom (body to face)", 13, Dim, bar); hint.VerticalAlignment = VerticalAlignment.Center;
        _crPrev.Request(CrStats(), keep == null);
        if (keep == null && (_crTab is "face" or "hair" or "marks")) _crPrev.ZoomTo = 1f;
    }
}

/// <summary>
/// The creator's live preview: the hero (a real Avatar, dressed) on a turntable in its own small 3D world, drawn into the window.
/// Drag with the mouse or push the right stick to turn it; the wheel or the triggers zoom between the whole body and the face.
/// Request() asks for a new hero; it is rebuilt at most every 70 ms, so a slider drag stays smooth.
/// </summary>
public partial class CreatorPreview : Control
{
    SubViewport _vp = null!; Node3D _turn = null!; Camera3D _cam = null!; Avatar? _av; Label? _msg;
    PlayerStats? _want; bool _dirty, _drag; float _wait, _headY = 1.6f, _scale = 1f; int _headBone = -1;
    public float Yaw, Zoom, ZoomTo;
    static float P(string k, float d) => Creator.Num("preview", k, d);

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop; ClipContents = true; MouseDefaultCursorShape = CursorShape.Drag;
        Yaw = P("startYaw", .35f);
        var c = new SubViewportContainer { Stretch = true, MouseFilter = MouseFilterEnum.Ignore }; c.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(c);
        _vp = new SubViewport { OwnWorld3D = true, TransparentBg = false, Msaa3D = Viewport.Msaa.Msaa4X, RenderTargetUpdateMode = SubViewport.UpdateMode.Always, GuiDisableInput = true, Size = new Vector2I(640, 640) };
        c.AddChild(_vp);
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Creator.Col("preview", "back", "0b0c10"),
            AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Creator.Col("preview", "ambient", "8a8fa0"), AmbientLightEnergy = P("ambientEnergy", .55f),
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
        };
        _vp.AddChild(new WorldEnvironment { Environment = env });
        // the camera stands at -Z looking +Z (the hero faces -Z): key light from the camera's side and above, a cool fill, a rim from behind
        _vp.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-38, 150, 0), LightColor = Creator.Col("preview", "key", "fff0dc"), LightEnergy = P("keyEnergy", 1.35f), ShadowEnabled = true });
        _vp.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-12, -145, 0), LightColor = Creator.Col("preview", "fill", "9ab4e0"), LightEnergy = P("fillEnergy", .45f) });
        _vp.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-28, 25, 0), LightColor = Creator.Col("preview", "rim", "c8d8ff"), LightEnergy = P("rimEnergy", .8f) });
        _vp.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = .85f, BottomRadius = .9f, Height = .06f, RadialSegments = 40 }, Position = new Vector3(0, -.03f, 0), MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(.09f, .085f, .08f), Roughness = .9f } });
        _turn = new Node3D { Name = "Turntable" }; _vp.AddChild(_turn);
        _cam = new Camera3D { Fov = Mathf.Clamp(P("fov", 30f), 10f, 70f), Near = .05f, Far = 40f, Rotation = new Vector3(0, Mathf.Pi, 0), Position = new Vector3(0, 1f, -4.5f), Current = true };
        _vp.AddChild(_cam);
        if (!Avatar.Available)
        {
            _msg = new Label { Text = "The character models are not installed.", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
            _msg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(_msg);
        }
    }

    /// <summary>Show this hero (race, class, body, look and worn gear). now: build it this frame instead of after the short wait.</summary>
    public void Request(PlayerStats ps, bool now = false) { _want = ps; _dirty = true; if (now) _wait = 0; }

    void Rebuild()
    {
        if (_want == null || !Avatar.Available || _turn == null) return;
        var ps = _want;
        try
        {
            var ap = Races.Look(ps);
            var av = new Avatar { Name = "Avatar" }; _turn.AddChild(av);
            av.Build(ap, ps.Name);
            av.Dress(s => ps.Eq.GetValueOrDefault(s), ps.WType(), Classes.Get(ps.Class).Tunic);
            if (_av != null && IsInstanceValid(_av)) { _av.GetParent()?.RemoveChild(_av); _av.QueueFree(); }   // the old one goes only once the new one stands
            _av = av; _scale = ap.Height * ap.HeadScale; _headBone = av.Skel?.FindBone("Head") ?? -1; _headY = 1.56f * ap.Height;
        }
        catch (Exception e) { GD.PrintErr("[CREATOR] preview: " + e.Message); }
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex is MouseButton.Left or MouseButton.Right) { _drag = mb.Pressed; AcceptEvent(); }
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp) { ZoomTo = Mathf.Clamp(ZoomTo + P("wheelZoom", .2f), 0, 1); AcceptEvent(); }
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown) { ZoomTo = Mathf.Clamp(ZoomTo - P("wheelZoom", .2f), 0, 1); AcceptEvent(); }
        }
        else if (e is InputEventMouseMotion mm && _drag) { Yaw += mm.Relative.X * P("dragTurn", .01f); AcceptEvent(); }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (_dirty) { _wait -= dt; if (_wait <= 0) { _dirty = false; _wait = .07f; Rebuild(); } }
        if (!IsVisibleInTree() || _cam == null) return;
        if (Inputs.UsingPad)
        {
            float rx = Inputs.PadAxis(JoyAxis.RightX); if (Mathf.Abs(rx) > .25f) Yaw += rx * P("stickTurn", 2.6f) * dt;
            float z = Inputs.PadAxis(JoyAxis.TriggerRight) - Inputs.PadAxis(JoyAxis.TriggerLeft);
            if (Mathf.Abs(z) > .15f) ZoomTo = Mathf.Clamp(ZoomTo + z * P("triggerZoom", 1.4f) * dt, 0, 1);
        }
        Zoom = Mathf.Lerp(Zoom, ZoomTo, 1f - Mathf.Exp(-10f * dt));
        _turn.Rotation = new Vector3(0, Yaw, 0);
        // the head's real height (it moves with the race, the height slider and the idle sway), smoothed
        if (_av != null && IsInstanceValid(_av) && _av.Skel is { } sk && _headBone >= 0 && sk.IsInsideTree())
            _headY = Mathf.Lerp(_headY, (sk.GlobalTransform * sk.GetBoneGlobalPose(_headBone).Origin).Y, 1f - Mathf.Exp(-6f * dt));
        float half = Mathf.Tan(Mathf.DegToRad(_cam.Fov) * .5f), aspect = Size.Y > 1 ? Size.X / Size.Y : 1f;
        float top = _headY + .3f * _scale;                                   // crown of the head, with hair
        float fullD = (top * .5f + P("margin", .16f)) / half; if (aspect < .62f) fullD *= .62f / aspect;   // a narrow window: step back so the arms fit
        float faceD = Mathf.Max(.3f, P("faceDist", .72f) * _scale), faceY = _headY + P("faceUp", .045f) * _scale;
        float d = fullD * Mathf.Pow(faceD / fullD, Zoom), y = Mathf.Lerp(top * .5f, faceY, Zoom);
        _cam.Position = new Vector3(0, y, -d);
    }
}
