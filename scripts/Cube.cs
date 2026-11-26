using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TwinRealms;

/// <summary>Soul Cubes in the hero's bag (PlayerStats.Cubes by tier, data/pets.json "cubes"): pick a tier, throw one at a foe.</summary>
public static class Cubes
{
    public static int Count(string id) => Game.D.Cubes.GetValueOrDefault(id);
    public static int Total => Game.D.Cubes.Values.Sum();
    public static void Give(string id, int n, bool note = true)
    {
        if (n <= 0) return; var d = Game.D; d.Cubes[id] = Count(id) + n;
        if (note) Ui.Note($"+{n} {PetDb.Cube(id).Name}", PetDb.Cube(id).Col.Lightened(.2f));
        if (Count(d.CubeSel) <= 0) d.CubeSel = id;
        Game.Notify();
    }
    /// <summary>The tier the throw key uses (the chosen one, or the first tier you carry).</summary>
    public static string Selected
    {
        get { var d = Game.D; if (Count(d.CubeSel) > 0) return d.CubeSel; foreach (var c in PetDb.Cubes) if (Count(c.Id) > 0) return c.Id; return d.CubeSel; }
    }
    /// <summary>Next tier you carry.</summary>
    public static void Cycle()
    {
        var all = PetDb.Cubes; if (all.Count == 0) return; int i = all.FindIndex(c => c.Id == Selected);
        for (int k = 1; k <= all.Count; k++) { var c = all[(i + k + all.Count) % all.Count]; if (Count(c.Id) > 0) { Game.D.CubeSel = c.Id; Ui.Note($"{c.Name} ({Count(c.Id)})", c.Col.Lightened(.2f)); Game.Notify(); return; } }
        Ui.Note("No Soul Cubes. Brynn sells them; the craft bench makes them.", new Color(.8f, .75f, .9f));
    }

    /// <summary>Throws the selected cube at the locked target (or the foe under the aim / in front). False when nothing was thrown.</summary>
    public static bool Throw(Player p, string? tier = null, Enemy? at = null)
    {
        string id = tier ?? Selected; var cd = PetDb.Cube(id);
        if (p.Mounted) { Ui.Note("Dismount to throw a cube", new Color(.7f, .8f, 1)); return false; }
        if (Count(id) <= 0) { Ui.Note($"No {cd.Name}s", new Color(1, .6f, .5f)); return false; }
        float range = PetDb.R("throwRange", 22);
        var e = at ?? Pick(p, range);
        if (e == null) { Ui.Note("No foe to bind in reach", new Color(.8f, .75f, .9f)); return false; }
        if (!PetDb.Species(e.Type).Catchable) { Ui.Note($"{e.Def.N} is a mirror of a hero: no soul of its own to bind", new Color(.8f, .7f, 1f)); return false; }
        if (e.CatchBusy) return false;
        Game.D.Cubes[id] = Count(id) - 1; if (Game.D.Cubes[id] <= 0) Game.D.Cubes.Remove(id); Game.Notify();
        var from = p.GlobalPosition + new Vector3(0, 1.5f, 0) + p.Facing() * .5f;
        var c = new SoulCube { Target = e, Tier = id, Chance = PetDb.CatchChance(e, id) };
        Game.Root!.AddChild(c); c.GlobalPosition = from; c.From = from;
        Sfx.Play("swing", .8f); Flag.Event($"cube {id} at {e.Def.N} Lv{e.Level} hp {e.Hp:0}/{e.MaxHp:0}: {c.Chance:P1}");
        return true;
    }
    static Enemy? Pick(Player p, float range)
    {
        if (Targeting.Current is Enemy t && GodotObject.IsInstanceValid(t) && !t.Dead && t.GlobalPosition.DistanceTo(p.GlobalPosition) <= range) return t;
        if (p.AimPoint(range).enemy is Enemy a && !a.Dead && a.GlobalPosition.DistanceTo(p.GlobalPosition) <= range) return a;
        Enemy? best = null; float bs = float.MaxValue; var f = p.Facing();
        foreach (var n in p.GetTree().GetNodesInGroup("enemies"))
        {
            if (n is not Enemy e || e.Dead || !e.Visible || e.CatchBusy || !e.Active) continue;
            var d = e.GlobalPosition - p.GlobalPosition; d.Y = 0; float l = d.Length(); if (l > range || l < .1f) continue;
            float dot = d.Normalized().Dot(f); if (dot < .35f) continue;
            float s = l * (2 - dot); if (s < bs) { bs = s; best = e; }
        }
        return best;
    }

    /// <summary>The cube drawn flat for the HUD and menus: a three-face hexagon in the tier colour with a glowing rune.</summary>
    public static void DrawIcon(CanvasItem ci, Vector2 c, float size, Color col, float a = 1f)
    {
        float s = size * .42f; col = col with { A = a };
        Vector2 P(float x, float y) => c + new Vector2(x, y) * s;
        var p0 = P(0, -1); var p1 = P(.866f, -.5f); var p2 = P(.866f, .5f); var p3 = P(0, 1); var p4 = P(-.866f, .5f); var p5 = P(-.866f, -.5f); var m = P(0, 0);
        ci.DrawColoredPolygon(new[] { p0, p1, m, p5 }, col.Lightened(.25f)); ci.DrawColoredPolygon(new[] { p5, m, p3, p4 }, col.Darkened(.3f)); ci.DrawColoredPolygon(new[] { m, p1, p2, p3 }, col.Darkened(.12f));
        var ol = new Color(0, 0, 0, .7f * a); ci.DrawPolyline(new[] { p0, p1, p2, p3, p4, p5, p0 }, ol, 1.5f, true);
        var rune = new Color(1f, .85f, .45f, .9f * a); ci.DrawLine(P(-.35f, .05f), P(-.35f, .6f), rune, 1.5f, true); ci.DrawLine(P(.4f, .0f), P(.4f, .55f), rune, 1.5f, true); ci.DrawCircle(P(0, -.5f), s * .12f, rune);
    }
}

/// <summary>
/// A thrown Soul Cube: flies in an arc, opens and pulls the soul in with ribbons, shakes up to three times (each shake one roll of
/// chance^(1/3)), then a gold burst (bound: a new pet) or a crack and the foe breaks free enraged.
/// </summary>
public partial class SoulCube : Node3D
{
    public Enemy Target = null!; public string Tier = "iron"; public float Chance; public Vector3 From;
    enum Ph { Fly, Pull, Shake, Done }
    Ph _ph = Ph.Fly; float _t; int _shake, _passes; bool _ok; Vector3 _rest;
    MeshInstance3D _box = null!, _rune = null!; StandardMaterial3D _runeMat = null!; OmniLight3D _light = null!;
    const float FlyT = .55f, PullT = .7f, ShakeT = .6f;

    public override void _Ready()
    {
        AddToGroup("soulcubes"); var col = PetDb.Cube(Tier).Col;
        _box = new MeshInstance3D { Mesh = new BoxMesh { Size = Vector3.One * .32f }, MaterialOverride = new StandardMaterial3D { AlbedoColor = col.Darkened(.25f), Metallic = .8f, Roughness = .35f } };
        AddChild(_box);
        _runeMat = new StandardMaterial3D { AlbedoColor = col.Lerp(new Color(1f, .8f, .4f), .3f) with { A = .3f }, EmissionEnabled = true, Emission = col.Lerp(new Color(1f, .8f, .4f), .3f), EmissionEnergyMultiplier = 1.5f, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        _rune = new MeshInstance3D { Mesh = new BoxMesh { Size = Vector3.One * .345f }, MaterialOverride = _runeMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off }; _box.AddChild(_rune);
        _light = new OmniLight3D { LightColor = col.Lerp(new Color(1f, .8f, .4f), .4f), OmniRange = 3f, LightEnergy = .8f, ShadowEnabled = false }; AddChild(_light);
        // with the decided outcome: how many shakes pass (each a roll of chance^(1/3))
        float per = Mathf.Pow(Mathf.Clamp(Chance, 0, 1), 1f / 3f);
        for (_passes = 0; _passes < 3 && GD.Randf() < per; _passes++) { }
        _ok = _passes >= 3;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta; _t += dt;
        bool alive = GodotObject.IsInstanceValid(Target);
        switch (_ph)
        {
            case Ph.Fly:
            {
                if (!alive || Target.Dead) { Fizzle(); return; }
                var to = Target.GlobalPosition + new Vector3(0, Mathf.Max(.8f, Target.HeightM() * .55f) + .6f, 0);
                float k = Mathf.Clamp(_t / FlyT, 0, 1);
                GlobalPosition = From.Lerp(to, k) + Vector3.Up * Mathf.Sin(k * Mathf.Pi) * 2f;
                _box.Rotation += new Vector3(9f, 6f, 0) * dt;
                if (k >= 1) { _ph = Ph.Pull; _t = 0; _rest = to; Open(); }
                break;
            }
            case Ph.Pull:
            {
                float k = Mathf.Clamp(_t / PullT, 0, 1);
                _box.Rotation = _box.Rotation.Lerp(Vector3.Zero, 1f - Mathf.Exp(-10f * dt));
                if (alive && (int)(_t * 20) != (int)((_t - dt) * 20))
                {   // soul ribbons: motes from the body into the cube
                    var at = Target.GlobalPosition + Vector3.Up * Target.HeightM() * .5f;
                    for (int i = 0; i < 3; i++) Fx.Burst(at.Lerp(_rest, (float)GD.RandRange(.1, .9)), PetDb.Cube(Tier).Col.Lerp(new Color(1f, .85f, .5f), .5f), 2, 1.2f, .35f, .07f, 0f, "magic");
                }
                if (k >= 1)
                {
                    if (alive) { Target.Visible = false; Target.SetPhysicsProcess(false); Target.CollisionLayer = 0; }
                    _ph = Ph.Shake; _t = 0; _shake = 0;
                    // the cube drops to the ground to shake
                    var g = alive ? Target.GlobalPosition : GlobalPosition; _rest = g + new Vector3(0, .2f, 0);
                }
                else GlobalPosition = _rest;
                break;
            }
            case Ph.Shake:
            {
                GlobalPosition = GlobalPosition.Lerp(_rest, 1f - Mathf.Exp(-12f * dt));
                float k = _t / ShakeT;
                _box.Rotation = new Vector3(0, 0, Mathf.Sin(k * Mathf.Tau) * .4f * (k < 1 ? 1 : 0));
                _runeMat.EmissionEnergyMultiplier = 1.5f + Mathf.Sin(k * Mathf.Pi) * 3f;
                if (k >= 1)
                {
                    _shake++; _t = 0; Sfx.Play("click", .9f, .8f + _shake * .1f);
                    FloatText.Spawn(this, GlobalPosition + Vector3.Up * .6f, new string('.', _shake), new Color(1f, .85f, .5f), .7f);
                    if (_shake > _passes || _shake >= 3) { _ph = Ph.Done; _t = 0; if (_ok) Bind(); else Crack(); }
                }
                break;
            }
            case Ph.Done:
                if (_t > (_ok ? .9f : .4f)) QueueFree();
                else if (_ok) { Scale = Vector3.One * (1 + _t * .6f); }
                break;
        }
    }

    void Open()
    {
        if (!GodotObject.IsInstanceValid(Target)) return;
        Target.CatchBusy = true; Target.SetPhysicsProcess(false);
        Fx.Ring(Target.GlobalPosition + Vector3.Up * .1f, Mathf.Max(1.2f, Target.HeightM() * .6f), PetDb.Cube(Tier).Col.Lightened(.3f), .5f);
        Sfx.Play("zap", .7f, .7f);
    }
    void Fizzle() { Fx.Burst(GlobalPosition, new Color(.6f, .6f, .65f), 6, 2f); QueueFree(); }

    void Bind()
    {
        var at = GlobalPosition; var gold = new Color(1f, .82f, .35f);
        Fx.Burst(at, gold, 40, 6f, .8f, .12f, -2f, "magic"); Fx.Ring(at, 3f, gold, .7f); Sfx.Play("level"); Sfx.Play("coin", .8f);
        if (!GodotObject.IsInstanceValid(Target)) return;
        var e = Target; e.CatchBusy = false; e.SetPhysicsProcess(true); e.Captured();
        var d = Game.D; int lvl = Mathf.Clamp(e.Level, 1, d.Level + (int)PetDb.R("levelCapOverHero", 5));
        var pet = PetDb.New(e.Type, lvl, Tier); d.Pets.Add(pet);
        if (d.ActivePet == "" || d.Pets.Find(x => x.Id == d.ActivePet) == null) d.ActivePet = pet.Id;
        var sp = pet.Sp();
        Ui.Banner(pet.Name(), $"bound - {PetDb.RarityName(sp.Rarity)}, level {pet.Level}");
        Ui.Note($"{pet.Name()} joins you ({Inputs.KeyOf(Inputs.PetsUi)}: Pets)", gold);
        Flag.Event($"bound {e.Type} Lv{lvl} with {Tier}");
        Game.Notify(); Game.Save();
    }
    void Crack()
    {
        var at = GlobalPosition; Fx.Burst(at, new Color(.45f, .3f, .6f), 18, 5f, .5f, .1f, -6f); Fx.Burst(at, PetDb.Cube(Tier).Col.Darkened(.3f), 10, 4f, .4f, .08f);
        Sfx.Play("hit", 1f, .7f); _box.Visible = false; _light.Visible = false;
        if (!GodotObject.IsInstanceValid(Target)) return;
        var e = Target; e.CatchBusy = false; e.Visible = true; e.SetPhysicsProcess(true); e.CollisionLayer = 2; e.Enrage();
        Ui.Note($"{e.Def.N} broke free!", new Color(1f, .55f, .45f));
    }
}
