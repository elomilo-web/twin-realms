using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>
/// Effekseer particle effects for spells (plugin: addons/effekseer, the EffekseerForGodot4 GDExtension; effects: assets/vfx/effekseer).
/// SpellFx.Skill / SkillAttach ask here first for a spell moment (cast, hit, ground, after, finisher) and Projectile asks for a bolt's
/// "projectile" effect; what plays is chosen by the skill (data/effekseer.json "skills") or its damage type ("byType"). When an effect
/// plays and "replace" is on, the old flipbook for that moment is skipped. Everything falls back to the old effects when Effekseer is not
/// there: the dedicated server, a headless run, the plugin's library missing, or effect files the editor has not imported yet.
/// Emitters are made with ClassDB (the plugin's classes are native, not C#), free themselves when the effect ends, and are stopped and
/// freed after the effect's "life" in any case (some effects loop). Friends see them too: remote spell effects come through SpellFx.Skill.
/// Dev command: "/efk" (status, list, play &lt;id&gt;, on, off, reload).
/// </summary>
public static class Effekseer
{
    public sealed class Def { public string Id = "", File = ""; public float Scale = 1f, Life = 3f, Speed = 1f; public Color? Color; }

    static bool _loaded; static bool _enabled = true, _replace = true; static float _scale = 1f;
    static readonly Dictionary<string, Def> _defs = new();
    static readonly Dictionary<string, Dictionary<string, string>> _byType = new(), _skills = new();
    static readonly Dictionary<string, Resource?> _res = new();
    static bool? _native;
    static readonly HashSet<string> _warned = new();

    /// <summary>When true, an Effekseer effect that played replaces the old flipbook for that moment.</summary>
    public static bool Replace { get { Load(); return _replace; } }

    public static void Init() => Load();
    static void Load()
    {
        if (_loaded) return; _loaded = true;
        Commands.Register("efk", "Effekseer effects: efk (status), efk list, efk play <id> [scale], efk on|off, efk replace on|off, efk reload", Cmd);
        _defs.Clear(); _byType.Clear(); _skills.Clear();
        if (!FileAccess.FileExists("res://data/effekseer.json")) { _enabled = false; return; }
        try
        {
            using var doc = JsonUtil.Load("res://data/effekseer.json"); var r = doc.RootElement;
            _enabled = !r.TryGetProperty("enabled", out var en) || en.ValueKind != JsonValueKind.False;
            _replace = !r.TryGetProperty("replace", out var rp) || rp.ValueKind != JsonValueKind.False;
            _scale = JsonUtil.F(r, "scale", 1f);
            if (r.TryGetProperty("effects", out var es))
                foreach (var e in es.EnumerateObject())
                {
                    var v = e.Value; var d = new Def { Id = e.Name, File = JsonUtil.S(v, "file"), Scale = JsonUtil.F(v, "scale", 1f), Life = JsonUtil.F(v, "life", 3f), Speed = JsonUtil.F(v, "speed", 1f) };
                    string c = JsonUtil.S(v, "color"); if (c != "") d.Color = Color.FromHtml(c);
                    _defs[e.Name] = d;
                }
            void Map(string key, Dictionary<string, Dictionary<string, string>> into)
            {
                if (!r.TryGetProperty(key, out var m)) return;
                foreach (var t in m.EnumerateObject())
                {
                    var moments = new Dictionary<string, string>();
                    foreach (var mo in t.Value.EnumerateObject()) moments[mo.Name] = mo.Value.GetString() ?? "";
                    into[t.Name] = moments;
                }
            }
            Map("byType", _byType); Map("skills", _skills);
        }
        catch (Exception ex) { GD.PushError("effekseer.json: " + ex.Message); _enabled = false; }
    }

    /// <summary>Effekseer can draw here: the plugin's native classes are loaded and this is not a headless server.</summary>
    public static bool Native
    {
        get
        {
            if (_native is bool b) return b;
            bool ok = !Net.Dedicated && DisplayServer.GetName() != "headless" && ClassDB.ClassExists("EffekseerEmitter3D");
            if (!ok && !Net.Dedicated) GD.Print("[EFFEKSEER] not available (plugin library not loaded): spells use the old effects");
            _native = ok; return ok;
        }
    }
    public static bool On { get { Load(); return _enabled && Native; } }

    static Resource? Effect(Def d)
    {
        if (_res.TryGetValue(d.File, out var cached)) return cached;
        string path = "res://assets/vfx/effekseer/" + d.File; Resource? res = null;
        if (ResourceLoader.Exists(path)) res = GD.Load<Resource>(path);
        if (res == null && _warned.Add(path)) GD.Print($"[EFFEKSEER] {path} not imported yet (open the project in the Godot editor once): the old effect plays");
        return _res[d.File] = res;
    }

    /// <summary>The effect id for this skill and moment ("" = none): the skill's own entry first, then its damage type.</summary>
    public static string For(string skillId, string moment)
    {
        Load(); skillId = Spellcraft.FxOf(skillId);
        if (_skills.TryGetValue(skillId, out var own) && own.TryGetValue(moment, out var id)) return id;
        string type = Skills.Get(skillId)?.Type ?? "";
        return type != "" && _byType.TryGetValue(type, out var bt) && bt.TryGetValue(moment, out var tid) ? tid : "";
    }

    /// <summary>Plays the effect for a skill moment at a point (or on a node when follow is set). True if an Effekseer effect started.</summary>
    public static bool Skill(string skillId, string moment, Vector3 pos, float scale = 1f, float life = 0f, Node3D? follow = null, Vector3? offset = null)
    {
        if (!On) return false;
        string id = For(skillId, moment); if (id == "") return false;
        return follow != null ? Attach(id, follow, offset ?? Vector3.Zero, scale, life) != null : Play(id, pos, scale, life) != null;
    }

    static Node3D? Make(string id, float scale, float life)
    {
        if (!On || !_defs.TryGetValue(id, out var d) || d.File == "") return null;
        var fx = Effect(d); if (fx == null) return null;
        Node3D? n;
        try { n = ClassDB.Instantiate("EffekseerEmitter3D").As<Node3D>(); }
        catch (Exception ex) { GD.PushError("[EFFEKSEER] emitter: " + ex.Message); _native = false; return null; }
        if (n == null) return null;
        n.Set("effect", fx); n.Set("autoplay", true); n.Set("autofree", true);
        if (Mathf.Abs(d.Speed - 1f) > .001f) n.Set("speed", d.Speed);
        if (d.Color is { } c) n.Set("color", c);
        float s = Mathf.Max(.01f, _scale * d.Scale * scale); n.Scale = new Vector3(s, s, s);
        float keep = life > 0 ? life : d.Life;
        if (keep > 0 && Engine.GetMainLoop() is SceneTree tree)
        {   // looping effects never end on their own: stop and free them after their life
            var weak = new WeakReference<Node3D>(n);
            tree.CreateTimer(keep, false).Timeout += () => { if (weak.TryGetTarget(out var e) && GodotObject.IsInstanceValid(e)) { e.Call("stop"); e.QueueFree(); } };
        }
        return n;
    }

    /// <summary>An effect by id at a world point. Null if Effekseer is off or the effect is missing.</summary>
    public static Node3D? Play(string id, Vector3 pos, float scale = 1f, float life = 0f)
    {
        var root = Game.Root; if (root == null) return null;
        var n = Make(id, scale, life); if (n == null) return null;
        root.AddChild(n); n.GlobalPosition = pos;
        return n;
    }

    /// <summary>An effect by id that follows a node (a projectile, the hero) and is freed with it.</summary>
    public static Node3D? Attach(string id, Node3D to, Vector3 offset, float scale = 1f, float life = 0f)
    {
        if (!GodotObject.IsInstanceValid(to)) return null;
        var n = Make(id, scale, life); if (n == null) return null;
        to.AddChild(n); n.Position = offset;
        return n;
    }

    // ------------------------------------------------------------------ dev command
    static string Cmd(string[] a)
    {
        Load();
        if (a.Length == 0)
        {
            int imported = _defs.Values.Select(d => d.File).Distinct().Count(f => ResourceLoader.Exists("res://assets/vfx/effekseer/" + f));
            return $"effekseer: {(On ? "ON" : _enabled ? "enabled but not available (plugin not loaded or headless)" : "off")}, replace {_replace}, scale {_scale}; {_defs.Count} effects ({imported} files imported), types {string.Join(", ", _byType.Keys)}; skill overrides {_skills.Count}";
        }
        switch (a[0])
        {
            case "on": _enabled = true; return "effekseer on";
            case "off": _enabled = false; return "effekseer off: the old effects play";
            case "replace": if (a.Length > 1) _replace = a[1] != "off"; return "replace " + _replace;
            case "reload": _loaded = false; _res.Clear(); _warned.Clear(); _native = null; Load(); return "reloaded data/effekseer.json";
            case "list": return string.Join("\n", _defs.Values.Select(d => $"{d.Id,-18} {d.File} x{d.Scale} {d.Life}s{(ResourceLoader.Exists("res://assets/vfx/effekseer/" + d.File) ? "" : "  (not imported)")}"));
            case "play":
                {
                    if (a.Length < 2) return "usage: /efk play <id> [scale]";
                    var p = Player.Instance; if (p == null) return "no hero";
                    float sc = a.Length > 2 && float.TryParse(a[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : 1f;
                    var at = p.GlobalPosition - p.GlobalTransform.Basis.Z * 4f + Vector3.Up * .2f;
                    return Play(a[1], at, sc) != null ? $"playing {a[1]} 4 m in front" : On ? $"could not play {a[1]} (unknown id or not imported)" : "effekseer is not available";
                }
        }
        return "usage: /efk [list | play <id> [scale] | on | off | replace on|off | reload]";
    }
}
