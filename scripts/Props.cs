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
        var cloth = World.Mat(Color.FromHsv((float)rg.NextDouble(), 0.45f, 0.55f)); var skin = World.Mat(new Color(.85f, .7f, .55f));
        AddChild(new MeshInstance3D { Mesh = new CapsuleMesh { Radius = .34f, Height = 1.4f }, MaterialOverride = cloth, Position = new Vector3(0, 0.95f, 0) });
        AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = .2f, Height = .4f }, MaterialOverride = skin, Position = new Vector3(0, 1.8f, 0) });
        if (Role == "arena") AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(.5f, .25f, .5f) }, MaterialOverride = World.Mat(new Color(.6f, .1f, .1f)), Position = new Vector3(0, 2.08f, 0) });
        var shape = new StaticBody3D { CollisionLayer = 1 }; shape.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = .4f, Height = 1.8f }, Position = new Vector3(0, .9f, 0) }); AddChild(shape);
        AddChild(new Label3D { Text = NpcName, FontSize = 36, OutlineSize = 8, PixelSize = 0.006f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = new Vector3(0, 2.4f, 0), Modulate = new Color(1f, .95f, .7f), NoDepthTest = true });
    }
    public override void _Process(double delta)
    {
        var p = Player.Instance; if (p == null) return;
        var d = p.GlobalPosition - GlobalPosition; if (d.Length() < 8f) Rotation = new Vector3(0, Mathf.LerpAngle(Rotation.Y, Util.YawTo(d.X, d.Z), 0.08f), 0);
    }
    public string NextLine() { if (Lines.Length == 0) return "..."; return Lines[_line++ % Lines.Length]; }
    public void Interact() { Sfx.Play("click"); Ui.OpenDialogue(this); }
}
