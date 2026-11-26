using System.Threading.Tasks;
using Godot;

namespace TwinRealms;

/// <summary>Focus F-18 capture action "protect_test": dig + place inside the town the hero stands in and outside it, then as admin; logged as [F18] lines.</summary>
public partial class DevCapture
{
    async Task ProtectTest()
    {
        if (BlockWorld.I?.Area == null) { Say("[F18] no block world"); return; }
        for (int i = 0; i < 40 && !W.ReadyAround(P.GlobalPosition); i++) await Secs(.1f);
        var home = P.GlobalPosition; float yaw = P.Rotation.Y; byte planks = Blocks.Id("planks");
        Say($"[F18] zones: {string.Join(", ", TownZones.All.ConvertAll(t => $"{t.Name} r{t.R:0}"))}");
        Admin.IsAdmin = false;
        TryDigPlace("inside (player)", planks);
        string? msg = TownZones.Deny(Mathf.FloorToInt(home.X), Mathf.FloorToInt(home.Z));
        Say($"[F18] message inside: '{msg}'");
        Admin.IsAdmin = true; TryDigPlace("inside (admin)", planks); Admin.IsAdmin = false;
        if (FindSpot(5) is { } sp)
        {
            P.Teleport(World.OnGround(sp.X, sp.Y, .5f), yaw); await Secs(1.2f);
            for (int i = 0; i < 40 && !W.ReadyAround(P.GlobalPosition); i++) await Secs(.1f);
            TryDigPlace("outside (player)", planks);
        }
        else Say("[F18] outside: no spot");
        P.Teleport(home, yaw); await Secs(.8f);
        Admin.IsAdmin = false;
        if (msg != null) TownZones.Notify(msg);   // what PlayerBlocks shows on a refused dig or placement
        await Secs(.3f);
    }

    /// <summary>Digs the ground block three metres ahead east of the hero, then places a plank block on top and into the hole; logs each result.</summary>
    void TryDigPlace(string label, byte planks)
    {
        var p = P.GlobalPosition; int bx = Mathf.FloorToInt(p.X) + 3, bz = Mathf.FloorToInt(p.Z), gy = Mathf.FloorToInt(p.Y + .05f) - 1;
        var town = TownZones.AtBlock(bx, bz);
        byte orig = Ar.Get(bx, gy, bz), origMeta = Ar.GetMeta(bx, gy, bz);
        bool dug = BlockFx.Dig(bx, gy, bz);
        string? why = BlockFx.CanPlace(bx, gy + 1, bz);
        bool placed = why == null && BlockFx.Place(bx, gy + 1, bz, planks, 0);
        Say($"[F18] {label} at {bx},{gy},{bz} town '{town?.Name ?? "none"}' admin {Admin.IsAdmin}: dig {(dug ? "ALLOWED" : "DENIED")}, place {(placed ? "ALLOWED" : "DENIED (" + why + ")")}");
        // put things back: refill the dug hole (admin or outside only), remove the test plank
        if (placed) BlockWorld.I!.Change(bx, gy + 1, bz, 0);
        if (dug) BlockWorld.I!.Change(bx, gy, bz, orig, origMeta);
    }
}
