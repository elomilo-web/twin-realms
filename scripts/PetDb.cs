using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace TwinRealms;

/// <summary>A pet skill (data/pets.json "skills"): world fields (range, cd, fx) and turn-based Soul Duel fields (power, acc, cost, priority, effect, turns).</summary>
public class PetSkillDef
{
    public string Id = "", Name = "", Kind = "attack", Elem = "physical", Fx = "melee", Effect = "", Desc = "";
    public float Power = 40, Acc = 95, Range = 2.4f, Cd = 1.5f, Chance, Dur = 6, BuffMul = 1.3f;
    public int Cost, Priority, Turns = 2;
}
public class CubeDef { public string Id = "", Name = "", Desc = ""; public float Mul = 1, BossMul = 1, Drop; public int Cost; public Color Col = Colors.Gray; public Dictionary<string, int> Craft = new(); }
public class NatureDef { public string Id = "hardy", Name = "Hardy"; public float Hp = 1, Atk = 1, Def = 1, Spd = 1, Mag = 1; }
/// <summary>A catchable species: derived from its data/enemies.json def, overridden by pets.json "species" or the enemy's own "catch".</summary>
public class SpeciesDef
{
    public string Type = "", Name = "", Rarity = "common", Arch = "humanoid", Elem = "physical";
    public float Base = .5f, PetScale = 1, MountScale = 1, StatMul = 1;
    public bool Catchable = true, Mountable, Boss, Raid;
    public List<(int lvl, string skill)> Learn = new();
    // riding (A-19): NaturalH = the creature's own height in metres (too small = cannot be ridden, rules.mountMinHeight); "mount" in
    // pets.json species: speed / jump / stamina multipliers, fly, saddle [up, forward] in metres at riding size
    public float NaturalH = 1, MountSpeed = 1, MountJump = 1, MountStamina = 1, SaddleUp, SaddleFwd;
    public bool Flies;
    /// <summary>Skill-tree family (pets.json tree.familyOf by archetype): beast, flyer, spirit, construct.</summary>
    public string Family = "beast";
}
/// <summary>What a mount gives its rider (PetDb.Mount): speeds in m/s, jump as a multiplier of the hero's jump, stamina points.</summary>
public struct MountStats { public float Speed, Gallop, Jump, Stamina, FlySpeed; public bool Flies; }
/// <summary>Duel/world stats of one pet at its level (integers, like a creature sheet).</summary>
public struct PetStats { public int Hp, Atk, Def, Spd, Mag; }

/// <summary>
/// One bound soul, saved in PlayerStats.Pets. Plain public fields only (JSON with IncludeFields) so the duel/trade agent can send it
/// as one compact string (ToJson / FromJson). Everything derived is a method, never serialised.
/// </summary>
public class PetState
{
    public string Id = "", Species = "wolf", Nick = "", Nature = "hardy", Ot = "", Cube = "iron";
    public int Level = 1, Xp = 0;
    public float Bond = 0, HpFrac = 1, KoT = 0;            // KoT: seconds until it recovers from a knock-out (0 = fine)
    public List<string> Learned = new();                    // every skill it knows
    public List<string> Skills = new();                     // up to 4 equipped (from Learned)
    public Dictionary<string, Item?> Gear = new();          // pet_collar / pet_armor / pet_charm
    public bool Stay = false;                               // orders: hold position instead of following
    public string Aggro = "defensive";                      // passive / defensive / aggressive / assist
    public int Loot = 1, LootRar = 2;                       // auto-loot: 0 off, 1 gold (and potions, cubes), 2 rarity >= LootRar, 3 everything
    public Dictionary<string, int> Tree = new();            // pet skill tree (A-20, PetTree.cs): node id -> rank; points come from the level, so older pets load with all of theirs unspent

    public string Name() => Nick != "" ? Nick : Sp().Name;
    public SpeciesDef Sp() => PetDb.Species(Species);
    public bool Mountable() => Sp().Mountable;
    public bool Ko() => KoT > 0;
    public string Elem() => Sp().Elem;
    public int XpNeed() => PetDb.XpNeed(Level);
    public PetStats Stats() => PetDb.StatsOf(this);
    public int GearStat(string k)
    {
        float s = 0; foreach (var it in Gear.Values) { if (it == null) continue; s += k switch { "atk" => it.Dmg, "def" => it.Def, "hp" => it.Hp, "mag" => it.Mp, "spd" => it.Mods?.GetValueOrDefault("spd") ?? 0, _ => 0 }; }
        return (int)s;
    }
    /// <summary>Adds experience; returns the levels gained (new skills by level are learned, and equipped while a slot is free).</summary>
    public int GainXp(int n)
    {
        int cap = Math.Min((int)PetDb.R("maxLevel", 60), Game.D.Level + (int)PetDb.R("levelCapOverHero", 5)), up = 0;
        if (Level >= cap) return 0;
        Xp += Math.Max(0, n);
        while (Xp >= XpNeed() && Level < cap) { Xp -= XpNeed(); Level++; up++; LearnUpTo(); }
        if (Level >= cap) Xp = Math.Min(Xp, XpNeed() - 1);
        return up;
    }
    /// <summary>Learns every species skill up to the current level.</summary>
    public List<string> LearnUpTo()
    {
        var got = new List<string>();
        foreach (var (lvl, sk) in Sp().Learn) if (lvl <= Level && !Learned.Contains(sk) && PetDb.Skill(sk) != null) { Learned.Add(sk); got.Add(sk); if (Skills.Count < 4) Skills.Add(sk); }
        return got;
    }
    public string ToJson() => JsonSerializer.Serialize(this, PetDb.JOpt);
    public static PetState? FromJson(string s) { try { return JsonSerializer.Deserialize<PetState>(s, PetDb.JOpt); } catch { return null; } }
}

/// <summary>data/pets.json: species (from enemies.json), Soul Cubes, natures, skills, pet gear and the catch formula.</summary>
public static class PetDb
{
    public static readonly JsonSerializerOptions JOpt = new() { IncludeFields = true };
    static JsonElement _root; static bool _loaded;
    static readonly Dictionary<string, float> _rules = new();
    static readonly Dictionary<string, PetSkillDef> _skills = new();
    public static readonly List<CubeDef> Cubes = new();
    public static readonly List<NatureDef> Natures = new();
    static readonly Dictionary<string, SpeciesDef> _species = new();
    static readonly HashSet<string> _uncatchable = new();
    public static readonly string[] GearSlots = { "pet_collar", "pet_armor", "pet_charm" };
    public static readonly string[] AggroModes = { "passive", "defensive", "aggressive", "assist" };
    public static readonly string[] LootModes = { "Off", "Gold only", "Rarity", "Everything" };

    public static JsonElement Root { get { Load(); return _root; } }
    public static float R(string k, float d) { Load(); return _rules.TryGetValue(k, out var v) ? v : d; }
    public static bool IsGear(string? slot) => slot != null && slot.StartsWith("pet_");
    public static string GearName(string slot) => slot switch { "pet_collar" => "Collar", "pet_armor" => "Barding", "pet_charm" => "Charm", _ => slot };

    static void Load()
    {
        if (_loaded) return; _loaded = true;
        try
        {
            using var doc = JsonUtil.Load("res://data/pets.json"); _root = doc.RootElement.Clone();
            if (_root.TryGetProperty("rules", out var ru)) foreach (var p in ru.EnumerateObject()) if (p.Value.ValueKind == JsonValueKind.Number) _rules[p.Name] = p.Value.GetSingle();
            _rules["raidHp"] = JsonUtil.F(_root, "raidHp", 2000);
            if (_root.TryGetProperty("uncatchable", out var un)) foreach (var x in un.EnumerateArray()) _uncatchable.Add(x.GetString() ?? "");
            foreach (var p in _root.GetProperty("skills").EnumerateObject())
            {
                var e = p.Value;
                _skills[p.Name] = new PetSkillDef
                {
                    Id = p.Name, Name = JsonUtil.S(e, "name", p.Name), Kind = JsonUtil.S(e, "kind", "attack"), Elem = JsonUtil.S(e, "elem", "physical"), Fx = JsonUtil.S(e, "fx", "melee"),
                    Effect = JsonUtil.S(e, "effect"), Desc = JsonUtil.S(e, "d"), Power = JsonUtil.F(e, "power", 40), Acc = JsonUtil.F(e, "acc", 95), Range = JsonUtil.F(e, "range", 2.4f),
                    Cd = JsonUtil.F(e, "cd", 1.5f), Chance = JsonUtil.F(e, "chance"), Dur = JsonUtil.F(e, "dur", 6), BuffMul = JsonUtil.F(e, "buffMul", 1.3f),
                    Cost = (int)JsonUtil.F(e, "cost"), Priority = (int)JsonUtil.F(e, "priority"), Turns = (int)JsonUtil.F(e, "turns", 2),
                };
            }
            foreach (var e in _root.GetProperty("cubes").EnumerateArray())
            {
                var c = new CubeDef { Id = JsonUtil.S(e, "id"), Name = JsonUtil.S(e, "name"), Desc = JsonUtil.S(e, "d"), Mul = JsonUtil.F(e, "mul", 1), BossMul = JsonUtil.F(e, "bossMul", 1), Drop = JsonUtil.F(e, "drop"), Cost = (int)JsonUtil.F(e, "cost", 50), Col = Color.FromHtml(JsonUtil.S(e, "color", "#888888")) };
                if (e.TryGetProperty("craft", out var cr)) foreach (var p in cr.EnumerateObject()) c.Craft[p.Name] = p.Value.GetInt32();
                Cubes.Add(c);
            }
            foreach (var e in _root.GetProperty("natures").EnumerateArray())
                Natures.Add(new NatureDef { Id = JsonUtil.S(e, "id"), Name = JsonUtil.S(e, "name"), Hp = JsonUtil.F(e, "hp", 1), Atk = JsonUtil.F(e, "atk", 1), Def = JsonUtil.F(e, "def", 1), Spd = JsonUtil.F(e, "spd", 1), Mag = JsonUtil.F(e, "mag", 1) });
        }
        catch (Exception e) { GD.PushError("pets.json: " + e.Message); }
        if (Natures.Count == 0) Natures.Add(new NatureDef());
    }

    public static PetSkillDef? Skill(string id) { Load(); return _skills.TryGetValue(id, out var s) ? s : null; }
    public static IEnumerable<PetSkillDef> AllSkills { get { Load(); return _skills.Values; } }
    public static CubeDef Cube(string id) { Load(); return Cubes.Find(c => c.Id == id) ?? Cubes.FirstOrDefault() ?? new CubeDef { Id = id, Name = id }; }
    public static NatureDef Nature(string id) { Load(); return Natures.Find(n => n.Id == id) ?? Natures[0]; }
    public static Color RarityColor(string r) { Load(); return _root.TryGetProperty("rarity", out var ra) && ra.TryGetProperty(r, out var e) ? Color.FromHtml(JsonUtil.S(e, "color", "#cfcfcf")) : Colors.White; }
    public static string RarityName(string r) => r switch { "boss" => "Boss", "raid" => "Raid boss", _ => char.ToUpper(r[0]) + r[1..] };

    // ------------------------------------------------------------------ species
    static JsonElement? _enemyRaw;
    public static SpeciesDef Species(string type)
    {
        Load(); if (_species.TryGetValue(type, out var s)) return s;
        var d = EnemyDb.Get(type); s = new SpeciesDef { Type = type, Name = d.N, Elem = d.DmgType, Boss = d.Boss };
        s.Raid = d.Boss && d.Hp >= R("raidHp", 2000);
        var ra = _root.GetProperty("rarity");
        if (s.Raid) s.Rarity = "raid"; else if (d.Boss) s.Rarity = "boss";
        else s.Rarity = d.Hp < JsonUtil.F(ra.GetProperty("common"), "maxHp", 45) ? "common" : d.Hp < JsonUtil.F(ra.GetProperty("uncommon"), "maxHp", 90) ? "uncommon" : d.Hp < JsonUtil.F(ra.GetProperty("rare"), "maxHp", 200) ? "rare" : "epic";
        s.Catchable = !_uncatchable.Contains(type);   // hero mirror copies (Enemy.ShadeOf) are not souls of their own
        // archetype: from the table, otherwise from the body
        s.Arch = d.Kind == "quad" ? (d.Look == "" ? "canine" : d.Look) : d.Kind == "dragon" ? "dragon" : d.Kind == "wraith" ? "ghost" : d.Ranged == "arrow" ? "archer" : d.Ranged != null ? "caster" : d.Hp >= 200 ? "brute" : "humanoid";
        s.Mountable = d.Kind == "quad" && d.Look != "rabbit";
        float h = d.Model != "" ? (d.H > 0 ? d.H : 1.6f) * Mathf.Max(.1f, d.Scale) + d.Lift : d.Kind == "quad" ? d.R * 2.4f : d.Kind == "dragon" ? 5f : d.Kind == "wraith" ? 2.4f : 1.9f * Mathf.Max(.1f, d.Scale);
        s.PetScale = Mathf.Clamp(R("petHeight", 1.4f) / Mathf.Max(.3f, h), .25f, 1f);
        s.MountScale = Mathf.Clamp(R("mountHeight", 1.6f) / Mathf.Max(.3f, h), .25f, 1.8f);
        s.NaturalH = h; if (h < R("mountMinHeight", .8f)) s.Mountable = false;   // too small to carry a hero (unless the data says otherwise)
        s.Flies = d.Kind == "dragon" || d.Fly;
        void Apply(JsonElement o)
        {
            if (o.TryGetProperty("mount", out var mo) && mo.ValueKind == JsonValueKind.Object)
            {
                s.MountSpeed = JsonUtil.F(mo, "speed", s.MountSpeed); s.MountJump = JsonUtil.F(mo, "jump", s.MountJump); s.MountStamina = JsonUtil.F(mo, "stamina", s.MountStamina);
                if (mo.TryGetProperty("fly", out var fl) && (fl.ValueKind == JsonValueKind.True || fl.ValueKind == JsonValueKind.False)) s.Flies = fl.GetBoolean();
                if (mo.TryGetProperty("saddle", out var sd) && sd.ValueKind == JsonValueKind.Array && sd.GetArrayLength() >= 2) { s.SaddleUp = sd[0].GetSingle(); s.SaddleFwd = sd[1].GetSingle(); }
            }
            if (o.TryGetProperty("rarity", out var r) && r.ValueKind == JsonValueKind.String) s.Rarity = r.GetString()!;
            if (o.TryGetProperty("archetype", out var a) && a.ValueKind == JsonValueKind.String) s.Arch = a.GetString()!;
            if (o.TryGetProperty("mountable", out var m) && (m.ValueKind == JsonValueKind.True || m.ValueKind == JsonValueKind.False)) s.Mountable = m.GetBoolean();
            if (o.TryGetProperty("catchable", out var c) && (c.ValueKind == JsonValueKind.True || c.ValueKind == JsonValueKind.False)) s.Catchable = c.GetBoolean();
            if (o.TryGetProperty("petScale", out _)) s.PetScale = JsonUtil.F(o, "petScale", s.PetScale);
            if (o.TryGetProperty("mountScale", out _)) s.MountScale = JsonUtil.F(o, "mountScale", s.MountScale);
            if (o.TryGetProperty("elem", out _)) s.Elem = JsonUtil.S(o, "elem", s.Elem);
            if (o.TryGetProperty("skills", out var sk) && sk.ValueKind == JsonValueKind.Array) { s.Learn.Clear(); foreach (var x in sk.EnumerateArray()) s.Learn.Add(((int)x[0].GetSingle(), x[1].GetString() ?? "")); }
        }
        if (_root.TryGetProperty("species", out var spT) && spT.TryGetProperty(type, out var so)) Apply(so);
        try
        {
            if (_enemyRaw == null) { using var ed = JsonUtil.Load("res://data/enemies.json"); _enemyRaw = ed.RootElement.GetProperty("enemies").Clone(); }
            if (_enemyRaw.Value.TryGetProperty(type, out var ee) && ee.TryGetProperty("catch", out var co)) Apply(co);
        }
        catch { }
        var rd = ra.TryGetProperty(s.Rarity, out var rr) ? rr : ra.GetProperty("common");
        s.Base = JsonUtil.F(rd, "base", .5f); s.StatMul = JsonUtil.F(rd, "statMul", 1);
        if (_root.TryGetProperty("species", out var spT2) && spT2.TryGetProperty(type, out var so2) && so2.TryGetProperty("base", out _)) s.Base = JsonUtil.F(so2, "base", s.Base);
        if (s.Learn.Count == 0)
        {
            if (_root.GetProperty("archetypes").TryGetProperty(s.Arch, out var al) || _root.GetProperty("archetypes").TryGetProperty("humanoid", out al))
                foreach (var x in al.EnumerateArray()) s.Learn.Add(((int)x[0].GetSingle(), x[1].GetString() ?? ""));
            if (s.Elem != "physical" && _root.GetProperty("elemLearn").TryGetProperty(s.Elem, out var el))
                foreach (var x in el.EnumerateArray()) { string id = x[1].GetString() ?? ""; if (!s.Learn.Exists(l => l.skill == id)) s.Learn.Add(((int)x[0].GetSingle(), id)); }
            if (d.Ranged == "arrow" && !s.Learn.Exists(l => l.skill == "quill_shot")) s.Learn.Add((1, "quill_shot"));
            if (d.Slam && !s.Learn.Exists(l => l.skill == "slam")) s.Learn.Add((10, "slam"));
            if (d.Breath && !s.Learn.Exists(l => l.skill == "dragon_fire")) s.Learn.Add((15, "dragon_fire"));
            s.Learn.Sort((a, b) => a.lvl.CompareTo(b.lvl));
        }
        s.Family = PetTree.FamilyOf(s.Arch, s.Elem, s.Flies);
        _species[type] = s; return s;
    }

    // ------------------------------------------------------------------ riding (A-19): one set of numbers for the horse and for pets
    static List<string>? _mountIds;
    /// <summary>A species as a small number for the pose packet (the same on every machine: enemy types sorted by name); -1 unknown.</summary>
    public static int MountIndex(string species) { _mountIds ??= EnemyDb.Types.OrderBy(t => t, StringComparer.Ordinal).ToList(); return _mountIds.IndexOf(species); }
    public static string MountSpeciesAt(int i) { _mountIds ??= EnemyDb.Types.OrderBy(t => t, StringComparer.Ordinal).ToList(); return i >= 0 && i < _mountIds.Count ? _mountIds[i] : ""; }
    /// <summary>
    /// What this mount gives its rider (pets.json rules mount*): speed from its spd stat, gallop = speed x mountGallopMul, jump and
    /// stamina grow with its level; the species' "mount" multipliers and the pet's skill tree (mountSpdP, mountStP) on top.
    /// p = null: an unbound animal of that species at that level (the bought horse).
    /// </summary>
    public static MountStats Mount(string species, int level, PetState? p = null)
    {
        var sp = Species(species); p ??= new PetState { Species = species, Level = Math.Max(1, level) };
        float spd = Mathf.Min(R("mountSpeedMax", 15), R("mountSpeedBase", 9) + p.Stats().Spd * R("mountSpeedPerSpd", .03f)) * sp.MountSpeed * (1 + PetTree.Mod(p, "mountSpdP") / 100f);
        return new MountStats
        {
            Speed = spd, Gallop = spd * R("mountGallopMul", 1.35f), Flies = sp.Flies, FlySpeed = spd * R("mountFlySpeedMul", 1.5f),
            Jump = Mathf.Min(R("mountJumpMax", 1.7f), (R("mountJumpBase", 1.05f) + R("mountJumpPerLevel", .006f) * p.Level) * sp.MountJump),
            Stamina = (R("mountStaminaBase", 60) + R("mountStaminaPerLevel", 2) * p.Level) * sp.MountStamina * (1 + PetTree.Mod(p, "mountStP") / 100f),
        };
    }

    // ------------------------------------------------------------------ stats and levels
    public static int XpNeed(int lvl) { var st = Root.GetProperty("stats"); return Mathf.RoundToInt(JsonUtil.F(st, "xpBase", 30) * Mathf.Pow(Mathf.Max(1, lvl), JsonUtil.F(st, "xpExp", 1.6f))); }
    public static PetStats StatsOf(PetState p)
    {
        var d = EnemyDb.Get(p.Species); var sp = p.Sp(); var n = Nature(p.Nature); var st = Root.GetProperty("stats");
        float bond = 1 + Mathf.Clamp(p.Bond, 0, 100) / 100f * JsonUtil.F(st, "bondBonus", .05f), k = sp.StatMul * bond;
        int S(string key, float field, float nat)
        {
            var e = st.GetProperty(key);
            return Mathf.RoundToInt((JsonUtil.F(e, "a", 1) * field + JsonUtil.F(e, "b", 0)) * (1 + JsonUtil.F(e, "perLevel", .08f) * (p.Level - 1)) * nat * k * (1 + PetTree.Mod(p, key + "P") / 100f)) + p.GearStat(key);   // (key + "P": the pet's skill tree, A-20)
        }
        float magField = d.Mr + (d.DmgType != "physical" ? d.Dmg : d.Dmg * .4f);
        return new PetStats { Hp = Math.Max(10, S("hp", Mathf.Sqrt(Mathf.Max(1, d.Hp)), n.Hp)), Atk = Math.Max(1, S("atk", d.Dmg, n.Atk)), Def = Math.Max(1, S("def", d.Armor, n.Def)), Mag = Math.Max(1, S("mag", magField, n.Mag)), Spd = Math.Max(1, S("spd", d.Spd, n.Spd)) };
    }
    public static float World(string k, float d) => JsonUtil.F(Root.GetProperty("stats").GetProperty("world"), k, d);

    /// <summary>A freshly bound pet of this species at this level: random nature, skills learned up to its level.</summary>
    public static PetState New(string species, int level, string cube)
    {
        Load(); var d = Game.D;
        var p = new PetState { Id = $"{(long)Time.GetUnixTimeFromSystem():x}{GD.Randi() % 0xffff:x4}", Species = species, Level = Math.Clamp(level, 1, (int)R("maxLevel", 60)), Nature = Natures[(int)(GD.Randi() % (uint)Natures.Count)].Id, Ot = d.Name, Cube = cube };
        p.LearnUpTo(); return p;
    }

    // ------------------------------------------------------------------ catching
    /// <summary>The chance (0..1) that a cube of this tier binds this foe right now (data/pets.json "_help" has the formula).</summary>
    public static float CatchChance(Enemy e, string cubeId)
    {
        var sp = Species(e.Type); if (!sp.Catchable || e.Dead) return 0;
        var c = Cube(cubeId); float hpf = e.MaxHp > 0 ? Mathf.Clamp(e.Hp / e.MaxHp, 0, 1) : 1, floor = R("hpFloor", .15f);
        float hpK = floor + (1 - floor) * Mathf.Pow(1 - hpf, R("hpCurve", 1.5f));
        float stK = e.Status.Stunned ? R("statusStun", 1.6f) : e.Status.Has("chill") || e.Status.Has("root") ? R("statusSlow", 1.25f) : 1f;
        float gap = Mathf.Clamp(1 - R("levelGapPer", .06f) * (e.Level - Game.D.Level), R("levelGapMin", .4f), R("levelGapMax", 1.3f));
        bool big = sp.Boss || sp.Raid;
        float ch = sp.Base * hpK * stK * gap * (big ? c.BossMul : c.Mul);
        return Mathf.Clamp(ch, big ? R("bossMinChance", .002f) : R("minChance", .01f), R("maxChance", .99f));
    }

    /// <summary>A cube tier for a drop, by the "drop" weights.</summary>
    public static string RollCube()
    {
        Load(); float sum = Cubes.Sum(c => c.Drop), x = GD.Randf() * sum;
        foreach (var c in Cubes) { if (c.Drop <= 0) continue; if (x < c.Drop) return c.Id; x -= c.Drop; }
        return Cubes.Count > 0 ? Cubes[0].Id : "iron";
    }

    // ------------------------------------------------------------------ gear
    public static Item RollGear(int level, float bonus, string? slot = null, int rar = -1)
    {
        Load(); var g = _root.GetProperty("gear");
        slot ??= GearSlots[(int)(GD.Randi() % 3)]; if (rar < 0) rar = LootTables.RollRarity(bonus); rar = Math.Clamp(rar, 0, 4);
        var e = g.GetProperty(slot); var mulA = g.GetProperty("rarityMul"); float m = rar < mulA.GetArrayLength() ? mulA[rar].GetSingle() : 1;
        level = Math.Max(1, level);
        int St(string k) => e.TryGetProperty(k, out var a) ? (int)Math.Floor((a[0].GetSingle() + level * a[1].GetSingle()) * m) : 0;
        var names = e.GetProperty("names"); string bn = names[(int)(GD.Randi() % (uint)names.GetArrayLength())].GetString() ?? "Charm";
        var it = new Item { Slot = slot, Rar = rar, Lvl = level, Dmg = St("atk"), Def = St("def"), Hp = St("hp"), Mp = St("mag"), Name = Items.RarityName(rar) + " " + bn };
        int spd = St("spd"); if (spd > 0) it.Mods = new Dictionary<string, float> { ["spd"] = spd };
        return it;
    }
    /// <summary>"+4 atk, +12 hp" for pet gear.</summary>
    public static string GearText(Item it)
    {
        var p = new List<string>(); if (it.Dmg > 0) p.Add($"+{it.Dmg} atk"); if (it.Def > 0) p.Add($"+{it.Def} def"); if (it.Hp > 0) p.Add($"+{it.Hp} hp"); if (it.Mp > 0) p.Add($"+{it.Mp} mag");
        if (it.Mods?.GetValueOrDefault("spd") is float s && s > 0) p.Add($"+{s:0} spd");
        return string.Join(", ", p);
    }
}
