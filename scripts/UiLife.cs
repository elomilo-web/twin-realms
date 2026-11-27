using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TwinRealms;

/// <summary>
/// Windows for life and action skills (focus F-24/F-25): the character screen's Life skills tab (every skill with its level, a
/// progress bar and what its level gives), the cooking window at a fire, the cooking book (learned recipes, unknown ones and how to
/// learn them, food you carry with Eat), the lockpicking minigame window, the merchant's lockpicks and recipe books, crafting XP.
/// Every window is a fixed-size Ui.Frame whose body scrolls (the UI rule: windows never stretch).
/// </summary>
public partial class Ui
{
    /// <summary>Character screen tabs: Attributes | Life skills.</summary>
    void CharTabs(Node parent, string cur)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 6); parent.AddChild(row);
        Tabs(row, cur, ("char", "Attributes", OpenCharacterPanel), ("life", "Life skills", OpenLifeSkills));
    }

    // ---------------------------------------------------------------- the Life skills tab
    public void OpenLifeSkills()
    {
        LifeSkills.Load(); var d = Game.D;
        var v = Frame("life", "Character", new Vector2(1120, 620), OpenLifeSkills); HideInfo();
        CharTabs(v, "life");
        Row(v, "Everything you do trains a skill. Each level gives a small bonus (shown in /stats dump as the life stage). Skills grow from real progress: distance moved, jumps that go somewhere, blocks dug, dishes cooked, locks picked.", Dim, 13);
        var cols = new HBoxContainer(); cols.AddThemeConstantOverride("separation", 18); v.AddChild(cols);
        foreach (var (grp, title) in new[] { ("life", "LIFE SKILLS"), ("action", "ACTIONS") })
        {
            var col = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(520, 0) }; col.AddThemeConstantOverride("separation", 4); cols.AddChild(col);
            Lbl(title, 13, UiTheme.Highlight, col); col.AddChild(new HSeparator());
            foreach (var id in LifeSkills.Order.Where(i => LifeSkills.Defs[i].Group == grp)) SkillRow(col, d, id);
        }
    }
    void SkillRow(Node parent, PlayerStats d, string id)
    {
        var def = LifeSkills.Defs[id]; int lv = LifeSkills.Level(d, id), gem = Math.Max(0, (int)Game.TS["life_" + id]); var (into, need) = LifeSkills.Into(d, id);
        var card = CardBox(parent); var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 10); card.AddChild(h);
        card.TooltipText = def.Desc + "\nPerk: " + def.Perk;
        var badge = Lbl(lv.ToString(), 22, GoldC, h); badge.CustomMinimumSize = new Vector2(34, 0); badge.HorizontalAlignment = HorizontalAlignment.Center; badge.VerticalAlignment = VerticalAlignment.Center;
        var mid = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; mid.AddThemeConstantOverride("separation", 1); h.AddChild(mid);
        var top = new HBoxContainer(); mid.AddChild(top);
        var nm = Lbl(def.Name + (gem > 0 ? $"  (+{gem} from gems)" : ""), 15, Parch, top); nm.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        Lbl(need > 0 ? $"{into} / {need} xp" : "max level", 12, Dim, top);
        var bar = new ProgressBar { MinValue = 0, MaxValue = Math.Max(1, need), Value = need > 0 ? into : 1, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 6), MouseFilter = Control.MouseFilterEnum.Ignore };
        bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = def.Group == "life" ? GoldC : new Color(.55f, .8f, .95f) }); bar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0, 0, 0, .55f) });
        mid.AddChild(bar);
        var m = LifeSkills.Mods(id, lv + gem);
        var bl = Lbl(m.Count == 0 ? def.Perk + " (from level 2)" : string.Join(", ", m.Select(kv => BonusText(kv.Key, kv.Value))), 12, m.Count == 0 ? Dim : new Color(.7f, .9f, .7f), mid);
        bl.ClipText = true; bl.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; bl.CustomMinimumSize = new Vector2(360, 0);
    }
    /// <summary>A life-stage key as a short readable bonus.</summary>
    static string BonusText(string k, float v) => k switch
    {
        "digP" => $"+{v:0.#}% dig speed", "chopP" => $"+{v:0.#}% chop speed", "gatherSpeedP" => $"+{v:0.#}% gather speed",
        "mineDropChance" or "chopExtraChance" or "gatherExtraChance" => $"{v:0.#}% chance of one more", "mineGemChance" => $"{v:0.##}% gem chance",
        "fishEase" => $"+{v:0.#}% reel zone", "fishLuck" => $"+{v:0.##} catch levels (whole ones count)", "foodP" => $"+{v:0.#}% food strength", "foodDurP" => $"+{v:0.#}% food duration",
        "foodDoubleChance" => $"{v:0.#}% double portion", "craftSaveChance" => $"{v:0.#}% material back", "smithCost" => $"{v:0.#}% honing price",
        "lockW" => $"+{v:0.#}% sweet spot", "lockStrainP" => $"+{v:0.#}% pick strength", "jumpP" => $"+{v:0.#}% jump height", "move" => $"+{v:0.##}% move speed",
        "stRegenP" => $"+{v:0.#}% stamina regen", "sprintCost" => $"{v:0.#}% sprint stamina", "st" => $"+{v:0.#} stamina", "swimP" => $"+{v:0.#}% swim speed",
        "breathP" => $"+{v:0.#}% breath", "sneakP" => $"+{v:0.#}% sneak speed", "stealthP" => $"-{100 - 100 / (1 + v / 100f):0.#}% notice range", "climbP" => $"+{v:0.#}% climb speed",
        "rideP" => $"+{v:0.#}% mount speed", "block" => $"+{v:0.#}% block", _ => $"{k} {v:+0.##;-0.##}",
    };

    // ---------------------------------------------------------------- cooking at a fire
    public void OpenCooking()
    {
        var d = Game.D; Cooking.Ensure(d);
        var v = Frame("cooking_fire", "Cooking", new Vector2(960, 600), OpenCooking); HideInfo();
        CookHeader(v, d, "fire");
        bool fire = Player.Instance?.AtFire ?? false;
        if (!fire) Row(v, "You need a fire: a campfire or a cooking station (Crafting > Blocks) within a few steps.", new Color(1f, .6f, .5f), 14);
        var grid = new GridContainer { Columns = 2 }; grid.AddThemeConstantOverride("h_separation", 10); grid.AddThemeConstantOverride("v_separation", 8); v.AddChild(grid);
        foreach (var id in Cooking.Order)
        {
            var r = Cooking.All[id]; bool known = Cooking.Knows(d, id);
            if (!known && r.Kind != "discover") continue;   // level and book recipes show in the cooking book until learned
            var pay = Cooking.Resolve(d, r);
            var card = CardBox(grid); card.CustomMinimumSize = new Vector2(450, 0); var cv = new VBoxContainer(); cv.AddThemeConstantOverride("separation", 3); card.AddChild(cv);
            var top = new HBoxContainer(); top.AddThemeConstantOverride("separation", 8); cv.AddChild(top);
            top.AddChild(new ItemSlot { IconId = r.Name, Tint = r.Col, CustomMinimumSize = new Vector2(40, 40), Count = d.Mats.GetValueOrDefault(r.Food) });
            var tv = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; top.AddChild(tv);
            Lbl(known ? r.Name : "Unknown dish", 16, known ? r.Col.Lightened(.35f) : Dim, tv);
            var ef = Lbl(known ? Cooking.BuffText(r) : "Cook it once to learn what it does", 12, known ? new Color(.7f, .9f, .7f) : Dim, tv); ef.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            var ing = new HBoxContainer(); ing.AddThemeConstantOverride("separation", 10); cv.AddChild(ing);
            foreach (var (k, n) in r.Cost)
            {
                int have = k == "fish_*" ? d.Mats.Where(kv => kv.Key.StartsWith("fish_")).Sum(kv => kv.Value) : d.Mats.GetValueOrDefault(k);
                Lbl($"{(k == "fish_*" ? "any fish" : GatherDb.Mat(k).Name)} {have}/{n}", 13, have >= n ? new Color(.6f, 1f, .6f) : Muted, ing);
            }
            var br = new HBoxContainer(); br.AddThemeConstantOverride("separation", 8); cv.AddChild(br);
            var rr = r; int bp = Cost(r.Price);
            var cb = Btn(known ? "Cook" : "Try it", () => { if (Player.Instance?.AtFire != true) { Note("You need a fire to cook", new Color(1, .6f, .5f)); return; } if (Cooking.Cook(Game.D, rr) is { } why) { Note(why, new Color(1, .6f, .5f)); Sfx.UiError(); } OpenCooking(); }, true);
            cb.Disabled = pay == null || !fire; PriceStyle(cb, pay != null && fire); br.AddChild(cb);
            if (known)
            {
                var c5 = Btn("Cook x5", () => { if (Player.Instance?.AtFire != true) return; for (int i = 0; i < 5; i++) if (Cooking.Cook(Game.D, rr) != null) break; OpenCooking(); }, true);
                c5.Disabled = pay == null || !fire; PriceStyle(c5, pay != null && fire); br.AddChild(c5);
            }
            Lbl($"+{r.Xp} xp", 12, Dim, br).VerticalAlignment = VerticalAlignment.Center;
        }
    }
    void CookHeader(Node v, PlayerStats d, string cur)
    {
        var top = new HBoxContainer(); top.AddThemeConstantOverride("separation", 10); v.AddChild(top);
        Tabs(top, cur, ("fire", "At the fire", OpenCooking), ("book", "Cooking book", OpenCookbook));
        top.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        int lv = LifeSkills.Level(d, Cooking.Skill); var (into, need) = LifeSkills.Into(d, Cooking.Skill);
        Lbl($"Cooking level {lv}   {into}/{need} xp", 15, GoldC, top);
        var m = LifeSkills.Mods(Cooking.Skill, LifeSkills.Effective(Cooking.Skill));
        if (m.Count > 0) Row(v, "Your cooking: " + string.Join(", ", m.Select(kv => BonusText(kv.Key, kv.Value))), new Color(.7f, .9f, .7f), 13);
    }

    // ---------------------------------------------------------------- the cooking book
    public void OpenCookbook()
    {
        var d = Game.D; Cooking.Ensure(d);
        var v = Frame("cooking_book", "Cooking Book", new Vector2(960, 600), OpenCookbook); HideInfo();
        CookHeader(v, d, "book");
        int known = Cooking.Order.Count(id => Cooking.Knows(d, id));
        Row(v, $"{known} of {Cooking.Order.Count} recipes learned. Cook at a campfire or a cooking station; click food in your pack (Materials) or Eat here.", Dim, 13);
        foreach (var (title, filter) in new (string, Func<Cooking.Recipe, bool>)[] { ("LEARNED", r => Cooking.Knows(d, r.Id)), ("NOT YET LEARNED", r => !Cooking.Knows(d, r.Id)) })
        {
            var list = Cooking.Order.Select(i => Cooking.All[i]).Where(filter).ToList(); if (list.Count == 0) continue;
            Header(title, v);
            foreach (var r in list)
            {
                bool kn = Cooking.Knows(d, r.Id); var card = CardBox(v); var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 10); card.AddChild(h);
                int have = d.Mats.GetValueOrDefault(r.Food);
                h.AddChild(new ItemSlot { IconId = r.Name, Tint = r.Col, CustomMinimumSize = new Vector2(44, 44), Count = have });
                var tv = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; tv.AddThemeConstantOverride("separation", 1); h.AddChild(tv);
                Lbl(kn ? r.Name : "???  " + (r.Kind == "discover" ? "an unknown dish" : r.Name), 16, kn ? r.Col.Lightened(.35f) : Dim, tv);
                Lbl("Ingredients: " + Cooking.CostText(r), 12, Parch, tv);
                string how = r.Kind switch { "level" => $"Learned at cooking level {r.Level}", "book" => $"Recipe book sold by the merchant ({r.Price}g)", "discover" => "Discover it: cook these ingredients together at a fire", _ => "" };
                var ef = Lbl(kn ? Cooking.BuffText(r) : how, 12, kn ? new Color(.7f, .9f, .7f) : Dim, tv); ef.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                if (kn && have > 0) { string fk = r.Food; var eb = Btn($"Eat ({have})", () => { Cooking.Eat(fk); OpenCookbook(); }, true); PriceStyle(eb, true); h.AddChild(eb); }
            }
        }
    }

    // ---------------------------------------------------------------- lockpicking
    public void OpenLockpick(Vector3I c, bool door)
    {
        var a = BlockWorld.I?.Area; if (a == null) return;
        var def = Blocks.Def(a.Get(c.X, c.Y, c.Z)); string nm = def.Name;
        var v = Frame("lockpick", "Pick the Lock", new Vector2(640, 560), () => { }); HideInfo();
        int picks = Game.D.Mats.GetValueOrDefault(Lockpick.Item);
        Row(v, $"{nm}{(BlockUse.Owner(a, c) is { Length: > 0 } o ? " of " + o : "")}   ·   {picks} lockpick{(picks == 1 ? "" : "s")}   ·   lockpicking level {LifeSkills.Level("lockpicking")}", Parch, 15);
        var lp = new Lockpick { Cell = c, Door = door, LockName = nm, Diff = def.Sound == "iron" ? .6f : def.Key.Contains("dark") ? .45f : .3f, CustomMinimumSize = new Vector2(580, 360) };
        lp.Finished = () =>
        {
            if (!IsInstanceValid(lp) || _panelName != "lockpick") return;
            if (!lp.Solved || lp.Turn < .999f) { Close(); return; }
            if (door) { BlockUse.SetDoor(a, c.X, c.Y, c.Z, true); Close(); }
            else BlockUse.OpenChest(a, c);
        };
        v.AddChild(lp);
        Row(v, $"{Inputs.KeyOf(Inputs.Left)} / {Inputs.KeyOf(Inputs.Right)} or the left stick: move the pick.   Hold {Inputs.KeyOf(Inputs.Jump)} or {Inputs.KeyOf(Inputs.Use)} (pad A / RT): turn the lock.   Off the sweet spot the lock stops and the pick strains, then snaps.", Dim, 13);
    }
    public static Lockpick? LockpickGame => Lockpick.Cur;

    // ---------------------------------------------------------------- the merchant: lockpicks and recipe books
    void LifeShopRows(VBoxContainer list, string tab)
    {
        var d = Game.D; int lpc = Cost((int)LifeSkills.T("lockpickPrice", 25));   // Cost: by standing with the shopkeeper (F-28, UiShop.cs)
        AddRow(list, $"Lockpicks x2  (have {d.Mats.GetValueOrDefault(Lockpick.Item)})", Colors.White, $"Buy {lpc}g", () => { if (d.Gold < lpc) { Sfx.UiError(); Note("Not enough gold", new Color(1, .5f, .5f)); return; } d.Gold -= lpc; d.Mats[Lockpick.Item] = d.Mats.GetValueOrDefault(Lockpick.Item) + 2; Traded(); Sfx.Play("coin"); Game.Notify(); OpenShop(tab); }, d.Gold >= lpc);
        Cooking.Ensure(d);
        foreach (var r in Cooking.All.Values.Where(r => r.Kind == "book" && !Cooking.Knows(d, r.Id)))
        {
            var rr = r; int bp = Cost(r.Price);
            AddRow(list, $"Recipe book: {r.Name}  ({Cooking.CostText(r)})", r.Col.Lightened(.3f), $"Buy {bp}g", () => { if (d.Gold < bp) { Sfx.UiError(); Note("Not enough gold", new Color(1, .5f, .5f)); return; } d.Gold -= bp; Cooking.Learn(d, rr.Id); Traded(); Sfx.Play("coin"); Note($"New recipe: {rr.Name} (Cooking book)", UiTheme.Highlight); Game.Notify(); OpenShop(tab); }, d.Gold >= bp);
        }
    }

    // ---------------------------------------------------------------- crafting XP (UiCraft Go) and its perk
    static void CraftedLife(Dictionary<string, int> cost)
    {
        int total = cost.Values.Sum(); LifeSkills.Award("crafting", Mathf.Min(12f, 1.5f + total * .5f));
        if (total > 0 && GD.Randf() * 100f < StatPipeline.Key("craftSaveChance"))
        {
            var k = cost.Keys.ElementAt((int)(GD.Randi() % (uint)cost.Count)); Game.D.Mats[k] = Game.D.Mats.GetValueOrDefault(k) + 1;
            Note($"Crafting: you saved 1 {GatherDb.Mat(k).Name}", new Color(.7f, .9f, .7f));
        }
    }
}
