using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace TwinRealms;

/// <summary>
/// Focus F-22/F-23 capture actions "fish:school", "fish:bite", "fish:minigame", "fish:catch" and "fish:sim" (200 rolled catches across
/// biomes, water kinds, times, weather and levels; the loot distribution is logged as [F23] lines). Fish checks log as [F22].
/// </summary>
public partial class DevCapture
{
    /// <summary>The hero on the shore of the nearest open lake, facing its middle; returns the facing and a water point 7 m out.</summary>
    async Task<(Vector3 dir, Vector3 water)?> FishShore()
    {
        var lk = Lands.All.SelectMany(l => l.Lakes).Where(l => !l.Name.Contains("Frost")).OrderBy(l => l.X * l.X + l.Z * l.Z).FirstOrDefault();
        if (lk == null) { Say("[F22] no lake"); return null; }
        float sx = lk.X;
        for (float f = .3f; f < 1.6f; f += .02f) { float x = lk.X + lk.R * f; if (World.GroundAt(x, lk.Z) > Lands.WaterAt(x, lk.Z) + .15f) { sx = x + 1.2f; break; } }
        var dir = Vector3.Left;   // toward the lake's middle (west)
        P.Teleport(World.OnGround(sx, lk.Z, .5f), Mathf.Atan2(-dir.X, -dir.Z)); await Secs(1.2f);
        for (int i = 0; i < 50 && BlockWorld.I != null && !BlockWorld.I.ReadyAround(P.GlobalPosition); i++) await Secs(.1f);
        await Secs(.5f);
        var w = P.GlobalPosition + dir * 7f; w.Y = Lands.WaterAt(w.X, w.Z);
        Say($"[F22] lake {lk.Name} level {lk.Level:0.00}, hero at {P.GlobalPosition}, biome {FishDb.Biome(w.X, w.Z)}, water {FishDb.WaterKind(w + Vector3.Down * .3f)}, in water {FishDb.InWater(w + Vector3.Down * .4f)}");
        return (dir, w);
    }

    async Task FishAction(string what)
    {
        Fishing.I?.DevReset();
        if (what == "sim") { FishSim(); return; }
        var fs = FishField.I; var fi = Fishing.I; if (fs == null || fi == null) { Say("[F22] no fish field / fishing"); return; }
        if (await FishShore() is not { } sh) return;
        var (dir, water) = sh;
        if (what == "school")
        {
            fs.SeatAll(water, 1, 9); await Secs(.2f);
            Say("[F22] " + fs.Describe());
            // two seconds of swimming: every fish must stay in water (checked each frame)
            int frames = 0, outside = 0, n = 0;
            for (; frames < 120; frames++) { await Frames(1); foreach (var p in fs.Positions()) { n++; if (!FishDb.InWater(p) || p.Y > FishDb.Surface(p.X, p.Z, p.Y)) outside++; } }
            Say($"[F22] {n} fish-frames checked over {frames} frames: {outside} outside the water");
            // flee: drop the hero's position next to the biggest school for a moment (fish read the hero position); count fleeing frames
            int fled0 = fs.Fled; var sc = fs.SchoolCentre();
            if (sc is { } c)
            {
                float d0 = fs.Positions().Min(p => p.DistanceTo(c));
                var hero = P.GlobalPosition; P.GlobalPosition = new Vector3(c.X, c.Y - .6f, c.Z); await Frames(1);
                float near0 = fs.Positions().Where(p => p.DistanceTo(c) < 6).Select(p => p.DistanceTo(c)).DefaultIfEmpty(0).Average();
                await Secs(1.2f);
                float near1 = fs.Positions().Where(p => p.DistanceTo(c) < 12).Select(p => p.DistanceTo(c)).DefaultIfEmpty(0).Average();
                Say($"[F22] flee: hero dropped into the school at {c}; mean distance of nearby fish {near0:0.00} m -> {near1:0.00} m after 1.2 s, flee frames {fs.Fled - fled0}");
                P.Teleport(hero, P.Rotation.Y); await Secs(.4f);
                fs.SeatAll(water, 1, 8); await Secs(.6f);
                c = fs.SchoolCentre() ?? water + Vector3.Down;
                FreeCam(c + new Vector3(1.3f, 2.0f, 1.0f), c);
            }
            // species vary by biome: what a school would be seated as in each biome region's water
            var byBiome = new Dictionary<string, HashSet<string>>();
            foreach (var L in Lands.All) foreach (var lk in L.Lakes)
                {
                    var ctx = new FishDb.Ctx(FishDb.Biome(lk.X, lk.Z), "lake", "day", "clear", 99);
                    byBiome[ctx.Biome] = FishDb.Fish.Values.Where(f => FishDb.FishWeight(f, ctx) > 0).Select(f => f.Name).ToHashSet();
                }
            foreach (var kv in byBiome) Say($"[F22] lake fish in {kv.Key}: {string.Join(", ", kv.Value)}");
            Say("[F22] depth bands: " + string.Join(", ", FishDb.Fish.Values.Select(f => $"{f.Id} {f.D0:0.#}-{f.D1:0.#} m")));
            return;
        }
        fi.DevHoldRod(); await Secs(.4f);
        FreeCam(P.GlobalPosition - dir * 3.2f + new Vector3(0, 2.3f, 1.4f), P.GlobalPosition + dir * 6f + Vector3.Down * .5f);
        await Frames(2);
        fi.Cast(); for (int i = 0; i < 60 && fi.State == Fishing.St.Cast; i++) await Frames(1);
        Say($"[F23] cast: state {fi.State}, bobber {fi.Bobber}, in water {fi.InWaterCast}");
        if (fi.State != Fishing.St.Wait) return;
        fi.DevWait(.05f); await Secs(.15f);
        Say($"[F23] bite: state {fi.State}, hooked {(fi.Hooked is { } hk ? hk.Kind + " " + FishDb.Label(hk) : "none")}");
        if (what == "bite") return;
        if (what == "minigame") { fi.StartReel(); fi.DevReelState(.32f, .45f, .55f); await Secs(.25f); Say($"[F23] reel: zone {fi.Zone:0.00}+{fi.ZoneW:0.00}, fish {fi.FishAt:0.00}, progress {fi.Progress:0.00}"); return; }
        if (what == "catch")
        {
            int xp0 = LifeSkills.Xp(Game.D, FishDb.Skill);
            var pike = FishDb.Fish.GetValueOrDefault("pike") ?? FishDb.Fish.Values.First();
            fi.DevHook(new FishDb.Pick { Kind = "fish", Fish = pike, Size = 84, Diff = pike.Diff });
            fi.StartReel(); fi.DevReelState(.4f, .5f, .97f); fi.DevHold = false; await Secs(.3f);
            Say($"[F23] catch: state {fi.State}, '{fi.LastCatch}', pack {pike.Name} x{Game.D.Mats.GetValueOrDefault("fish_" + pike.Id)}, fishing xp {xp0} -> {LifeSkills.Xp(Game.D, FishDb.Skill)} (level {LifeSkills.Level(FishDb.Skill)})");
        }
    }

    /// <summary>200 catches spread over biomes, water kinds, times, weather and fishing levels: the tables must give different results.</summary>
    void FishSim()
    {
        var rng = new RandomNumberGenerator { Seed = 2323 };
        string[] biomes = { "meadow", "swamp", "tundra", "desert", "volcanic", "ancient" }, waters = { "lake", "river", "sea", "pond" }, times = { "dawn", "day", "dusk", "night" }, weathers = { "clear", "rain", "storm" }; int[] levels = { 1, 10, 30 };
        var all = new Dictionary<string, int>(); var kinds = new Dictionary<string, int>();
        var byKey = new Dictionary<string, Dictionary<string, int>>();
        void Add(Dictionary<string, int> d, string k) => d[k] = d.GetValueOrDefault(k) + 1;
        for (int i = 0; i < 200; i++)
        {
            var c = new FishDb.Ctx(biomes[i % biomes.Length], waters[i / 6 % waters.Length], times[i / 24 % times.Length], weathers[i / 3 % weathers.Length], levels[i / 67 % levels.Length]);
            var p = FishDb.Roll(c, () => rng.Randf()); string lab = p.Kind + ":" + FishDb.Label(p);
            Add(all, lab); Add(kinds, p.Kind);
            foreach (var key in new[] { "biome=" + c.Biome, "water=" + c.Water, "time=" + c.Time, "weather=" + c.Weather, "level=" + c.Level })
            { if (!byKey.TryGetValue(key, out var d)) byKey[key] = d = new(); Add(d, p.Kind == "fish" ? FishDb.Label(p) : p.Kind); }
        }
        Say($"[F23] sim 200 catches: kinds {string.Join(", ", kinds.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value}"))}");
        Say($"[F23] sim all: {string.Join(", ", all.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value}"))}");
        foreach (var kv in byKey.OrderBy(k => k.Key)) Say($"[F23] sim {kv.Key}: {string.Join(", ", kv.Value.OrderByDescending(k => k.Value).Take(6).Select(k => $"{k.Key} {k.Value}"))}");
        // the category odds at the extremes (deterministic): level 1 clear-day lake vs level 30 stormy night sea
        foreach (var c in new[] { new FishDb.Ctx("meadow", "lake", "day", "clear", 1), new FishDb.Ctx("meadow", "sea", "night", "storm", 30), new FishDb.Ctx("meadow", "pond", "day", "clear", 10) })
        {
            float tot = new[] { "fish", "junk", "treasure", "gem" }.Sum(k => FishDb.CatWeight(k, c));
            Say($"[F23] odds {c.Water}/{c.Time}/{c.Weather}/lv{c.Level}: " + string.Join(", ", new[] { "fish", "junk", "treasure", "gem" }.Select(k => $"{k} {FishDb.CatWeight(k, c) / tot:P0}")));
        }
        // XP and levels
        var d0 = new PlayerStats(); for (int i = 0; i < 40; i++) LifeSkills.Gain(FishDb.Skill, 12, d0, true);
        Say($"[F23] life skills: 40 catches x12 xp = {LifeSkills.Xp(d0, FishDb.Skill)} xp -> fishing level {LifeSkills.Level(d0, FishDb.Skill)} ({LifeSkills.Progress(d0, FishDb.Skill):P0} into it)");
    }
}
