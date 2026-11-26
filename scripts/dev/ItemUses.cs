using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>
/// Focus F-26 (every item has a purpose). <c>godot --headless --path . -- --item-uses</c> lists everything a player can hold (materials, scraps, fish, junk,
/// dishes, blocks, gems, cubes, potions, tools, recipe books, gear bases, pet gear, uniques, sets) and what each is used for, found by scanning the data
/// tables through the game's own loaders: crafting recipes (loot.json crafting + sets, blocks.json recipes and items, cooking, pet cubes and pet gear, the
/// fishing rod, pickaxes), the smith (hone, 10-for-1 trades), the shop (sell, buy), quests, equip slots, placeable blocks, consumables, gem fusing and cutting.
/// Writes .devloop/item_uses.md. An item whose only uses are "sell" and the smith's 10-for-1 trade is flagged NO USE (junk_* fish junk may stay sell-only).
/// </summary>
public static class ItemUses
{
    const string Sell = "sell", Trade = "smith trade 10:1";
    sealed class Row { public string Id = "", Name = "", Cat = ""; public readonly List<string> Uses = new(); }
    static readonly Dictionary<string, Row> _rows = new();
    static readonly List<string> _dangling = new();
    static readonly string[] GearSlots = { "weapon", "armor", "shield", "head", "hands", "feet", "cape", "trinket" };
    static readonly string[] PetSlots = { "pet_collar", "pet_armor", "pet_charm" };

    static Row Add(string id, string name, string cat) { if (!_rows.TryGetValue(id, out var r)) _rows[id] = r = new Row { Id = id, Name = name, Cat = cat }; return r; }
    static void Use(string id, string use)
    {
        if (_rows.TryGetValue(id, out var r)) { if (!r.Uses.Contains(use)) r.Uses.Add(use); }
        else _dangling.Add($"{id}  ({use})");
    }
    /// <summary>Every ingredient of a cost table gets this use ("fish_*" = every fish).</summary>
    static void UseCost(JsonElement cost, string use)
    {
        if (cost.ValueKind != JsonValueKind.Object) return;
        foreach (var k in cost.EnumerateObject())
        {
            if (k.Name == "fish_*") { foreach (var id in _rows.Keys.Where(x => x.StartsWith("fish_")).ToList()) Use(id, use + " (any fish)"); }
            else Use(Blocks.DropItem(k.Name), use);
        }
    }
    static JsonElement Prop(JsonElement e, string k) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) ? v : default;
    static IEnumerable<JsonProperty> Props(JsonElement e) => e.ValueKind == JsonValueKind.Object ? e.EnumerateObject().Where(p => !p.Name.StartsWith("_")) : Enumerable.Empty<JsonProperty>();

    public static void Run()
    {
        _rows.Clear(); _dangling.Clear();
        Blocks.Load(); GatherDb.Load(); Cooking.Load();

        // ---- materials (gather.json + fish/junk + dishes + blocks, as the pack holds them)
        foreach (var m in GatherDb.Mats.Values)
        {
            string cat = m.Id.StartsWith("blk_") ? "block" : m.Id.StartsWith("fish_") ? "fish" : m.Id.StartsWith("junk_") ? "junk" : m.Id.StartsWith("food_") ? "food" : m.Scrap ? "scrap" : m.Id == "lockpick" ? "tool" : "material";
            if (cat == "block")
            {
                var bd = Blocks.Defs.FirstOrDefault(d => Blocks.Item(d.Key) == m.Id);
                if (bd == null || bd.Hardness < 0 || bd.Key.EndsWith("_open")) continue;   // bedrock and the open twin of a door are never held
            }
            var r = Add(m.Id, m.Name, cat);
            if (m.Value > 0) r.Uses.Add(Sell);
            r.Uses.Add(Trade);
            if (cat == "block") r.Uses.Add("place (build mode)");
        }
        Use("lockpick", "pick locked doors and chests (Lockpick.cs)");

        // ---- data files
        using var blocksDoc = JsonUtil.Load("res://data/blocks.json"); var bk = blocksDoc.RootElement;
        using var cookDoc = JsonUtil.Load("res://data/cooking.json"); var ck = cookDoc.RootElement;
        using var fishDoc = JsonUtil.Load("res://data/fishing.json"); var fs = fishDoc.RootElement;
        using var gemDoc = JsonUtil.Load("res://data/gems.json"); var gm = gemDoc.RootElement;
        using var petDoc = JsonUtil.Load("res://data/pets.json"); var pt = petDoc.RootElement;
        using var lootDoc = JsonUtil.Load("res://data/loot.json"); var lt = lootDoc.RootElement;
        using var itemDoc = JsonUtil.Load("res://data/items.json"); var it = itemDoc.RootElement;
        using var wepDoc = JsonUtil.Load("res://data/weapons.json"); var wp = wepDoc.RootElement;
        using var questDoc = JsonUtil.Load("res://data/quests.json"); var qs = questDoc.RootElement;
        using var tuneDoc = JsonUtil.Load("res://data/tuning.json"); var tn = tuneDoc.RootElement;

        // ---- blocks: recipes (block -> block), cooking fires, non-block crafted items (boat, lockpicks, recycled junk)
        foreach (var p in Props(Prop(bk, "recipes"))) if (p.Value.ValueKind == JsonValueKind.Object) UseCost(Prop(p.Value, "cost"), "craft " + (Blocks.Exists(p.Name) ? Blocks.Def(Blocks.Id(p.Name)).Name : p.Name));
        foreach (var s in Prop(ck, "stations").ValueKind == JsonValueKind.Array ? Prop(ck, "stations").EnumerateArray() : Enumerable.Empty<JsonElement>()) Use(Blocks.Item(s.GetString() ?? ""), "cook at it (the cooking fire)");
        foreach (var p in Props(Prop(bk, "items")))
        {
            string nm = JsonUtil.S(p.Value, "n", p.Name);
            if (!_rows.ContainsKey(p.Name)) { Add(p.Name, nm, "tool").Uses.Add("place on water, board and row (Boat.cs)"); }
            UseCost(Prop(p.Value, "cost"), "craft " + nm);
        }
        var tiers = Prop(Prop(Prop(bk, "dig"), "pickaxes"), "tiers");
        if (tiers.ValueKind == JsonValueKind.Array)
        {
            int i = 0;
            foreach (var t in tiers.EnumerateArray())
            {
                string id = "pickaxe_" + (++i), nm = JsonUtil.S(t, "n", id); var r = Add(id, nm, "tool");
                r.Uses.Add($"dig in build mode (speed {JsonUtil.F(t, "speed"):0.#}, hardness {JsonUtil.F(t, "maxHardness"):0.#})");
                if (JsonUtil.F(t, "price") > 0) r.Uses.Add($"buy {JsonUtil.F(t, "price"):0}g at the smith");
                if (t.TryGetProperty("costs", out var cs)) foreach (var c in cs.EnumerateArray()) UseCost(c, "craft " + nm);
            }
        }

        // ---- cooking: dishes are eaten, ingredients are cooked, recipe books are bought
        foreach (var p in Props(Prop(ck, "recipes")))
        {
            var rc = Cooking.All.GetValueOrDefault(p.Name); if (rc == null) continue;
            Use(rc.Food, $"eat: heals {rc.Heal * 100:0}% + buff ({string.Join(", ", rc.Buff.Keys)})");
            UseCost(Prop(p.Value, "cost"), "cook " + rc.Name);
            if (rc.Kind == "book") { Add("book_" + p.Name, "Recipe Book: " + rc.Name, "book").Uses.Add($"buy {rc.Price}g from the merchant: teaches {rc.Name}"); Use("book_" + p.Name, "read to learn the recipe"); }
        }

        // ---- fishing: the rod (junk and fish are materials above)
        var rod = Prop(fs, "rod"); var rr = Add("rod", JsonUtil.S(rod, "n", "Fishing Rod"), "tool");
        rr.Uses.Add($"fish (take out with 7, attack casts)"); rr.Uses.Add($"buy {JsonUtil.F(rod, "price"):0}g at the shop"); UseCost(Prop(rod, "cost"), "craft fishing rod");
        var horse = Add("horse", "Horse", "tool"); horse.Uses.Add("ride (T)"); horse.Uses.Add($"buy {(int)(Prop(Prop(tn, "game"), "shop").ValueKind == JsonValueKind.Object ? JsonUtil.F(Prop(Prop(tn, "game"), "shop"), "horse", 350) : 350)}g at the shop");

        // ---- potions (the shop, the crafting window, drops)
        Add("potion_hp", "Health Potion", "potion").Uses.AddRange(new[] { "drink to heal", "buy at the shop" });
        Add("potion_mp", "Mana Potion", "potion").Uses.AddRange(new[] { "drink to restore mana", "buy at the shop" });
        var crafting = Prop(lt, "crafting");
        foreach (var p in Prop(crafting, "potions").ValueKind == JsonValueKind.Array ? Prop(crafting, "potions").EnumerateArray() : Enumerable.Empty<JsonElement>())
        { string pot = "potion_" + JsonUtil.S(p, "pot"); UseCost(Prop(p, "cost"), "craft " + JsonUtil.S(p, "pot") + " potions"); if (_rows.ContainsKey(pot)) Use(pot, "craft at Crafting > Potions"); }

        // ---- loot.json: gear crafting, elemental cores, sets, scraps used by the smith
        int rar = 0; foreach (var g in Prop(crafting, "gear").ValueKind == JsonValueKind.Array ? Prop(crafting, "gear").EnumerateArray() : Enumerable.Empty<JsonElement>()) UseCost(Prop(g, "cost"), $"craft gear (rarity {(int)JsonUtil.F(g, "rar", ++rar)})");
        UseCost(Prop(Prop(crafting, "elemental"), "cost"), "craft elemental gear");
        foreach (var p in Props(Prop(Prop(lt, "scraps"), "element"))) Use(p.Value.GetString() ?? "", $"{JsonUtil.F(Prop(crafting, "elemental"), "cores"):0} cores craft {p.Name} gear");
        UseCost(Prop(Prop(crafting, "sets"), "cost"), "forge set pieces");
        Use("ironscrap", "hone gear at the smith"); Use("runeshard", "hone gear at the smith (+5 and up)"); Use("riftprism", "hone gear at the smith (+9 and up)");   // UiSmith.HoneCost
        foreach (var s in Props(Prop(lt, "sets")))
        {
            if (s.Value.ValueKind != JsonValueKind.Object) continue;
            string sn = JsonUtil.S(s.Value, "name", s.Name); var r = Add("set:" + s.Name, sn, "set");
            r.Uses.Add("wear pieces for set bonuses"); r.Uses.Add("forge pieces" + (Prop(s.Value, "craftCost").ValueKind == JsonValueKind.Object ? "" : " once you own one"));
            if (Prop(s.Value, "craftCost").ValueKind == JsonValueKind.Object) UseCost(Prop(s.Value, "craftCost"), "forge " + sn);
        }
        foreach (var d in Props(Prop(lt, "dungeons")))
            foreach (var u in Prop(d.Value, "uniques").ValueKind == JsonValueKind.Array ? Prop(d.Value, "uniques").EnumerateArray() : Enumerable.Empty<JsonElement>())
            { string nm = JsonUtil.S(u, "name"), sl = JsonUtil.S(u, "slot", "weapon"); Gear(Add("unique:" + nm, nm, "unique gear"), sl); }

        // ---- gear bases: items.json names per slot, weapons by type
        var known = new HashSet<string>(Props(Prop(wp, "types")).Select(p => p.Name));
        foreach (var p in Props(Prop(it, "weaponNames")))
        {
            if (!known.Contains(p.Name)) _dangling.Add($"weapon type {p.Name} (items.json weaponNames) has no entry in weapons.json types");
            foreach (var n in p.Value.EnumerateArray()) Gear(Add($"weapon:{p.Name}:{n.GetString()}", n.GetString() ?? "", "weapon"), "weapon");
        }
        foreach (var p in Props(Prop(it, "itemNames")))
        {
            if (!GearSlots.Contains(p.Name)) { _dangling.Add($"slot {p.Name} (items.json itemNames) is not an equip slot"); continue; }
            foreach (var n in p.Value.EnumerateArray()) Gear(Add($"gear:{p.Name}:{n.GetString()}", n.GetString() ?? "", p.Name), p.Name);
        }

        // ---- gems: socket, fuse, break, cut
        foreach (var t in Props(Prop(gm, "types")))
        {
            var r = Add("gem_" + t.Name, JsonUtil.S(t.Value, "n", t.Name), "gem");
            r.Uses.Add($"socket in gear: {JsonUtil.S(t.Value, "stat")}"); r.Uses.Add("fuse 3 into the next tier (Gem Dust)"); r.Uses.Add("break into Gem Dust"); r.Uses.Add("cut as a random Chipped gem" + (t.Value.TryGetProperty("weight", out _) ? "" : ""));
        }
        Use("gemdust", "fuse gems"); Use("gemdust", "cut gems at Crafting > Gems");

        // ---- pets: cubes, pet gear
        foreach (var c in Prop(pt, "cubes").ValueKind == JsonValueKind.Array ? Prop(pt, "cubes").EnumerateArray() : Enumerable.Empty<JsonElement>())
        {
            string id = JsonUtil.S(c, "id"), nm = JsonUtil.S(c, "name", id); var r = Add("cube_" + id, nm, "cube");
            r.Uses.Add($"throw at a weakened monster to bind it (x{JsonUtil.F(c, "mul"):0.#} catch)"); r.Uses.Add($"buy {JsonUtil.F(c, "cost"):0}g at the shop");
            UseCost(Prop(c, "craft"), "craft " + nm);
        }
        var pg = Prop(pt, "gear");
        foreach (var slot in PetSlots)
            foreach (var n in Prop(Prop(pg, slot), "names").ValueKind == JsonValueKind.Array ? Prop(Prop(pg, slot), "names").EnumerateArray() : Enumerable.Empty<JsonElement>())
            { var r = Add($"petgear:{slot}:{n.GetString()}", n.GetString() ?? "", "pet gear"); r.Uses.Add($"equip on a pet ({slot})"); r.Uses.Add(Sell); }
        foreach (var c in Prop(pg, "craft").ValueKind == JsonValueKind.Array ? Prop(pg, "craft").EnumerateArray() : Enumerable.Empty<JsonElement>()) UseCost(Prop(c, "cost"), $"craft {JsonUtil.S(c, "slot")} (rarity {(int)JsonUtil.F(c, "rar")})");

        // ---- quests: items named in requirements or rewards
        foreach (var q in Prop(qs, "quests").ValueKind == JsonValueKind.Array ? Prop(qs, "quests").EnumerateArray() : Enumerable.Empty<JsonElement>()) QuestWalk(q, "quest " + JsonUtil.S(q, "id"));

        Write();
    }

    static void Gear(Row r, string slot)
    {
        r.Uses.Add($"equip ({slot})"); r.Uses.Add(Sell); r.Uses.Add("disassemble into scraps"); r.Uses.Add("socket gems"); r.Uses.Add("hone at the smith");
    }
    static void QuestWalk(JsonElement e, string use)
    {
        if (e.ValueKind == JsonValueKind.Object)
            foreach (var p in e.EnumerateObject())
            {
                if (_rows.ContainsKey(p.Name) && p.Value.ValueKind == JsonValueKind.Number) Use(p.Name, use);
                else if ((p.Name is "item" or "mat" or "give" or "turnIn") && p.Value.ValueKind == JsonValueKind.String && _rows.ContainsKey(p.Value.GetString() ?? "")) Use(p.Value.GetString()!, use);
                else QuestWalk(p.Value, use);
            }
        else if (e.ValueKind == JsonValueKind.Array) foreach (var x in e.EnumerateArray()) QuestWalk(x, use);
    }

    static bool Real(string u) => u != Sell && u != Trade;
    static string Flag(Row r) => r.Uses.Any(Real) ? "" : r.Cat == "junk" ? "sell-only (junk, ok)" : "NO USE";

    static void Write()
    {
        var rows = _rows.Values.OrderBy(r => r.Cat).ThenBy(r => r.Id).ToList();
        int none = rows.Count(r => Flag(r) == "NO USE"), junkOnly = rows.Count(r => Flag(r) != "" && Flag(r) != "NO USE");
        var sb = new StringBuilder();
        sb.AppendLine("# Item uses (focus F-26)").AppendLine();
        sb.AppendLine("Generated by `godot --headless --path . -- --item-uses` (scripts/dev/ItemUses.cs). Every item a player can hold and what it is used for.");
        sb.AppendLine("`sell` and `smith trade 10:1` apply to every material and do not count as a use; an item with only those is flagged NO USE (junk_* fish junk may stay sell-only).").AppendLine();
        sb.AppendLine($"Items: {rows.Count}   NO USE: {none}   sell-only junk: {junkOnly}   dangling references: {_dangling.Count}").AppendLine();
        sb.AppendLine("| item id | name | category | uses | flags |").AppendLine("|---|---|---|---|---|");
        foreach (var r in rows) sb.AppendLine($"| {Esc(r.Id)} | {Esc(r.Name)} | {r.Cat} | {Esc(string.Join(", ", r.Uses))} | {Flag(r)} |");
        if (_dangling.Count > 0) { sb.AppendLine().AppendLine("## Dangling references (an id used in a recipe or list that is not an item)").AppendLine(); foreach (var d in _dangling.Distinct()) sb.AppendLine("- " + d); }
        string path = ProjectSettings.GlobalizePath("res://.devloop/item_uses.md");
        try { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, sb.ToString()); } catch (Exception ex) { GD.PushError("[ITEMUSES] cannot write " + path + ": " + ex.Message); }
        GD.Print($"[ITEMUSES] {rows.Count} items, NO USE {none}, sell-only junk {junkOnly}, dangling {_dangling.Count} -> {path}");
        foreach (var r in rows.Where(r => Flag(r) == "NO USE")) GD.Print($"[ITEMUSES] NO USE: {r.Id} ({r.Name}, {r.Cat})");
    }
    static string Esc(string s) => s.Replace("|", "/");
}
