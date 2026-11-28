using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>
/// A-35: named weapons (Elden Ring in spirit, original names), colossal arms, greatshields and unexpected items used as weapons, from
/// data/weapon_finds.json. Each find has fixed name, type, model and flavour, and comes from set places: certain enemies, bosses, a
/// dungeon's floors, chests, roadside sites, traders, pedlars (the junk merchants) and sunken chests when fishing. Items stay ordinary
/// items (saved like any other); the flavour is looked up by name.
/// </summary>
public static class WeaponFinds
{
    public class FindDef
    {
        public string Id = "", Name = "", Slot = "weapon", Type = "", Model = "", Text = ""; public string? Elem;
        public int Rar0, Rar1 = 4, MinLvl = 1; public float Weight = 1, Size, StatMul = 1; public HashSet<string> From = new();
    }
    static readonly List<FindDef> _all = new(); static readonly Dictionary<string, FindDef> _byName = new();
    static readonly Dictionary<string, float> _ch = new(); static int _ped0 = 2, _ped1 = 3; static bool _loaded;

    static void Load()
    {
        if (_loaded) return; _loaded = true;
        using var doc = JsonUtil.Load("res://data/weapon_finds.json"); var r = doc.RootElement;
        if (r.TryGetProperty("chance", out var ch))
            foreach (var p in ch.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.Number) _ch[p.Name] = p.Value.GetSingle();
                else if (p.Name == "pedlar" && p.Value.ValueKind == JsonValueKind.Array && p.Value.GetArrayLength() >= 2) { _ped0 = p.Value[0].GetInt32(); _ped1 = Math.Max(_ped0, p.Value[1].GetInt32()); }
            }
        if (!r.TryGetProperty("finds", out var fs)) return;
        foreach (var e in fs.EnumerateArray())
        {
            var f = new FindDef
            {
                Id = JsonUtil.S(e, "id"), Name = JsonUtil.S(e, "name"), Slot = JsonUtil.S(e, "slot", "weapon"), Type = JsonUtil.S(e, "type"), Model = JsonUtil.S(e, "model"),
                Text = JsonUtil.S(e, "text"), Elem = e.TryGetProperty("elem", out var el) ? el.GetString() : null, MinLvl = (int)JsonUtil.F(e, "minLvl", 1),
                Weight = JsonUtil.F(e, "weight", 1), Size = JsonUtil.F(e, "size"), StatMul = JsonUtil.F(e, "statMul", 1),
            };
            if (e.TryGetProperty("rar", out var ra) && ra.ValueKind == JsonValueKind.Array && ra.GetArrayLength() >= 2) { f.Rar0 = Math.Clamp(ra[0].GetInt32(), 0, 4); f.Rar1 = Math.Clamp(ra[1].GetInt32(), f.Rar0, 4); }
            if (e.TryGetProperty("from", out var fr)) foreach (var x in fr.EnumerateArray()) f.From.Add(x.GetString() ?? "");
            if (f.Name == "" || (f.Slot == "weapon" && f.Type == "")) continue;
            _all.Add(f); _byName[f.Name] = f;
        }
    }

    static float Ch(string k, float def) { Load(); return _ch.TryGetValue(k, out var v) ? v : def; }
    /// <summary>The finds a source can give at this level (any of the keys).</summary>
    static List<FindDef> Pool(int level, params string[] keys) { Load(); return _all.Where(f => f.MinLvl <= level && keys.Any(k => k != "" && f.From.Contains(k))).ToList(); }
    static FindDef? Pick(List<FindDef> pool)
    {
        float tot = pool.Sum(f => Mathf.Max(0, f.Weight)); if (tot <= 0) return null;
        float x = GD.Randf() * tot; foreach (var f in pool) { x -= Mathf.Max(0, f.Weight); if (x <= 0) return f; }
        return pool[^1];
    }

    /// <summary>A find from any of these sources (a random one by weight among those allowed at this level); null if none.</summary>
    public static Item? Roll(int level, float bonus, params string[] sources) => Pick(Pool(level, sources)) is { } f ? Make(f, level, bonus) : null;

    /// <summary>Builds the item of a find: a normal roll of its slot and type, then its own name, model, size and stat boost.</summary>
    public static Item Make(FindDef f, int level, float bonus = 0)
    {
        int rar = Math.Clamp(LootTables.RollRarity(bonus), f.Rar0, f.Rar1);
        var it = f.Slot == "weapon" ? Items.Roll(level, 0, "weapon", rar, f.Elem, f.Type) : Items.Roll(level, 0, f.Slot, rar, f.Elem);
        if (Math.Abs(f.StatMul - 1f) > .001f) Items.Boost(it, f.StatMul);
        it.Name = f.Name;
        if (f.Slot == "weapon")
        {
            if (f.Size > 0) it.Size = f.Size;
            // the name already picks the model through data/weapons.json byName (with its scale); Model only when it would not
            if (f.Model != "" && Weapons.KitModelFor(f.Type, it).Model != f.Model) it.Model = f.Model;
        }
        return it;
    }

    /// <summary>A find's flavour text for the tooltip ("" for other items).</summary>
    public static string Tip(Item it) { Load(); return _byName.TryGetValue(it.Name, out var f) ? f.Text : ""; }

    static void Found(Vector3 at, Item it)
    {
        Loot.Drop(at + Vector3.Up * .3f, item: it);
        Ui.Note($"A rare find: {it.Name}", it.Color); Sfx.Play("level", .6f);
    }

    /// <summary>A monster died (from BlockDrops.Roll, so not in the arena, not summoned): its own finds, a boss's, the dungeon floor's.
    /// Solo kills only: shared party kills pay out through the network, which carries no finds yet.</summary>
    public static void OnKill(Enemy e, bool netKill)
    {
        Load(); if (_all.Count == 0 || netKill || e.Arena || e.Summoned || e.Def.NoLoot) return;
        int lvl = e.Level; string kill = "kill:" + e.Type, dun = Dungeon.Active ? "dungeon:" + Dungeon.Key : "";
        Item? it = null;
        if (e.Def.Boss) { if (GD.Randf() < Ch("boss", .3f)) it = Roll(lvl, 2f, "boss", kill, dun); }
        else if (GD.Randf() < Ch("kill", .008f)) it = Roll(lvl, 0, kill);
        if (it == null && dun != "" && GD.Randf() < Ch("dungeonKill", .003f)) it = Roll(lvl, LootTables.DepthBonus(Dungeon.Depth), dun);
        if (it != null) Found(e.GlobalPosition, it);
    }

    /// <summary>A chest was opened (Props.cs): now and then a find for chests, or for this dungeon's chests.</summary>
    public static void OnChest(Vector3 at, int level, float bonus)
    {
        bool dn = Dungeon.Active; if (GD.Randf() >= Ch(dn ? "dungeonChest" : "chest", dn ? .18f : .1f)) return;
        if (Roll(level, bonus, "chest", dn ? "dungeon:" + Dungeon.Key : "") is { } it) Found(at, it);
    }

    /// <summary>A roadside site's chest is filled (Wayside.FillChest): null, or a find to add to it.</summary>
    public static Item? ForWayside(int level, float bonus) => GD.Randf() < Ch("wayside", .15f) ? Roll(level, bonus, "wayside") : null;
    /// <summary>A sunken chest on the fishing line (Fishing.cs): null, or a find instead of the rolled item.</summary>
    public static Item? ForFishing(int level) => GD.Randf() < Ch("fishing", .25f) ? Roll(level, 0, "fishing") : null;

    // ---------------------------------------------------------------- traders
    static readonly Dictionary<string, List<Item>> _stock = new();
    /// <summary>What a trader has on offer besides the ordinary wares: a pedlar 2-3 curios, other traders now and then one named piece.
    /// Kept per trader until the hero's level changes.</summary>
    public static List<Item> Stock(string npc, string role, int level)
    {
        Load(); string key = npc + "#" + level;
        if (_stock.TryGetValue(key, out var l)) return l;
        foreach (var k in _stock.Keys.Where(k => k.StartsWith(npc + "#")).ToList()) _stock.Remove(k);
        l = new List<Item>();
        if (role == "pedlar")
        {
            int n = _ped0 + (int)(GD.Randi() % (uint)(_ped1 - _ped0 + 1));
            var pool = Pool(level + 1, "pedlar");
            for (int i = 0; i < n && pool.Count > 0; i++) { var f = Pick(pool)!; pool.Remove(f); l.Add(Make(f, level + 1, .3f)); }
        }
        else if (GD.Randf() < Ch("shop", .35f) && Roll(level + 1, .3f, "shop") is { } it) l.Add(it);
        _stock[key] = l; return l;
    }
    /// <summary>A pedlar's prices x this (they sell junk).</summary>
    public static float PedlarPriceMul => Ch("pedlarPriceMul", .8f);
}
