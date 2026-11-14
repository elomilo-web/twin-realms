using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>Other heroes to mark on the minimap and the map: your party members (or everyone online when you have no party) who are where you are.</summary>
public static class MapMarks
{
    public static readonly Color PartyCol = new(.45f, 1f, .55f), OtherCol = new(.6f, .8f, 1f);

    public static IEnumerable<(string Name, Vector3 Pos, bool Party)> Friends()
    {
        var net = Net.I; if (!Net.Active || net == null) yield break;
        int z = Net.MyZone; if (z != 0 && z < 1000) yield break;   // the arena and solo dungeons are yours alone
        var party = net.MyParty;
        foreach (var kv in net.Heroes)
        {
            var h = kv.Value; if (!GodotObject.IsInstanceValid(h) || !h.HasPose || h.Zone != z) continue;
            bool inParty = party != null && party.Members.Contains(kv.Key);
            if (party != null && !inParty) continue;
            yield return (h.PlayerName, h.GlobalPosition, inParty);
        }
    }

    /// <summary>A hero marker: a dot with a dark ring (dead heroes are greyed by the caller if needed).</summary>
    public static void Dot(CanvasItem ci, Vector2 at, float r, Color col)
    {
        ci.DrawCircle(at, r + 1.5f, new Color(0, 0, 0, .8f)); ci.DrawCircle(at, r, col);
    }
}
