using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;

namespace TwinRealms;

public static class JsonUtil
{
    public const string TuningPath = "res://data/tuning.json";
    static JsonObject? _files; static bool _tuned;

    /// <summary>
    /// Loads a data file. If data/tuning.json (the master tuning file) has an entry for this file under "files",
    /// those values are laid over it first: objects merge key by key, anything else (numbers, text, arrays) is replaced.
    /// </summary>
    public static JsonDocument Load(string path)
    {
        string text = Read(path); if (text == "") return JsonDocument.Parse("{}");
        string name = path.Replace("res://data/", "");
        if (path != TuningPath && Files() is { } files && files[name] is JsonObject over)
        {
            try { var node = JsonNode.Parse(text, null, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); if (node is JsonObject o) { Merge(o, over); return JsonDocument.Parse(o.ToJsonString()); } }
            catch (System.Exception e) { GD.PushError($"tuning.json could not be applied to {name}: {e.Message}"); }
        }
        return JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
    }
    static string Read(string path)
    {
        using var f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (f == null) { GD.PushError("Cannot open " + path); return ""; }
        return f.GetAsText();
    }
    /// <summary>The master tuning file (data/tuning.json), read once.</summary>
    public static JsonObject? Tuning()
    {
        if (_tuned) return _tuningRoot; _tuned = true;
        if (!Godot.FileAccess.FileExists(TuningPath)) return null;
        try { _tuningRoot = JsonNode.Parse(Read(TuningPath), null, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject; }
        catch (System.Exception e) { GD.PushError("data/tuning.json is not valid JSON (check commas and brackets): " + e.Message); }
        return _tuningRoot;
    }
    static JsonObject? _tuningRoot;
    static JsonObject? Files() => _files ??= Tuning()?["files"] as JsonObject;

    /// <summary>Lays b over a: objects merge recursively, keys starting with "_" (comments) are skipped, other values replace.</summary>
    static void Merge(JsonObject a, JsonObject b)
    {
        foreach (var (k, v) in b)
        {
            if (k.StartsWith("_") || v == null) continue;
            if (v is JsonObject bo && a[k] is JsonObject ao) Merge(ao, bo);
            else a[k] = v.DeepClone();
        }
    }

    public static float F(JsonElement e, string k, float d = 0f) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : d;
    public static string S(JsonElement e, string k, string d = "") => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? d) : d;
    public static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
    public static Color Hex(string s) => Color.FromHtml(s);
}

/// <summary>
/// Numbers that used to be written in code, now in data/tuning.json "game" (player base stats, drops, respawns, spawns, item formulas,
/// prices, stamina costs). Tune.F("player.hpBase", 100): dotted path into "game", with the old value as the default.
/// </summary>
public static class Tune
{
    static readonly System.Collections.Generic.Dictionary<string, float> _cache = new();
    public static float F(string path, float d)
    {
        if (_cache.TryGetValue(path, out var c)) return c;
        JsonNode? n = JsonUtil.Tuning()?["game"];
        // dotted path; a number part indexes an array ("items.armor.def.1" = the second number of [base, perLevel])
        foreach (var part in path.Split('.')) { n = n is JsonObject o ? o[part] : n is JsonArray a && int.TryParse(part, out int i) && i >= 0 && i < a.Count ? a[i] : null; if (n == null) break; }
        float v = d;
        if (n is JsonValue jv) { if (jv.TryGetValue<double>(out var dd)) v = (float)dd; else if (jv.TryGetValue<float>(out var ff)) v = ff; else if (jv.TryGetValue<int>(out var ii)) v = ii; }
        _cache[path] = v; return v;
    }
    public static int I(string path, int d) => Mathf.RoundToInt(F(path, d));
}
