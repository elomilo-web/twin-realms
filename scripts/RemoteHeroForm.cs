using Godot;

namespace TwinRealms;

/// <summary>
/// A-16: a friend in an animal or spirit form. The pose packet's 40th number is the form's index in data/forms.json (0 = none,
/// Player.FormIndex); while it is set the friend's hero model is hidden and the form's creature model (an Enemy "PetBody", the same
/// body the friend sees on their own screen) stands in its place, turned the way they face and animated from how fast they move.
/// </summary>
public partial class RemoteHero
{
    int _formIdx; Node3D? _formHolder; Enemy? _formBody;
    void FormShow(int idx, Vector3 moved, float dt)
    {
        if (idx != _formIdx)
        {
            _formIdx = idx;
            if (_formHolder != null && IsInstanceValid(_formHolder)) _formHolder.QueueFree(); _formHolder = null; _formBody = null;
            var f = Forms.At(idx);
            if (f != null)
            {
                _formHolder = new Node3D { Name = "FormBody", Scale = Vector3.One * Mathf.Max(.1f, f.Scale) }; AddChild(_formHolder);
                _formBody = new Enemy { Type = f.Body, PetBody = true, Level = 1, AnyRealm = true }; _formHolder.AddChild(_formBody);
                if (f.Tint != "" || f.Glow) Callable.From(() => Tint(f)).CallDeferred();
                if (Game.Set.OthersFx) { Fx.Ring(GlobalPosition, 3f, f.Col, .5f); Vfx.Flash(GlobalPosition + Vector3.Up, f.Col, 3f, 6f, .35f); }
            }
            else if (Game.Set.OthersFx) Fx.Ring(GlobalPosition, 3f, new Color(.9f, .9f, 1f), .5f);
        }
        bool on = _formHolder != null && IsInstanceValid(_formHolder);
        if (_rig != null && IsInstanceValid(_rig) && _rig.Visible == on) _rig.Visible = !on;
        if (!on) return;
        _horse.Visible = false; _trail.Emit = false;
        _formHolder!.Visible = !Dead; _formHolder.Rotation = new Vector3(0, _yaw, 0);
        if (_formBody != null && IsInstanceValid(_formBody)) _formBody.PetVel = new Vector3(moved.X, 0, moved.Z) / Mathf.Max(dt, .001f) / Mathf.Max(.3f, _formHolder.Scale.X);
    }
    void Tint(FormDef f)
    {
        if (_formBody == null || !IsInstanceValid(_formBody)) return;
        Color tint = f.Tint != "" && Color.HtmlIsValid(f.Tint) ? Color.FromHtml(f.Tint) : Colors.White;
        foreach (var n in _formBody.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (n is not MeshInstance3D mi || mi.Mesh == null) continue;
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
            {
                if (mi.GetSurfaceOverrideMaterial(s) is not BaseMaterial3D m) continue;
                if (f.Tint != "") m.AlbedoColor *= tint;
                if (f.Glow) { m.EmissionEnabled = true; m.Emission = f.Col.Darkened(.15f); m.EmissionEnergyMultiplier = .9f; }
            }
        }
    }
}
