using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>A saved build design: size, a palette of block keys and its cells (x, y, z, palette index, meta), relative to its lowest corner.</summary>
public sealed class Design
{
    public string Name = ""; public int W, H, D; public List<string> Pal = new(); public List<int[]> Cells = new();
}

/// <summary>
/// A-28 the build library (data/buildlib.json; format in its "_help"). A selection box between two marked corners in build mode is saved
/// as a design (block keys, meta / orientation, relative positions; air, fluids and crops are skipped; chest contents are not copied)
/// in user://designs, one file per design, shared by all heroes of this player. A design is placed as a whole: a ghost snapped to the
/// aimed block, turned in quarter turns (Structures.Rot / RotMeta), every cell checked with BlockFx.CanPlace (town zones, dungeons,
/// the arena and kept-clear paths refuse as for a single block), the materials taken from the pack all or nothing, and every block set
/// through BlockWorld.Change, so in multiplayer each one goes to the host (and the online server audits and logs each block).
/// </summary>
public static class BuildLibrary
{
    public static Vector3I? A, B; public static Design? Placing; public static int Rot;
    public static Vector3I Max = new(24, 24, 24); public static int MaxBlocks = 4000;
    public static Key KeyA = Key.Bracketleft, KeyB = Key.Bracketright, KeyLib = Key.Backslash;
    static bool _loaded; static readonly JsonSerializerOptions Opt = new() { IncludeFields = true };

    public static void Load()
    {
        if (_loaded) return; _loaded = true;
        try
        {
            using var doc = JsonUtil.Load("res://data/buildlib.json"); var r = doc.RootElement;
            if (r.TryGetProperty("max", out var m) && m.GetArrayLength() >= 3) Max = new Vector3I(Math.Max(1, m[0].GetInt32()), Math.Max(1, m[1].GetInt32()), Math.Max(1, m[2].GetInt32()));
            MaxBlocks = Math.Max(1, (int)JsonUtil.F(r, "maxBlocks", 4000));
            if (r.TryGetProperty("keys", out var k))
            {
                if (Enum.TryParse<Key>(JsonUtil.S(k, "corner1"), true, out var a)) KeyA = a;
                if (Enum.TryParse<Key>(JsonUtil.S(k, "corner2"), true, out var b)) KeyB = b;
                if (Enum.TryParse<Key>(JsonUtil.S(k, "library"), true, out var l)) KeyLib = l;
            }
        }
        catch (Exception e) { GD.PushWarning("[DESIGNS] buildlib.json: " + e.Message); }
    }

    // ---------------------------------------------------------------- the selection
    public static (Vector3I mn, Vector3I mx)? Box => A is { } a && B is { } b ? (new Vector3I(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z)), new Vector3I(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z))) : null;
    static bool Fits(Vector3I mn, Vector3I mx) => mx.X - mn.X < Max.X && mx.Y - mn.Y < Max.Y && mx.Z - mn.Z < Max.Z;
    public static void SetCorner(bool first, Vector3I cell)
    {
        Load(); var oa = A; var ob = B;
        if (first) A = cell; else B = cell;
        if (A == null) A = cell; if (B == null) B = cell;
        if (Box is { } bx && !Fits(bx.mn, bx.mx)) { A = oa; B = ob; Ui.Note($"A design is at most {Max.X} x {Max.Y} x {Max.Z} blocks", new Color(1, .6f, .5f)); return; }
        var q = Box!.Value; Ui.Note($"Corner {(first ? 1 : 2)} set: {q.mx.X - q.mn.X + 1} x {q.mx.Y - q.mn.Y + 1} x {q.mx.Z - q.mn.Z + 1} blocks   ({KeyLib} opens the library)", Ui.Dim);
    }
    /// <summary>Moves the low and the high corner of the box (the window's nudge and resize buttons); refused past the size limit.</summary>
    public static void Nudge(Vector3I dMin, Vector3I dMax)
    {
        if (Box is not { } b) return; var mn = b.mn + dMin; var mx = b.mx + dMax;
        if (mx.X < mn.X || mx.Y < mn.Y || mx.Z < mn.Z || !Fits(mn, mx)) { Sfx.UiError(); return; }
        A = mn; B = mx;
    }

    // ---------------------------------------------------------------- designs
    public static Design? Capture(BlockArea a, Vector3I mn, Vector3I mx, string name)
    {
        var d = new Design { Name = name, W = mx.X - mn.X + 1, H = mx.Y - mn.Y + 1, D = mx.Z - mn.Z + 1 }; var pal = new Dictionary<string, int>();
        for (int y = mn.Y; y <= mx.Y; y++) for (int z = mn.Z; z <= mx.Z; z++) for (int x = mn.X; x <= mx.X; x++)
        {
            byte id = a.Get(x, y, z); if (id == 0 || id == 255) continue; var def = Blocks.Def(id);
            if (def.IsFluid || def.Shape == "crop" || !def.Breakable) continue;
            if (!pal.TryGetValue(def.Key, out int pi)) { pal[def.Key] = pi = d.Pal.Count; d.Pal.Add(def.Key); }
            d.Cells.Add(new[] { x - mn.X, y - mn.Y, z - mn.Z, pi, a.GetMeta(x, y, z) & ~BlockUse.LockBit });
            if (d.Cells.Count > MaxBlocks) return null;
        }
        return d.Cells.Count == 0 ? null : d;
    }
    /// <summary>The pack item one cell costs ("" = nothing: the upper half of a door comes with the lower one).</summary>
    public static string ItemOf(byte id, int meta)
    {
        var def = Blocks.Def(id); if (def.Shape == "door" && (meta & 4) != 0) return "";
        string key = def.Key.EndsWith("_open") ? def.Key[..^5] : def.Key; return Blocks.Exists(key) ? Blocks.Item(key) : "";
    }
    public static Dictionary<string, int> Materials(Design d)
    {
        var need = new Dictionary<string, int>();
        foreach (var c in d.Cells)
        {
            if (c.Length < 5 || c[3] < 0 || c[3] >= d.Pal.Count) continue; byte id = Blocks.Id(d.Pal[c[3]]); if (id == 0) continue;
            string item = ItemOf(id, c[4]); if (item != "") need[item] = need.GetValueOrDefault(item) + 1;
        }
        return need;
    }
    /// <summary>The design's cells in the world: its low corner on origin, turned rot quarter turns about it.</summary>
    public static IEnumerable<(Vector3I p, byte id, byte meta)> WorldCells(Design d, Vector3I origin, int rot)
    {
        foreach (var c in d.Cells)
        {
            if (c.Length < 5 || c[3] < 0 || c[3] >= d.Pal.Count) continue; byte id = Blocks.Id(d.Pal[c[3]]); if (id == 0) continue;
            var (rx, rz) = Structures.Rot(c[0], c[2], rot);
            yield return (new Vector3I(origin.X + rx, origin.Y + c[1], origin.Z + rz), id, Structures.RotMeta(id, (byte)c[4], rot));
        }
    }
    public static bool CellFree(BlockArea a, Vector3I p)
    {
        byte cur = a.Get(p.X, p.Y, p.Z);
        return cur != 255 && (cur == 0 || Blocks.Def(cur).IsFluid) && !a.Gen.KeepClear(p.X, p.Y, p.Z) && TownZones.Deny(p.X, p.Z) == null;
    }
    /// <summary>A small top-down picture: per column the colour of its highest block.</summary>
    public static ImageTexture Preview(Design d)
    {
        int w = Math.Max(1, d.W), h = Math.Max(1, d.D); var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8); img.Fill(new Color(0, 0, 0, 0));
        var top = new Dictionary<int, int>();
        foreach (var c in d.Cells)
        {
            if (c.Length < 5 || c[0] < 0 || c[0] >= w || c[2] < 0 || c[2] >= h || c[3] < 0 || c[3] >= d.Pal.Count) continue; int k = c[2] * w + c[0];
            if (top.TryGetValue(k, out int ty) && ty > c[1]) continue; top[k] = c[1];
            byte id = Blocks.Id(d.Pal[c[3]]); var col = id == 0 ? new Color(.5f, .5f, .5f) : Blocks.Tint(Blocks.Def(id)); float sh = .7f + .3f * (c[1] + 1f) / Math.Max(1, d.H);
            img.SetPixel(c[0], c[2], new Color(col.R * sh, col.G * sh, col.B * sh, 1));
        }
        return ImageTexture.CreateFromImage(img);
    }

    // ---------------------------------------------------------------- files (user://designs)
    public static string Dir { get { string p = ProjectSettings.GlobalizePath("user://designs"); try { Directory.CreateDirectory(p); } catch { } return p; } }
    static string Safe(string name) { var s = new string(name.Where(ch => char.IsLetterOrDigit(ch) || ch is ' ' or '-' or '_').ToArray()).Trim(); return s == "" ? "design" : s.Length > 40 ? s[..40] : s; }
    public static List<(string file, Design d)> All()
    {
        var l = new List<(string, Design)>();
        try
        {
            foreach (var f in Directory.GetFiles(Dir, "*.json").OrderBy(x => x))
            {
                try { var d = JsonSerializer.Deserialize<Design>(File.ReadAllText(f), Opt); if (d != null && d.Cells != null && d.Pal != null) { if (d.Name == "") d.Name = Path.GetFileNameWithoutExtension(f); l.Add((f, d)); } }
                catch (Exception e) { GD.PushWarning("[DESIGNS] " + f + ": " + e.Message); }
            }
        }
        catch (Exception e) { GD.PushWarning("[DESIGNS] " + e.Message); }
        return l;
    }
    public static string? Save(Design d, string? file = null)
    {
        try
        {
            d.Name = Safe(d.Name);
            if (file == null) { file = Path.Combine(Dir, d.Name + ".json"); for (int i = 2; File.Exists(file); i++) file = Path.Combine(Dir, $"{d.Name} {i}.json"); }
            File.WriteAllText(file, JsonSerializer.Serialize(d, Opt)); return file;
        }
        catch (Exception e) { GD.PushWarning("[DESIGNS] save: " + e.Message); return null; }
    }
    public static void Delete(string file) { try { File.Delete(file); } catch { } }
    public static void Rename(string file, Design d, string name)
    {
        string old = d.Name; d.Name = name; if (Save(d) != null) Delete(file); else d.Name = old;
    }
}

public partial class Player
{
    MeshInstance3D? _selBox; StandardMaterial3D? _selMat; MultiMeshInstance3D? _dGhost; string _dKey = ""; float _dT;
    static readonly Dictionary<Key, bool> _keyWas = new();
    static bool KeyHit(Key k) { bool down = Input.IsPhysicalKeyPressed(k), was = _keyWas.GetValueOrDefault(k); _keyWas[k] = down; return down && !was; }

    /// <summary>Every physics frame: the selection box shows in build mode only; leaving build mode puts a design away.</summary>
    void DesignTick(float dt, bool captured)
    {
        bool on = BuildMode && BlockWorld.Enabled;
        if (!on)
        {
            if (_selBox != null && IsInstanceValid(_selBox)) _selBox.Visible = false;
            if (_dGhost != null && IsInstanceValid(_dGhost)) _dGhost.Visible = false;
            BuildLibrary.Placing = null; return;
        }
        if (BuildLibrary.Box is not { } b) { if (_selBox != null && IsInstanceValid(_selBox)) _selBox.Visible = false; return; }
        if (_selBox == null || !IsInstanceValid(_selBox))
        {
            _selMat = new StandardMaterial3D { Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(.4f, .8f, 1f, .16f), CullMode = BaseMaterial3D.CullModeEnum.Disabled };
            _selBox = new MeshInstance3D { Mesh = new BoxMesh { Size = Vector3.One }, MaterialOverride = _selMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, TopLevel = true, Name = "DesignSelection" };
            AddChild(_selBox);
        }
        var size = new Vector3(b.mx.X - b.mn.X + 1, b.mx.Y - b.mn.Y + 1, b.mx.Z - b.mn.Z + 1);
        _selBox.Visible = true; _selBox.GlobalPosition = new Vector3(b.mn.X, b.mn.Y, b.mn.Z) + size * .5f; _selBox.Scale = size + Vector3.One * .04f;
    }

    /// <summary>Build mode: the corner keys, the library key, and placing a design (true = the normal dig / place is skipped this frame).</summary>
    bool DesignInput(float dt, bool captured)
    {
        BuildLibrary.Load();
        if (captured)
        {
            bool k1 = KeyHit(BuildLibrary.KeyA), k2 = KeyHit(BuildLibrary.KeyB), kl = KeyHit(BuildLibrary.KeyLib);
            if ((k1 || k2) && AimNow() is { } t) BuildLibrary.SetCorner(k1, t.b);
            if (kl) { Ui.I?.OpenDesigns(); return true; }
        }
        var d = BuildLibrary.Placing;
        if (d == null) { if (_dGhost != null && IsInstanceValid(_dGhost)) _dGhost.Visible = false; return false; }
        HideBlockFx();
        if (BlockWorld.I?.Area is not { } a) return true;
        if (captured && Input.IsActionJustPressed(Inputs.Attack)) { BuildLibrary.Placing = null; Ui.Note("Design put away", Ui.Dim); return true; }
        if (captured && Input.IsActionJustPressed(Inputs.BlockRotate)) { BuildLibrary.Rot = (BuildLibrary.Rot + 1) & 3; _dKey = ""; }
        if (AimNow() is not { } aim) { if (_dGhost != null && IsInstanceValid(_dGhost)) _dGhost.Visible = false; return true; }
        var origin = aim.b + aim.n;
        // the ghost: one small cube per cell, pale where the cell is free, red where something is in the way (made again when it moves, and twice a second)
        _dT -= dt; string key = $"{d.Name}|{d.Cells.Count}|{origin}|{BuildLibrary.Rot}";
        if (key != _dKey || _dT <= 0)
        {
            _dKey = key; _dT = .5f; var cells = BuildLibrary.WorldCells(d, origin, BuildLibrary.Rot).ToList();
            if (_dGhost == null || !IsInstanceValid(_dGhost))
            {
                var gm = new StandardMaterial3D { Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true };
                _dGhost = new MultiMeshInstance3D { MaterialOverride = gm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, TopLevel = true, Name = "DesignGhost" }; AddChild(_dGhost);
            }
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = new BoxMesh { Size = new Vector3(.96f, .96f, .96f) }, InstanceCount = cells.Count };
            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i]; bool free = BuildLibrary.CellFree(a, c.p); var col = Blocks.Tint(Blocks.Def(c.id)).Lerp(Colors.White, .35f);
                mm.SetInstanceTransform(i, new Transform3D(Basis.Identity, new Vector3(c.p.X + .5f, c.p.Y + .5f, c.p.Z + .5f)));
                mm.SetInstanceColor(i, free ? new Color(col, .42f) : new Color(1f, .22f, .18f, .5f));
            }
            _dGhost.Multimesh = mm; _dGhost.GlobalTransform = Transform3D.Identity;
        }
        _dGhost!.Visible = true;
        if (captured && Input.IsActionJustPressed(Inputs.Heavy))
        {
            string? why = PlaceDesign(a, d, origin, BuildLibrary.Rot); _dKey = "";
            if (why != null) { Ui.Note(why, new Color(1, .6f, .5f)); Sfx.UiError(); }
            else Ui.Note($"Placed {d.Name}", UiTheme.Highlight);
        }
        return true;
    }

    /// <summary>Sets a whole design: all cells free (BlockFx.CanPlace, as for one block) and all materials in the pack, or nothing happens (null = done, else why not).</summary>
    string? PlaceDesign(BlockArea a, Design d, Vector3I origin, int rot)
    {
        if (BlockFx.NoEdit() is { } no) return no;
        var w = BlockWorld.I; if (w == null) return "No block world here";
        var cells = BuildLibrary.WorldCells(d, origin, rot).ToList(); if (cells.Count == 0) return "The design holds no blocks this world knows";
        int blocked = 0; string first = "";
        foreach (var c in cells) if (BlockFx.CanPlace(c.p.X, c.p.Y, c.p.Z) is { } bw) { blocked++; if (first == "") first = bw; }
        if (blocked > 0) return $"{blocked} of {cells.Count} blocks cannot go there: {first}";
        var need = BuildLibrary.Materials(d); var mats = Game.D.Mats;
        var miss = need.Where(kv => mats.GetValueOrDefault(kv.Key) < kv.Value).Select(kv => $"{kv.Value - mats.GetValueOrDefault(kv.Key)} {GatherDb.Mat(kv.Key).Name}").ToList();
        if (miss.Count > 0) return "Missing: " + string.Join(", ", miss);
        foreach (var c in cells.OrderBy(c => c.p.Y).ThenBy(c => Blocks.Def(c.id).Shape == "" ? 0 : 1))
        {
            if (!w.Change(c.p.X, c.p.Y, c.p.Z, c.id, c.meta)) continue;
            BlockUse.Placed(a, c.p.X, c.p.Y, c.p.Z, BlockUse.Me); NotePlaced(c.p);
            string item = BuildLibrary.ItemOf(c.id, c.meta);
            if (item != "") { mats[item] = mats.GetValueOrDefault(item) - 1; if (mats[item] <= 0) mats.Remove(item); }
        }
        Sfx.Play("click", .8f); Sfx.Play("hit", .4f, .75f); Game.Notify(); _barRefresh = 0; _swingT = .28f; _swingSerial++;
        return null;
    }
}

public partial class Ui
{
    static string _designName = "";
    /// <summary>A-28: the Build Library window: the selection (nudge, resize, save) and the saved designs (place, rename, delete).</summary>
    public void OpenDesigns()
    {
        BuildLibrary.Load(); var d = Game.D; var v = Frame("designs", "Build Library", new Vector2(980, 600), OpenDesigns);
        Row(v, $"In build mode: {BuildLibrary.KeyA} and {BuildLibrary.KeyB} mark the two corners of a box on the aimed block, {BuildLibrary.KeyLib} opens this window. Placing: {Inputs.KeyOf(Inputs.Heavy)} sets the design, {Inputs.KeyOf(Inputs.BlockRotate)} turns it, {Inputs.KeyOf(Inputs.Attack)} puts it away.", Dim, 13);
        var nameRow = new HBoxContainer(); nameRow.AddThemeConstantOverride("separation", 8); v.AddChild(nameRow);
        Lbl("Name", 14, Parch, nameRow);
        var name = new LineEdit { Text = _designName, PlaceholderText = "name of the design", MaxLength = 40, CustomMinimumSize = new Vector2(260, 0) }; name.TextChanged += t => _designName = t; nameRow.AddChild(name);
        Header("Selection", v);
        var area = BlockWorld.I?.Area;
        if (BuildLibrary.Box is { } bx)
        {
            Row(v, $"{bx.mx.X - bx.mn.X + 1} x {bx.mx.Y - bx.mn.Y + 1} x {bx.mx.Z - bx.mn.Z + 1} blocks, from ({bx.mn.X}, {bx.mn.Y}, {bx.mn.Z}) to ({bx.mx.X}, {bx.mx.Y}, {bx.mx.Z})   (at most {BuildLibrary.Max.X} x {BuildLibrary.Max.Y} x {BuildLibrary.Max.Z})", Parch, 14);
            var ax = new[] { ("X", new Vector3I(1, 0, 0)), ("Y", new Vector3I(0, 1, 0)), ("Z", new Vector3I(0, 0, 1)) };
            void NRow(string title, Func<Vector3I, int, (Vector3I, Vector3I)> f)
            {
                var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 4); v.AddChild(row); var tl = Lbl(title, 13, Dim, row); tl.CustomMinimumSize = new Vector2(150, 0);
                foreach (var (an, av) in ax) foreach (int s in new[] { -1, 1 })
                { var (da, db) = f(av, s); row.AddChild(Btn((s < 0 ? "- " : "+ ") + an, () => { BuildLibrary.Nudge(da, db); OpenDesigns(); }, true)); }
            }
            NRow("Move the box", (a, s) => (a * s, a * s));
            NRow("Low side (- grows)", (a, s) => (a * s, Vector3I.Zero));
            NRow("High side (+ grows)", (a, s) => (Vector3I.Zero, a * s));
            var sr = new HBoxContainer(); sr.AddThemeConstantOverride("separation", 8); v.AddChild(sr);
            sr.AddChild(Btn("Save design", () =>
            {
                if (area == null) { Note("No block world here", ErrRed); return; }
                var des = BuildLibrary.Capture(area, bx.mn, bx.mx, _designName.Trim() == "" ? "design" : _designName.Trim());
                if (des == null) { Note($"Nothing to save there (or more than {BuildLibrary.MaxBlocks} blocks)", ErrRed); Sfx.UiError(); return; }
                if (BuildLibrary.Save(des) == null) { Note("The design could not be written", ErrRed); return; }
                Note($"Saved the design {des.Name}: {des.Cells.Count} blocks", UiTheme.Highlight); Sfx.UiConfirm(); OpenDesigns();
            }));
            sr.AddChild(Btn("Clear the selection", () => { BuildLibrary.A = null; BuildLibrary.B = null; OpenDesigns(); }, true));
        }
        else CraftHint(v, "Nothing is selected. Close this window and mark two corners in build mode.", 13);
        var all = BuildLibrary.All();
        Header($"Saved designs ({all.Count})", v);
        if (all.Count == 0) CraftHint(v, "No designs yet.", 13);
        foreach (var (file, des0) in all)
        {
            var des = des0; string fl = file; var card = CardBox(v); var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 12); card.AddChild(h);
            h.AddChild(new TextureRect { Texture = BuildLibrary.Preview(des), CustomMinimumSize = new Vector2(72, 72), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TextureFilter = CanvasItem.TextureFilterEnum.Nearest });
            var tv = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; tv.AddThemeConstantOverride("separation", 1); h.AddChild(tv);
            Lbl(des.Name, 16, GoldC, tv); Lbl($"{des.W} x {des.H} x {des.D}, {des.Cells.Count} blocks", 12, Dim, tv);
            var need = BuildLibrary.Materials(des); bool ok = need.All(kv => d.Mats.GetValueOrDefault(kv.Key) >= kv.Value);
            var ml = Lbl(string.Join(",  ", need.OrderByDescending(kv => kv.Value).Select(kv => $"{GatherDb.Mat(kv.Key).Name} {d.Mats.GetValueOrDefault(kv.Key)}/{kv.Value}")), 12, ok ? new Color(.6f, 1f, .6f) : Muted, tv);
            ml.AutowrapMode = TextServer.AutowrapMode.WordSmart; ml.CustomMinimumSize = new Vector2(420, 0);
            var bv = new VBoxContainer(); bv.AddThemeConstantOverride("separation", 3); h.AddChild(bv);
            bv.AddChild(Btn("Place", () =>
            {
                BuildLibrary.Placing = des; BuildLibrary.Rot = 0; Player.BuildMode = true; Close();
                Note($"{des.Name}: aim at a block, {Inputs.KeyOf(Inputs.Heavy)} places it, {Inputs.KeyOf(Inputs.BlockRotate)} turns it, {Inputs.KeyOf(Inputs.Attack)} puts it away", UiTheme.Highlight);
            }, true));
            bv.AddChild(Btn("Rename", () => { if (_designName.Trim() == "") { Note("Type the new name in the Name box first", Dim); return; } BuildLibrary.Rename(fl, des, _designName.Trim()); OpenDesigns(); }, true));
            bv.AddChild(Btn("Delete", () => { BuildLibrary.Delete(fl); OpenDesigns(); }, true));
        }
        var foot = new HBoxContainer(); foot.AddThemeConstantOverride("separation", 8); v.AddChild(foot);
        foot.AddChild(Btn("Open the designs folder", () => OS.ShellOpen(BuildLibrary.Dir), true)); foot.AddChild(Btn("Look again", OpenDesigns, true));
        var pl = Lbl("Export / import: every design is one .json file in " + BuildLibrary.Dir + ". Copy files out to share them; files put there show up here.", 12, Dim, v); pl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
    }
}
