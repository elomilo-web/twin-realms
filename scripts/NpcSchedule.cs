using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TwinRealms;

/// <summary>An NPC that has a node now: its record, what the schedule has it doing, and its social state (NpcSocial, F-29).</summary>
public sealed class NpcLive
{
    public Npc N = null!; public NpcRec Rec = null!; public NpcAct Act = new(); public string Key = "";
    public readonly List<Vector3> Path = new(); public int At; public NpcHomes.House? Inside; public bool Hidden, Moving; public Vector3 Last; public float Still;
    // social: Busy until this clock time (a chat or walking up to someone: the schedule waits), cooldowns, what it last did
    public float Busy, ChatCd, GreetCd, SayAt = -1f, SayBy; public string SayText = "", LastAct = ""; public bool Wet; public NpcLive? Partner;
}

/// <summary>
/// F-27: runs the daily schedules of data/npcs.json (NpcDb). A child of the overworld root (pauses with it, like CampStreamer).
/// Only NPCs whose current place is within ~120 m of the hero get a node (freed past 140 m); the rest are records. Where an NPC
/// should be follows from World.TimeOfDay alone (synced by Net), so the people are client-local yet the same for every player.
/// Activity changes walk the NPC there with Npc.WalkTo through house doors (door, street point in front, then the next place);
/// asleep = inside the home (or a roadside bedroll) and hidden. A blocked or stuck walker skips to its next waypoint.
/// Core NPCs (also in data/quests.json) are spawned by Builder and only moved / hidden here, never freed.
/// F-29: NpcSocial runs on the same tick (chats, greetings, weather and time-of-day reactions); an NPC that is busy with one waits,
/// then walks back to where its schedule wants it.
/// </summary>
public partial class NpcSchedule : Node
{
    public static int Spawned;
    readonly Dictionary<string, NpcLive> _live = new();
    static readonly Dictionary<string, Npc> _core = new();
    public static void Bind(Npc n) => _core[n.Id] = n;
    const float Near = 120f, Far = 140f, Dt = .2f;
    double _t; float _lastH = -1f;

    static NpcSchedule? _i;
    public override void _Ready() => _i = this;
    public override void _Process(double delta) { Story.Tick((float)delta); _t -= delta; if (_t > 0) return; _t = Dt; Tick(); }   // Story: quest objectives and time limits (F-31)
    /// <summary>The NPC's node, if it has one now (for /npc and the later Phase 6 items).</summary>
    public static Npc? NodeOf(string id) => _i != null && IsInstanceValid(_i) && _i._live.TryGetValue(id, out var l) && IsInstanceValid(l.N) ? l.N : null;
    public static NpcLive? LiveOf(string id) => _i != null && IsInstanceValid(_i) && _i._live.TryGetValue(id, out var l) && IsInstanceValid(l.N) ? l : null;
    /// <summary>Every NPC with a node now (hidden sleepers too).</summary>
    public static IEnumerable<NpcLive> Live => _i != null && IsInstanceValid(_i) ? _i._live.Values.Where(l => IsInstanceValid(l.N)) : Enumerable.Empty<NpcLive>();
    public static string NodeInfo(string id) => NodeOf(id) is { } n ? $"node at {n.GlobalPosition.X:0.0},{n.GlobalPosition.Y:0.0},{n.GlobalPosition.Z:0.0}{(n.Visible ? "" : " hidden")}{(n.Goal != null ? " walking" : "")}" : "no node";

    public static float Flat(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();
    static Vector3 Stand(Vector3 v) => float.IsNaN(v.Y) ? World.OnGround(v.X, v.Z) : v;
    static void Show(NpcLive L, bool on)
    {
        L.Hidden = !on; L.N.Visible = on; L.N.ProcessMode = on ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
        if (on) { if (!L.N.IsInGroup("interact")) L.N.AddToGroup("interact"); } else { L.N.RemoveFromGroup("interact"); L.N.Goal = null; }
    }

    void Tick()
    {
        var p = Player.Instance; if (p == null || NpcDb.All.Count == 0) return;
        var hp = p.GlobalPosition; float h = NpcDb.Hour; int budget = 3;
        bool jump = _lastH >= 0 && Mathf.Abs(Mathf.Wrap(h - _lastH, -12f, 12f)) > .5f; _lastH = h;
        NpcDb.Shelter = Weather.Rain > NpcSocial.RainAt;   // F-29: in the rain the square empties into the inn
        foreach (var r in NpcDb.All)
        {
            var (at, act, hidden, moving, house) = NpcDb.Target(r, h);
            _live.TryGetValue(r.Id, out var L);
            if (L != null && !IsInstanceValid(L.N)) { _live.Remove(r.Id); L = null; }
            if (Story.NpcGone(r.Id))
            {   // F-31: a quest sent this one away (left town, jailed, dead): no node; a core NPC is kept but hidden
                if (L != null && L.N.Gone && NpcSocial.Clock - L.N.GoneAt < 8f) continue;   // just killed: the body lies a moment
                if (L != null) { NpcSocial.Break(L); if (r.Core) { if (!L.Hidden) Show(L, false); } else { L.N.QueueFree(); _live.Remove(r.Id); } }
                else if (r.Core && _core.TryGetValue(r.Id, out var gn) && IsInstanceValid(gn) && gn.Visible) { _live[r.Id] = L = new NpcLive { N = gn, Rec = r, Last = gn.GlobalPosition }; Show(L, false); }
                continue;
            }
            if (L == null && r.Core)
            {
                if (!_core.TryGetValue(r.Id, out var cn) || !IsInstanceValid(cn)) continue;
                _live[r.Id] = L = new NpcLive { N = cn, Rec = r, Last = cn.GlobalPosition };
            }
            if (L == null)
            {   // a record only: give it a node once its place is near the hero (asleep ones too, hidden: they wake and walk out)
                if (Flat(at, hp) > Near || budget-- <= 0) continue;
                Color? tint = r.Color != "" ? Color.FromHtml(r.Color) : null;
                var n = new Npc { Id = r.Id, NpcName = r.Name, Role = r.Talk, Lines = r.Greet, QuestIds = r.Quests, Outfit = r.Outfit, Tint = tint, Sex = r.Sex, Position = Stand(at) };
                GetParent().AddChild(n);
                _live[r.Id] = L = new NpcLive { N = n, Rec = r, Act = act, Moving = moving, Key = act.Act + "|" + act.Place, Inside = house, Last = n.GlobalPosition, LastAct = act.Act, Wet = NpcDb.Shelter };
                if (hidden) Show(L, false);
                continue;
            }
            if (!r.Core && Flat(L.N.GlobalPosition, hp) > Far && Flat(at, hp) > Far) { L.N.QueueFree(); _live.Remove(r.Id); continue; }
            L.Act = act; L.Moving = moving;
            if (L.N.Down) continue;   // knocked out (F-32)
            string key = act.Act + "|" + act.Place + (moving ? "" : $"|{Mathf.Round(at.X)},{Mathf.Round(at.Z)}");
            if (jump || L.Key == "")
            {   // the clock jumped (sleep, dev time, rejoin) or a core NPC's first tick: everyone is simply where the schedule says
                L.Key = key; L.Path.Clear(); L.At = 0; L.Inside = house; Snap(L, at); L.N.Goal = null; L.Busy = 0; L.Partner = null; L.N.FaceAt = null; L.LastAct = act.Act;
                if (hidden != L.Hidden) Show(L, !hidden);
                continue;
            }
            if (L.Hidden) { if (hidden) continue; Show(L, true); }
            if (L.Key != key) { L.Key = key; Plan(L, at, house, moving); NpcSocial.Break(L); }
            if (L.Busy > NpcSocial.Clock) { L.Still = 0; L.Last = L.N.GlobalPosition; continue; }   // talking to someone: the schedule waits
            Step(L, at, hidden, moving);
        }
        Spawned = _live.Values.Count(l => !l.Hidden);
        NpcSocial.Tick(_live.Values, p, Dt); Crime.Tick(Dt);
    }

    static void Plan(NpcLive L, Vector3 at, NpcHomes.House? house, bool moving)
    {
        L.Path.Clear(); L.At = 0; L.Still = 0;
        if (house != null && house == L.Inside) { L.Path.Add(at); return; }   // from room to room of the same house
        if (L.Inside is { } hi) { L.Path.Add(hi.Door); L.Path.Add(hi.Out); }
        if (house != null) { L.Path.Add(house.Out); L.Path.Add(house.Door); }
        if (!moving) L.Path.Add(at);
        L.Inside = house;
    }

    static void Snap(NpcLive L, Vector3 wp) { L.N.Goal = null; L.N.GlobalPosition = Stand(wp); L.Last = L.N.GlobalPosition; L.Still = 0; }

    static void Step(NpcLive L, Vector3 at, bool hidden, bool moving)
    {
        var n = L.N;
        if (L.At < L.Path.Count)
        {
            var wp = L.Path[L.At];
            if (Flat(n.GlobalPosition, wp) < .35f) { L.At++; n.Goal = null; return; }
            if (n.Stopped != "" && n.Goal == null) { n.Stopped = ""; Snap(L, wp); L.At++; return; }   // a wall or a locked door in the way
            if (n.Goal == null) n.WalkTo(wp);
            L.Still = Flat(n.GlobalPosition, L.Last) < .15f ? L.Still + Dt : 0f; L.Last = n.GlobalPosition;
            if (L.Still > 3f) { Snap(L, wp); L.At++; }   // stuck on something: skip ahead
            return;
        }
        if (moving)
        {   // patrol rounds and roads: follow the moving point
            float d = Flat(n.GlobalPosition, at);
            if (d > 15f || n.Stopped != "") { n.Stopped = ""; Snap(L, at); return; }
            if (d > .5f) { n.WalkSpeed = d > 4f ? 2.4f : 1.6f; n.WalkTo(at); }
            return;
        }
        if (hidden && !L.Hidden && n.Goal == null) { Show(L, false); return; }   // home (or in the bedroll): asleep
        // F-29: after a chat somewhere else, back to the spot the schedule wants
        if (!hidden && n.Goal == null && Flat(n.GlobalPosition, at) > .9f)
        {
            if (n.Stopped != "") { n.Stopped = ""; Snap(L, at); }
            else { n.WalkSpeed = 1.6f; n.WalkTo(Stand(at)); }
        }
    }
}
