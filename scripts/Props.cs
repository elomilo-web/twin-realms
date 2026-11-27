using Godot;

namespace TwinRealms;

/// <summary>Rotates a node continuously (windmill blades, orbs).</summary>
public partial class Spinner : Node3D
{
    public Vector3 Axis = Vector3.Up; public float Speed = 1f;
    public override void _Process(double delta) { Rotate(Axis, Speed * (float)delta); }
}

/// <summary>Shows its node only while the player is in the given realm (0 light, 1 shadow).</summary>
public partial class RealmGate : Node3D
{
    public int Only = 1;
    public override void _Process(double delta) { Visible = Only == 1 ? World.RealmT > 0.5f : World.RealmT < 0.5f; }
}

/// <summary>Flickering torch / brazier.</summary>
public partial class Torch : Node3D
{
    public Color Col = new Color(1f, 0.6f, 0.2f); public float Size = 1f, Range = 12f, Energy = 1.4f; public bool Light = true;
    /// <summary>Block torches (BlockWorld.LightTick): the light's colour wavers with its energy (dimmer = redder); Flames off = light only, no particles.</summary>
    public bool HueFlicker, Flames = true;
    OmniLight3D? _l; float _ph; readonly System.Collections.Generic.List<CpuParticles3D> _fx = new();
    /// <summary>Turns the flame and smoke particles on or off (the nearest torches keep theirs).</summary>
    public void SetFlames(bool on) { Flames = on; foreach (var p in _fx) if (IsInstanceValid(p)) { p.Emitting = on; p.Visible = on; } }
    /// <summary>A campfire prop (a big flame low over the ground, not a block light): you can cook at it (Cooking.FireNear, F-24).</summary>
    public bool IsCampfire => !HueFlicker && Size >= 1.1f && (GlobalPosition.Y - World.GroundAt(GlobalPosition.X, GlobalPosition.Z) < 1f || GlobalPosition.Y - World.OnGround(GlobalPosition.X, GlobalPosition.Z).Y < 1f);
    public override void _Ready()
    {
        _ph = GD.Randf() * 6f; AddToGroup("torches");
        var m = new StandardMaterial3D { AlbedoColor = Col, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.08f * Size, Height = 0.16f * Size, RadialSegments = 6, Rings = 3 }, MaterialOverride = m });   // the hot ember core under the flames
        // licking flames (Kenney flame sprites) over the glowing core, with a wisp of smoke above
        var g = Vfx.Ramp((0, new Color(1f, .95f, .6f, 0)), (.12f, new Color(1f, .78f, .32f, .95f)), (.45f, new Color(1f, .5f, .12f, .85f)), (1, new Color(1f, .3f, .05f, 0)));
        var sc = new Curve(); sc.AddPoint(new Vector2(0, .6f)); sc.AddPoint(new Vector2(.3f, 1f)); sc.AddPoint(new Vector2(1, .2f));
        var fx = new CpuParticles3D { Amount = 12, Lifetime = 0.6f, Direction = Vector3.Up, Spread = 12f, InitialVelocityMin = 0.5f, InitialVelocityMax = 1.1f, Gravity = new Vector3(0, 1.5f, 0), Emitting = true,
            Mesh = new QuadMesh { Size = new Vector2(.42f * Size, .42f * Size), Material = Vfx.SpriteMat(false, "fire") }, ColorRamp = g, ScaleAmountCurve = sc, EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere, EmissionSphereRadius = .05f * Size };
        Vfx.Spin(fx, "fire", 60f); AddChild(fx);
        var sg = Vfx.Ramp((0, new Color(.85f, .82f, .78f, 0)), (.3f, new Color(.9f, .88f, .85f, .12f)), (1, new Color(1f, 1f, 1f, 0)));   // pale wisps (the textures are mid-grey, so the colour runs light)
        var smoke = new CpuParticles3D { Amount = 5, Lifetime = 2.2f, Direction = Vector3.Up, Spread = 10f, InitialVelocityMin = .4f, InitialVelocityMax = .7f, Gravity = new Vector3(.15f, .3f, 0), Emitting = true, Position = new Vector3(0, .4f * Size, 0),
            Mesh = new QuadMesh { Size = new Vector2(.6f * Size, .6f * Size), Material = Vfx.SpriteMat(false, "smoke") }, ColorRamp = sg, ScaleAmountMin = .7f, ScaleAmountMax = 1.4f };
        Vfx.Spin(smoke, "smoke", 30f); AddChild(smoke);
        _fx.Add(fx); _fx.Add(smoke); if (!Flames) SetFlames(false);
        if (Light) { _l = new OmniLight3D { LightColor = Col, OmniRange = Range, LightEnergy = Energy, Position = new Vector3(0, 0.4f * (HueFlicker ? .4f : 1f), 0), LightVolumetricFogEnergy = 2.5f, DistanceFadeEnabled = true, DistanceFadeBegin = 60f, DistanceFadeLength = 15f }; AddChild(_l); }
    }
    static readonly Color Ember = new(1f, .42f, .12f);
    public override void _Process(double delta)
    {
        if (_l == null) return;
        // shared time-based noise (three sines per torch, phase-shifted): no per-torch state beyond the phase
        float t = Game.PlayTime, f = Mathf.Sin(t * 9f + _ph) * 0.14f + Mathf.Sin(t * 23f + _ph) * 0.07f + (HueFlicker ? Mathf.Sin(t * 3.1f + _ph * 2f) * .05f : 0f);
        _l.LightEnergy = Energy * (.93f + f);
        if (HueFlicker) _l.LightColor = Col.Lerp(Ember, Mathf.Clamp(.25f - f * 1.6f, 0f, .6f));
    }
}

public partial class Chest : Node3D, IInteractable
{
    public string Id = ""; public int Level = 1; public float Bonus = 1f; bool _open; Node3D _lid = null!;
    public string Prompt => "Open chest";
    public bool CanInteract => !_open;
    public override void _Ready()
    {
        _open = Game.D.Disc.Contains(Id);
        var wood = World.Mat(new Color(.42f, .29f, .13f)); var gold = World.Mat(new Color(.9f, .7f, .25f), null, .4f, .8f);
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(.95f, .5f, .65f) }, MaterialOverride = wood, Position = new Vector3(0, .25f, 0) });
        _lid = new Node3D { Position = new Vector3(0, .5f, -.32f) }; AddChild(_lid);
        _lid.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(.95f, .22f, .65f) }, MaterialOverride = wood, Position = new Vector3(0, .1f, .32f) });
        _lid.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(.14f, .14f, .08f) }, MaterialOverride = gold, Position = new Vector3(0, .02f, .66f) });
        if (_open) _lid.Rotation = new Vector3(-1.9f, 0, 0);
        AddToGroup("interact");
    }
    public void Interact()
    {
        if (_open) return; _open = true; Game.D.Disc.Add(Id); Sfx.Play("click"); Sfx.Play("coin");
        var tw = _lid.CreateTween(); tw.TweenProperty(_lid, "rotation:x", -1.9f, 0.45f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        OpenFx();
        int lvl = Dungeon.Active ? Level : Progression.OverworldLevel(Level, false);   // overworld chests keep up with the heroes, like the monsters
        var top = GlobalPosition + Vector3.Up * 0.3f;
        Loot.Pop(top, () =>
        {
            Loot.DropFrom(top, lvl, 1f, Bonus, GD.RandRange(10, 30) + lvl * 6, 0.5f);
            if (GD.Randf() < 0.45f) Loot.Drop(top, item: LootTables.Roll(lvl, Bonus));
        });
        Spellcraft.OnChest(GlobalPosition);   // chests may hold a spell card
        Fx.Burst(GlobalPosition + Vector3.Up * 0.6f, new Color(1, 0.85f, 0.3f), 14, 3f, 0.6f, 0.08f);
        Game.Save();
    }

    /// <summary>Golden light spilling out, dust motes and glints rising.</summary>
    void OpenFx()
    {
        var gold = new Color(1f, .8f, .4f);
        var l = new OmniLight3D { LightColor = gold, OmniRange = 5f, LightEnergy = 0f, ShadowEnabled = false, Position = new Vector3(0, .6f, 0) };
        AddChild(l);
        var tw = l.CreateTween(); tw.TweenProperty(l, "light_energy", 2.5f, .3f); tw.TweenProperty(l, "light_energy", 0f, .7f); tw.TweenCallback(Callable.From(l.QueueFree));
        var m = new StandardMaterial3D { AlbedoColor = gold, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add, VertexColorUseAsAlbedo = true, BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled };
        var p = new CpuParticles3D
        {
            Amount = 20, Lifetime = 1.6f, OneShot = true, Explosiveness = .7f, Emitting = true, Position = new Vector3(0, .55f, 0),
            Direction = Vector3.Up, Spread = 35f, InitialVelocityMin = .5f, InitialVelocityMax = 1.6f, Gravity = new Vector3(0, .25f, 0),
            ScaleAmountMin = .6f, ScaleAmountMax = 1.4f, Mesh = new QuadMesh { Size = new Vector2(.05f, .05f), Material = m },
            ColorRamp = Vfx.Ramp((0, new Color(1f, .9f, .55f, 0)), (.2f, new Color(1f, .9f, .55f, .9f)), (1, new Color(1f, .8f, .4f, 0)))
        };
        AddChild(p);
        GetTree().CreateTimer(2.2f).Timeout += () => { if (IsInstanceValid(p)) p.QueueFree(); };
    }
}

/// <summary>A dungeon mouth. Press E to enter.</summary>
public partial class Door : Node3D, IInteractable
{
    public string Key = ""; public string Title = "";
    public string Prompt => "Enter " + Title;
    public bool CanInteract => !Dungeon.Active && World.Realm == 0;
    public override void _Ready() { AddToGroup("interact"); }
    public void Interact() { Dungeon.Enter(Key, 1); }
}

/// <summary>Rift obelisk: restores you and records a checkpoint.</summary>
public partial class Obelisk : Node3D, IInteractable
{
    public string Prompt => "Touch the obelisk";
    public bool CanInteract => World.RealmT > 0.5f;
    public override void _Ready() { AddToGroup("interact"); }
    public void Interact()
    {
        var p = Player.Instance; if (p == null) return;
        p.Hp = Game.D.MaxHp(); p.Mp = Game.D.MaxMp(); p.St = Game.D.MaxSt(); Sfx.Play("heal");
        Fx.Ring(GlobalPosition + Vector3.Up * 0.2f, 5f, new Color(.7f, .4f, 1f), 0.6f); Ui.Note("The obelisk restores you", new Color(.8f, .6f, 1f));
        Game.D.PosX = GlobalPosition.X; Game.D.PosZ = GlobalPosition.Z + 2f; Game.Save();
    }
}

public partial class Npc : Node3D, IInteractable
{
    public string Id = "", NpcName = "", Role = "talk"; public string[] Lines = new string[0]; public string[] QuestIds = new string[0];
    public string Outfit = "", Sex = ""; public Color? Tint;   // F-27 (data/npcs.json): data/avatar.json "npcs" outfit role, "f"/"m", outfit colour
    int _line;
    public string Prompt => "Talk to " + NpcName;
    public bool CanInteract => true;
    public override void _Ready()
    {
        AddToGroup("interact");
        var rg = new System.Random(Id.GetHashCode());
        if (Avatar.Available) BuildAvatar(rg); else BuildBody(rg);
        var shape = new StaticBody3D { CollisionLayer = 1 }; shape.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = .4f, Height = 1.8f }, Position = new Vector3(0, .9f, 0) }); AddChild(shape);
        AddChild(new Label3D { Text = NpcName, FontSize = 34, OutlineSize = 8, PixelSize = 0.005f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = new Vector3(0, 2.25f, 0), Modulate = new Color(1f, .95f, .7f),
            VisibilityRangeEnd = 24f, VisibilityRangeEndMargin = 4f, VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self });
        _phase = (float)rg.NextDouble() * 6f;
    }

    // ---- villager as a character model: outfit and idle by role (data/avatar.json "npcs"), body and colours from the id
    Avatar? _av;
    void BuildAvatar(System.Random rg)
    {
        string role = Outfit != "" && Avatar.Npc(Outfit) != null ? Outfit : NpcName.StartsWith("Guard") ? "guard" : Role;
        var skins = new[] { new Color(.96f, .82f, .7f), new Color(.86f, .66f, .5f), new Color(.62f, .44f, .32f), new Color(.98f, .86f, .78f) };
        var hairs = new[] { new Color(.15f, .1f, .06f), new Color(.45f, .3f, .15f), new Color(.8f, .65f, .38f), new Color(.68f, .68f, .7f), new Color(.55f, .22f, .1f) };
        bool female = Sex != "" ? Sex == "f" : role != "guard" && role != "arena" && role != "smith" && rg.Next(2) == 0;
        var ap = new Appearance { Female = female, Skin = skins[rg.Next(skins.Length)], Hair = hairs[Id == "maren" ? 3 : rg.Next(hairs.Length)], Beard = !female && rg.Next(4) == 0 };
        _av = new Avatar { Name = "Avatar" }; AddChild(_av); _av.Build(ap, Id + NpcName);
        var cfg = Avatar.Npc(role) ?? Avatar.Npc("talk");
        Color? tint = Tint ?? (cfg is { } c && Avatar.Str(c, "tint") != "" ? Color.FromHtml(Avatar.Str(c, "tint")) : Color.FromHsv((float)rg.NextDouble(), .35f, .95f));
        string wt = cfg is { } c2 ? Avatar.Str(c2, "weapon") : "";
        _av.Dress(cfg is { } c3 ? Avatar.Outfit(c3) : _ => null, wt == "" ? "none" : wt, tint.Value, tint);
        _idle = cfg is { } c4 ? Avatar.Str(c4, "idle", "idle") : "idle"; _av.Play(_idle);
    }

    // ---- villager body: legs, tunic, belt, arms with hands, head with hair, eyes and nose, plus gear by role. Upper body sways while idle.
    Node3D _upper = null!, _armL = null!, _armR = null!; float _phase, _t;
    static MeshInstance3D Part(Node3D p, Mesh m, Material mat, Vector3 pos, Vector3? rot = null) { var mi = new MeshInstance3D { Mesh = m, MaterialOverride = mat, Position = pos, Rotation = rot ?? Vector3.Zero }; p.AddChild(mi); return mi; }
    void BuildBody(System.Random rg)
    {
        float hue = (float)rg.NextDouble(); Color C(float h, float s, float v) => Color.FromHsv(h % 1f, s, v);
        var tunic = World.Mat(C(hue, .45f, .55f)); Gfx.Tex(tunic, "cloth", .6f); var trim = World.Mat(C(hue + .08f, .5f, .35f));
        var pants = World.Mat(C(hue + .5f, .25f, .28f)); var boots = World.Mat(new Color(.22f, .15f, .1f)); var belt = World.Mat(new Color(.3f, .2f, .12f));
        var skinC = new[] { new Color(.92f, .76f, .62f), new Color(.8f, .6f, .45f), new Color(.6f, .42f, .3f), new Color(.95f, .8f, .7f) }[rg.Next(4)];
        var skin = World.Mat(skinC); var hairC = new[] { new Color(.15f, .1f, .06f), new Color(.45f, .3f, .15f), new Color(.75f, .6f, .35f), new Color(.6f, .6f, .6f), new Color(.5f, .2f, .1f) }[Role == "quest" && Id == "maren" ? 3 : rg.Next(5)];
        var hair = World.Mat(hairC); var eye = World.Mat(new Color(.05f, .04f, .04f));
        foreach (float s in new[] { -1f, 1f })
        {
            Part(this, new CylinderMesh { TopRadius = .1f, BottomRadius = .085f, Height = .78f, RadialSegments = 8 }, pants, new Vector3(s * .13f, .47f, 0));
            Part(this, new BoxMesh { Size = new Vector3(.17f, .12f, .3f) }, boots, new Vector3(s * .13f, .06f, -.05f));
        }
        _upper = new Node3D { Position = new Vector3(0, .86f, 0) }; AddChild(_upper);
        bool robe = (Role == "quest" && !NpcName.StartsWith("Guard")) || Role == "talk" && rg.Next(2) == 0;
        Part(_upper, new CylinderMesh { TopRadius = .25f, BottomRadius = robe ? .36f : .3f, Height = robe ? .95f : .7f, RadialSegments = 10 }, tunic, new Vector3(0, robe ? .2f : .32f, 0));
        Part(_upper, new CylinderMesh { TopRadius = .2f, BottomRadius = .26f, Height = .3f, RadialSegments = 10 }, tunic, new Vector3(0, .8f, 0));
        Part(_upper, new SphereMesh { Radius = .2f, Height = .22f, RadialSegments = 10, Rings = 4 }, tunic, new Vector3(0, .94f, 0));
        Part(_upper, new CylinderMesh { TopRadius = .275f, BottomRadius = .285f, Height = .07f, RadialSegments = 10 }, belt, new Vector3(0, .55f, 0));
        Part(_upper, new BoxMesh { Size = new Vector3(.08f, .07f, .03f) }, World.Mat(new Color(.85f, .7f, .3f), null, .4f, .7f), new Vector3(0, .55f, -.29f));
        Part(_upper, new CylinderMesh { TopRadius = .06f, BottomRadius = .07f, Height = .12f, RadialSegments = 8 }, skin, new Vector3(0, 1.02f, 0));
        var head = new Node3D { Position = new Vector3(0, 1.22f, 0) }; _upper.AddChild(head);
        Part(head, new SphereMesh { Radius = .17f, Height = .38f, RadialSegments = 12, Rings = 6 }, skin, Vector3.Zero);
        Part(head, new SphereMesh { Radius = .185f, Height = .25f, RadialSegments = 12, Rings = 5, IsHemisphere = true }, hair, new Vector3(0, .03f, .02f), new Vector3(-.25f, 0, 0));
        foreach (float s in new[] { -1f, 1f }) Part(head, new SphereMesh { Radius = .022f, Height = .044f, RadialSegments = 6, Rings = 3 }, eye, new Vector3(s * .065f, .02f, -.155f));
        Part(head, new PrismMesh { Size = new Vector3(.05f, .07f, .06f) }, skin, new Vector3(0, -.03f, -.17f), new Vector3(-Mathf.Pi / 2, 0, 0));
        if (Id == "maren" || Role == "quest" && rg.Next(2) == 0) Part(head, new CylinderMesh { TopRadius = .12f, BottomRadius = .02f, Height = .3f, RadialSegments = 8 }, hair, new Vector3(0, -.2f, -.1f), new Vector3(.3f, 0, 0));
        // arms hang from shoulder pivots
        Node3D Arm(float s) { var a = new Node3D { Position = new Vector3(s * .29f, .9f, 0), Rotation = new Vector3(0, 0, s * .12f) }; _upper.AddChild(a);
            Part(a, new CapsuleMesh { Radius = .07f, Height = .62f, RadialSegments = 8, Rings = 2 }, tunic, new Vector3(0, -.28f, 0));
            Part(a, new SphereMesh { Radius = .065f, Height = .13f, RadialSegments = 8, Rings = 4 }, skin, new Vector3(0, -.6f, 0)); return a; }
        _armL = Arm(-1); _armR = Arm(1);
        var wood = World.Mat(new Color(.4f, .28f, .16f)); var steel = World.Mat(new Color(.7f, .72f, .76f), null, .35f, .8f);
        switch (NpcName.StartsWith("Guard") ? "guard" : Role)
        {
            case "shop": Part(_upper, new CylinderMesh { TopRadius = .27f, BottomRadius = .33f, Height = .55f, RadialSegments = 10 }, World.Mat(new Color(.9f, .86f, .76f)), new Vector3(0, .26f, -.03f)); break;
            case "smith":
                Part(_upper, new CylinderMesh { TopRadius = .28f, BottomRadius = .34f, Height = .75f, RadialSegments = 10 }, World.Mat(new Color(.35f, .22f, .13f)), new Vector3(0, .35f, -.02f));
                Part(_armR, new CylinderMesh { TopRadius = .025f, BottomRadius = .025f, Height = .45f }, wood, new Vector3(0, -.65f, -.15f), new Vector3(Mathf.Pi / 2, 0, 0));
                Part(_armR, new BoxMesh { Size = new Vector3(.1f, .1f, .2f) }, steel, new Vector3(0, -.65f, -.38f)); break;
            case "arena":
            case "guard":
                Part(head, new SphereMesh { Radius = .2f, Height = .24f, RadialSegments = 12, Rings = 5, IsHemisphere = true }, steel, new Vector3(0, .02f, 0));
                Part(_upper, new CylinderMesh { TopRadius = .27f, BottomRadius = .31f, Height = .5f, RadialSegments = 10 }, steel, new Vector3(0, .7f, 0));
                Part(_armR, new CylinderMesh { TopRadius = .022f, BottomRadius = .022f, Height = 2.1f }, wood, new Vector3(0, -.45f, -.05f));
                Part(_armR, new CylinderMesh { TopRadius = 0f, BottomRadius = .05f, Height = .25f, RadialSegments = 6 }, steel, new Vector3(0, .72f, -.05f)); break;
            case "quest": Part(_armR, new CylinderMesh { TopRadius = .025f, BottomRadius = .03f, Height = 1.7f }, wood, new Vector3(0, -.15f, -.12f), new Vector3(.08f, 0, 0)); break;
        }
        foreach (var n in new[] { this, _upper, head, _armL, _armR }) Gfx.MergeChildren(n);
    }

    // ---- walking (focus F-14/F-16; roaming schedules come with F-29): WalkTo a point; the villager follows the block surface up stairs and
    // slabs without jumping, opens unlocked doors in its way and closes them again once past. Blocked (a wall, a locked door): it stops.
    public Vector3? Goal; public float WalkSpeed = 1.6f; public string Stopped = "";
    readonly System.Collections.Generic.List<Vector3I> _opened = new(); bool _walking; float _groundY = float.NaN; string _idle = "idle";
    public void WalkTo(Vector3 to) { Goal = to; Stopped = ""; _groundY = GlobalPosition.Y; }
    void WalkTick(float dt)
    {
        var a = BlockWorld.I?.Area;
        for (int i = _opened.Count - 1; i >= 0; i--)   // close the doors we came through once we are clear of them
        {
            var dc = _opened[i];
            if (new Vector2(GlobalPosition.X - dc.X - .5f, GlobalPosition.Z - dc.Z - .5f).Length() < 1.3f) continue;
            if (a != null) BlockUse.SetDoor(a, dc.X, dc.Y, dc.Z, false);
            _opened.RemoveAt(i);
        }
        if (Goal is not { } g) { if (_walking) { _walking = false; _av?.Play(_idle); } return; }
        var to = g - GlobalPosition; to.Y = 0; float left = to.Length();
        if (left < .12f) { Goal = null; return; }
        var dir = to / left;
        if (a != null && BlockUse.DoorAhead(GlobalPosition, dir) is { } door)
        {
            if (!BlockUse.MayOpen(a, door, "", false)) { Goal = null; Stopped = "locked door"; return; }   // villagers never open a locked door
            BlockUse.SetDoor(a, door.X, door.Y, door.Z, true); if (!_opened.Contains(door)) _opened.Add(door);
        }
        if (float.IsNaN(_groundY)) _groundY = GlobalPosition.Y;
        var next = GlobalPosition + dir * Mathf.Min(WalkSpeed * dt, left);
        if (BlockStep.GroundY(next.X, next.Z, _groundY) is not float gy) { Goal = null; Stopped = "blocked"; return; }
        _groundY = gy;
        float y = Mathf.MoveToward(GlobalPosition.Y, gy, (gy > GlobalPosition.Y ? 3.2f : 6f) * dt);   // a half step eases up over ~.15 s
        GlobalPosition = new Vector3(next.X, y, next.Z);
        Rotation = new Vector3(0, Mathf.LerpAngle(Rotation.Y, Util.YawTo(dir.X, dir.Z), 1f - Mathf.Exp(-10f * dt)), 0);
        if (!_walking) { _walking = true; _av?.Play("walk"); }
    }

    // ---- F-29 / F-30: gestures and speech bubbles. FaceAt: who it is talking to (else it turns to a hero close by). Emote plays a clip of
    // data/avatar.json "clips" for a while, then the idle again. Say shows a short line above the head: it fades in and out, fades with
    // distance (gone past BubbleFar) and when the camera is right on top of it, and is hidden by walls, so it never covers the view.
    public Vector3? FaceAt; float _emoteT; Label3D? _bub; float _bubT, _bubDur;
    public const float BubbleNear = 9f, BubbleFar = 18f;
    public bool Speaking => _bub != null && _bub.Visible;
    public void Emote(string clip, float secs) { if (_av == null || _walking) return; _av.Play(clip); _emoteT = secs; }
    public void Say(string text, float secs = 3.6f)
    {
        if (text == "") return;
        if (_bub == null)
        {
            _bub = new Label3D { FontSize = 28, OutlineSize = 9, PixelSize = .005f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = new Vector3(0, 2.6f, 0),
                AutowrapMode = TextServer.AutowrapMode.WordSmart, Width = 400, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center };
            AddChild(_bub);
        }
        _bub.Text = text; _bubT = 0; _bubDur = secs; _bub.Visible = true; BubbleTick(0);
    }
    void BubbleTick(float dt)
    {
        if (_bub == null || !_bub.Visible) return; _bubT += dt;
        float a = Mathf.Clamp(_bubT / .25f, 0, 1) * Mathf.Clamp((_bubDur - _bubT) / .5f, 0, 1);
        if (GetViewport()?.GetCamera3D() is { } cam)
        {
            float dist = cam.GlobalPosition.DistanceTo(GlobalPosition + Vector3.Up * 2.4f);
            a *= Mathf.Clamp((BubbleFar - dist) / (BubbleFar - BubbleNear), 0, 1) * Mathf.Clamp((dist - .8f) / 1.2f, 0, 1);
        }
        if (_bubT >= _bubDur) { _bub.Visible = false; return; }
        _bub.Modulate = new Color(.97f, .95f, .88f, a); _bub.OutlineModulate = new Color(.05f, .04f, .03f, a * .9f);
    }

    public override void _Process(double delta)
    {
        WalkTick((float)delta); BubbleTick((float)delta);
        if (_emoteT > 0 && (_emoteT -= (float)delta) <= 0 && !_walking) _av?.Play(_idle);
        var p = Player.Instance; if (p == null) return; _t += (float)delta;
        var d = p.GlobalPosition - GlobalPosition;
        if (FaceAt is { } fa) { var fd = fa - GlobalPosition; if (!_walking && new Vector2(fd.X, fd.Z).LengthSquared() > .01f) Rotation = new Vector3(0, Mathf.LerpAngle(Rotation.Y, Util.YawTo(fd.X, fd.Z), 0.12f), 0); }
        else if (d.Length() < 8f && !_walking) Rotation = new Vector3(0, Mathf.LerpAngle(Rotation.Y, Util.YawTo(d.X, d.Z), 0.08f), 0);
        if (d.Length() > 40f || _av != null) return;   // the model breathes through its idle clip
        // idle life: breathing, a slow weight shift, arms drifting
        float b = Mathf.Sin(_t * 1.6f + _phase), w = Mathf.Sin(_t * .45f + _phase);
        _upper.Position = new Vector3(0, .86f + b * .008f, 0); _upper.Rotation = new Vector3(b * .015f, 0, w * .035f);
        _armL.Rotation = new Vector3(Mathf.Sin(_t * .7f + _phase) * .06f, 0, -.12f - b * .02f); _armR.Rotation = new Vector3(Mathf.Sin(_t * .8f + _phase + 1) * .05f, 0, .12f + b * .02f);
    }
    public string NextLine() { if (Lines.Length == 0) return "..."; return Lines[_line++ % Lines.Length]; }
    public void Interact() { Sfx.Play("click"); Ui.OpenDialogue(this); }
}
