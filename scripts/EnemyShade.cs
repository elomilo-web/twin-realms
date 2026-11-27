using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// Shades (Scarletta's Rotting Hollow, before the boss room): black mirror images of the party. Each wears one hero's body, gear and
/// weapon under a shadow shader, and fights like that hero's weapon: melee ("shade"), bow ("shadeArcher") or wand and staff
/// ("shadeCaster"). They give no experience; each hero rolls their own drops when one falls: white angel feathers, health and
/// mana potions, gold, and now and then a katana of any rarity.
/// </summary>
public partial class Enemy
{
    public PlayerStats? ShadeOf;
    public bool IsShade => Type is "shade" or "shadeArcher" or "shadeCaster";   // (not the shade stag, which is wildlife)
    static Shader? _shadeSh;

    /// <summary>The party as shades should mirror them, in the same order on every machine (by player id); solo: just us.</summary>
    public static List<PlayerStats> ShadeModels()
    {
        var l = new List<PlayerStats>(); var net = Net.I;
        if (Net.Active && net != null && net.MyParty is { } party && party.Members.Count > 1)
        {
            var ids = new List<long>(party.Members); ids.Sort();
            foreach (var id in ids)
            {
                if (id == Net.MyId) { l.Add(Game.D); continue; }
                if (net.Players.GetValueOrDefault(id) is { } p) l.Add(new PlayerStats { Name = p.Name, Race = p.Race, Class = p.Class, Body = p.Body, Look = HeroLook.Unpack(p.Look), Eq = new Dictionary<string, Item?>(p.Eq) });
            }
        }
        if (l.Count == 0) l.Add(Game.D);
        return l;
    }

    void BuildShade()
    {
        var ps = ShadeOf ?? Game.D;
        Def = Def.Clone(); Def.N = "Shade of " + ps.Name;
        _av = new Avatar { Name = "Avatar" }; _body.AddChild(_av); _av.Build(Races.Look(ps), ps.Name + "_shade");
        _av.Dress(sl => ps.Eq.GetValueOrDefault(sl), ps.WType(), Classes.Get(ps.Class).Tunic);
        // a black shade: nearly no light of its own, a cold grey rim, a little see-through
        _shadeSh ??= new Shader { Code = @"shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_back, shadows_disabled;
uniform vec4 rim : source_color = vec4(.55, .58, .68, 1.0);
void fragment() {
	float f = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 2.4);
	float smoke = .5 + .5 * sin(TIME * 2.0 + UV.y * 20.0 + UV.x * 7.0);
	ALBEDO = mix(vec3(.01, .01, .015), rim.rgb, f * (.6 + .4 * smoke)); ALPHA = clamp(.82 + .18 * f, 0.0, 1.0);
}" };
        var mat = new ShaderMaterial { Shader = _shadeSh };
        void Over(Node n) { foreach (var c in n.GetChildren()) { if (c is GeometryInstance3D g) g.MaterialOverlay = mat; Over(c); } }
        Over(_av); _shadeMat = mat; _shadeT = 0;
        // dark smoke drifting off it
        var pm = new ParticleProcessMaterial { EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(.3f, .8f, .3f), Direction = Vector3.Up, Spread = 30, InitialVelocityMin = .3f, InitialVelocityMax = .8f, Gravity = new Vector3(0, .4f, 0), ScaleMin = .6f, ScaleMax = 1.4f };
        AddChild(new GpuParticles3D { Position = new Vector3(0, 1f, 0), Amount = 16, Lifetime = 1.4f, ProcessMaterial = pm, DrawPass1 = new QuadMesh { Size = new Vector2(.35f, .35f), Material = new StandardMaterial3D { AlbedoColor = new Color(0, 0, 0, .45f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled } } });
    }
    ShaderMaterial? _shadeMat; float _shadeT;
    /// <summary>Gear models load a moment late: give them the shade too.</summary>
    void ShadeTick(float dt)
    {
        if (_shadeMat == null || _av == null || _shadeT > 3f) return; _shadeT += dt;
        void Over(Node n) { foreach (var c in n.GetChildren()) { if (c is GeometryInstance3D g && (g.MaterialOverlay == null || g.MaterialOverlay == CharRim.Mat)) g.MaterialOverlay = _shadeMat; Over(c); } }
        Over(_av);
    }

    /// <summary>A shade fell: our own hero's drops (every hero in the party rolls their own, on their own game).</summary>
    void ShadeDrops()
    {
        var at = GlobalPosition + Vector3.Up * .3f; int lvl = Mathf.Max(1, Level);
        // A-23: rewards follow the shade's level against the lair's (data/dungeons.json scarletta.shades): higher shades drop more often and more, elites much more
        float k = Mathf.Clamp(1 + Dungeon.ShadeF("rewardPerLevel", .12f) * (Level - ShadeBase), Dungeon.ShadeF("rewardMin", .6f), Dungeon.ShadeF("rewardMax", 2.2f)) * (ShadeElite ? Dungeon.ShadeF("eliteReward", 2.5f) : 1f);
        if (GD.Randf() < Tune.F("raid.shadeFeather", .35f) * k) GatherDb.Give("angelFeather", GD.RandRange(1, 2) + (ShadeElite ? 2 : 0), at);
        if (GD.Randf() < Tune.F("raid.shadeGold", .75f) * k) Loot.Drop(at, gold: Mathf.RoundToInt((GD.RandRange(15, 30) + lvl * 5) * k));
        if (GD.Randf() < Tune.F("raid.shadeHpPot", .25f) * k) Loot.Drop(at, pot: "hp");
        if (GD.Randf() < Tune.F("raid.shadeMpPot", .2f) * k) Loot.Drop(at, pot: "mp");
        if (GD.Randf() < Tune.F("raid.shadeKatana", .12f) * k) Loot.Drop(at, item: Items.Roll(lvl, 1.5f + (ShadeElite ? 1.5f : 0), "weapon", -1, null, "katana"));   // a katana, any rarity
    }

    // ---- A-23: levels spread around the lair's, elites, and a cap on how many shades hunt at once
    /// <summary>The lair's own level when this shade was made (its Level is spread around it), and whether it is an elite.</summary>
    public int ShadeBase; public bool ShadeElite;
    static int _shadeAwake; static ulong _shadeFrame;
    /// <summary>May another shade join the hunt? At most shades.maxAwake at once (maxAwakeBoss while the boss fight runs), so the tripled halls come in waves.</summary>
    bool ShadeMayWake()
    {
        ulong f = Engine.GetPhysicsFrames();
        if (f - _shadeFrame > 12 || f < _shadeFrame)
        {
            _shadeFrame = f; _shadeAwake = 0;
            foreach (var n in GetTree().GetNodesInGroup("enemies")) if (n is Enemy e && e.IsShade && !e.Dead && e.Hunting) _shadeAwake++;
        }
        bool boss = CurrentBoss != null && IsInstanceValid(CurrentBoss) && CurrentBoss.Scarletta && !CurrentBoss.Dead;
        if (_shadeAwake >= (int)Dungeon.ShadeF(boss ? "maxAwakeBoss" : "maxAwake", boss ? 4 : 14)) return false;
        _shadeAwake++; return true;
    }
}
