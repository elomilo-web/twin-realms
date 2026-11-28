using System;
using Godot;

namespace TwinRealms;

/// <summary>
/// Animals (A-21 / A-22): an enemy whose data has "proc" wears a procedural body (CreatureModel, data/creatures.json) instead of a
/// .glb or the old box shapes: the wolf, the deer, the dragon and the new wildlife. This partial builds it, feeds it the AI state
/// (gait, bite, breath, flight), runs the simple brain of birds and bats ("fly"), hides animals of the day or the night ("time"),
/// and holds the livestock hooks ranching uses (IsLivestock, HomePoint, SetPen).
/// Other code that wants an animal body: <see cref="MakeBody"/> (any enemy type, model only) or CreatureModel.Make (the bare model).
/// </summary>
public partial class Enemy : ILivestock
{
    CreatureModel? _cm; State _cmState = State.Idle;
    /// <summary>The procedural body, if this type has one.</summary>
    public CreatureModel? Creature => _cm;
    /// <summary>Pet body / mount: show it flying (wings beat, legs tucked).</summary>
    public bool PetAir;

    /// <summary>
    /// Only the model of an enemy type (procedural animal, .glb monster or the built-in shapes): no AI, collision, group or nameplate.
    /// Add it to the tree, then drive it: PetVel (gait speed in its own space), PetAir, PetSwing(wind), PetFlinch(from), PetDown(bool).
    /// Pets, mounts, the pets menu preview and hero transformations use this.
    /// </summary>
    public static Enemy MakeBody(string type, int level = 1) => new() { Type = type, PetBody = true, Level = Math.Max(1, level), AnyRealm = true };

    bool BuildCreature()
    {
        float want = (Def.H > 0 ? Def.H : 1f) * _scale; if (Def.Size > 0) want *= Def.Size;
        Color? tint = null;
        if (Def.Tint != "") { try { tint = Color.FromHtml(Def.Tint); } catch { } }
        else if (Realm_() == 1 && !Def.Emissive) tint = new Color(.85f, .78f, 1f);
        var c = CreatureModel.Make(Def.Proc, want, tint, Def.Emissive); if (c == null) return false;
        _cm = c; _mdl = c; _body.AddChild(c); c.Position = new Vector3(0, Def.Fly ? 0 : Def.Lift, 0);
        foreach (var m in c.Mats) { _mats.Add(m); _baseCols.Add(m.AlbedoColor); }
        CharRim.Apply(c);
        if (Def.Livestock && !PetBody) AddToGroup("livestock");
        return true;
    }

    /// <summary>Every tick: hand the body what we are doing; keep penned livestock inside; call out when startled.</summary>
    void AnimateCreature()
    {
        var c = _cm; if (c == null) return;
        var hv = PetBody ? PetVel : Velocity;
        c.Speed = new Vector2(hv.X, hv.Z).Length();
        c.Attack = _flyAtk > 0 ? 1 - _flyAtk / .35f : _state == State.Wind ? .5f * (1 - Mathf.Clamp(_timer / Mathf.Max(.05f, WindTotal), 0, 1)) : _state == State.Strike ? .5f + .5f * (1 - Mathf.Clamp(_timer / Mathf.Max(.05f, ActiveTime), 0, 1)) : -1;   // the bite peaks as the strike opens (its jaws are the hitbox, EnemyStrike.cs)
        c.Breath = _state == State.Breath; c.Menace = Hunting && !PetBody; c.Dead = _state == State.Dead; c.Calm = _state == State.Idle && !PetBody;
        c.Flash = _flash > 0; c.Airborne = PetBody ? PetAir : _flying;
        if (PetBody) return;
        if (Tame && _state == State.Flee) _state = State.Idle;   // a led, penned or owned animal does not bolt
        if (_state != _cmState)
        {
            bool startled = (_state == State.Chase || _state == State.Flee) && (_cmState == State.Idle || _cmState == State.Sleep || _cmState == State.Return);
            if (startled && Def.Sound != "" && Visible) Sfx.PlayAt(this, Def.Sound, GlobalPosition, .55f, Def.Pitch);
            if (_state == State.Dead && _animalId != "") { Ranch.Remove(_animalId); RespawnTime = 0; }   // an owned animal that dies is gone
            _cmState = _state;
        }
        if (HasPen)
        {
            if (!InPen(_grazeTo)) _grazeTo = _home;
            var off = GlobalPosition - _home; off.Y = 0;
            if (_state == State.Idle && !InPen(GlobalPosition) && off.LengthSquared() > 1f) { _grazeTo = _home; _grazeT = Mathf.Min(_grazeT, 4f); }   // strayed: walk back in
        }
    }

    // ------------------------------------------------------------------ day and night animals
    bool TimeOk()
    {
        if (Def.Time == "" || PetBody || Hunting || Dead) return true;
        float h = World.TimeOfDay * 24f; bool night = h < Tune.F("wildlife.dawn", 5.5f) || h > Tune.F("wildlife.dusk", 19.5f);
        return Def.Time == "night" ? night : !night;
    }

    // ------------------------------------------------------------------ livestock (ranching)
    /// <summary>A farm animal (data/enemies.json "livestock": cow, sheep, pig, chicken, goat, horse, duck).</summary>
    public bool IsLivestock => Def.Livestock;
    /// <summary>Where it lives: it wanders around this point and returns to it (and respawns there).</summary>
    public Vector3 HomePoint { get => _home; set { _home = value; _grazeTo = value; } }
    float _penR; Func<Vector3, bool>? _penTest;
    public bool HasPen => _penR > 0 || _penTest != null;
    /// <summary>Keep it inside a round pen: it grazes within radius of centre, walks back if pushed out, and no longer flees.</summary>
    public void SetPen(Vector3 centre, float radius) { HomePoint = centre; _penR = Mathf.Max(.5f, radius); _penTest = null; }
    /// <summary>Keep it inside any shape (a fenced yard): inside(world point) says whether a spot is in the pen; centre must be inside.</summary>
    public void SetPen(Vector3 centre, Func<Vector3, bool> inside) { HomePoint = centre; _penTest = inside; _penR = 0; }
    public void ClearPen() { _penR = 0; _penTest = null; }
    /// <summary>The round pen's radius (0: none, or a shaped pen).</summary>
    public float PenRadius => _penR;
    bool InPen(Vector3 p)
    {
        if (_penTest != null) return _penTest(p);
        if (_penR <= 0) return true;
        var d = p - _home; d.Y = 0; return d.LengthSquared() <= _penR * _penR;
    }

    // ---- ILivestock (Ranch.cs, A-25): the ranch leads, pens, owns and breeds these through the group "livestock"
    Node3D? _lead; string _animalId = ""; bool _baby;
    /// <summary>Led, penned or owned: it does not run from heroes.</summary>
    bool Tame => _lead != null || _animalId != "" || HasPen;
    public string Kind => Type;
    public Node3D Body => this;
    public string AnimalId
    {
        get => _animalId;
        set
        {
            _animalId = value ?? ""; if (_animalId == "") return;
            // yours now: it leaves its wild herd (the herd's place restocks much later) and is no longer a networked world monster
            foreach (var s in Zones.Slots) if (s.Node == this) { s.Node = null; s.Dead = true; s.DeadAt = Zones.Now; s.RespawnT = Tune.F("wildlife.restock", 1800f); }
            NetIdx = -1; Puppet = false; NoLeash = true; RespawnTime = 0;
        }
    }
    public bool Baby { get => _baby; set { _baby = value; if (_cm != null) _cm.Scale = Vector3.One * _cm.K * (value ? Tune.F("wildlife.babyScale", .55f) : 1f); } }
    public void Lead(Node3D? leader) { _lead = leader; if (leader != null && _state == State.Flee) _state = State.Idle; if (leader == null) _home = GlobalPosition; }
    public void SetHome(Vector3? home, System.Collections.Generic.HashSet<Vector3I>? pen)
    {
        if (home == null) { ClearPen(); return; }
        if (pen == null || pen.Count == 0) { ClearPen(); HomePoint = home.Value; return; }
        var cells = new System.Collections.Generic.HashSet<(int, int)>(); foreach (var c in pen) cells.Add((c.X, c.Z));
        SetPen(home.Value, p => cells.Contains((Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Z))));
    }
    /// <summary>Idle livestock on a lead: walk after the leader (keeping a couple of metres), home moves along.</summary>
    void LeadStep(ref Vector3 move, float dt)
    {
        if (_lead == null || !IsInstanceValid(_lead) || !_lead.IsInsideTree()) { _lead = null; return; }
        var to = _lead.GlobalPosition - GlobalPosition; to.Y = 0; float d = to.Length(); _home = GlobalPosition; _grazeTo = GlobalPosition;
        if (d > 40f) { _lead = null; return; }
        if (d < 2.4f) { move = Vector3.Zero; return; }
        move = to / d * Mathf.Min(Mathf.Max(Def.Spd, 5.5f), Mathf.Max(1.5f, (d - 2f) * 2f));
        _yaw = Mathf.LerpAngle(_yaw, Util.YawTo(to.X, to.Z), 1f - Mathf.Exp(-6f * dt));
    }

    // ------------------------------------------------------------------ birds and bats
    bool _flying; float _flyAng = -1, _flyCd, _flyClimb, _flyAtk;
    /// <summary>
    /// Fliers ("fly"): circle their home at flyAlt, climb away from a hero if they flee, or swoop (those with damage: when they notice a
    /// hero, or, if neutral, when struck), strike in passing and climb out again. They move freely through the air; a dead one falls.
    /// </summary>
    void FlyTick(float dt)
    {
        var pl = _hero; bool plOk = pl is IDamageable pd && !pd.Dead;
        var pos = GlobalPosition; Vector3 toP = plOk ? pl!.GlobalPosition - pos : Vector3.Zero; float dist = plOk ? new Vector2(toP.X, toP.Z).Length() : 999f;
        if (_flyAng < 0) _flyAng = (GetInstanceId() % 628) / 100f;
        float alt = Mathf.Max(1.5f, Def.FlyAlt), rad = 5f + GetInstanceId() % 6, speed = Def.Spd * .55f; _flying = true;
        _flyCd -= dt; if (_flyAtk > 0) _flyAtk -= dt;
        Vector3 want;
        if (_state == State.Stagger) { _stun -= dt; if (_stun <= 0) _state = Def.Flee ? State.Flee : State.Chase; }
        if (_state == State.Sleep || _state == State.Return) { _state = State.Idle; if (Hp < MaxHp) Hp = MaxHp; }
        switch (_state)
        {
            case State.Flee:
            {
                if (!plOk || dist > Def.Aggro * 2.2f) { _state = State.Idle; goto default; }
                var away = new Vector3(-toP.X, 0, -toP.Z); if (away.LengthSquared() < .01f) away = Vector3.Forward; away = away.Normalized();
                if ((pos - _home).Length() > 60f) away = (away + (_home - pos).Normalized()).Normalized();
                want = pos + away * 8f; want.Y = World.GroundAt(want.X, want.Z) + alt * 1.7f; speed = Def.Spd; break;
            }
            case State.Stagger: want = pos; break;   // stunned: hangs in the air
            case State.Chase: case State.Wind: case State.Strike: case State.Recover:
            {
                if (!plOk || dist > Mathf.Max(Def.Aggro * 2.5f, 30f) || (pos - _home).Length() > 75f) { _state = State.Idle; DropAggro(); goto default; }
                _state = State.Chase;
                if (_flyClimb > 0)
                {
                    _flyClimb -= dt; var away = new Vector3(-toP.X, 0, -toP.Z); away = away.LengthSquared() < .01f ? Vector3.Forward : away.Normalized();
                    want = pos + away * 6f; want.Y = World.GroundAt(pos.X, pos.Z) + alt; speed = Def.Spd;
                }
                else
                {
                    want = pl!.GlobalPosition + Vector3.Up * 1.3f; speed = Def.Spd * 1.35f;
                    if (pos.DistanceTo(want) < Def.Reach * .6f + .5f && _flyCd <= 0)
                    {
                        _flyCd = Def.Cd; _flyClimb = 1.4f + GD.Randf() * .8f; _flyAtk = .35f; _swingSerial++;
                        if (Def.Dmg > 0) ((IDamageable)pl!).TakeHit(OnHit(new HitInfo { Damage = Damage, From = pos, Knock = 2f, Attacker = this }));
                    }
                }
                break;
            }
            default:
            {
                _flyAng += dt * Def.Spd * .5f / rad;
                want = _home + new Vector3(Mathf.Cos(_flyAng) * rad, 0, Mathf.Sin(_flyAng) * rad);
                want.Y = World.GroundAt(want.X, want.Z) + alt + Mathf.Sin(_flyAng * .37f) * alt * .15f;
                if (plOk && Mathf.Abs(toP.Y) < alt + 6f)
                {
                    if (Def.Flee) { if (dist < Def.Aggro * .5f && toP.Y > -alt * .6f) _state = State.Flee; }
                    else if (!Def.Neutral && Def.Dmg > 0 && dist < AggroR) { _state = State.Chase; Alert(); }
                }
                break;
            }
        }
        var d = want - pos; float len = d.Length();
        var vel = len > .15f ? d / len * Mathf.Min(speed * Status.SpeedMul, len / Mathf.Max(dt, .001f)) : Vector3.Zero;
        _kb = _kb.MoveToward(Vector3.Zero, 22f * dt);
        var np = pos + (vel + _kb) * dt; np.Y = Mathf.Max(np.Y, World.GroundAt(np.X, np.Z) + .6f);
        GlobalPosition = np; Velocity = vel;
        if (vel.X * vel.X + vel.Z * vel.Z > .04f) _yaw = Mathf.LerpAngle(_yaw, Util.YawTo(vel.X, vel.Z), 1f - Mathf.Exp(-6f * dt));
        Rotation = new Vector3(0, _yaw, 0);
        AnimateCreature();
    }
}
