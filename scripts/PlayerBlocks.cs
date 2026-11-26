using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TwinRealms;

/// <summary>
/// The hero and the block world: build mode (Inputs.BuildMode, N) with a crosshair target and the pickaxe in hand (Game.D.Pick, data/blocks.json
/// dig.pickaxes): hold attack to dig (swings, chips and cracks; time from the block's hardness and the pickaxe or weapon as a tool), heavy attack
/// places the hotbar block (held: repeats every .2 s) with a ghost preview that turns red where it cannot go. A 9-slot block hotbar (keys 1-9,
/// mouse wheel, D-pad left/right; Game.D.BuildBar). Stairs face the way you look, slabs go in the upper half when placed against a ceiling.
/// Swimming in water blocks, burning in lava, sliding on ice and frozen blocks, stepping up onto stairs and slabs.
/// </summary>
public partial class Player
{
    public static bool BuildMode;
    /// <summary>Tests aim here instead of at the crosshair (block x, y, z and the face normal).</summary>
    public static (Vector3I b, Vector3I n)? ForcedTarget;
    (Vector3I b, Vector3I n)? _bTarget; float _digT; Vector3I _digAt; float _placeCd, _placeRep; byte _rot;
    string _sel = ""; MeshInstance3D? _ghost, _crack; StandardMaterial3D? _ghostMat, _crackMat; ImageTexture[]? _cracks;
    float _fluidSpeed = 1f, _accelMul = 1f; float _lavaT; bool _wasWater;
    float _swingT, _swingCd, _swingHit; int _swingSerial, _avSwing; const float SwingTime = .42f;
    int _barSel; float _barRefresh, _nameT; string _barShown = ""; readonly bool[] _numDown = new bool[9];
    public string BlockSelected => _sel;

    /// <summary>Water, lava and ice before this frame's movement: speed and acceleration factors, swimming, lava burns.</summary>
    void BlockMove(float dt, ref Vector3 v)
    {
        _fluidSpeed = 1f; _accelMul = 1f; _onLadder = false;
        bool water = SwimMove(dt, ref v);   // water blocks and the smooth lakes: float, swim, dive, breath (PlayerSwim.cs, F-20)
        if (!BlockWorld.Enabled) return;
        string body = water ? "water" : BlockWorld.FluidAt(GlobalPosition + Vector3.Up * .9f);
        if (body == "lava")
        {
            // wading in lava: sink slowly, Jump rises; slower on the move
            v.Y += _gravity * dt * .85f;
            bool up = Input.MouseMode == Input.MouseModeEnum.Captured && Input.IsActionPressed(Inputs.Jump);
            v.Y = Mathf.MoveToward(v.Y, up ? Blocks.F("fluids.swim.rise", 3.2f) : -Blocks.F("fluids.swim.sink", 1.6f), 9f * dt);
            if (up && BlockWorld.FluidAt(GlobalPosition + Vector3.Up * 1.5f) == "" && IsOnWall()) v.Y = Mathf.Max(v.Y, JumpVelocity * .8f);   // climb out onto the bank
            _fluidSpeed = Blocks.F("fluids.swim.speed", .6f);
            {
                _lavaT -= dt; _fluidSpeed *= .5f;
                if (_lavaT <= 0) { _lavaT = .5f; TakeHit(new HitInfo { Damage = Blocks.F("fluids.lavaDps", 18) * .5f * (1 - Mathf.Clamp(Game.TS["res_fire"], -100, 75) / 100f), From = GlobalPosition + Vector3.Down, NoBlock = true, Type = "fire", Fire = true }); }
            }
        }
        if (IsOnFloor() && BlockWorld.SlipperyAt(GlobalPosition)) _accelMul = .12f;
        // ladders: inside a ladder cell, forward/jump climbs at 3 m/s, back descends, otherwise hold; no gravity while on it
        byte lad = Blocks.Id("ladder");
        if (lad != 0 && body == "" && (BlockWorld.At(GlobalPosition + Vector3.Up * .3f) == lad || BlockWorld.At(GlobalPosition + Vector3.Up * 1f) == lad))
        {
            bool cap = Input.MouseMode == Input.MouseModeEnum.Captured;
            bool upK = cap && (Input.IsActionPressed(Inputs.Forward) || Input.IsActionPressed(Inputs.Jump)) || DevClimb > 0, dnK = cap && Input.IsActionPressed(Inputs.Back) || DevClimb < 0;
            float cs = 3f * (1 + StatPipeline.Key("climbP") / 100f);   // F-25: climbing levels climb faster
            v.Y = upK ? cs : dnK ? -cs : 0f; _onLadder = true;
        }
        StepUp(v);
    }

    /// <summary>Walking into a stair, a slab (or a full block from a slab's top): a half-block step is climbed without jumping.</summary>
    void StepUp(Vector3 v) { StepLift += BlockStep.StepUp(this, v); }   // the shared step (monsters and pets use it too); StepLift eases the camera/model over it
    /// <summary>How far the hero was just lifted by a step and is still easing over (the model is drawn this much lower, shrinking to 0).</summary>
    internal float StepLift;

    /// <summary>Wall side (0 +X, 1 -X, 2 +Z, 3 -Z) for a ladder or door being placed: against the clicked wall, else the way the camera faces.</summary>
    byte WallSide(Vector3I n)
    {
        if (n.X != 0) return (byte)(n.X > 0 ? 1 : 0);
        if (n.Z != 0) return (byte)(n.Z > 0 ? 3 : 2);
        var f = -(GetViewport().GetCamera3D()?.GlobalTransform.Basis.Z ?? Vector3.Forward);
        return (byte)(Mathf.Abs(f.X) > Mathf.Abs(f.Z) ? (f.X > 0 ? 0 : 1) : (f.Z > 0 ? 2 : 3));
    }
    /// <summary>The block's meta for how it is placed: walls for ladders and doors, the look direction for stairs, upper/lower half for slabs, the log axis otherwise.</summary>
    byte PlaceMeta(string shape, Vector3I n) => shape switch
    {
        "ladder" or "door" => WallSide(n),
        // stairs face the way you look; against a ceiling or the upper half of a wall they hang upside down (meta & 4), like slabs go in the upper half
        "stairs" => (byte)(WallSide(Vector3I.Zero) | (UpperHalf(n) ? 4 : 0)),
        "slab" => (byte)(UpperHalf(n) ? 4 : 0),
        "post" => (byte)(n.X != 0 || n.Z != 0 ? 1 + WallSide(n) : 0),   // a torch on a wall leans out from it (1 + wall side), else it stands
        "chest" => (byte)(WallSide(Vector3I.Zero) ^ 1),                 // the latch faces you
        "fence" => 0,
        _ => _rot,
    };
    /// <summary>Placing against a ceiling, or high on a wall (the aimed point in the upper half of the face).</summary>
    bool UpperHalf(Vector3I n)
    {
        if (n.Y < 0) return true; if (n.Y > 0) return false;
        if (ForcedTarget != null) return ForcedUpper;
        return BlockAim.Target(GetViewport(), BlockEye, Blocks.F("world.reach", 6f)) is { } t && t.hit.Y - Mathf.Floor(t.hit.Y) > .5f;
    }
    /// <summary>Tests: with a ForcedTarget on a wall, place as if aimed at the upper half of the face.</summary>
    public static bool ForcedUpper;
    /// <summary>Tests (DevCapture blocks:stairs): the meta a block of this shape gets when placed against face n.</summary>
    internal byte DevPlaceMeta(string shape, Vector3I n) => PlaceMeta(shape, n);

    /// <summary>
    /// E on a door within reach opens/closes it (both halves swing, BlockUse.SetDoor; block changes sync in multiplayer), E on a chest opens its
    /// window; locked ones only for their owner. In build mode the rotate key locks or unlocks a door or chest you own.
    /// </summary>
    bool BlockUseInput(bool captured)
    {
        if (!captured || !BlockWorld.Enabled || BlockWorld.I?.Area is not { } a) return false;
        if (Aim() is not { } t || (new Vector3(t.b.X + .5f, t.b.Y + .5f, t.b.Z + .5f) - (GlobalPosition + Vector3.Up)).Length() > 3.6f) return false;
        byte id = a.Get(t.b.X, t.b.Y, t.b.Z); bool door = BlockUse.IsDoor(id), chest = BlockUse.IsChest(id);
        if (!door && !chest) return false;
        var c = BlockUse.Anchor(a, t.b.X, t.b.Y, t.b.Z); bool locked = BlockUse.Locked(a, c), may = BlockUse.MayOpen(a, c, BlockUse.Me, BlockUse.Admin);
        bool canLock = BuildMode && BlockUse.MayLock(a, c, BlockUse.Me, BlockUse.Admin);
        bool pick = !BuildMode && BlockUse.CanLockpick(c, this); int picks = Game.D.Mats.GetValueOrDefault(Lockpick.Item);   // F-25: someone else's lock
        string what = door ? (BlockUse.IsOpen(id) ? "Close door" : "Open door") : "Open chest";
        string p = pick ? (picks > 0 ? $"[{Inputs.KeyOf(Inputs.Use)}] Pick the lock  ({picks} lockpick{(picks == 1 ? "" : "s")})" : "Locked: you need a lockpick")
            : may ? "[" + Inputs.KeyOf(Inputs.Use) + "] " + what + (locked ? " (locked)" : "") : "Locked" + (BlockUse.Owner(a, c) is { Length: > 0 } o ? " by " + o : "");
        if (canLock) p += "   [" + Inputs.KeyOf(Inputs.BlockRotate) + "] " + (locked ? "Unlock" : "Lock");
        if (Prompt == "") Prompt = p;
        if (canLock && Input.IsActionJustPressed(Inputs.BlockRotate))
        {
            if (BlockUse.Owner(a, c) == "") BlockUse.SetOwner(a, c, BlockUse.Me);
            BlockUse.SetLock(a, c, !locked); Ui.Note((locked ? "Unlocked: anyone may open it" : "Locked: only you can open it") , Ui.Dim);
            return true;
        }
        if (!Input.IsActionJustPressed(Inputs.Use)) return false;
        if (pick)
        {
            if (picks <= 0) { Ui.Note("Locked: you need a lockpick (Crafting > Blocks, or the merchant)", new Color(1, .6f, .5f)); Sfx.Play("lock", .6f, .7f); }
            else Ui.I?.OpenLockpick(c, door);
            return false;
        }
        if (door) { if (BlockUse.UseDoor(a, t.b.X, t.b.Y, t.b.Z, BlockUse.Me, BlockUse.Admin) is { } why) Ui.Note(why, new Color(1, .6f, .5f)); }
        else BlockUse.OpenChest(a, c);
        return false;
    }

    /// <summary>Build mode input (replaces attacking while it is on). Called every physics frame.</summary>
    void BlockInput(float dt, bool captured)
    {
        if (captured && Input.IsActionJustPressed(Inputs.BuildMode))
        {
            BuildMode = !BuildMode;
            Ui.Note(BuildMode ? $"Build mode ({Blocks.PickName(Game.D.Pick)}): hold attack to dig, heavy attack to place, 1-9 or the wheel to choose" : "Build mode off", UiTheme.Highlight);
        }
        bool on = BuildMode && BlockWorld.Enabled && BlockWorld.I?.Area != null;
        _av?.ShowTool(on && !Mounted ? Blocks.PickModel(Game.D.Pick) : null);   // the pickaxe in the right hand, weapon and shield away
        _swingT = Mathf.Max(0, _swingT - dt);
        if (on) BarInput(captured, dt);
        BlockHud(); bool lockUsed = BlockUseInput(captured);
        if (!on) { HideBlockFx(); _digT = 0; _swingCd = 0; return; }
        if (captured && !lockUsed && Input.IsActionJustPressed(Inputs.BlockRotate)) { _rot = (byte)((_rot + 1) % 3); Ui.Note("Placing along " + (_rot == 0 ? "up" : _rot == 1 ? "east-west" : "north-south"), Ui.Dim); }
        _placeCd = Mathf.Max(0, _placeCd - dt); _placeRep -= dt;
        _bTarget = ForcedTarget ?? Aim();
        bool act = captured || ForcedTarget != null;
        bool dig = act && Input.IsActionPressed(Inputs.Attack);
        if (_bTarget is not { } t)
        {
            HideBlockFx(); _digT = 0;
            if (dig) DigSwing(dt, null); else _swingCd = 0;   // swinging at the air
            return;
        }
        var a = BlockWorld.I!.Area!;
        ShowOutline(t.b, a.Get(t.b.X, t.b.Y, t.b.Z));
        // dig: hold attack; the pickaxe swings every SwingTime, chips fly at each blow
        byte id = a.Get(t.b.X, t.b.Y, t.b.Z);
        if (dig && id != 0 && id != 255)
        {
            if (t.b != _digAt) { _digAt = t.b; _digT = 0; }
            var def = Blocks.Def(id); float need = Blocks.DigTime(def, Game.D.WType(), Game.D.Pick) * (BlockWorld.I.Frozen(t.b.X, t.b.Y, t.b.Z) ? .5f : 1f) / DigMul(def);   // F-24: mining / woodcutting levels dig faster
            bool locked = BlockUse.Protects(a, t.b.X, t.b.Y, t.b.Z) && !BlockUse.MayOpen(a, BlockUse.Anchor(a, t.b.X, t.b.Y, t.b.Z), BlockUse.Me, BlockUse.Admin);
            string? townMsg = TownZones.Deny(t.b.X, t.b.Z);
            bool prot = a.Gen.Protected(t.b.X, t.b.Y, t.b.Z) || locked || townMsg != null;
            _modelYaw = Rig?.Yaw ?? _modelYaw;
            if (float.IsInfinity(need) || prot)
            {
                if (townMsg != null) TownZones.Notify(townMsg);
                else if (_digT == 0)
                {
                    int needT = Blocks.PickNeeded(def);
                    Ui.Note(locked ? def.Name + " is locked: only its owner can break it" : !prot && def.Breakable && needT > Game.D.Pick ? $"{def.Name} needs a {Blocks.PickName(needT)}" : def.Name + " cannot be broken", Ui.Dim);
                }
                _digT = .001f; DigSwing(dt, null);
            }
            else
            {
                _digT += dt; DigSwing(dt, t.b);
                if (_digT >= need)
                {
                    byte dm = a.GetMeta(t.b.X, t.b.Y, t.b.Z);
                    bool dug = BlockFx.Dig(t.b.X, t.b.Y, t.b.Z);
                    if (dug) MinedBlock(t.b, def);   // F-24: mining / woodcutting XP and perks
                    if (dug && def.Shape == "door")   // the other half goes too (no second drop)
                    {
                        int oy = (dm & 4) != 0 ? t.b.Y - 1 : t.b.Y + 1; byte ob = a.Get(t.b.X, oy, t.b.Z);
                        if (Blocks.Def(ob).Shape == "door") BlockWorld.I.Change(t.b.X, oy, t.b.Z, 0);
                    }
                    _digT = 0; _barRefresh = 0;
                }
                ShowCrack(t.b, Mathf.Clamp(_digT / need, 0, 1));
            }
        }
        else { _digT = 0; _swingCd = 0; if (_crack != null) _crack.Visible = false; }
        // place: heavy attack (held: again every .2 s, like Minecraft)
        var at = t.b + t.n;
        string shape = _sel == "" ? "" : Blocks.Def(Blocks.Id(_sel)).Shape;
        string? why = _sel == "" ? (Owned().Any() ? "Nothing in this slot" : "No blocks in your pack") : BlockFx.CanPlace(at.X, at.Y, at.Z);
        if (why == null && shape == "door") why = BlockFx.CanPlace(at.X, at.Y + 1, at.Z);   // a door is two blocks tall
        byte pm = PlaceMeta(shape, t.n);
        ShowGhost(at, why == null, shape, pm);
        bool press = act && Input.IsActionJustPressed(Inputs.Heavy), hold = act && Input.IsActionPressed(Inputs.Heavy);
        if ((press || (hold && _placeRep <= 0)) && _placeCd <= 0)
        {
            _placeCd = Blocks.F("world.placeCooldown", .18f); _placeRep = press ? .3f : .2f;
            if (why != null) { if (press) { if (TownZones.Deny(at.X, at.Z) != null) TownZones.Notify(why); else Ui.Note(why, new Color(1, .6f, .5f)); } }
            else
            {
                var bid = Blocks.Id(_sel);
                bool placed = BlockFx.Place(at.X, at.Y, at.Z, bid, pm);
                if (placed && shape == "door" && !BlockFx.Place(at.X, at.Y + 1, at.Z, bid, (byte)(pm | 4))) { BlockWorld.I.Change(at.X, at.Y, at.Z, 0); placed = false; }
                if (placed)
                {
                    BlockUse.Placed(a, at.X, at.Y, at.Z, BlockUse.Me);   // doors and chests: you own them (and may lock them)
                    NotePlaced(at);   // digging your own block back gives no mining XP (F-25 anti-spam)
                    var m = Game.D.Mats; string k = Blocks.Item(_sel); m[k] = m.GetValueOrDefault(k) - 1; if (m[k] <= 0) m.Remove(k); Game.Notify();
                    BlockFx.PlacePuff(at.X, at.Y, at.Z, bid); _swingT = .28f; _swingSerial++; _modelYaw = Rig?.Yaw ?? _modelYaw; _barRefresh = 0;
                }
            }
        }
    }

    /// <summary>One pickaxe blow every SwingTime while digging: the chop clip (BuildSwing), then chips and a knock at the moment it lands.</summary>
    void DigSwing(float dt, Vector3I? b)
    {
        _swingCd -= dt;
        if (_swingCd <= 0) { _swingCd = SwingTime; _swingT = SwingTime; _swingSerial++; _swingHit = SwingTime * .45f; }
        if (_swingHit > 0)
        {
            _swingHit -= dt;
            if (_swingHit <= 0)
            {
                if (b is { } c) { BlockFx.Chip(c.X, c.Y, c.Z); BlockFx.Chip(c.X, c.Y, c.Z); Sfx.Play("hit", .32f, 1.2f); }
                else if (_bTarget != null) Sfx.Play("hit", .4f, 1.7f);   // a clink off something too hard
            }
        }
    }
    /// <summary>Plays the pickaxe swing on the model while one is running (called from AnimateAvatar).</summary>
    bool BuildSwing(Avatar a)
    {
        if (!BuildMode || _swingT <= 0) return false;
        bool fresh = _swingSerial != _avSwing; _avSwing = _swingSerial;
        return a.PlayAttack("chop", SwingTime * .45f, fresh) || a.PlayAttack("slam", SwingTime * .45f, fresh);
    }

    // ---------------------------------------------------------------- the block hotbar (Game.D.BuildBar: 9 keys, "" = empty slot)
    List<string> Bar
    {
        get
        {
            var b = Game.D.BuildBar ??= new(); Game.D.BuildBar = b;
            while (b.Count < 9) b.Add(""); if (b.Count > 9) b.RemoveRange(9, b.Count - 9);
            for (int i = 0; i < 9; i++) b[i] ??= "";
            return b;
        }
    }
    /// <summary>Slots whose block ran out empty; blocks owned but not on the bar fill the first empty slots (in build.order).</summary>
    void RefreshBar()
    {
        var b = Bar;
        for (int i = 0; i < 9; i++) if (b[i] != "" && (!Blocks.Exists(b[i]) || Game.D.Mats.GetValueOrDefault(Blocks.Item(b[i])) <= 0)) b[i] = "";
        foreach (var k in Owned()) { if (b.Contains(k)) continue; int e = b.IndexOf(""); if (e < 0) break; b[e] = k; }
    }
    void SelectSlot(int i) { _barSel = Math.Clamp(i, 0, 8); _sel = Bar[_barSel]; }
    void BarInput(bool captured, float dt)
    {
        _barRefresh -= dt; if (_barRefresh <= 0) { _barRefresh = .25f; RefreshBar(); }
        if (captured)
        {
            for (int i = 0; i < 9; i++)
            {
                bool down = Input.IsPhysicalKeyPressed(Key.Key1 + i);
                string sp = "spell" + (i + 1);
                if ((down && !_numDown[i]) || (i < 6 && InputMap.HasAction(sp) && Input.IsActionJustPressed(sp))) SelectSlot(i);
                _numDown[i] = down;
            }
            if (InputMap.HasAction("ui_left") && Input.IsActionJustPressed("ui_left")) CycleBlock(-1);   // D-pad
            if (InputMap.HasAction("ui_right") && Input.IsActionJustPressed("ui_right")) CycleBlock(1);
        }
        _sel = Bar[Math.Clamp(_barSel, 0, 8)];
        if (_sel != _barShown) { _barShown = _sel; _nameT = 2.5f; }
        _nameT = Mathf.Max(0, _nameT - dt);
    }
    /// <summary>Mouse wheel / D-pad in build mode: the next hotbar slot that holds a block (Ui and CameraRig forward wheel events here).</summary>
    public void CycleBlock(int dir)
    {
        RefreshBar(); var b = Bar;
        if (!b.Any(k => k != "")) { _barSel = ((_barSel + dir) % 9 + 9) % 9; _sel = ""; return; }
        int i = _barSel;
        if (dir == 0) { if (b[i] == "") dir = 1; else { _sel = b[i]; return; } }
        for (int n = 0; n < 9; n++) { i = ((i + dir) % 9 + 9) % 9; if (b[i] != "") break; }
        _barSel = i; _sel = b[i];
    }
    static IEnumerable<string> Owned()
    {
        var order = new List<string>(); if (Blocks.Node("build.order") is { } o) foreach (var x in o.EnumerateArray()) order.Add(x.GetString() ?? "");
        foreach (var d in Blocks.Defs) if (d.Id != 0 && !d.IsFluid && !order.Contains(d.Key)) order.Add(d.Key);
        foreach (var k in order) if (Game.D.Mats.GetValueOrDefault(Blocks.Item(k)) > 0) yield return k;
    }
    internal IReadOnlyList<string> HotbarKeys => Bar;
    internal int HotbarSel => _barSel;
    internal float HotbarNameT => _nameT;

    /// <summary>The hero's eye, where block reach is measured from.</summary>
    internal Vector3 BlockEye => GlobalPosition + Vector3.Up * 1.5f;
    /// <summary>The block under the reticle within reach (BlockAim.Target: the camera ray through the screen centre, reach from the eye).</summary>
    (Vector3I b, Vector3I n)? Aim() => BlockAim.Target(GetViewport(), BlockEye, Blocks.F("world.reach", 6f)) is { } t ? (t.b, t.n) : null;
    /// <summary>This frame's build-mode target (block, face normal) for tests and captures; null when nothing is in reach.</summary>
    internal (Vector3I b, Vector3I n)? BlockTarget => BuildMode ? _bTarget : null;

    BuildHotbar? _bHud; CanvasLayer? _bLayer;
    /// <summary>The build-mode hotbar at the bottom centre (its own canvas layer above the HUD).</summary>
    void BlockHud()
    {
        if (_bHud == null || !IsInstanceValid(_bHud))
        {
            _bLayer = new CanvasLayer { Layer = 6 }; AddChild(_bLayer);
            _bHud = new BuildHotbar { P = this, MouseFilter = Control.MouseFilterEnum.Ignore };
            _bHud.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); _bLayer.AddChild(_bHud);
        }
        _bHud.Visible = BuildMode && BlockWorld.Enabled;
    }

    // ---------------------------------------------------------------- looks: the ghost block and the cracks
    void ShowGhost(Vector3I at, bool ok, string shape = "", byte meta = 0)
    {
        if (_ghost == null || !IsInstanceValid(_ghost))
        {
            _ghostMat = new StandardMaterial3D { Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = false };
            _ghost = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.01f, 1.01f, 1.01f) }, MaterialOverride = _ghostMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, TopLevel = true };
            AddChild(_ghost);
        }
        _ghost.Visible = true;
        var c = new Vector3(at.X + .5f, at.Y + .5f, at.Z + .5f); var sc = Vector3.One;
        if (shape == "slab") { sc = new Vector3(1, .5f, 1); c.Y += (meta & 4) != 0 ? .25f : -.25f; }
        else if (shape is "post" or "fence") { sc = new Vector3(.3f, shape == "post" ? .8f : 1f, .3f); c.Y -= shape == "post" ? .1f : 0; }
        _ghost.GlobalPosition = c; _ghost.Scale = sc;
        var col = _sel != "" ? Blocks.Tint(Blocks.Def(Blocks.Id(_sel))) : Colors.White;
        _ghostMat!.AlbedoColor = ok ? new Color(col.Lerp(Colors.White, .4f), .35f) : new Color(1f, .25f, .2f, .35f);
    }
    void ShowCrack(Vector3I b, float k)
    {
        _cracks ??= MakeCracks();
        if (_crack == null || !IsInstanceValid(_crack))
        {
            _crackMat = new StandardMaterial3D { Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(0, 0, 0, .85f), Uv1Triplanar = true, Uv1WorldTriplanar = true, TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest };
            _crack = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.02f, 1.02f, 1.02f) }, MaterialOverride = _crackMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, TopLevel = true };
            AddChild(_crack);
        }
        _crack.Visible = k > .02f; _crack.GlobalPosition = new Vector3(b.X + .5f, b.Y + .5f, b.Z + .5f);
        _crackMat!.AlbedoTexture = _cracks[Mathf.Clamp((int)(k * _cracks.Length), 0, _cracks.Length - 1)];
    }
    void HideBlockFx()
    {
        if (_ghost != null && IsInstanceValid(_ghost)) _ghost.Visible = false;
        if (_crack != null && IsInstanceValid(_crack)) _crack.Visible = false;
        if (_outline != null && IsInstanceValid(_outline)) _outline.Visible = false;
    }

    // the targeted block's outline: 12 thin edge bars (dark rims around pale cores, so it reads on light and dark blocks), unshaded, shared meshes
    Node3D? _outline; static BoxMesh[]? _edgeMeshes; static StandardMaterial3D? _edgeDark, _edgeLight;
    void ShowOutline(Vector3I b, byte id)
    {
        if (_outline == null || !IsInstanceValid(_outline))
        {
            const float L = 1.012f, Wd = .034f, Wl = .014f;   // a hair larger than the block, so the bars do not fight its faces
            _edgeMeshes ??= new[]
            {
                new BoxMesh { Size = new Vector3(L + Wd, Wd, Wd) }, new BoxMesh { Size = new Vector3(Wd, L + Wd, Wd) }, new BoxMesh { Size = new Vector3(Wd, Wd, L + Wd) },
                new BoxMesh { Size = new Vector3(L + Wl, Wl, Wl) }, new BoxMesh { Size = new Vector3(Wl, L + Wl, Wl) }, new BoxMesh { Size = new Vector3(Wl, Wl, L + Wl) },
            };
            _edgeDark ??= new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(.02f, .02f, .03f, .8f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha };
            _edgeLight ??= new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(.95f, .93f, .85f), RenderPriority = 1 };
            _outline = new Node3D { Name = "BlockOutline", TopLevel = true }; AddChild(_outline);
            float h = L / 2f;
            for (int axis = 0; axis < 3; axis++)
                foreach (var (u, v) in new[] { (-h, -h), (-h, h), (h, -h), (h, h) })
                {
                    var p = axis == 0 ? new Vector3(0, u, v) : axis == 1 ? new Vector3(u, 0, v) : new Vector3(u, v, 0);
                    // the pale core sits on the outer corner of the dark bar, so both show from any side
                    var po = p + new Vector3(axis == 0 ? 0 : Mathf.Sign(p.X) * .012f, axis == 1 ? 0 : Mathf.Sign(p.Y) * .012f, axis == 2 ? 0 : Mathf.Sign(p.Z) * .012f);
                    _outline.AddChild(new MeshInstance3D { Mesh = _edgeMeshes[axis], MaterialOverride = _edgeDark, Position = p, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
                    _outline.AddChild(new MeshInstance3D { Mesh = _edgeMeshes[3 + axis], MaterialOverride = _edgeLight, Position = po, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
                }
        }
        _outline.Visible = true;
        // slabs, posts and fences: the outline hugs the shape the ghost uses
        var c = new Vector3(b.X + .5f, b.Y + .5f, b.Z + .5f); var sc = Vector3.One;
        var a = BlockWorld.I?.Area; string shape = id != 0 && id != 255 ? Blocks.Def(id).Shape : "";
        if (shape == "slab" && a != null) { sc = new Vector3(1, .5f, 1); c.Y += (a.GetMeta(b.X, b.Y, b.Z) & 4) != 0 ? .25f : -.25f; }
        _outline.GlobalPosition = c; _outline.Scale = sc;
    }
    /// <summary>Five crack stages, drawn once: random walks from the centre, more and longer each stage.</summary>
    static ImageTexture[] MakeCracks()
    {
        var res = new ImageTexture[5]; var rnd = new Random(7); const int N = 32;
        var img = Image.CreateEmpty(N, N, false, Image.Format.Rgba8); img.Fill(new Color(0, 0, 0, 0));
        for (int s = 0; s < 5; s++)
        {
            for (int line = 0; line < 3; line++)
            {
                float x = N / 2f, y = N / 2f, ang = (float)rnd.NextDouble() * Mathf.Tau;
                for (int k = 0; k < 6 + s * 3; k++) { ang += (float)(rnd.NextDouble() - .5) * 1.2f; x += Mathf.Cos(ang) * 1.3f; y += Mathf.Sin(ang) * 1.3f; int ix = ((int)x % N + N) % N, iy = ((int)y % N + N) % N; img.SetPixel(ix, iy, new Color(0, 0, 0, 1)); }
            }
            res[s] = ImageTexture.CreateFromImage((Image)img.Duplicate());
        }
        return res;
    }
}

/// <summary>
/// The build-mode block hotbar: 9 gothic slots at the bottom centre, each with a small textured block (iso cube in the block's top and side
/// materials), its count and key; the chosen slot is gold-framed and raised, the block's name fades above it, the keys line above that.
/// </summary>
public partial class BuildHotbar : Control
{
    public Player? P;
    static readonly Color Gold = new(.96f, .78f, .36f);
    public override void _Process(double delta) { if (Visible) QueueRedraw(); }

    public override void _Draw()
    {
        var p = P; if (p == null || !IsInstanceValid(p)) return;
        var sz = Size; float u = sz.X / 100f;
        float s = Mathf.Clamp(u * 4.2f, 40, 76), gap = Mathf.Max(4, u * .45f), w = 9 * s + 8 * gap, x0 = (sz.X - w) / 2, y0 = sz.Y - s - Mathf.Max(16, u * 1.6f);
        var font = ThemeDB.FallbackFont; int fs = Mathf.Max(11, (int)(u * .95f));
        var keys = p.HotbarKeys; int sel = p.HotbarSel;
        // a dark iron strip behind the slots
        var strip = new Rect2(x0 - gap * 1.6f, y0 - gap * 1.6f, w + gap * 3.2f, s + gap * 3.2f);
        DrawRect(strip, new Color(.03f, .025f, .03f, .58f)); DrawRect(strip, UiTheme.AccentSoft with { A = .5f }, false, 1f);
        for (int i = 0; i < 9; i++)
        {
            bool on = i == sel; var r = new Rect2(x0 + i * (s + gap), y0, s, s);
            if (on) r = new Rect2(r.Position - new Vector2(s * .06f, s * .14f), r.Size + new Vector2(s * .12f, s * .12f));
            DrawRect(r, UiTheme.Panel with { A = on ? .86f : .7f });
            DrawRect(r.Grow(-3), new Color(0, 0, 0, .28f), false, 1f);   // inner bevel
            DrawRect(r, on ? Gold : UiTheme.AccentSoft, false, on ? 2.5f : 1f);
            if (on)
            {
                float L = Mathf.Min(12, r.Size.X * .24f);
                foreach (var (c, dx, dy) in new[] { (r.Position, 1f, 1f), (new Vector2(r.End.X, r.Position.Y), -1f, 1f), (new Vector2(r.Position.X, r.End.Y), 1f, -1f), (r.End, -1f, -1f) })
                { DrawLine(c, c + new Vector2(dx * L, 0), Gold.Lightened(.2f), 3f); DrawLine(c, c + new Vector2(0, dy * L), Gold.Lightened(.2f), 3f); }
            }
            string k = i < keys.Count ? keys[i] : "";
            if (k != "" && Blocks.Exists(k))
            {
                var d = Blocks.Def(Blocks.Id(k)); Icon(r.Grow(-r.Size.X * .17f), d);
                int n = Game.D.Mats.GetValueOrDefault(Blocks.Item(k));
                Txt(font, new Vector2(r.Position.X, r.End.Y - 4), n.ToString(), fs, Colors.White, HorizontalAlignment.Right, r.Size.X - 5);
            }
            Txt(font, r.Position + new Vector2(5, fs + 2), (i + 1).ToString(), Mathf.Max(10, fs - 2), on ? Gold : UiTheme.Dim, HorizontalAlignment.Left, -1);
        }
        // the chosen block's name, fading out; the keys and the pickaxe above
        string sk = sel < keys.Count ? keys[sel] : "";
        float nameY = y0 - s * .32f;
        if (p.HotbarNameT > 0 && sk != "" && Blocks.Exists(sk))
            Txt(font, new Vector2(0, nameY), Blocks.Def(Blocks.Id(sk)).Name, fs + 4, Gold with { A = Mathf.Clamp(p.HotbarNameT, 0, 1) }, HorizontalAlignment.Center, sz.X);
        string hint = $"{Blocks.PickName(Game.D.Pick)}   ·   hold {Inputs.KeyOf(Inputs.Attack)} dig   ·   {Inputs.KeyOf(Inputs.Heavy)} place   ·   1-9 / wheel choose   ·   {Inputs.KeyOf(Inputs.BlockRotate)} rotate   ·   {Inputs.KeyOf(Inputs.BuildMode)} leave";
        if (keys.All(x => x == "")) hint = "No blocks in your pack: dig some   ·   " + hint;
        Txt(font, new Vector2(0, nameY - fs * 1.9f), hint, fs, UiTheme.Dim.Lightened(.25f), HorizontalAlignment.Center, sz.X);
    }

    void Txt(Font f, Vector2 at, string t, int size, Color c, HorizontalAlignment al, float width)
    {
        DrawStringOutline(f, at, t, al, width, size, 5, new Color(0, 0, 0, c.A * .9f));
        DrawString(f, at, t, al, width, size, c);
    }

    static (Texture2D? tex, Color col) Look(BlockDef d, Blocks.Face face)
    {
        var m = Blocks.Material(d, face) as BaseMaterial3D; var col = m?.AlbedoColor ?? Blocks.Tint(d);
        return (m?.AlbedoTexture, new Color(col, Mathf.Max(.55f, col.A)));
    }
    /// <summary>A small isometric block in its own textures (slabs half height); torches and fences get their own little drawings.</summary>
    void Icon(Rect2 r, BlockDef d)
    {
        var c = r.GetCenter(); float h = r.Size.X * .5f;
        if (d.Shape == "post")
        {
            DrawRect(new Rect2(c.X - h * .1f, c.Y - h * .2f, h * .2f, h * 1.1f), new Color(.42f, .29f, .17f));
            DrawCircle(c + new Vector2(0, -h * .38f), h * .26f, new Color(1f, .55f, .18f, .9f));
            DrawCircle(c + new Vector2(0, -h * .45f), h * .14f, new Color(1f, .9f, .55f));
            return;
        }
        if (d.Shape == "fence")
        {
            var (_, fc) = Look(d, Blocks.Face.Side);
            foreach (float px in new[] { -.6f, .6f }) DrawRect(new Rect2(c.X + px * h - h * .13f, c.Y - h * .8f, h * .26f, h * 1.6f), fc.Darkened(.1f));
            foreach (float py in new[] { -.45f, .2f }) DrawRect(new Rect2(c.X - h * .8f, c.Y + py * h, h * 1.6f, h * .18f), fc);
            return;
        }
        if (d.Shape == "stairs") { Cube(c + new Vector2(0, h * .2f), h * .8f, .5f, d); Cube(c + new Vector2(-h * .25f, -h * .3f), h * .5f, 1f, d); return; }   // a slab with a step on the back
        Cube(c, h, d.Shape == "slab" ? .5f : 1f, d);
    }
    /// <summary>An isometric block centred at c (half-size h), k = height fraction (slabs .5).</summary>
    void Cube(Vector2 c, float h, float k, BlockDef d)
    {
        float drop = h * (1 - k);
        Vector2 top = c + new Vector2(0, -h + drop), rt = c + new Vector2(h * .87f, -h * .5f + drop), lt = c + new Vector2(-h * .87f, -h * .5f + drop), mid = c + new Vector2(0, drop);
        Vector2 bot = c + new Vector2(0, h), rb = c + new Vector2(h * .87f, h * .5f), lb = c + new Vector2(-h * .87f, h * .5f);
        var (tt, tc) = Look(d, Blocks.Face.Top); var (st, sc) = Look(d, Blocks.Face.Side);
        Face(new[] { top, rt, mid, lt }, tt, tc, 1f, 1f);
        Face(new[] { lt, mid, bot, lb }, st, sc, .74f, k);
        Face(new[] { mid, rt, rb, bot }, st, sc, .56f, k);
        var edge = new Color(0, 0, 0, .55f);
        DrawPolyline(new[] { top, rt, rb, bot, lb, lt, top }, edge, 1.2f); DrawLine(mid, lt, edge, 1f); DrawLine(mid, rt, edge, 1f); DrawLine(mid, bot, edge, 1f);
    }
    void Face(Vector2[] pts, Texture2D? tex, Color col, float shade, float vSpan)
    {
        var cc = new Color(col.R * shade, col.G * shade, col.B * shade, col.A); var cols = new[] { cc, cc, cc, cc };
        if (tex == null) { DrawPolygon(pts, cols); return; }
        float t = .3f; var uvs = new[] { new Vector2(0, 0), new Vector2(t, 0), new Vector2(t, t * vSpan), new Vector2(0, t * vSpan) };
        DrawPolygon(pts, cols, uvs, tex);
    }
}
