using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>
/// A-18: named unique quest rewards (data/uniques.json). A unique has a fixed name, slot, weapon class, stats (a base plus a share per
/// item level, set once when it is given), a signature effect in stat keys (Item.Mods), a glow colour and a flavour text.
/// Story quests give them through the effect "unique": "<id>" (Story.Apply: a quest's rewards, or a choice's "do", so each ending
/// has its own); the nine older quests through "oldQuest" (Quests.Reward). Each is given once per hero (PlayerStats.Flags
/// "unique:<id>"), is flagged Item.Unique (always picked up, never scrapped in bulk), is never re-rolled by the smith, and shows its
/// flavour text in the tooltip. The item itself is an ordinary Item, so old saves and the trade / auction code need nothing new:
/// the definition is found again by the item's name.
/// </summary>
public static class Uniques
{
    public sealed class Def
    {
        public string Id = "", Name = "", Slot = "trinket", Wt = "", Elem = "", Glow = "", Flavour = "", From = "", OldQuest = "";
        public int Rar = 3, MinLvl = 1, Sockets;
        public Dictionary<string, float> Base = new(), Per = new(), Mods = new();
    }
    static readonly Dictionary<string, Def> _all = new(), _byName = new(), _byQuest = new();
    static bool _loaded;
    static void Load()
    {
        if (_loaded) return; _loaded = true;
        if (!FileAccess.FileExists("res://data/uniques.json")) return;
        using var doc = JsonUtil.Load("res://data/uniques.json");
        if (!doc.RootElement.TryGetProperty("uniques", out var us)) return;
        static Dictionary<string, float> Nums(JsonElement e, string k)
        {
            var d = new Dictionary<string, float>(); if (!e.TryGetProperty(k, out var o) || o.ValueKind != JsonValueKind.Object) return d;
            foreach (var x in o.EnumerateObject()) if (x.Value.ValueKind == JsonValueKind.Number) d[x.Name] = x.Value.GetSingle();
            return d;
        }
        foreach (var p in us.EnumerateObject())
        {
            if (p.Name.StartsWith("_") || p.Value.ValueKind != JsonValueKind.Object) continue; var e = p.Value;
            var u = new Def
            {
                Id = p.Name, Name = JsonUtil.S(e, "n", p.Name), Slot = JsonUtil.S(e, "slot", "trinket"), Wt = JsonUtil.S(e, "wt"), Elem = JsonUtil.S(e, "elem"), Glow = JsonUtil.S(e, "glow"),
                Flavour = JsonUtil.S(e, "flavour"), From = JsonUtil.S(e, "from"), OldQuest = JsonUtil.S(e, "oldQuest"),
                Rar = Math.Clamp((int)JsonUtil.F(e, "rar", 3), 0, 4), MinLvl = Math.Max(1, (int)JsonUtil.F(e, "minLvl", 1)), Sockets = (int)JsonUtil.F(e, "sockets"),
                Base = Nums(e, "base"), Per = Nums(e, "per"), Mods = Nums(e, "mods"),
            };
            _all[u.Id] = u; _byName[u.Name] = u; if (u.OldQuest != "") _byQuest[u.OldQuest] = u;
        }
    }
    public static Def? Get(string id) { Load(); return _all.GetValueOrDefault(id); }
    public static IEnumerable<Def> All { get { Load(); return _all.Values; } }
    /// <summary>The unique this item is (by its name), or null.</summary>
    public static Def? Of(Item? it) { if (it == null || !it.Unique) return null; Load(); return _byName.GetValueOrDefault(it.Name); }
    /// <summary>A quest unique: the smith does not re-roll its signature stats.</summary>
    public static bool Fixed(Item? it) => Of(it) != null;
    /// <summary>The unique an older quest (data/quests.json) rewards, or null.</summary>
    public static Def? ForOldQuest(string questId) { Load(); return _byQuest.GetValueOrDefault(questId); }
    public static bool Has(string id) => Game.D.Flags != null && Game.D.Flags.ContainsKey("unique:" + id);

    /// <summary>Builds the item at an item level (the hero's level when it is given, at least the unique's own minimum).</summary>
    public static Item Make(Def u, int level)
    {
        int lvl = Math.Max(u.MinLvl, Math.Max(1, level));
        float V(string k) => u.Base.GetValueOrDefault(k) + u.Per.GetValueOrDefault(k) * lvl;
        var it = new Item
        {
            Name = u.Name, Slot = u.Slot, Rar = u.Rar, Lvl = lvl, Unique = true,
            Dmg = (int)MathF.Round(V("dmg")), Def = (int)MathF.Round(V("def")), Hp = (int)MathF.Round(V("hp")), Mp = (int)MathF.Round(V("mp")), Sp = MathF.Round(V("sp") * 100f) / 100f,
            Elem = u.Elem == "" ? null : u.Elem, Glow = u.Glow == "" ? null : u.Glow, Mods = u.Mods.Count > 0 ? new Dictionary<string, float>(u.Mods) : null, Sockets = Math.Clamp(u.Sockets, 0, 4),
        };
        if (u.Slot == "weapon")
        {
            it.Wt = Weapons.Types.Contains(u.Wt) ? u.Wt : "sword"; it.Two = Weapons.Get(it.Wt).Hands > 1;
            if (it.Wt == "staff") it.Mc = Tune.F("items.weapon.staffManaCost", .1f);
        }
        return it;
    }

    /// <summary>Gives the hero a unique, once. Returns the item's name ("" when it was given before: the caller may pay something else).
    /// A full pack sends it to the storage chest at home, so it is never lost.</summary>
    public static string Give(string id)
    {
        var u = Get(id); var d = Game.D;
        if (u == null) { GD.PushWarning("uniques.json: no unique '" + id + "'"); return ""; }
        if (Has(id)) return "";
        d.Flags ??= new(); d.Flags["unique:" + id] = "1";
        var it = Make(u, d.Level); bool stash = d.Inv.Count >= 28;
        if (stash) d.Stash.Add(it); else d.Inv.Add(it);
        var col = Glow(u, it);
        Ui.Note("Unique reward: " + it.Describe() + (stash ? "  (your pack is full: it waits in your storage chest at home)" : ""), col);
        if (u.Flavour != "") Ui.Note("\"" + u.Flavour + "\"", col.Lerp(new Color(.8f, .8f, .8f), .5f));
        if (Player.Instance is { } p) { FloatText.Spawn(p, p.GlobalPosition + new Vector3(0, 2.5f, 0), u.Name, col, 1.6f); Fx.Ring(p.GlobalPosition, 3f, col, .7f); }
        Sfx.Play("level");
        return it.Name;
    }
    public static Color Glow(Def u, Item it) => u.Glow != "" && Color.HtmlIsValid(u.Glow) ? Color.FromHtml(u.Glow) : it.Color;

    /// <summary>Tooltip lines for a quest unique: its flavour text and where it came from ("" when the item is not one).</summary>
    public static (string flavour, string from) Tip(Item it) => Of(it) is { } u ? (u.Flavour, u.From) : ("", "");

    /// <summary>Data check for the dev console: every unique names a real slot, weapon class, element and known stat keys.</summary>
    public static string Check()
    {
        Load(); TreeDb.Load(); var bad = new List<string>();
        foreach (var u in _all.Values)
        {
            if (Array.IndexOf(PlayerStats.Slots, u.Slot) < 0) bad.Add($"{u.Id}: slot {u.Slot}");
            if (u.Slot == "weapon" && !Weapons.Types.Contains(u.Wt)) bad.Add($"{u.Id}: weapon class {u.Wt}");
            if (u.Elem != "" && GearFx.Get(u.Elem) == null) bad.Add($"{u.Id}: element {u.Elem}");
            foreach (var k in u.Mods.Keys) if (!TreeDb.Known.Contains(k) && !k.StartsWith("form_")) bad.Add($"{u.Id}: stat key {k}");
            if (u.OldQuest != "" && Quests.ById(u.OldQuest) == null) bad.Add($"{u.Id}: old quest {u.OldQuest}");
        }
        return $"uniques: {_all.Count} ({_byQuest.Count} for the older quests)" + (bad.Count == 0 ? ", all check out" : "\n  BAD " + string.Join("\n  BAD ", bad));
    }
}
