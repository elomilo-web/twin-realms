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
    public Color Col = new Color(1f, 0.6f, 0.2f); public float Size = 1f, Range = 12f; public bool Light = true;
    OmniLight3D? _l; float _ph;
    public override void _Ready()
    {
        _ph = GD.Randf() * 6f;
        var m = new StandardMaterial3D { AlbedoColor = Col, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.14f * Size, Height = 0.4f * Size, RadialSegments = 6, Rings = 3 }, MaterialOverride = m });
        var fx = new CpuParticles3D { Amount = 10, Lifetime = 0.6f, Direction = Vector3.Up, Spread = 15f, InitialVelocityMin = 0.6f, InitialVelocityMax = 1.3f, Gravity = new Vector3(0, 1.5f, 0), Emitting = true,
            Mesh = new SphereMesh { Radius = 0.06f * Size, Height = 0.12f * Size, RadialSegments = 4, Rings = 2, Material = m } };
        AddChild(fx);
        if (Light) { _l = new OmniLight3D { LightColor = Col, OmniRange = Range, LightEnergy = 1.4f, Position = new Vector3(0, 0.4f, 0) }; AddChild(_l); }
    }
    public override void _Process(double delta) { if (_l != null) _l.LightEnergy = 1.3f + Mathf.Sin(Game.PlayTime * 9f + _ph) * 0.2f + Mathf.Sin(Game.PlayTime * 23f + _ph) * 0.1f; }
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
        if (_open) return; _open = true; Game.D.Disc.Add(Id); Sfx.Play("coin");
        var tw = _lid.CreateTween(); tw.TweenProperty(_lid, "rotation:x", -1.9f, 0.4f);
        Loot.DropFrom(GlobalPosition + Vector3.Up * 0.3f, Level, 1f, Bonus, GD.RandRange(10, 30) + Level * 6, 0.5f);
        if (GD.Randf() < 0.45f) Loot.Drop(GlobalPosition + Vector3.Up * 0.3f, item: Items.Roll(Level, Bonus));
        Fx.Burst(GlobalPosition + Vector3.Up * 0.6f, new Color(1, 0.85f, 0.3f), 14, 3f, 0.6f, 0.08f);
        Game.Save();
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
    int _line;
    public string Prompt => "Talk to " + NpcName;
    public bool CanInteract => true;
    public override void _Ready()
    {
        AddToGroup("interact");
        var rg = new System.Random(Id.GetHashCode());
        BuildBody(rg);
        var shape = new StaticBody3D { CollisionLayer = 1 }; shape.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = .4f, Height = 1.8f }, Position = new Vector3(0, .9f, 0) }); AddChild(shape);
        AddChild(new Label3D { Text = NpcName, FontSize = 34, OutlineSize = 8, PixelSize = 0.005f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = new Vector3(0, 2.25f, 0), Modulate = new Color(1f, .95f, .7f),
            VisibilityRangeEnd = 24f, VisibilityRangeEndMargin = 4f, VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self });
        _phase = (float)rg.NextDouble() * 6f;
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
    }

    public override void _Process(double delta)
    {
        var p = Player.Instance; if (p == null) return; _t += (float)delta;
        var d = p.GlobalPosition - GlobalPosition; if (d.Length() < 8f) Rotation = new Vector3(0, Mathf.LerpAngle(Rotation.Y, Util.YawTo(d.X, d.Z), 0.08f), 0);
        if (d.Length() > 40f) return;
        // idle life: breathing, a slow weight shift, arms drifting
        float b = Mathf.Sin(_t * 1.6f + _phase), w = Mathf.Sin(_t * .45f + _phase);
        _upper.Position = new Vector3(0, .86f + b * .008f, 0); _upper.Rotation = new Vector3(b * .015f, 0, w * .035f);
        _armL.Rotation = new Vector3(Mathf.Sin(_t * .7f + _phase) * .06f, 0, -.12f - b * .02f); _armR.Rotation = new Vector3(Mathf.Sin(_t * .8f + _phase + 1) * .05f, 0, .12f + b * .02f);
    }
    public string NextLine() { if (Lines.Length == 0) return "..."; return Lines[_line++ % Lines.Length]; }
    public void Interact() { Sfx.Play("click"); Ui.OpenDialogue(this); }
}
