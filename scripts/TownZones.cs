using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>
/// F-18 protected town zones (data/towns.json). Inside a zone block digging, placing and block-breaking blasts are refused
/// unless Admin.IsAdmin. Static and independent of the Pois list so a dedicated server can use it to reject edits too.
/// </summary>
public static class TownZones
{
    public sealed class Zone { public string Id = "", Name = ""; public float X, Z, R; }
    static List<Zone>? _zones; static string _msg = "This is {town}: building and digging are not allowed in town."; static int _landsSeen = -1;
    static ulong _lastNote;

    public static List<Zone> All
    {
        get
        {
            if (_zones != null && _landsSeen == Lands.All.Count) return _zones;
            _landsSeen = Lands.All.Count; _zones = new();
            using var doc = JsonUtil.Load("res://data/towns.json"); var root = doc.RootElement;
            _msg = JsonUtil.S(root, "message", _msg);
            foreach (var t in root.GetProperty("towns").EnumerateArray())
            {
                float r = JsonUtil.F(t, "radius", 40f);
                if (t.TryGetProperty("land", out var lid) && t.TryGetProperty("place", out var pid))
                {
                    var p = Lands.All.Find(l => l.Id == lid.GetString())?.Places.Find(q => q.Id == pid.GetString());
                    if (p != null) _zones.Add(new Zone { Id = lid.GetString() + "_" + p.Id, Name = p.Name, X = p.X, Z = p.Z, R = r });
                }
                else _zones.Add(new Zone { Id = JsonUtil.S(t, "id"), Name = JsonUtil.S(t, "name", "this town"), X = JsonUtil.F(t, "x"), Z = JsonUtil.F(t, "z"), R = r });
            }
            return _zones;
        }
    }

    /// <summary>The zone containing this world point (XZ), whoever asks.</summary>
    public static Zone? At(float x, float z)
    {
        foreach (var t in All) { float dx = x - t.X, dz = z - t.Z; if (dx * dx + dz * dz < t.R * t.R) return t; }
        return null;
    }
    /// <summary>The zone a block cell is in, ignoring admins (server-side checks).</summary>
    public static Zone? AtBlock(int bx, int bz) => At(bx + .5f, bz + .5f);
    /// <summary>The refusal text for a zone (the host sends it with a rejected edit).</summary>
    public static string Message(Zone z) => _msg.Replace("{town}", z.Name);

    /// <summary>True if this machine's player may not change the block here (in a zone and not an admin).</summary>
    public static bool Blocks(int bx, int bz) => !Admin.IsAdmin && AtBlock(bx, bz) != null;
    /// <summary>The refusal text for a block cell, or null if changing it is allowed.</summary>
    public static string? Deny(int bx, int bz) => Admin.IsAdmin || AtBlock(bx, bz) is not { } z ? null : _msg.Replace("{town}", z.Name);

    /// <summary>Shows the refusal now and then (not every frame).</summary>
    public static void Notify(string msg)
    {
        ulong now = Time.GetTicksMsec(); if (now - _lastNote < 1800 && _lastNote != 0) return;
        _lastNote = now; Ui.Note(msg, new Color(1, .6f, .5f));
    }
}
