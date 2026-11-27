using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>
/// The hero's own look from the character creator (focus F-36), saved as PlayerStats.Look and sent to other players as one short
/// string (Pack). Sliders run -100..100 (0 = the race's own proportions), list values are indices into data/creator.json, colours are
/// "rrggbb" or "" for the race's own. A default HeroLook (V = 0: a save from before the creator) changes nothing.
/// </summary>
public class HeroLook
{
    public int V;                                          // 0 = never set (older saves: the plain race look), 1 = made in the creator
    public int Height, Build, Shoulders, Legs, Head;      // body sliders
    public int Face, Eyes, EyeSize, Brows, Mouth;         // face: list indices (data/creator.json) and the eye size slider
    public int Hair = -1;                                  // hair style index (data/avatar.json hair list of the body); -1 = picked by the name as before; past the list = bald
    public int Mark, Voice;                                // marking and voice indices
    public string Skin = "", HairCol = "", EyeCol = "", MarkCol = "";

    public const int Bald = 99;
    public HeroLook Copy() => (HeroLook)MemberwiseClone();

    static string Hex(string s) { s = (s ?? "").Trim().TrimStart('#').ToLowerInvariant(); return s.Length == 6 && s.All(Uri.IsHexDigit) ? s : ""; }
    static int Sl(int v) => Math.Clamp(v, -100, 100);
    /// <summary>Values from another machine or an edited save, forced into range.</summary>
    public HeroLook Clean()
    {
        V = V > 0 ? 1 : 0; Height = Sl(Height); Build = Sl(Build); Shoulders = Sl(Shoulders); Legs = Sl(Legs); Head = Sl(Head); EyeSize = Sl(EyeSize);
        Face = Math.Clamp(Face, 0, 99); Eyes = Math.Clamp(Eyes, 0, 99); Brows = Math.Clamp(Brows, 0, 99); Mouth = Math.Clamp(Mouth, 0, 99);
        Hair = Math.Clamp(Hair, -1, Bald); Mark = Math.Clamp(Mark, 0, 99); Voice = Math.Clamp(Voice, 0, 99);
        Skin = Hex(Skin); HairCol = Hex(HairCol); EyeCol = Hex(EyeCol); MarkCol = Hex(MarkCol);
        return this;
    }

    /// <summary>The whole look as one short string for the network ("" for a look that was never set): 14 numbers and 4 colours, dot-separated.</summary>
    public string Pack()
    {
        if (V == 0) return "";
        var c = Copy().Clean();
        return string.Join(".", new object[] { c.V, c.Height, c.Build, c.Shoulders, c.Legs, c.Head, c.Face, c.Eyes, c.EyeSize, c.Brows, c.Mouth, c.Hair, c.Mark, c.Voice, c.Skin, c.HairCol, c.EyeCol, c.MarkCol }
            .Select(o => o is int i ? i.ToString(CultureInfo.InvariantCulture) : (string)o));
    }
    /// <summary>Reads a packed look; anything malformed (or an older peer sending nothing) gives the default look.</summary>
    public static HeroLook Unpack(string? s)
    {
        var l = new HeroLook(); if (string.IsNullOrEmpty(s) || s.Length > 160) return l;
        var p = s.Split('.'); if (p.Length != 18) return l;
        var n = new int[14];
        for (int i = 0; i < 14; i++) if (!int.TryParse(p[i], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out n[i])) return new HeroLook();
        l.V = n[0]; l.Height = n[1]; l.Build = n[2]; l.Shoulders = n[3]; l.Legs = n[4]; l.Head = n[5]; l.Face = n[6]; l.Eyes = n[7]; l.EyeSize = n[8]; l.Brows = n[9]; l.Mouth = n[10]; l.Hair = n[11]; l.Mark = n[12]; l.Voice = n[13];
        l.Skin = p[14]; l.HairCol = p[15]; l.EyeCol = p[16]; l.MarkCol = p[17];
        return l.Clean();
    }
}

/// <summary>One sculpt step on the face (data/creator.json "_faceHelp").</summary>
public struct FaceOp { public string At; public float R, Rot; public Vector3 Move, Scale; }

/// <summary>
/// Character creator data (data/creator.json): option lists, ranges and swatches, and how a HeroLook becomes an Appearance
/// (Creator.Apply, called by Races.Look for every hero: the local one, the creator preview, friends online).
/// </summary>
public static class Creator
{
    public class Preset { public string Name = ""; public List<FaceOp> Ops = new(); public Vector3 Head = Vector3.One; }
    public class MarkDef { public string Name = ""; public string Col = "8a4a44"; public JsonElement Shapes; public bool Has; }

    static JsonDocument? _doc; static bool _loaded;
    public static JsonElement Root { get { Load(); return _doc!.RootElement; } }
    public static readonly List<Preset> Face = new(), Eyes = new(), Brows = new(), Mouth = new();
    public static readonly List<MarkDef> Marks = new();
    public static readonly List<(string name, float pitch)> Voices = new();
    static readonly Dictionary<string, Vector3> _land = new();

    static Vector3 V3(JsonElement e, string k, Vector3 d)
    {
        if (!e.TryGetProperty(k, out var v)) return d;
        if (v.ValueKind == JsonValueKind.Number) return Vector3.One * v.GetSingle();
        return v.ValueKind == JsonValueKind.Array && v.GetArrayLength() >= 3 ? new Vector3(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle()) : d;
    }
    static void Load()
    {
        if (_loaded) return; _loaded = true;
        _doc = Godot.FileAccess.FileExists("res://data/creator.json") ? JsonUtil.Load("res://data/creator.json") : JsonDocument.Parse("{}");
        var r = _doc.RootElement;
        if (r.TryGetProperty("landmarks", out var lm) && lm.ValueKind == JsonValueKind.Object)
            foreach (var p in lm.EnumerateObject()) if (p.Value.ValueKind == JsonValueKind.Array && p.Value.GetArrayLength() >= 3) _land[p.Name] = new Vector3(p.Value[0].GetSingle(), p.Value[1].GetSingle(), p.Value[2].GetSingle());
        void Presets(string key, List<Preset> into)
        {
            if (r.TryGetProperty(key, out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var e in list.EnumerateArray())
                {
                    var ps = new Preset { Name = JsonUtil.S(e, "n", "?"), Head = V3(e, "head", Vector3.One) };
                    if (e.TryGetProperty("ops", out var ops) && ops.ValueKind == JsonValueKind.Array)
                        foreach (var o in ops.EnumerateArray())
                            ps.Ops.Add(new FaceOp { At = JsonUtil.S(o, "at"), R = Mathf.Max(.001f, JsonUtil.F(o, "r", .03f)), Rot = JsonUtil.F(o, "rot"), Move = V3(o, "move", Vector3.Zero), Scale = V3(o, "scale", Vector3.One) });
                    into.Add(ps);
                }
            if (into.Count == 0) into.Add(new Preset { Name = "Natural" });
        }
        Presets("face", Face); Presets("eyes", Eyes); Presets("brows", Brows); Presets("mouth", Mouth);
        if (r.TryGetProperty("markings", out var mk) && mk.ValueKind == JsonValueKind.Array)
            foreach (var e in mk.EnumerateArray())
            {
                var m = new MarkDef { Name = JsonUtil.S(e, "n", "?"), Col = JsonUtil.S(e, "col", "8a4a44") };
                if (e.TryGetProperty("shapes", out var sh) && sh.ValueKind == JsonValueKind.Array && sh.GetArrayLength() > 0) { m.Shapes = sh; m.Has = true; }
                Marks.Add(m);
            }
        if (Marks.Count == 0) Marks.Add(new MarkDef { Name = "None" });
        if (r.TryGetProperty("voices", out var vs) && vs.ValueKind == JsonValueKind.Array)
            foreach (var e in vs.EnumerateArray()) Voices.Add((JsonUtil.S(e, "n", "?"), Mathf.Clamp(JsonUtil.F(e, "pitch", 1f), .5f, 2f)));
        if (Voices.Count == 0) Voices.Add(("Natural", 1f));
        Commands.Register("look", "F-36: look: the hero's creator look (packed) and what it resolves to; look test: pack / network / save round trips; look set <packed>|random|reset: change the hero's saved look (the model follows on the next load); in the creator: look tab <origin|body|face|hair|marks>, look zoom <0..1>, look yaw <deg>, look gear <on|off>", Cmd);
    }
    /// <summary>Registers the "/look" dev command (also happens the first time any hero is built).</summary>
    public static void Init() => Load();

    static T At<T>(List<T> l, int i) { Load(); return l[i >= 0 && i < l.Count ? i : 0]; }
    public static Vector3? Landmark(string name) { Load(); return _land.TryGetValue(name, out var v) ? v : null; }
    public static JsonElement? Section(string key) => Root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;
    public static float Num(string section, string key, float d) => Section(section) is { } s ? JsonUtil.F(s, key, d) : d;
    public static Color Col(string section, string key, string d) { var s = Section(section) is { } e ? JsonUtil.S(e, key, d) : d; return TryHex(s, out var c) ? c : Color.FromHtml(d); }
    public static bool TryHex(string? s, out Color c)
    {
        c = Colors.White; s = (s ?? "").TrimStart('#');
        if (s.Length != 6 || !s.All(Uri.IsHexDigit)) return false;
        c = Color.FromHtml(s); return true;
    }
    public static string ToHex(Color c) => c.ToHtml(false);
    public static List<string> Swatches(string kind)
    {
        var l = new List<string>();
        if (Section("swatches") is { } s && s.TryGetProperty(kind, out var v) && v.ValueKind == JsonValueKind.Array)
            foreach (var e in v.EnumerateArray()) if (e.ValueKind == JsonValueKind.String && TryHex(e.GetString(), out _)) l.Add(e.GetString()!.TrimStart('#').ToLowerInvariant());
        return l;
    }
    /// <summary>The race's skin in lighter and darker shades (extra skin swatches; also what Random picks from, so an orc stays green).</summary>
    public static List<string> SkinShades(string race)
    {
        var l = new List<string>(); var c = Races.Get(race).Look.Skin;
        if (Section("swatches") is { } s && s.TryGetProperty("skinShades", out var v) && v.ValueKind == JsonValueKind.Array)
            foreach (var e in v.EnumerateArray()) if (e.ValueKind == JsonValueKind.Number) { float k = e.GetSingle(); l.Add(ToHex(k >= 0 ? c.Lightened(Mathf.Min(k, 1f)) : c.Darkened(Mathf.Min(-k, 1f)))); }
        return l;
    }

    /// <summary>Slider value (-100..100) to a factor: body.&lt;key&gt;.min at -100, 1 at 0, max at +100.</summary>
    public static float Factor(string key, int v, float dMin, float dMax)
    {
        float mn = dMin, mx = dMax;
        if (Section("body") is { } b && b.TryGetProperty(key, out var e) && e.ValueKind == JsonValueKind.Object) { mn = JsonUtil.F(e, "min", dMin); mx = JsonUtil.F(e, "max", dMax); }
        float t = Mathf.Clamp(v, -100, 100) / 100f;
        return t < 0 ? Mathf.Lerp(1f, mn, -t) : Mathf.Lerp(1f, mx, t);
    }

    /// <summary>Hair models of a body (data/avatar.json hair list) with their labels.</summary>
    public static List<(string file, string label)> HairList(bool female)
    {
        var l = new List<(string, string)>(); var names = Section("hairNames");
        foreach (var f in Avatar.HairFiles(female))
        {
            string label = names is { } n ? JsonUtil.S(n, f) : "";
            if (label == "") { label = f; int dot = label.LastIndexOf('.'); if (dot > 0) label = label[..dot]; int us = label.LastIndexOf('_'); if (us >= 0) label = label[(us + 1)..]; if (label.Length > 0) label = char.ToUpperInvariant(label[0]) + label[1..]; }
            l.Add((f, label));
        }
        return l;
    }
    public static bool BaldAllowed => !Root.TryGetProperty("bald", out var b) || b.ValueKind != JsonValueKind.False;

    /// <summary>Lays the hero's own choices over the race look. Nothing changes for a look that was never set (V = 0).</summary>
    public static void Apply(Appearance ap, HeroLook? lk)
    {
        Load();
        if (lk == null || lk.V == 0) return;
        float build = Factor("build", lk.Build, .9f, 1.14f);
        ap.Height *= Factor("height", lk.Height, .93f, 1.07f); ap.Bulk *= build;
        ap.HeadScale *= Factor("head", lk.Head, .93f, 1.08f);
        ap.Shoulders = Factor("shoulders", lk.Shoulders, .8f, 1.22f); ap.Legs = Factor("legs", lk.Legs, .94f, 1.06f);
        // a broad build widens the whole body (like the race's bulk); most of that is taken back off the head
        float keep = Mathf.Clamp(Section("body") is { } b && b.TryGetProperty("build", out var be) ? JsonUtil.F(be, "headKeep", .7f) : .7f, 0f, 1f);
        float hw = Mathf.Lerp(1f, 1f / build, keep); var fh = At(Face, lk.Face).Head;
        ap.HeadShape = new Vector3(hw * fh.X, fh.Y, hw * fh.Z);
        if (TryHex(lk.Skin, out var sc)) ap.Skin = sc;
        if (TryHex(lk.HairCol, out var hc)) ap.Hair = hc;
        if (TryHex(lk.EyeCol, out var ec)) { ap.Eyes = ec; ap.IrisTint = true; }
        ap.HairStyle = lk.Hair; ap.Custom = lk;
    }

    /// <summary>Every sculpt step of a look: face shape, eyes, eye size, brows, mouth.</summary>
    public static List<FaceOp> Ops(HeroLook lk)
    {
        Load(); var l = new List<FaceOp>();
        l.AddRange(At(Face, lk.Face).Ops); l.AddRange(At(Eyes, lk.Eyes).Ops); l.AddRange(At(Brows, lk.Brows).Ops); l.AddRange(At(Mouth, lk.Mouth).Ops);
        if (lk.EyeSize != 0)
        {
            float t = Mathf.Clamp(lk.EyeSize, -100, 100) / 100f, mn = Num("eyeSize", "min", .86f), mx = Num("eyeSize", "max", 1.16f);
            l.Add(new FaceOp { At = "eye", R = Mathf.Max(.005f, Num("eyeSize", "r", .03f)), Scale = Vector3.One * (t < 0 ? Mathf.Lerp(1f, mn, -t) : Mathf.Lerp(1f, mx, t)), Move = Vector3.Zero });
        }
        return l;
    }
    public static MarkDef? MarkOf(HeroLook lk) { var m = At(Marks, lk.Mark); return m.Has ? m : null; }
    public static Color MarkColor(HeroLook lk) { var m = At(Marks, lk.Mark); return TryHex(lk.MarkCol, out var c) ? c : TryHex(m.Col, out var d) ? d : new Color(.5f, .25f, .22f); }

    // ------------------------------------------------------------------ voice
    /// <summary>Pitch factor of the hero's chosen voice, for Sfx.Voice (the hero's pain grunt and death cry, Player.cs).</summary>
    public static float VoicePitch(HeroLook? lk) { Load(); return lk == null || lk.V == 0 ? 1f : At(Voices, lk.Voice).pitch; }

    // ------------------------------------------------------------------ random / reset
    /// <summary>The look a new hero starts from: the race's own proportions and colours, the first hair style.</summary>
    public static HeroLook Fresh() => new() { V = 1, Hair = 0 };
    /// <summary>A random look for a race and body: every slider, list and colour (skin stays a shade of the race's own).</summary>
    public static HeroLook Random(string race, bool female, Random? rng = null)
    {
        Load(); rng ??= new Random(); var l = Fresh();
        int Sl(int spread) => (int)Math.Round((rng.NextDouble() + rng.NextDouble() - 1.0) * spread / 5.0) * 5;   // bell-shaped, in steps of 5
        l.Height = Sl(100); l.Build = Sl(100); l.Shoulders = Sl(100); l.Legs = Sl(80); l.Head = Sl(70); l.EyeSize = Sl(80);
        l.Face = rng.Next(Face.Count); l.Eyes = rng.Next(Eyes.Count); l.Brows = rng.Next(Brows.Count); l.Mouth = rng.Next(Mouth.Count);
        int hairs = Avatar.HairFiles(female).Count;
        l.Hair = hairs == 0 ? 0 : BaldAllowed && rng.Next(12) == 0 ? HeroLook.Bald : rng.Next(hairs);
        l.Mark = Marks.Count > 1 && rng.Next(100) < 45 ? 1 + rng.Next(Marks.Count - 1) : 0;
        l.Voice = rng.Next(Voices.Count);
        string Pick(List<string> s, int keepRace) => s.Count == 0 || rng.Next(100) < keepRace ? "" : s[rng.Next(s.Count)];
        l.Skin = Pick(SkinShades(race), 40); l.HairCol = Pick(Swatches("hair"), 25); l.EyeCol = Pick(Swatches("eyes"), 30);
        l.MarkCol = Pick(Swatches("mark"), 60);
        return l;
    }

    // ------------------------------------------------------------------ dev command
    static string Cmd(string[] a)
    {
        string sub = a.Length > 0 ? a[0].ToLowerInvariant() : "";
        var d = Game.D;
        switch (sub)
        {
            case "test": return Test();
            case "set":
                if (a.Length < 2) return "usage: /look set <packed look>";
                { var l = HeroLook.Unpack(a[1]); if (l.V == 0) return "not a packed look (18 dot-separated fields)"; if (Ui.DevLook(l)) return "creator look set: " + l.Pack(); d.Look = l; Game.Notify(); return "look set: " + l.Pack() + " (the hero's model follows on the next load)"; }
            case "random":
                { var l = Random(d.Race, d.Body == "female", a.Length > 1 && int.TryParse(a[1], out int seed) ? new Random(seed) : null); if (Ui.DevLook(l)) return "creator look: " + l.Pack(); d.Look = l; Game.Notify(); return "look: " + l.Pack() + " (the hero's model follows on the next load)"; }
            case "reset":
                { var l = Fresh(); if (Ui.DevLook(l)) return "creator look reset"; d.Look = l; Game.Notify(); return "look reset to the race defaults (the hero's model follows on the next load)"; }
            case "tab": case "zoom": case "yaw": case "gear":
                return Ui.DevCreate(sub, a.Length > 1 ? a[1] : "") ? $"creator {sub} {(a.Length > 1 ? a[1] : "")}" : "the creator is not open (or a bad value)";
        }
        var lk = d.Look ?? new HeroLook(); var ap = Races.Look(d);
        return $"look [{(lk.V == 0 ? "never set: race defaults" : lk.Pack())}]  {d.Race} {d.Body}: height x{ap.Height:0.00} bulk x{ap.Bulk:0.00} head x{ap.HeadScale:0.00} shoulders x{ap.Shoulders:0.00} legs x{ap.Legs:0.00}"
            + $"  face {At(Face, lk.Face).Name} / eyes {At(Eyes, lk.Eyes).Name} / brows {At(Brows, lk.Brows).Name} / mouth {At(Mouth, lk.Mouth).Name}"
            + $"  hair {(lk.Hair < 0 ? "by name" : lk.Hair.ToString())} #{ToHex(ap.Hair)}  skin #{ToHex(ap.Skin)}  eyes #{ToHex(ap.Eyes)}  mark {At(Marks, lk.Mark).Name}  voice {At(Voices, lk.Voice).name} x{VoicePitch(lk):0.00}";
    }

    /// <summary>
    /// "/look test": random looks through every path a look travels: Pack / Unpack, the roster entry other players get (PeerInfo as JSON,
    /// Net.ToJson / FromJson), the save file (PlayerStats as JSON, the same options as Game.Save), plus old saves and bad input.
    /// </summary>
    public static string Test()
    {
        Load(); var rng = new Random(36); int n = 200, bad = 0, longest = 0; var fails = new List<string>();
        var opt = new JsonSerializerOptions { IncludeFields = true };
        var races = Races.All.Select(r => r.Id).ToList();
        for (int i = 0; i < n; i++)
        {
            string race = races[rng.Next(races.Count)]; bool female = rng.Next(2) == 0;
            var l = Random(race, female, rng); if (i % 7 == 0) l.Skin = ToHex(new Color((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble()));
            string p = l.Pack(); longest = Math.Max(longest, p.Length);
            string p2 = HeroLook.Unpack(p).Pack();
            // the roster entry: what R_Hello / R_Me / R_Roster carry
            var info = new PeerInfo { Name = "T" + i, Race = race, Body = female ? "female" : "male", Look = p };
            string p3 = HeroLook.Unpack(Net.FromJson<PeerInfo>(Net.ToJson(info))?.Look).Pack();
            // the save: PlayerStats with the look object
            var ps = new PlayerStats { Name = "T" + i, Race = race, Body = female ? "female" : "male", Look = l };
            string p4 = (JsonSerializer.Deserialize<PlayerStats>(JsonSerializer.Serialize(ps, opt), opt)?.Look ?? new HeroLook()).Pack();
            // and the appearance both ends build from it
            var a1 = Races.Look(ps); var a2 = Races.Look(new PlayerStats { Race = race, Body = ps.Body, Look = HeroLook.Unpack(p3) });
            bool same = Mathf.IsEqualApprox(a1.Height, a2.Height) && Mathf.IsEqualApprox(a1.Bulk, a2.Bulk) && Mathf.IsEqualApprox(a1.HeadScale, a2.HeadScale) && a1.Skin.IsEqualApprox(a2.Skin) && a1.Hair.IsEqualApprox(a2.Hair) && a1.Eyes.IsEqualApprox(a2.Eyes) && a1.HairStyle == a2.HairStyle;
            if (p != p2 || p != p3 || p != p4 || !same) { bad++; if (fails.Count < 5) fails.Add($"  FAIL #{i} {race}: pack [{p}] unpack [{p2}] peer [{p3}] save [{p4}] appearance {(same ? "same" : "DIFFERENT")}"); }
        }
        // a save from before F-36 (no Look at all, or null) loads as the plain race look
        var old = JsonSerializer.Deserialize<PlayerStats>("{\"Name\":\"Old\",\"Race\":\"elf\",\"Body\":\"female\"}", opt);
        bool oldOk = old?.Look != null && old.Look.V == 0 && old.Look.Pack() == "";
        var nul = JsonSerializer.Deserialize<PlayerStats>("{\"Name\":\"Old\",\"Race\":\"elf\",\"Look\":null}", opt);
        bool nulOk = nul != null && Races.Look(nul).Custom == null;
        var plain = Races.LookFor("elf", "warrior", true); var viaOld = old != null ? Races.Look(old) : plain;
        bool sameOld = Mathf.IsEqualApprox(plain.Height, viaOld.Height) && Mathf.IsEqualApprox(plain.Bulk, viaOld.Bulk) && plain.Skin.IsEqualApprox(viaOld.Skin) && viaOld.Custom == null && viaOld.HairStyle == -1;
        // an older peer sends no Look; junk must not throw
        bool peerOk = HeroLook.Unpack(Net.FromJson<PeerInfo>("{\"Name\":\"X\",\"Race\":\"orc\"}")?.Look).V == 0;
        bool junkOk = true;
        foreach (var j in new[] { "", "1", "x.y.z", "1.2.3.4.5.6.7.8.9.10.11.12.13.14.a.b.c", new string('9', 400), "1.9999.-9999.0.0.0.500.0.0.0.0.777.0.0.zzzzzz.12.#ffffff.ff00ff", "1.a.0.0.0.0.0.0.0.0.0.0.0.0...." })
            try { var l = HeroLook.Unpack(j); if (Math.Abs(l.Height) > 100 || Math.Abs(l.Build) > 100 || l.Face > 99 || l.Hair > HeroLook.Bald || (l.Skin != "" && l.Skin.Length != 6)) junkOk = false; Apply(Races.LookFor("human", "warrior", false), l); Ops(l); }
            catch (Exception e) { junkOk = false; fails.Add("  FAIL junk [" + (j.Length > 40 ? j[..40] + "..." : j) + "]: " + e.Message); }
        bool ok = bad == 0 && oldOk && nulOk && sameOld && peerOk && junkOk;
        return $"look test: {(ok ? "OK" : "FAILED")}  {n - bad}/{n} random looks survive pack, roster JSON and save JSON unchanged (longest packed look {longest} chars); "
            + $"old save without a look {(oldOk && sameOld ? "loads as the plain race look" : "BROKEN")}; null look {(nulOk ? "safe" : "BROKEN")}; older peer without a look {(peerOk ? "gets the default" : "BROKEN")}; junk input {(junkOk ? "rejected or clamped" : "BROKEN")}"
            + (fails.Count > 0 ? "\n" + string.Join("\n", fails) : "");
    }
}
