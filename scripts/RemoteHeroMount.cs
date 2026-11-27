using Godot;

namespace TwinRealms;

/// <summary>
/// A friend's mount (A-19): pose slot 30 is 0 on foot, 1 the plain horse, 2 a boat, 10 + species number (PetDb.MountIndex) when they
/// ride a creature: the bought horse's model or their pet. We draw that species' body under them at riding size, seated at its saddle.
/// </summary>
public partial class RemoteHero
{
    float _mountCode, _mountShown = -1, _mountY = .9f; Node3D? _mountHolder; Enemy? _mountBody; Vector3 _mountLast;

    void MountShow(float dt)
    {
        if (_mountCode != _mountShown)
        {
            _mountShown = _mountCode;
            if (_mountHolder != null && IsInstanceValid(_mountHolder)) _mountHolder.QueueFree(); _mountHolder = null; _mountBody = null; _mountY = .9f;
            string species = _mountCode >= 9.5f ? PetDb.MountSpeciesAt(Mathf.RoundToInt(_mountCode) - 10) : "";
            foreach (var c in _horse.GetChildren()) if (c is MeshInstance3D mi) mi.Visible = species == "";
            if (species != "" && EnemyDb.Has(species))
            {
                var sp = PetDb.Species(species);
                _mountHolder = new Node3D { Scale = Vector3.One * sp.MountScale }; _horse.AddChild(_mountHolder);
                _mountBody = Enemy.MakeBody(species); _mountHolder.AddChild(_mountBody);
                if (_mountBody.Creature is { } cm) { var sd = cm.Saddle * sp.MountScale; _mountY = Mathf.Clamp(sd.Y - .5f + sp.SaddleUp, .2f, 4f); _mountHolder.Position = new Vector3(0, 0, -sd.Z - sp.SaddleFwd); }
                else _mountY = Mathf.Clamp(_mountBody.HeightM() * sp.MountScale * .62f + sp.SaddleUp, .5f, 2.6f);
            }
        }
        if (_mountBody != null && _mountHolder != null && IsInstanceValid(_mountBody))
        {
            var v = (GlobalPosition - _mountLast) / Mathf.Max(dt, .001f); _mountLast = GlobalPosition;
            _mountBody.PetVel = new Vector3(v.X, 0, v.Z) / Mathf.Max(.3f, _mountHolder.Scale.X);
            _mountBody.PetAir = PetDb.Species(_mountBody.Type).Flies && GlobalPosition.Y > World.GroundAt(GlobalPosition.X, GlobalPosition.Z) + 2.5f;
        }
    }
}
