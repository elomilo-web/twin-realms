using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TwinRealms;

/// <summary>
/// The hero's life and action skills in play (focus F-24/F-25; data/life.json, LifeSkills.cs):
/// - XP from real movement only: every physics frame the hero's actual displacement (not the keys) feeds walking, running (sprinting),
///   swimming, sneaking, creeping (sneaking within notice range of a monster that has not noticed you), climbing (height gained on a
///   ladder) and riding; a step over track.maxStep in one frame (a teleport) counts nothing, standing still or walking into a wall
///   moves nothing. A jump gives jumping XP only when it lands at least minTravel metres away or minRise higher, once per cooldown.
/// - Sneak (Inputs.Sneak, Caps Lock, a toggle): slower, no sprint; monsters notice you only within SneakNoticeMul of their range.
/// - The bonuses (jump height, sprint stamina, sneak speed and notice range, mount speed, dig speed...) are stat-table keys from the
///   pipeline's life stage, read through StatPipeline.Key.
/// - Cooking at a fire (CookScan), eating food (EatFood), mining XP and perks for dug blocks (MinedBlock).
/// </summary>
public partial class Player
{
    public bool Sneaking { get; private set; }
    bool _onLadder;
    /// <summary>DevCapture: jump this frame without input; climb a ladder up (+1) or down (-1).</summary>
    public static bool DevJump; public static int DevClimb;

    // ---- bonuses from the life stage (stat keys), shown in the stats dump
    public static float JumpMul => Mathf.Clamp(1 + StatPipeline.Key("jumpP") / 100f, .5f, 1.6f);
    public static float SprintCostMul => Mathf.Max(.4f, 1 + StatPipeline.Key("sprintCost") / 100f);
    public static float SneakSpeedMul => Mathf.Min(LifeSkills.T("sneak.maxSpeed", .85f), LifeSkills.T("sneak.speed", .5f) * (1 + StatPipeline.Key("sneakP") / 100f));
    public static float SneakNoticeMul => Mathf.Max(LifeSkills.T("sneak.minNotice", .15f), LifeSkills.T("sneak.notice", .5f) / (1 + StatPipeline.Key("stealthP") / 100f));

    public void ToggleSneak(bool? on = null)
    {
        bool want = on ?? !Sneaking; if (want && (Mounted || InBoat != null || Flying || Swimming)) want = false;   // (in water the same key dives)
        if (want == Sneaking) return; Sneaking = want;
        Ui.Note(Sneaking ? $"Sneaking: slower, monsters notice you from {SneakNoticeMul * 100:0}% as far ({Inputs.KeyOf(Inputs.Sneak)} to stand up)" : "You stop sneaking", new Color(.75f, .8f, .9f));
    }
    /// <summary>A-10: the crouch itself (HeroPose over the clips: hips down, knees bent, back leaning), in while sneaking on the ground.</summary>
    void SneakPose()
    {
        if (!Sneaking && _pose == null) return;
        if (PoseLayer() is not { } pl) return;
        pl.Crouch = Sneaking && !Dead && !Swimming && _rollT <= 0 ? 1f : 0f;
        pl.Drop = LifeSkills.T("sneak.crouchDrop", .26f); pl.Lean = LifeSkills.T("sneak.crouchLean", .3f);
    }

    // ---- movement XP
    Vector3 _lifeLast; bool _lifeInit, _jumpAir; float _jumpCd, _jumpT, _foeScan; Vector3 _jumpFrom; bool _foeNear;
    /// <summary>The last jump's numbers (for the dev capture): travel, rise, whether it counted.</summary>
    public string LastJump = "";
    void LifeTick(float dt)
    {
        var p = GlobalPosition;
        if (Sneaking && Swimming) Sneaking = false;
        SneakPose();
        if (!_lifeInit) { _lifeLast = p; _lifeInit = true; return; }
        var d = p - _lifeLast; _lifeLast = p;
        float h = new Vector2(d.X, d.Z).Length(), maxStep = LifeSkills.T("maxStep", 1.2f);
        _jumpCd -= dt; _jumpT += dt;
        if (Sneaking && (Mounted || InBoat != null || Flying)) Sneaking = false;
        if (h > maxStep || Mathf.Abs(d.Y) > maxStep * 3) { _jumpAir = false; return; }   // a teleport or a load: nothing counts
        if (Flying || Dead) { _jumpAir = false; return; }
        if (Mounted) LifeSkills.Moved("riding", h);
        else if (Swimming) LifeSkills.Moved("swimming", h + Mathf.Abs(d.Y) * .5f);
        else if (_onLadder) { if (d.Y > 0) LifeSkills.Moved("climbing", d.Y); }
        else if (IsOnFloor() && h > .0005f)
        {
            if (Sneaking)
            {
                LifeSkills.Moved("sneaking", h);
                _foeScan -= dt; if (_foeScan <= 0) { _foeScan = .25f; _foeNear = UnawareFoeNear(); }
                if (_foeNear) LifeSkills.Moved("creeping", h);
            }
            else if (_sprWas) LifeSkills.Moved("running", h);
            else LifeSkills.Moved("walking", h);
        }
        // a jump counts when it lands somewhere else: far enough along, or up onto higher ground
        if (_jumpAir && _jumpT > .15f && IsOnFloor())
        {
            _jumpAir = false;
            float travel = new Vector2(p.X - _jumpFrom.X, p.Z - _jumpFrom.Z).Length(), rise = p.Y - _jumpFrom.Y;
            bool ok = (travel >= LifeSkills.T("jumping.minTravel", 2.2f) || rise >= LifeSkills.T("jumping.minRise", .8f)) && _jumpCd <= 0;
            if (ok) { LifeSkills.Award("jumping", LifeSkills.T("jumping.xp", 2)); _jumpCd = LifeSkills.T("jumping.cooldown", 1.5f); }
            LastJump = $"travel {travel:0.00} m, rise {rise:0.00} m, {(ok ? "counted" : "no XP")}";
            JumpsLanded++; if (ok) JumpsCounted++;
        }
        if (_jumpAir && _jumpT > 4f) _jumpAir = false;
    }
    public int JumpsLanded, JumpsCounted;
    void LifeJumped() { _jumpAir = true; _jumpFrom = GlobalPosition; _jumpT = 0; }
    bool UnawareFoeNear()
    {
        float near = LifeSkills.T("creeping.near", 1f);
        foreach (var n in GetTree().GetNodesInGroup("enemies"))
            if (n is Enemy e && !e.Dead && e.Active && e.Unaware && e.GlobalPosition.DistanceTo(GlobalPosition) < e.NoticeRange * near) return true;
        return false;
    }

    // ---- cooking (F-24)
    float _cookScanT; Vector3? _fireAt;
    /// <summary>Called by ScanInteract when nothing else is in reach: a fire nearby offers "Cook".</summary>
    void CookScan()
    {
        _cookScanT -= (float)GetPhysicsProcessDeltaTime();
        if (_cookScanT <= 0) { _cookScanT = .25f; _fireAt = Cooking.FireNear(GlobalPosition); }
        if (_fireAt == null || Mounted || InBoat != null || Dead) return;
        Prompt = "[" + Inputs.KeyOf(Inputs.Use) + "] Cook";
        if (Input.IsActionJustPressed(Inputs.Use)) Ui.I?.OpenCooking();
    }
    /// <summary>Whether a fire to cook at is within reach right now (the cooking window checks it).</summary>
    public bool AtFire => Cooking.FireNear(GlobalPosition) != null;

    /// <summary>Eats a dish: heals at once and gives its buff (the only food buff: eating again replaces it).</summary>
    public void EatFood(Cooking.Recipe r)
    {
        float k = 1 + StatPipeline.Key("foodP") / 100f, dk = 1 + StatPipeline.Key("foodDurP") / 100f;
        float a = Game.D.MaxHp() * r.Heal * k * HealIn; Hp = Mathf.Min(Game.D.MaxHp(), Hp + a);
        if (r.Buff.Count > 0)
        {
            var mods = r.Buff.ToDictionary(kv => kv.Key, kv => kv.Value * k);
            Buffs.RemoveAll(b => b.id == "food"); Buffs.Add(("food", mods, r.Dur * dk)); Game.RecalcTree();
        }
        if (a >= 1) FloatText.Spawn(this, GlobalPosition + new Vector3(0, 2.2f, 0), "+" + Mathf.RoundToInt(a), new Color(.5f, 1f, .6f));
        Sfx.Play("heal", .7f, 1.15f); Ui.Note($"You eat the {r.Name}: {Cooking.BuffText(r)}", r.Col.Lightened(.35f));
    }

    // ---- mining (F-24) and the anti-spam memory of blocks you placed yourself
    readonly HashSet<long> _placedCells = new();
    internal void DevNotePlaced(Vector3I c) => NotePlaced(c);
    void NotePlaced(Vector3I c) { if (_placedCells.Count > 4096) _placedCells.Clear(); _placedCells.Add(BlockArea.BKey(c.X, c.Y, c.Z)); }
    static float DigMul(BlockDef def) => Mathf.Max(.2f, 1 + StatPipeline.Key(def.Tool == "axe" ? "chopP" : "digP") / 100f);
    /// <summary>A block dug out by hand: mining (woodcutting for axe blocks) XP by hardness, and the perks: an extra drop, a gem.
    /// Blocks you placed yourself give nothing back (placing and digging the same block is not practice).</summary>
    internal void MinedBlock(Vector3I b, BlockDef def)
    {
        if (_placedCells.Remove(BlockArea.BKey(b.X, b.Y, b.Z))) return;
        bool wood = def.Tool == "axe"; string sk = wood ? "woodcutting" : "mining";
        bool ore = def.Key.EndsWith("_ore");
        LifeSkills.Award(sk, .6f + Mathf.Max(0, def.Hardness) * (ore ? 3f : .8f));
        var at = new Vector3(b.X + .5f, b.Y + .5f, b.Z + .5f);
        string item = Blocks.DropItem(def.Drops);
        if (item != "" && GD.Randf() * 100f < StatPipeline.Key(wood ? "chopExtraChance" : "mineDropChance")) { GatherDb.Give(item, 1, at); MineBonus++; }
        if (!wood && def.Tool == "pickaxe" && GD.Randf() * 100f < StatPipeline.Key("mineGemChance"))
        {
            GemDb.Load(); var g = GemDb.Roll(GemDb.RollTier(Mathf.Max(0, (int)(b.Y < 0 ? -b.Y : 0) / 4 + LifeSkills.Effective("mining") / 10)));
            GemDb.Give(Game.D, g); Ui.Note("Struck a gem: " + GemDb.Name(g), GemDb.Col(g)); MineBonus++;
        }
        BlocksMined++;
    }
    public int BlocksMined, MineBonus;
}
