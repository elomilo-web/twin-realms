using Godot;

namespace TwinRealms;

/// <summary>
/// Riding (A-19): one code path for the bought horse and for a mountable pet. Mount key: the active pet if it is big enough, not
/// knocked out and beside the hero, else the horse. The hero sits on the species' own model (an Enemy body) at its saddle point;
/// speed, gallop (sprint key, drains the mount's stamina), jump and flight (dragons, wyverns: Jump in the air) come from the species
/// and its level (PetDb.Mount, pets.json rules mount*). While ridden the pet does not fight by itself; the pet order key makes it use
/// its strongest attack (PetWorld.RiderSkill). Death, dungeons, the arena, boats, deep water and MountBlock end the ride; the pet
/// then follows again. Friends see the species through the pose packet (MountNetCode, RemoteHeroMount.cs).
/// </summary>
public partial class Player
{
    Node3D? _petMount; Enemy? _petBody; float _mountY = .9f;
    string _mountSpecies = ""; bool _mountIsPet, _mountTired; MountStats _ms;
    /// <summary>The mount's stamina (gallop, flight) and its maximum; 0 / 0 on foot.</summary>
    public float MountSt, MountStMax;
    /// <summary>Set by other systems to a reason (e.g. "You cannot ride in beast form") to forbid riding; "" allows it. A ride in progress ends.</summary>
    public string MountBlock = "";
    /// <summary>The species ridden right now ("" on foot or on the plain fallback horse).</summary>
    public string MountSpecies => Mounted ? _mountSpecies : "";
    public bool MountIsPet => Mounted && _mountIsPet;
    public bool MountFlies => Mounted && _ms.Flies;
    /// <summary>May the hero take off / stay in the air: on a flying mount, or on foot with the Golden Wings.</summary>
    bool FlightOk => Mounted ? _ms.Flies : (Wings.Worn || Admin.CanFly);
    float MountJumpMul => Mounted ? Mathf.Max(.3f, _ms.Jump) : 1f;
    /// <summary>For the pose packet: 0 on foot, 1 the plain horse, 10 + species number for a creature mount.</summary>
    float MountNetCode { get { if (!Mounted) return 0; int i = _mountSpecies == "" ? -1 : PetDb.MountIndex(_mountSpecies); return i < 0 ? 1 : 10 + i; } }

    public void ToggleMount(bool forceOff = false)
    {
        if (forceOff || Mounted)
        {
            bool was = Mounted; Mounted = false; _horse.Visible = false; if (Flying && !(Wings.Worn || Admin.CanFly)) Flying = false;
            if (_petMount != null) { _petMount.QueueFree(); _petMount = null; _petBody = null; foreach (var c in _horse.GetChildren()) if (c is MeshInstance3D mi) mi.Visible = true; }
            _mountY = .9f; MountSt = MountStMax = 0;
            if (_mountIsPet) { PetWorld.Riding = false; if (was && PetWorld.Active is { } pa) pa.Stay = false; }   // the pet comes out beside us and follows again
            _mountIsPet = false; _mountSpecies = "";
            return;
        }
        var blue = new Color(0.7f, 0.8f, 1);
        if (Dead) return;
        if (MountBlock != "") { Ui.Note(MountBlock, blue); return; }
        if (Dungeon.Active || Arenas.Active) { Ui.Note("No riding here", blue); return; }
        if (InBoat != null) return;
        if (Swimming) { Ui.Note("You cannot mount in deep water", blue); return; }
        var a = PetWorld.Active; bool pet = false; string why = "";
        if (a != null)
        {
            if (!a.Mountable()) why = a.Sp().NaturalH < PetDb.R("mountMinHeight", .8f) ? $"{a.Name()} is too small to ride" : $"{a.Name()} cannot be ridden";
            else if (a.Ko()) why = $"{a.Name()} is still in its cube ({a.KoT:0} s)";
            else if (!PetWorld.NearHero(this)) why = $"{a.Name()} is too far away to mount (call it to you: {Inputs.KeyOf(Inputs.PetStay)})";
            else pet = true;
        }
        if (!pet && !Game.D.Horse) { Ui.Note(why != "" ? why + ". (Brynn sells a horse.)" : "You do not own a horse. Brynn sells one. (A mountable pet can carry you too.)", blue); return; }
        if (!pet && why != "") Ui.Note(why + ": you take your horse", blue);
        if (Sneaking) ToggleSneak(false);
        Mounted = true; _horse.Visible = true; _mountIsPet = pet; _mountTired = false;
        string species = pet ? a!.Species : EnemyDb.Has("horse") ? "horse" : ""; int level = pet ? a!.Level : Game.D.Level;
        _mountSpecies = species;
        if (pet) { PetWorld.Riding = true; PetWorld.Despawn(); }
        if (species != "")
        {   // the species' own model at riding size under the hero; the box horse stays as the fallback when the data has no horse
            var sp = PetDb.Species(species); _ms = PetDb.Mount(species, level, pet ? a : null);
            foreach (var c in _horse.GetChildren()) if (c is MeshInstance3D mi) mi.Visible = false;
            var holder = new Node3D { Scale = Vector3.One * sp.MountScale }; _horse.AddChild(holder);
            _petBody = Enemy.MakeBody(species, level); holder.AddChild(_petBody); _petMount = holder;
            if (_petBody.Creature is { } cm)
            {   // a procedural animal knows its saddle point (behind the withers)
                var sd = cm.Saddle * sp.MountScale; _mountY = Mathf.Clamp(sd.Y - .5f + sp.SaddleUp, .2f, 4f); holder.Position = new Vector3(0, 0, -sd.Z - sp.SaddleFwd);
            }
            else _mountY = Mathf.Clamp(_petBody.HeightM() * sp.MountScale * .62f + sp.SaddleUp, .5f, 2.6f);
        }
        else { _ms = new MountStats { Speed = 10.5f, Gallop = 13f, Jump = 1f, Stamina = 80 }; _mountY = .9f; }
        MountStMax = MountSt = Mathf.Max(10, _ms.Stamina);
        Ui.Note(pet ? $"You ride {a!.Name()}" + (_ms.Flies ? " (Jump in the air to fly)" : "") : "You mount your horse", new Color(0.8f, 0.9f, 1));
    }

    /// <summary>Ground speed on the mount this frame: a canter, or a gallop while the sprint key is held and the mount has breath left.</summary>
    float MountMove(bool sprintHeld, bool moving, float dt)
    {
        if (_mountTired && MountSt >= MountStMax * PetDb.R("mountTiredResume", .3f)) _mountTired = false;
        bool gallop = sprintHeld && moving && !_mountTired && MountSt > 0 && IsOnFloor();
        if (gallop) { MountSt = Mathf.Max(0, MountSt - PetDb.R("mountGallopDrain", 9) * dt); _mountRest = 1f; if (MountSt <= 0) { _mountTired = true; Ui.Note("Your mount is winded", new Color(1f, .8f, .6f)); } }
        return gallop ? _ms.Gallop : _ms.Speed;
    }
    float _mountRest;

    /// <summary>Flight on a flying mount: Jump climbs, Roll dives; it drains the mount's stamina and with none left it can only glide down.</summary>
    void MountFly(Vector3 dir, float moveMul, bool captured, float dt, ref Vector3 v)
    {
        float speed = _ms.FlySpeed * moveMul * Statuses.SpeedMul; bool spent = MountSt <= 0;
        var target = dir * speed; float acc = Accel * 1.2f * dt;
        v.X = Mathf.MoveToward(v.X, target.X, acc); v.Z = Mathf.MoveToward(v.Z, target.Z, acc);
        v.Y += _gravity * dt;   // the wings hold us up: undo this frame's gravity
        float vy = spent ? -PetDb.R("mountGlideSink", 3) : captured && Input.IsActionPressed(Inputs.Jump) ? PetDb.R("mountFlyClimb", 8) : captured && Input.IsActionPressed(Inputs.Roll) ? -PetDb.R("mountFlyDive", 13) : 0f;
        v.Y = Mathf.MoveToward(v.Y, vy, 30f * dt);
        if (!spent) { MountSt = Mathf.Max(0, MountSt - PetDb.R("mountFlyDrain", 5) * dt); _mountRest = 1f; if (MountSt <= 0) Ui.Note("Your mount is spent: it glides down", new Color(1f, .8f, .6f)); }
        if (dir.LengthSquared() > .01f) _modelYaw = Mathf.LerpAngle(_modelYaw, Util.YawTo(dir.X, dir.Z), 1f - Mathf.Exp(-5f * dt));
    }

    /// <summary>Every physics tick (PetWorld.Tick): the mount's breath comes back; places and states that do not allow riding end the ride.</summary>
    internal void MountTick(float dt)
    {
        if (!Mounted) return;
        string why = Swimming ? "Your mount will not swim: you slide off" : MountBlock != "" ? MountBlock : Dungeon.Active || Arenas.Active ? "No riding here" : "";
        bool lost = _mountIsPet && (PetWorld.Active == null || PetWorld.Active.Ko() || PetWorld.Active.Species != _mountSpecies);
        if (Dead || InBoat != null || lost || why != "") { ToggleMount(true); if (why != "" && !Dead) Ui.Note(why, new Color(0.7f, 0.8f, 1)); return; }
        _mountRest -= dt;
        if (_mountRest <= 0 && !Flying) MountSt = Mathf.Min(MountStMax, MountSt + PetDb.R("mountStaminaRegen", 7) * dt);
    }

    /// <summary>Each frame from Animate: the mount turns with us, its gait follows our speed, it beats its wings in the air.</summary>
    void MountAnimate()
    {
        _horse.Rotation = new Vector3(0, _modelYaw, 0);
        if (_petBody == null || _petMount == null || !IsInstanceValid(_petBody)) return;
        _petBody.PetVel = new Vector3(Velocity.X, 0, Velocity.Z) / Mathf.Max(.3f, _petMount.Scale.X);
        _petBody.PetAir = Flying || (_ms.Flies && !IsOnFloor() && _airT > .3f);
    }
    /// <summary>The mount plays its bite (the rider's pet skill).</summary>
    public void MountSwing() { if (_petBody != null && IsInstanceValid(_petBody)) _petBody.PetSwing(.3f); }
}
