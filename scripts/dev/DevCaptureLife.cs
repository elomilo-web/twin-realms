using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace TwinRealms;

/// <summary>
/// Focus F-24/F-25 captures: "life:sim" (jump spam in place, real travel jumps, walking into a wall, walking and sprinting, digging,
/// your own placed block, cooking at a campfire block and a campfire prop, eating, picking a locked chest, diminishing returns, then
/// levels and their bonuses through the stats dump; [F24]/[F25] lines), "life:skills" (character screen Life skills tab),
/// "life:book" (cooking book), "life:fire" (cooking at a campfire), "life:lockpick" (the minigame mid-strain).
/// The hero's real skills, recipes, materials and pickaxe come back after the view (DevLifeCleanup).
/// </summary>
public partial class DevCapture
{
    (Dictionary<string, int> life, List<string> rec, Dictionary<string, int> mats, int pick, int gold)? _lifeBak;
    void LifeBackup()
    {
        var d = Game.D; _lifeBak ??= (new Dictionary<string, int>(d.Life ?? new()), new List<string>(d.Recipes ?? new()), new Dictionary<string, int>(d.Mats), d.Pick, d.Gold);
    }
    void DevLifeCleanup()
    {
        Lockpick.DevTension = false; Lockpick.DevAxis = 0; Player.ForcedTarget = null; Player.DevJump = false; Player.DevClimb = 0;
        if (Player.Instance is { } p) p.ToggleSneak(false);
        if (_lifeBak is { } b) { var d = Game.D; d.Life = b.life; d.Recipes = b.rec; d.Mats = b.mats; d.Pick = b.pick; d.Gold = b.gold; _lifeBak = null; LifeSkills.DevReset(); Game.RecalcTree(); }
    }
    static string LifeXp(string id) =>$"{id} {LifeSkills.Xp(Game.D, id)}xp/lv{LifeSkills.Level(id)}";

    async Task LifeAction(string what)
    {
        if (BlockWorld.I?.Area == null) { Say("[F25] no block world"); return; }
        for (int i = 0; i < 40 && !W.ReadyAround(P.GlobalPosition); i++) await Secs(.1f);
        LifeBackup(); var d = Game.D; d.Life = new(); d.Recipes = new(); LifeSkills.DevReset(); Game.RecalcTree();
        switch (what)
        {
            case "sim": await LifeSim(); break;
            case "skills":
            {
                var rng = new Random(25); foreach (var id in LifeSkills.Order) LifeSkills.Gain(id, rng.Next(0, 2600), d, true);
                Game.RecalcTree(); if (Ui.I != null) { Ui.I.Visible = true; Ui.I.OpenLifeSkills(); }
                Say("[F25] life tab: " + string.Join(", ", LifeSkills.Order.Select(i => $"{i} {LifeSkills.Level(i)} ({LifeSkills.Progress(d, i):P0})")));
                await Secs(.5f); break;
            }
            case "book":
            {
                LifeSkills.Gain(Cooking.Skill, 900, d, true); Cooking.Ensure(d); Cooking.Learn(d, "moon_broth"); Cooking.Learn(d, "herb_fish"); Game.RecalcTree();
                d.Mats["food_grilled_fish"] = 3; d.Mats["food_roast_meat"] = 2; d.Mats["food_moon_broth"] = 1;
                if (Ui.I != null) { Ui.I.Visible = true; Ui.I.OpenCookbook(); }
                Say($"[F24] cooking book: level {LifeSkills.Level(Cooking.Skill)}, known {string.Join(", ", d.Recipes)}");
                await Secs(.5f); break;
            }
            case "fire":
            {
                var fire = PlaceFire();
                d.Mats["fish_trout"] = 2; d.Mats["meat"] = 4; d.Mats["berries"] = 6; d.Mats["sunleaf"] = 3; d.Mats["redcap"] = 1;
                LifeSkills.Gain(Cooking.Skill, 200, d, true); Cooking.Ensure(d); Game.RecalcTree(); await Secs(.4f);
                Say($"[F24] fire: campfire block at {fire}, FireNear {Cooking.FireNear(P.GlobalPosition)?.ToString() ?? "none"}, prompt '{P.Prompt}'");
                FreeCam(P.GlobalPosition + new Vector3(2.5f, 2.2f, 2.5f), fire);
                if (Ui.I != null) { Ui.I.Visible = true; Ui.I.OpenCooking(); }
                await Secs(.5f); break;
            }
            case "lockpick":
            {
                var c = PlaceLockedChest(); d.Mats[Lockpick.Item] = 4; LifeSkills.Gain("lockpicking", 300, d, true); Game.RecalcTree();
                if (Ui.I != null) { Ui.I.Visible = true; Ui.I.OpenLockpick(c, false); }
                await Frames(2);
                if (Lockpick.Cur is { } lp) { lp.Angle = Mathf.Clamp(lp.Sweet + 28, 0, 180); Lockpick.DevTension = true; await Secs(.9f); Say($"[F25] lockpick view: angle {lp.Angle:0} (sweet {lp.Sweet:0} +-{lp.Width / 2:0.#}), turn {lp.Turn:0.00} of allowed {lp.Allowed():0.00}, strain {lp.Strain:0.00}"); }
                break;
            }
        }
    }

    Vector3 PlaceFire()
    {
        Pad(-3, 3, -5, 2); SetB(0, 0, -2, "campfire"); return C(.5f, .5f, -1.5f);
    }
    Vector3I PlaceLockedChest()
    {
        Pad(-3, 3, -5, 2); SetB(0, 0, -2, "chest", 2); var c = new Vector3I(_bx, _L, _bz - 2);
        BlockUse.SetOwner(Ar, c, "Stranger"); BlockUse.SetLock(Ar, c, true); return c;
    }

    async Task LifeSim()
    {
        var d = Game.D; d.Pick = Math.Max(d.Pick, 3);
        // ---- outside any town (towns forbid digging, F-18), on dry ground: a long flat runway along -Z
        var h0 = P.GlobalPosition; Vector3? spot = null;
        for (float r = 60; r < 600 && spot == null; r += 30)
            for (int k = 0; k < 8 && spot == null; k++)
            {
                float a = k * Mathf.Tau / 8; float x = h0.X + Mathf.Cos(a) * r, z = h0.Z + Mathf.Sin(a) * r;
                bool ok = true; for (float dz = -48; dz <= 6 && ok; dz += 6) ok = TownZones.At(x, z + dz) == null && TownZones.At(x - 4, z + dz) == null && TownZones.At(x + 4, z + dz) == null && Water.SurfaceAt(new Vector3(x, World.GroundAt(x, z + dz) + 1, z + dz)) == null;
                if (ok) spot = World.OnGround(x, z, .5f);
            }
        if (spot is { } sp) { P.Teleport(sp, 0); await Secs(1f); for (int i = 0; i < 60 && !W.ReadyAround(P.GlobalPosition); i++) await Secs(.1f); await Secs(.4f); }
        Say($"[F25] sim spot {P.GlobalPosition} (town zone {TownZones.At(P.GlobalPosition.X, P.GlobalPosition.Z)?.Name ?? "none"})");
        Pad(-3, 3, -46, 3); await Secs(.6f);
        if (P.Rig is { } rig) rig.Rotation = rig.Rotation with { Y = 0 };
        P.Teleport(C(.5f, .05f, .5f), 0); await Secs(.6f);
        // 1. jump spam standing still: 8 jumps, no XP
        int j0 = LifeSkills.Xp(d, "jumping"), l0 = P.JumpsLanded, c0 = P.JumpsCounted;
        for (int i = 0; i < 8; i++) { Player.DevJump = true; await Secs(.75f); }
        Say($"[F25] jump spam in place: {P.JumpsLanded - l0} jumps landed, {P.JumpsCounted - c0} counted, jumping xp {j0} -> {LifeSkills.Xp(d, "jumping")}; last: {P.LastJump}; walking xp {LifeSkills.Xp(d, "walking")}");
        // 2. walking into a wall: keys held, no distance, no XP
        SetB(-1, 0, -1, "cobblestone"); SetB(0, 0, -1, "cobblestone"); SetB(1, 0, -1, "cobblestone"); SetB(-1, 1, -1, "cobblestone"); SetB(0, 1, -1, "cobblestone"); SetB(1, 1, -1, "cobblestone");
        await Secs(.3f); P.Teleport(C(.5f, .05f, .5f), 0); await Secs(.3f);
        int w0 = LifeSkills.Xp(d, "walking"), r0 = LifeSkills.Xp(d, "running"); var p0 = P.GlobalPosition;
        Input.ActionPress(Inputs.Forward); await Secs(2f); Input.ActionPress(Inputs.Sprint); await Secs(1.5f); Input.ActionRelease(Inputs.Sprint); Input.ActionRelease(Inputs.Forward);
        Say($"[F25] pushing into a wall 3.5 s (forward + sprint held): moved {P.GlobalPosition.DistanceTo(p0):0.00} m, walking xp {w0} -> {LifeSkills.Xp(d, "walking")}, running xp {r0} -> {LifeSkills.Xp(d, "running")}");
        for (int y = 0; y <= 1; y++) for (int x = -1; x <= 1; x++) SetB(x, y, -1, "");
        await Secs(.3f);
        // 3. real travel jumps: running forward, a jump every 1.7 s
        P.Teleport(C(.5f, .05f, 1.5f), 0); await Secs(.4f);
        j0 = LifeSkills.Xp(d, "jumping"); l0 = P.JumpsLanded; c0 = P.JumpsCounted; w0 = LifeSkills.Xp(d, "walking"); p0 = P.GlobalPosition;
        Input.ActionPress(Inputs.Forward);
        for (int i = 0; i < 5; i++) { await Secs(1.0f); Player.DevJump = true; await Secs(.75f); }
        Input.ActionRelease(Inputs.Forward); await Secs(.4f);
        Say($"[F25] travel jumps: moved {P.GlobalPosition.DistanceTo(p0):0.0} m, {P.JumpsLanded - l0} jumps landed, {P.JumpsCounted - c0} counted, jumping xp {j0} -> {LifeSkills.Xp(d, "jumping")}; last: {P.LastJump}; walking xp {w0} -> {LifeSkills.Xp(d, "walking")}");
        // 4. sprinting back along the runway
        r0 = LifeSkills.Xp(d, "running"); p0 = P.GlobalPosition;
        if (P.Rig is { } rig2) rig2.Rotation = rig2.Rotation with { Y = Mathf.Pi };
        P.Teleport(C(.5f, .05f, -42f), Mathf.Pi); await Secs(.3f);
        if (P.Rig is { } rig3) rig3.Rotation = rig3.Rotation with { Y = Mathf.Pi };
        p0 = P.GlobalPosition; Input.ActionPress(Inputs.Forward); Input.ActionPress(Inputs.Sprint); await Secs(3f); Input.ActionRelease(Inputs.Sprint); Input.ActionRelease(Inputs.Forward); await Secs(.3f);
        Say($"[F25] sprint: moved {P.GlobalPosition.DistanceTo(p0):0.0} m, running xp {r0} -> {LifeSkills.Xp(d, "running")} (teleport to the start counted nothing)");
        // 5. sneaking
        int s0 = LifeSkills.Xp(d, "sneaking"); P.ToggleSneak(true); p0 = P.GlobalPosition;
        Input.ActionPress(Inputs.Forward); await Secs(3f); Input.ActionRelease(Inputs.Forward); await Secs(.2f);
        Say($"[F25] sneak: moved {P.GlobalPosition.DistanceTo(p0):0.0} m in 3 s (x{Player.SneakSpeedMul:0.00} speed), sneaking xp {s0} -> {LifeSkills.Xp(d, "sneaking")}, notice range x{Player.SneakNoticeMul:0.00}");
        P.ToggleSneak(false);
        if (P.Rig is { } rig4) rig4.Rotation = rig4.Rotation with { Y = 0 };

        // ---- 6. digging: five stone blocks dug by hand, then one you placed yourself
        P.Teleport(C(.5f, .05f, .5f), 0); await Secs(.3f);
        Player.BuildMode = true; int m0 = LifeSkills.Xp(d, "mining"), b0 = P.BlocksMined;
        var stoneDef = Blocks.Def(Blocks.Id("stone"));
        for (int i = 0; i < 5; i++)
        {
            SetB(i - 2, 0, -2, "stone"); await Frames(2);
            var cell = new Vector3I(_bx + i - 2, _L, _bz - 2); Player.ForcedTarget = (cell, Vector3I.Back);
            Input.ActionPress(Inputs.Attack); float t = 0; while (Ar.Get(cell.X, cell.Y, cell.Z) != 0 && t < 4f) { await Secs(.1f); t += .1f; } Input.ActionRelease(Inputs.Attack);
        }
        Say($"[F24] dig: {P.BlocksMined - b0} stone blocks mined by hand, mining xp {m0} -> {LifeSkills.Xp(d, "mining")} (level {LifeSkills.Level("mining")}), dig time stone {Blocks.DigTime(stoneDef, d.WType(), d.Pick):0.00}s base");
        { var own = new Vector3I(_bx, _L, _bz - 3); SetB(0, 0, -3, "stone"); P.DevNotePlaced(own); await Frames(2); int mm = LifeSkills.Xp(d, "mining"); Player.ForcedTarget = (own, Vector3I.Back); Input.ActionPress(Inputs.Attack); float t = 0; while (Ar.Get(own.X, own.Y, own.Z) != 0 && t < 4f) { await Secs(.1f); t += .1f; } Input.ActionRelease(Inputs.Attack); Say($"[F25] digging a block you placed: gone {Ar.Get(own.X, own.Y, own.Z) == 0}, mining xp {mm} -> {LifeSkills.Xp(d, "mining")} (none)"); }
        Player.ForcedTarget = null; Player.BuildMode = false;

        // ---- 7. cooking at a campfire block, then at a campfire prop
        var fire = PlaceFire(); P.Teleport(C(.5f, .05f, .5f), 0); await Secs(.5f);
        d.Mats["fish_trout"] = 3; d.Mats["sunleaf"] = 3; d.Mats["meat"] = 2;
        int k0 = LifeSkills.Xp(d, Cooking.Skill); Cooking.Ensure(d);
        Say($"[F24] campfire block: FireNear {Cooking.FireNear(P.GlobalPosition)?.ToString() ?? "none"}, AtFire {P.AtFire}, prompt '{P.Prompt}', known recipes {string.Join(",", d.Recipes)}");
        string? why = Cooking.Cook(d, Cooking.All["grilled_fish"]); Say($"[F24] cook grilled fish: {why ?? Cooking.LastCook}");
        why = Cooking.Cook(d, Cooking.All["sunleaf_tea"]); Say($"[F24] discover sunleaf tea: {why ?? Cooking.LastCook}; learned {Cooking.Knows(d, "sunleaf_tea")}");
        why = Cooking.Cook(d, Cooking.All["mushroom_stew"]); Say($"[F24] stew without redcaps: {why ?? "cooked?!"}");
        float hp0 = P.Hp = P.Hp * .5f; Game.RecalcTree(); float reg0 = Game.TS["regenHp"];
        bool ate = Cooking.Eat("food_grilled_fish"); Say($"[F24] eat grilled fish: {ate}, health {hp0:0} -> {P.Hp:0}, regenHp {reg0:0.##} -> {Game.TS["regenHp"]:0.##}, buffs [{string.Join(", ", P.Buffs.Select(b => $"{b.id} {b.t:0}s"))}], cooking xp {k0} -> {LifeSkills.Xp(d, Cooking.Skill)}");
        SetB(0, 0, -2, ""); await Frames(2);
        var prop = new Torch { Size = 1.2f, Range = 10f }; Game.Root!.AddChild(prop); prop.GlobalPosition = World.OnGround(P.GlobalPosition.X + 1.5f, P.GlobalPosition.Z, .5f); _devNodes.Add(prop); await Frames(2);
        Say($"[F24] campfire prop (Torch, the mesh villages): IsCampfire {prop.IsCampfire}, FireNear {Cooking.FireNear(P.GlobalPosition)?.ToString() ?? "none"} (hero {P.GlobalPosition.Y - World.GroundAt(P.GlobalPosition.X, P.GlobalPosition.Z):0.0} m over the smooth ground)");
        prop.QueueFree(); _devNodes.Remove(prop);
        Say($"[F24] cooking station: block '{Blocks.Def(Blocks.Id("cooking_station")).Name}' id {Blocks.Id("cooking_station")}, station {Cooking.IsBlockStation(Blocks.Id("cooking_station"))}, recipe {(Blocks.Root.GetProperty("recipes").TryGetProperty("cooking_station", out var cr) ? cr.GetRawText() : "none")}");

        // ---- 8. picking a locked chest: once off the sweet spot (the pick snaps), once in it
        var chest = PlaceLockedChest(); d.Mats[Lockpick.Item] = 3; int lk0 = LifeSkills.Xp(d, "lockpicking");
        Say($"[F25] locked chest of '{BlockUse.Owner(Ar, chest)}': locked {BlockUse.Locked(Ar, chest)}, CanLockpick {BlockUse.CanLockpick(chest, P)}, sweet spot {Lockpick.SweetDeg():0.#} deg");
        if (Ui.I != null) { Ui.I.Visible = true; Ui.I.OpenLockpick(chest, false); }
        await Frames(2);
        if (Lockpick.Cur is { } lp)
        {
            lp.Angle = lp.Sweet > 90 ? lp.Sweet - 60 : lp.Sweet + 60; Lockpick.DevTension = true;
            float t = 0; while (lp.Broken == 0 && t < 4f) { await Secs(.1f); t += .1f; }
            Lockpick.DevTension = false; await Secs(.3f);
            Say($"[F25] off the sweet spot: turned to {lp.Turn:0.00}, broke {lp.Broken} pick(s) after {t:0.0}s, lockpicks left {d.Mats.GetValueOrDefault(Lockpick.Item)}");
            lp.Angle = lp.Sweet; Lockpick.DevTension = true; t = 0; while (!lp.Solved && t < 4f) { await Secs(.1f); t += .1f; }
            Lockpick.DevTension = false; await Secs(.3f);
            Say($"[F25] in the sweet spot: {Lockpick.LastResult}; chest locked now {BlockUse.Locked(Ar, chest)}, window '{Ui.I?.PanelName}', lockpicking xp {lk0} -> {LifeSkills.Xp(d, "lockpicking")}");
        }
        else Say("[F25] lockpick window did not open");
        Ui.I?.Close(); if (Ui.I != null) Ui.I.Visible = false;

        // ---- 9. diminishing returns: 20 awards of 10 climbing XP in a row
        var gotList = new List<int>(); for (int i = 0; i < 20; i++) { int x0 = LifeSkills.Xp(d, "climbing"); LifeSkills.Award("climbing", 10); gotList.Add(LifeSkills.Xp(d, "climbing") - x0); }
        Say($"[F25] diminishing returns (20 x 10 xp to climbing within a minute, soft {LifeSkills.Defs["climbing"].Soft}): got {string.Join(" ", gotList)}");
        Say("[F25] xp so far: " + string.Join(", ", LifeSkills.Order.Select(LifeXp)));
        Say("[F25] raw vs awarded: " + string.Join(", ", LifeSkills.Log.Select(kv => $"{kv.Key} {kv.Value.raw:0.#}->{kv.Value.got:0.#}")));

        // ---- 10. levels and their bonuses through the stat pipeline (life stage)
        foreach (var id in new[] { "jumping", "running", "swimming", "sneaking", "creeping", "lockpicking", "climbing", "mining", "fishing", "cooking", "walking", "riding", "blocking" }) LifeSkills.Gain(id, 6000, d, true);
        Game.RecalcTree();
        Say("[F25] after 6000 xp each: " + string.Join(", ", new[] { "jumping", "mining", "cooking", "lockpicking", "walking" }.Select(i => $"{i} lv{LifeSkills.Level(i)}")));
        foreach (var l in Commands.Run("stats dump").Split('\n'))
            if (l.Contains("life") || l.Contains("[moveMul]") || l.Contains("[maxSt]") || l.Contains("jumpP") || l.Contains("lockW") || l.Contains("digP") || l.Contains("foodP") || l.Contains("Jumping") || l.Contains("Running ") || l.Contains("[check]") || l.StartsWith("pipeline"))
                Say("[F25] dump> " + l.TrimEnd('\r'));
    }
}
