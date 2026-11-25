using System.Collections.Generic;
using Godot;

namespace TwinRealms;

// Enemy AI helpers (aggro / noticing, attack telegraph, summoning); the state switch itself stays in Enemy._PhysicsProcess
public partial class Enemy
{
    // ------------------------------------------------------------------ noticing heroes
    float _seeT; bool _saw;
    /// <summary>Within aggro range: outdoors that is enough; in a dungeon the monster must see the hero (no walls between), or hear them up close.</summary>
    bool Notices(Node3D hero, float dist, float dt)
    {
        if (!FromDungeon || dist < 4f) return true;
        _seeT -= dt; if (_seeT > 0) return _saw; _seeT = .25f + GD.Randf() * .1f;
        var from = GlobalPosition + Vector3.Up * Mathf.Min(1.4f, HeightM() * .8f); var to = hero.GlobalPosition + Vector3.Up * 1.2f;
        var q = PhysicsRayQueryParameters3D.Create(from, to, 1); q.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
        _saw = GetWorld3D().DirectSpaceState.IntersectRay(q).Count == 0;
        return _saw;
    }
    /// <summary>This monster noticed a hero (or was struck): the idle pack around it (9 m) joins the fight.</summary>
    void Alert()
    {
        float r = IsShade ? Tune.F("raid.shadeAlert", 45f) : 9f;   // shades call every shade in the hall
        foreach (var n in GetTree().GetNodesInGroup("enemies"))
            if (n is Enemy e && e != this && !e.Dead && !e.Puppet && e._state == State.Idle && !e.Def.Flee && e.FromDungeon == FromDungeon && e.GlobalPosition.DistanceSquaredTo(GlobalPosition) < r * r) e.Wake();
    }

    // ------------------------------------------------------------------ attack telegraph
    void ClearTele() { if (_tele != null && IsInstanceValid(_tele)) _tele.QueueFree(); _tele = null; }
    // wind-up telegraph: cold glint at the head, plus a growing dark-red ground ring for heavy / boss / area blows (visuals are local)
    void Telegraph()
    {
        if (Dead || Puppet) return;
        Fx.Burst(GlobalPosition + new Vector3(0, HeightM() * 0.85f, 0), new Color(0.75f, 0.9f, 1f), 7, 1.5f, Mathf.Max(.2f, Def.Wind), 0.14f);
        ClearTele();
        if (!(Def.Slam || Def.Boss || Def.Kind == "dragon") || Tune.F("monsters.telegraphRing", 1) <= 0) return;
        float rad = Def.Reach + 0.8f;
        var mat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = new Color(.45f, .02f, .02f, .1f), CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        // a thin ring at the edge of the blow (a solid disc hid the ground and the hero: devloop #4)
        _tele = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = rad - 0.14f, OuterRadius = rad, Rings = 48, RingSegments = 4 }, MaterialOverride = mat, TopLevel = true, Scale = new Vector3(1, 0.05f, 1), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_tele); _tele.GlobalPosition = GlobalPosition + Vector3.Up * 0.08f;
    }
    void TeleTick()
    {
        if (_tele == null || !IsInstanceValid(_tele)) return;
        if (_tele.MaterialOverride is StandardMaterial3D m) m.AlbedoColor = new Color(.6f, .05f, .03f, Mathf.Lerp(.85f, .15f, Mathf.Clamp(_timer / Mathf.Max(.05f, Def.Wind), 0, 1)));
    }

    // ------------------------------------------------------------------ summoning
    void SummonAdds()
    {
        if (Def.Summon == null) return;
        for (int i = 0; i < 2; i++)
        {
            var a = i == 0 ? 1f : -1f;
            var pos = GlobalPosition + new Vector3(a * 3f, 0.3f, 2f);
            var e = new Enemy { Type = Def.Summon, Level = Mathf.Max(1, Level - 1), Summoned = true, FromDungeon = FromDungeon, Position = pos, Owner_ = this };
            GetParent().AddChild(e); _summoned++; if (FromDungeon) Dungeon.Register(e);
            Fx.Burst(pos + Vector3.Up, new Color(0.6f, 0.3f, 0.9f), 14, 4f);
        }
        Ui.Note(Def.N + " calls for aid!", new Color(1, 0.6f, 0.4f));
    }
}
