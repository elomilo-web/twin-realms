using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// A plank boat (F-21). Crafted in the Blocks tab (data/blocks.json "items".boat: 5 planks, kept in PlayerStats.Mats["boat"]), set on
/// water with Interact while looking at open water, boarded with Interact, steered with W/S (throttle) and A/D (turn) or the left stick,
/// left with Interact (onto the nearest land, else into the water), and picked back up by hitting it (attack) while nobody is in it.
/// It floats on block water and on the smooth lakes alike (Water.cs) and stops where the water ends (Water.Floatable at the bow).
/// Made of boxes in the block planks look: a slab floor, plank walls, a pointed bow and a seat slab.
/// </summary>
public partial class Boat : Node3D, IInteractable
{
    public static readonly List<Boat> All = new();
    public Player? Rider;
    float _speed, _yaw, _surf, _t, _turn, _bumpCd;
    public float Speed => _speed;

    public string Prompt => $"Board boat   [{Inputs.KeyOf(Inputs.Attack)}] Pick up";
    public bool CanInteract => Rider == null && Player.Instance is { InBoat: null, Dead: false };
    public void Interact() => Player.Instance?.BoardBoat(this);
    /// <summary>Where the rider's feet go (the seated pose puts the hips on the seat slab).</summary>
    public Vector3 Seat => GlobalPosition + Vector3.Up * .02f + Back * .15f;
    Vector3 Fwd => new(-Mathf.Sin(_yaw), 0, -Mathf.Cos(_yaw));
    Vector3 Back => -Fwd;
    public float Yaw => _yaw;

    /// <summary>Puts a boat on the water at (x, z), facing yaw.</summary>
    public static Boat Place(Vector3 at, float yaw)
    {
        var b = new Boat { _yaw = yaw }; Game.Root!.AddChild(b);
        b._surf = Water.SurfaceAt(at) ?? at.Y; b.GlobalPosition = new Vector3(at.X, b._surf - .12f, at.Z); b.Rotation = new Vector3(0, yaw, 0);
        return b;
    }

    static StandardMaterial3D? _wood, _dark;
    public override void _Ready()
    {
        AddToGroup("interact"); All.Add(this); BuildModel(this);
    }
    /// <summary>The boat's boxes under a node (also a friend's boat in multiplayer, RemoteHero).</summary>
    public static void BuildModel(Node3D root)
    {
        _wood ??= Gfx.Tex(World.Mat(new Color(.62f, .47f, .3f), new Color(.22f, .17f, .2f)), "planks", .6f);
        _dark ??= Gfx.Tex(World.Mat(new Color(.42f, .3f, .18f), new Color(.16f, .12f, .15f)), "planks", .6f);
        void Box(Vector3 size, Vector3 pos, StandardMaterial3D m, float rotY = 0) =>
            root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = m, Position = pos, Rotation = new Vector3(0, rotY, 0) });
        // length along Z (-Z is the bow): 2.4 m long, 1.2 m wide, walls .5 m high
        Box(new Vector3(1.1f, .14f, 2.1f), new Vector3(0, .07f, .1f), _dark);                       // the slab floor
        Box(new Vector3(.12f, .5f, 2.2f), new Vector3(-.6f, .3f, .1f), _wood);                      // side planks
        Box(new Vector3(.12f, .5f, 2.2f), new Vector3(.6f, .3f, .1f), _wood);
        Box(new Vector3(1.32f, .5f, .12f), new Vector3(0, .3f, 1.2f), _wood);                       // stern
        Box(new Vector3(.12f, .5f, .9f), new Vector3(-.3f, .32f, -1.2f), _wood, -.6f);              // the bow: two planks meeting at a point
        Box(new Vector3(.12f, .5f, .9f), new Vector3(.3f, .32f, -1.2f), _wood, .6f);
        Box(new Vector3(.3f, .56f, .3f), new Vector3(0, .34f, -1.55f), _dark, Mathf.Pi / 4);       // the stem post
        Box(new Vector3(1.1f, .1f, .36f), new Vector3(0, .32f, .45f), _dark);                       // seat slab
        Box(new Vector3(1.1f, .1f, .3f), new Vector3(0, .32f, -.55f), _dark);                       // bow slab
        Box(new Vector3(1.36f, .08f, .16f), new Vector3(0, .58f, 1.2f), _dark);                     // gunwale caps
        Box(new Vector3(.16f, .08f, 2.2f), new Vector3(-.62f, .58f, .1f), _dark);
        Box(new Vector3(.16f, .08f, 2.2f), new Vector3(.62f, .58f, .1f), _dark);
    }
    public override void _ExitTree() { All.Remove(this); if (Rider != null && GodotObject.IsInstanceValid(Rider) && Rider.InBoat == this) Rider.InBoat = null; }

    /// <summary>The rider's controls this frame: throttle -1..1 (W/S), turn -1..1 (A/D; + turns right).</summary>
    public void Steer(float throttle, float turn, float dt)
    {
        float max = 7.5f, target = throttle > 0 ? max * throttle : throttle < 0 ? 2.5f * throttle : 0f;
        _speed = Mathf.MoveToward(_speed, target, (throttle != 0 ? 3.2f : 1.4f) * dt);
        float grip = Mathf.Clamp(.35f + Mathf.Abs(_speed) / 4f, 0f, 1f) * (_speed < -.1f ? -1f : 1f);
        _turn = Mathf.MoveToward(_turn, turn, 4f * dt);
        _yaw -= _turn * 1.5f * dt * grip;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta; _t += dt; _bumpCd -= dt;
        if (Water.SurfaceAt(GlobalPosition + Vector3.Up * .3f) is { } s) _surf = Mathf.Lerp(_surf, s, .2f);
        var pos = GlobalPosition;
        if (Rider == null)
        {
            _speed = Mathf.MoveToward(_speed, 0, 1.6f * dt); _turn = 0;
            // an empty boat drifts very slowly with the current; hitting it picks it up
            var cur = Water.Current(pos.X, pos.Z) * .25f * dt; var np = pos + cur;
            if (Water.Floatable(np.X, np.Z, _surf) && Water.Floatable(np.X + Fwd.X * 1.3f, np.Z + Fwd.Z * 1.3f, _surf)) pos = np;
            if (Player.Instance is { Dead: false, InBoat: null } pl && Input.MouseMode == Input.MouseModeEnum.Captured && !Player.BuildMode
                && Input.IsActionJustPressed(Inputs.Attack) && pl.GlobalPosition.DistanceTo(GlobalPosition) < 3f) { PickUp(); return; }
        }
        if (Mathf.Abs(_speed) > .01f)
        {
            var step = Fwd * _speed * dt; var np = pos + step; var bow = np + Fwd * (Mathf.Sign(_speed) * 1.35f);
            var side = Fwd.Cross(Vector3.Up) * .55f;
            if (Water.Floatable(bow.X, bow.Z, _surf) && Water.Floatable(np.X, np.Z, _surf) && Water.Floatable(bow.X + side.X, bow.Z + side.Z, _surf) && Water.Floatable(bow.X - side.X, bow.Z - side.Z, _surf)) pos = np;
            else
            {   // the shore: stop with a small knock back
                if (_bumpCd <= 0 && Mathf.Abs(_speed) > 1.5f) { Sfx.Play("land_thud", .5f, 1.2f); Rider?.Rig?.Shake(.03f); _bumpCd = .6f; }
                _speed = -_speed * .15f;
            }
        }
        pos.Y = _surf - .04f + Mathf.Sin(_t * 1.6f) * .035f;   // the floor slab's top rides just above the water line
        GlobalPosition = pos;
        Rotation = new Vector3(Mathf.Sin(_t * 1.3f) * .02f + _speed * .006f, _yaw, Mathf.Sin(_t * 1.1f) * .03f + _turn * Mathf.Clamp(_speed / 7f, -1, 1) * .06f);
    }

    void PickUp()
    {
        var m = Game.D.Mats; m["boat"] = m.GetValueOrDefault("boat") + 1;
        Ui.Note("Picked up the boat", UiTheme.Highlight); Sfx.Play("pickup"); Game.Notify(); QueueFree();
    }
}
