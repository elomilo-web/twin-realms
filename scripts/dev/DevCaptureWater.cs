using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace TwinRealms;

/// <summary>
/// Dev capture views for swimming, floating loot and boats (F-20, F-21): actions "water:surface", "water:under", "water:loot", "water:boat".
/// All of them use the nearest unfrozen lake (Lands lakes), like the F-03 "swim" action.
/// </summary>
public partial class DevCapture
{
    async Task<(float x, float z, float surf, float r)?> LakeSpot()
    {
        var lk = Lands.All.SelectMany(l => l.Lakes).Where(l => !l.Name.Contains("Frost")).OrderBy(l => l.X * l.X + l.Z * l.Z).FirstOrDefault();
        if (lk == null) { Say("water: no lake"); return null; }
        P.Teleport(new Vector3(lk.X, lk.Level + 1f, lk.Z), 0f); await Secs(1.2f);   // let the block world fill in around it
        float surf = Water.SurfaceAt(new Vector3(lk.X, lk.Level, lk.Z)) ?? lk.Level;
        Say($"water: lake {lk.Name} at ({lk.X:0},{lk.Z:0}) R {lk.R:0} level {lk.Level:0.00} surface {surf:0.00} depth {Water.DepthAt(lk.X, lk.Z, surf):0.0} blocks {BlockWorld.Drawn(lk.X, lk.Z)}");
        return (lk.X, lk.Z, surf, lk.R);
    }

    async Task WaterAction(string what)
    {
        if (await LakeSpot() is not { } L) return;
        switch (what)
        {
            case "surface":
                P.Teleport(new Vector3(L.x, L.surf - 1.35f, L.z), 0f); await Secs(.6f);
                Input.ActionPress(Inputs.Forward); await Secs(1.4f);
                Say($"water:surface: swimming {P.Swimming}, head under {P.HeadUnder}, breath {P.Breath:0.0}, hero {P.GlobalPosition}");
                break;
            case "under":
            {
                float depth = Water.DepthAt(L.x, L.z, L.surf);
                P.Teleport(new Vector3(L.x, L.surf - Mathf.Clamp(depth - .3f, 2f, 4.5f), L.z), 0f); Player.DevSwim = -1; await Secs(.3f);
                P.Breath = Player.MaxBreath * .45f;   // part of the breath used: the bar shows how much is left
                Input.ActionPress(Inputs.Forward); await Secs(.6f);
                // this lake is only ~2 m deep, so the follow camera stays above it: look from a camera under the surface beside the diver
                var hp = P.GlobalPosition; FreeCam(hp + new Vector3(1.6f, Mathf.Min(.7f, L.surf - hp.Y - .5f), 2.2f), hp + new Vector3(0, .5f, -.6f));
                await Secs(.3f);
                var cam = GetViewport().GetCamera3D();
                Say($"water:under: swimming {P.Swimming}, head under {P.HeadUnder}, breath {P.Breath:0.0}, camera {cam?.GlobalPosition} under {Underwater.CamUnder} k {Underwater.K:0.00}, hero {P.GlobalPosition}");
                break;
            }
            case "loot":
            {
                // the hero swims at the surface; five pickups land in the water 4-7 m in front and float off with the current
                P.Teleport(new Vector3(L.x, L.surf - 1.35f, L.z), 0f); await Secs(.5f);
                for (int i = 0; i < 5; i++)
                {
                    var at = new Vector3(L.x - 2f + i * 1.1f, L.surf + .2f, L.z - 5f - (i % 2) * 1.5f);
                    if (i == 0) Loot.Drop(at, gold: 40); else if (i == 1) Loot.Drop(at, pot: "hp"); else if (i == 2) Loot.Drop(at, pot: "mp");
                    else Loot.Drop(at, item: Items.Roll(20, 0, "weapon", 2 + i % 2, null, "sword"));
                }
                await Secs(2.5f);
                var ls = GetTree().GetNodesInGroup("loot").OfType<Loot>().Where(l => l.GlobalPosition.DistanceTo(P.GlobalPosition) < 12f).ToList();
                foreach (var l in ls) Say($"water:loot: {l.GlobalPosition} surface {Water.SurfaceAt(l.GlobalPosition):0.00}");
                FreeCam(new Vector3(L.x + 4f, L.surf + 3.2f, L.z + 2.5f), new Vector3(L.x, L.surf, L.z - 5f));
                break;
            }
            case "boat":
            {
                var b = Boat.Place(new Vector3(L.x, L.surf, L.z + 4f), 0f); await Secs(.3f);
                P.BoardBoat(b); Player.DevBoatThrottle = 1f; await Secs(2.2f);
                if (P.Rig is { } rig) { rig.Rotation = new Vector3(-.3f, rig.Rotation.Y, 0); rig.Distance = 5.5f; }
                await Secs(.3f);
                Say($"water:boat: in boat {P.InBoat != null}, boat {b.GlobalPosition} speed {b.Speed:0.0}, hero {P.GlobalPosition}, prompt '{P.Prompt}'");
                break;
            }
        }
    }

    void DevWaterCleanup()
    {
        Player.DevSwim = 0; Player.DevBoatThrottle = 0;
        if (Player.Instance is { } p) { if (p.InBoat != null) p.LeaveBoat(); p.Breath = Player.MaxBreath; }
        foreach (var b in Boat.All.ToList()) b.QueueFree();
        foreach (var n in GetTree().GetNodesInGroup("loot")) if (n is Loot l && Water.SurfaceAt(l.GlobalPosition) != null) l.QueueFree();
    }
}
