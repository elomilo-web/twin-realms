using System;
using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// Icon strings, drawn by <see cref="Icons.Draw"/>: "skill:&lt;id&gt;" (a built-in skill's PNG, res://assets/icons/skills/), and the
/// player's own icons saved on a crafted spell (<see cref="CraftedSpell.Icon"/>): "lib:&lt;skill id&gt;" (picked from the library),
/// "ed:shape.symbol.bg.fg.border" (the icon editor, drawn by <see cref="IconArt.RenderEditor"/>) or "png:&lt;base64&gt;" (an imported
/// PNG from user://icons/, shrunk to 32 px). The string travels inside the save, so it goes online with it; <see cref="MaxLen"/> caps it.
/// </summary>
public static class SpellIcon
{
    public const int MaxLen = 6000, ImportPx = 32;
    public const string ImportDir = "user://icons/";
    static readonly Dictionary<string, Texture2D?> _tex = new();
    static readonly Dictionary<string, bool> _has = new();
    static readonly HashSet<string> _warned = new();

    public static bool IsTex(string id) => id.StartsWith("skill:") || id.StartsWith("lib:") || id.StartsWith("ed:") || id.StartsWith("png:");

    /// <summary>Whether a built-in skill has its generated PNG.</summary>
    public static bool HasBuiltin(string skill)
    {
        if (_has.TryGetValue(skill, out var b)) return b;
        string path = IconArt.Dir + skill + ".png";
        b = ResourceLoader.Exists(path) || FileAccess.FileExists(path);
        if (!b && _warned.Add(skill)) GD.PushWarning($"[ICONS] no icon {path}: using the old vector glyph (run --gen-icons)");
        return _has[skill] = b;
    }

    public static Texture2D? Tex(string icon)
    {
        if (_tex.TryGetValue(icon, out var t)) return t;
        t = null;
        try
        {
            if (icon.StartsWith("skill:") || icon.StartsWith("lib:"))
            {
                string path = IconArt.Dir + icon[(icon.IndexOf(':') + 1)..] + ".png";
                if (ResourceLoader.Exists(path)) t = ResourceLoader.Load<Texture2D>(path);
                else if (FileAccess.FileExists(path)) { var img = Image.LoadFromFile(ProjectSettings.GlobalizePath(path)); if (img != null) t = ImageTexture.CreateFromImage(img); }
            }
            else if (icon.StartsWith("ed:") && ParseEd(icon) is { } e) t = ImageTexture.CreateFromImage(IconArt.RenderEditor(e.shape, e.symbol, e.bg, e.fg, e.border, 64));
            else if (icon.StartsWith("png:") && FromB64(icon[4..]) is { } im) t = ImageTexture.CreateFromImage(im);
        }
        catch (Exception ex) { GD.PushWarning("[ICONS] bad icon: " + ex.Message); }
        if (_tex.Count > 400) _tex.Clear();
        return _tex[icon] = t;
    }

    public static string Ed(int shape, int symbol, Color bg, Color fg, int border) => $"ed:{shape}.{symbol}.{bg.ToHtml(false)}.{fg.ToHtml(false)}.{border}";
    public static (int shape, int symbol, Color bg, Color fg, int border)? ParseEd(string s)
    {
        if (!s.StartsWith("ed:")) return null;
        var p = s[3..].Split('.'); if (p.Length != 5) return null;
        if (!int.TryParse(p[0], out int sh) || !int.TryParse(p[1], out int sy) || !int.TryParse(p[4], out int bo)) return null;
        if (!Color.HtmlIsValid(p[2]) || !Color.HtmlIsValid(p[3])) return null;
        return (Math.Clamp(sh, 0, IconArt.EdShapes.Length - 1), Math.Clamp(sy, 0, IconArt.EdSymbols.Length - 1), Color.FromHtml(p[2]), Color.FromHtml(p[3]), Math.Clamp(bo, 0, IconArt.EdBorders.Length - 1));
    }

    static Image? FromB64(string b64)
    {
        var img = new Image(); if (img.LoadPngFromBuffer(Convert.FromBase64String(b64)) != Error.Ok) return null;
        if (img.GetWidth() > 64 || img.GetHeight() > 64) return null;
        return img;
    }

    /// <summary>A saved icon string made safe: too long, unknown or broken icons become "" (the spell's automatic icon).</summary>
    public static string Clean(string? s)
    {
        if (string.IsNullOrEmpty(s) || s.Length > MaxLen) return "";
        if (s.StartsWith("lib:")) { var id = s[4..]; return id.Length is > 0 and < 40 && Skills.Get(id) is { Crafted: false } ? s : ""; }
        if (s.StartsWith("ed:")) return ParseEd(s) is { } e ? Ed(e.shape, e.symbol, e.bg, e.fg, e.border) : "";
        if (s.StartsWith("png:")) { try { return FromB64(s[4..]) != null ? s : ""; } catch { return ""; } }
        return "";
    }

    /// <summary>Imports a PNG (any size) as a compact "png:" icon, or returns null with the reason.</summary>
    public static string? FromPng(string path, out string why)
    {
        why = "";
        var img = Image.LoadFromFile(ProjectSettings.GlobalizePath(path));
        if (img == null || img.IsEmpty()) { why = "Could not read that PNG."; return null; }
        img.Convert(Image.Format.Rgba8);
        foreach (int px in new[] { ImportPx, 24, 16 })
        {
            var c = (Image)img.Duplicate(); c.Resize(px, px, Image.Interpolation.Lanczos);
            var s = "png:" + Convert.ToBase64String(c.SavePngToBuffer());
            if (s.Length <= MaxLen) return s;
        }
        why = "That picture is too detailed to fit the icon size limit."; return null;
    }

    /// <summary>PNG files waiting in user://icons/ (the folder is made if missing).</summary>
    public static List<string> ImportFiles()
    {
        DirAccess.MakeDirRecursiveAbsolute(ImportDir);
        var l = new List<string>(); using var d = DirAccess.Open(ImportDir); if (d == null) return l;
        foreach (var f in d.GetFiles()) if (f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) l.Add(ImportDir + f);
        l.Sort(); return l;
    }

    /// <summary>The automatic icon of a crafted spell without its own: an editor icon from its delivery and element.</summary>
    public static string Auto(SkillDef k)
    {
        int sym = k.Type switch { "fire" => 0, "frost" => 1, "lightning" => 2, "poison" => 3, "shadow" => 5, "holy" => 7, _ => 21 };
        int shape = k.Kind switch { "projectile" or "boomerang" => 1, "zone" or "vortex" or "nova" => 4, "enchant" or "ward" or "buff" => 3, "beam" or "chain" => 2, _ => 0 };
        var c = Dmg.TypeColor(k.Type);
        return Ed(shape, sym, c.Darkened(.45f), c.Lightened(.25f), 2);
    }
}
