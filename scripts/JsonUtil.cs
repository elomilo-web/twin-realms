using System.Text.Json;
using Godot;

namespace TwinRealms;

public static class JsonUtil
{
    public static JsonDocument Load(string path)
    {
        using var f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (f == null) { GD.PushError("Cannot open " + path); return JsonDocument.Parse("{}"); }
        return JsonDocument.Parse(f.GetAsText());
    }
    public static float F(JsonElement e, string k, float d = 0f) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : d;
    public static string S(JsonElement e, string k, string d = "") => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? d) : d;
    public static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
    public static Color Hex(string s) => Color.FromHtml(s);
}
