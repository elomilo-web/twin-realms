using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>
/// What the ranch needs from a livestock animal (cow, sheep, pig, chicken, goat, horse). The animals themselves belong to the pets /
/// animals system: its animal node implements this interface and joins the group "livestock", and sets <see cref="Ranch.Spawner"/>
/// so saved ranch animals and babies can be made. Nothing else is needed: Ranch finds the nodes through the group.
/// </summary>
public interface ILivestock
{
    /// <summary>"cow", "sheep", "pig", "chicken", "goat", "horse" (data/farm.json ranch.animals).</summary>
    string Kind { get; }
    /// <summary>"" while wild; the ranch's id once it is yours.</summary>
    string AnimalId { get; set; }
    bool Baby { get; set; }
    Node3D Body { get; }
    /// <summary>Follow this node (the hero carrying its food); null = stop following.</summary>
    void Lead(Node3D? leader);
    /// <summary>Its home and the cells of its pen (wander targets stay inside them); null = roam free.</summary>
    void SetHome(Vector3? home, HashSet<Vector3I>? pen);
}

/// <summary>
/// A-25 ranching (data/farm.json "ranch"; format in its "_help"): leading livestock with their favourite food, pens (a flood fill
/// bounded by fences, closed gates and walls), ownership with the pen as home (PlayerStats.Stead.Animals), breeding and produce.
/// </summary>
public static class Ranch
{
    /// <summary>Makes a livestock animal of a kind at a point (baby or grown), in the scene. Set by the animals system; without it saved
    /// animals stay records and no babies are born.</summary>
    public static Func<string, Vector3, bool, ILivestock?>? Spawner;
    sealed class Kind { public string Food = "", Produce = ""; public float Every = 300; public int Max = 3; }
    static readonly Dictionary<string, Kind> _kinds = new(); static bool _loaded;
    static readonly Dictionary<string, ILivestock> _live = new();
    static readonly Dictionary<ILivestock, float> _led = new();
    static float _t; static int _rr;
    static float R(string k, float d) => Farm.N("ranch." + k, d);

    static void Load()
    {
        if (_loaded) return; _loaded = true;
        if (Farm.Node("ranch.animals") is { ValueKind: JsonValueKind.Object } an)
            foreach (var p in an.EnumerateObject())
                _kinds[p.Name] = new Kind { Food = JsonUtil.S(p.Value, "food"), Produce = JsonUtil.S(p.Value, "produce"), Every = Mathf.Max(1f, JsonUtil.F(p.Value, "every", 300)), Max = (int)JsonUtil.F(p.Value, "max", 3) };
    }
    public static bool IsLivestock(string kind) { Load(); return _kinds.ContainsKey(kind); }
    public static string FoodOf(string kind) { Load(); return _kinds.TryGetValue(kind, out var k) ? k.Food : ""; }
    static AnimalSave? Rec(string id) => id == "" ? null : Stead.S.Animals.Find(a => a.Id == id);
    /// <summary>The animals system tells the ranch an owned animal is gone for good (killed, sold).</summary>
    public static void Remove(string id) { Stead.S.Animals.RemoveAll(a => a.Id == id); _live.Remove(id); }

    // ---------------------------------------------------------------- pens
    static bool Free(byte b) { if (b == 0) return true; var d = Blocks.Def(b); return !d.Solid || d.IsFluid; }
    static bool Climbable(byte b) { if (b == 0 || b == 255) return false; var d = Blocks.Def(b); return d.Solid && !d.IsFluid && d.Shape is "" or "slab" or "stairs"; }
    /// <summary>
    /// The cells an animal standing in <paramref name="start"/> can walk to: 4-way, one block up or a drop of up to three, never
    /// through or over fences and closed gates (their collision is 1.5 high). Null when the area is open (more than max cells, or it
    /// reaches blocks that are not loaded): then it is no pen.
    /// </summary>
    public static HashSet<Vector3I>? Pen(BlockArea a, Vector3I start, int max)
    {
        var seen = new HashSet<Vector3I> { start }; var q = new Queue<Vector3I>(); q.Enqueue(start);
        while (q.Count > 0)
        {
            var c = q.Dequeue();
            foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = c.X + dx, nz = c.Z + dz; byte b0 = a.Get(nx, c.Y, nz), b1 = a.Get(nx, c.Y + 1, nz);
                if (b0 == 255 || b1 == 255) return null;
                Vector3I? to = null;
                if (Free(b0) && Free(b1))
                {
                    int ny = c.Y; while (ny > c.Y - 4 && Free(a.Get(nx, ny - 1, nz))) ny--;
                    if (ny > c.Y - 4) to = new Vector3I(nx, ny, nz);
                }
                else if (Climbable(b0) && Free(b1) && Free(a.Get(nx, c.Y + 2, nz)) && Free(a.Get(c.X, c.Y + 2, c.Z))) to = new Vector3I(nx, c.Y + 1, nz);
                if (to is { } t && seen.Add(t)) { if (seen.Count > max) return null; q.Enqueue(t); }
            }
        }
        return seen;
    }
    static Vector3I CellOf(Vector3 p) => new(Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Y + .3f), Mathf.FloorToInt(p.Z));
    static HashSet<Vector3I>? PenAt(Vector3 p)
    {
        var a = BlockWorld.I?.Area; if (a == null || a.Key != "overworld") return null;
        return Pen(a, CellOf(p), (int)R("penMax", 400));
    }
    static Vector3 Centre(HashSet<Vector3I> pen) { var s = Vector3.Zero; foreach (var c in pen) s += new Vector3(c.X + .5f, c.Y, c.Z + .5f); return s / Mathf.Max(1, pen.Count); }

    // ---------------------------------------------------------------- the tick
    public static void Tick(Player p, float dt)
    {
        Load(); string tag = Stead.WorldTag; if (tag == "guest" || p.Dead) return;
        float now = Game.D.Played; var tree = p.GetTree(); if (tree == null) return;
        var all = new List<ILivestock>();
        foreach (var n in tree.GetNodesInGroup("livestock")) if (n is ILivestock l && GodotObject.IsInstanceValid(l.Body) && IsLivestock(l.Kind)) all.Add(l);
        Near(p, all, now, tag);
        _t -= dt; if (_t > 0) return; _t = Mathf.Max(.25f, R("scan", 1f));
        // saved animals whose node is gone (a new session): made again when the hero comes near their home
        _live.Clear(); foreach (var l in all) if (l.AnimalId != "") _live[l.AnimalId] = l;
        if (Spawner != null)
            foreach (var s in Stead.S.Animals)
            {
                if (s.World != tag || _live.ContainsKey(s.Id)) continue;
                var at = new Vector3(s.X, s.Y, s.Z); if (at.DistanceTo(p.GlobalPosition) > 48f || BlockWorld.I?.ReadyAround(at) != true) continue;
                if (Spawner(s.Kind, at, s.Baby) is not { } made) continue;
                made.AnimalId = s.Id; made.Baby = s.Baby; made.SetHome(new Vector3(s.Hx, s.Hy, s.Hz), PenAt(at)); _live[s.Id] = made; all.Add(made);
                break;   // one per tick
            }
        // leads run out
        foreach (var l in _led.Keys.ToList())
            if (!GodotObject.IsInstanceValid(l.Body) || now > _led[l] || l.Body.GlobalPosition.DistanceTo(p.GlobalPosition) > R("leadRange", 10f) * 2.5f) { if (GodotObject.IsInstanceValid(l.Body)) StopLead(l, tag, now); else _led.Remove(l); }
        if (all.Count == 0) return;
        // one animal per tick: where it stands, whether it grew up, whether it stands in a pen
        var an = all[_rr++ % all.Count]; var pos = an.Body.GlobalPosition; var rec = Rec(an.AnimalId);
        if (rec != null)
        {
            rec.X = pos.X; rec.Y = pos.Y; rec.Z = pos.Z;
            if (rec.Baby && now - rec.Born > R("growTime", 600f)) { rec.Baby = false; an.Baby = false; Ui.Note($"Your young {an.Kind} has grown up", UiTheme.Highlight); }
        }
        else if (!_led.ContainsKey(an) && an.AnimalId == "" && PenAt(pos) is { } pen) Claim(an, pen, tag, now);
    }
    static void Claim(ILivestock an, HashSet<Vector3I> pen, string tag, float now)
    {
        var s = Stead.S; var pos = an.Body.GlobalPosition; var home = Centre(pen);
        var rec = Rec(an.AnimalId);
        if (rec == null)
        {
            rec = new AnimalSave { Id = "a" + (++s.AnimalSeq), Kind = an.Kind, World = tag, Baby = an.Baby, Born = now, ProdT = now, BreedT = -99999f, LoveT = -99999f };
            s.Animals.Add(rec); an.AnimalId = rec.Id; Ui.Note($"The {an.Kind} settles into your pen: it is yours now", UiTheme.Highlight); Sfx.Play("pickup", .6f);
        }
        rec.X = pos.X; rec.Y = pos.Y; rec.Z = pos.Z; rec.Hx = home.X; rec.Hy = home.Y; rec.Hz = home.Z;
        an.SetHome(home, pen); _live[rec.Id] = an;
    }
    static void StopLead(ILivestock an, string tag, float now)
    {
        _led.Remove(an); an.Lead(null);
        if (PenAt(an.Body.GlobalPosition) is { } pen) Claim(an, pen, tag, now); else an.SetHome(null, null);
    }

    // ---------------------------------------------------------------- Interact on the nearest animal
    static void Near(Player p, List<ILivestock> all, float now, string tag)
    {
        if (p.Prompt != "" || all.Count == 0) return;
        ILivestock? best = null; float bd = 3.2f;
        foreach (var l in all) { float d = l.Body.GlobalPosition.DistanceTo(p.GlobalPosition); if (d < bd && l.Body.IsVisibleInTree()) { bd = d; best = l; } }
        if (best == null || !_kinds.TryGetValue(best.Kind, out var k)) return;
        var mats = Game.D.Mats; bool food = k.Food != "" && mats.GetValueOrDefault(k.Food) > 0, led = _led.ContainsKey(best); var rec = Rec(best.AnimalId);
        string key = "[" + Inputs.KeyOf(Inputs.Use) + "] ", name = char.ToUpper(best.Kind[0]) + best.Kind[1..], fn = GatherDb.Mat(k.Food).Name; Action? act = null; string text;
        int ready = rec != null && k.Produce != "" && !rec.Baby ? Math.Min(k.Max, (int)((now - rec.ProdT) / k.Every)) : 0;
        if (led) { text = key + "Stop leading the " + best.Kind; act = () => StopLead(best, tag, now); }
        else if (ready > 0) { text = key + $"Collect {ready} {GatherDb.Mat(k.Produce).Name}"; act = () => { rec!.ProdT = now; GatherDb.Give(k.Produce, ready, best.Body.GlobalPosition); LifeSkills.Award(Farm.Skill, Farm.N("xp.collect", 4)); Sfx.Play("pickup", .7f); }; }
        else if (rec != null && food && !rec.Baby && now - rec.BreedT > R("breedCooldown", 600f) && now - rec.LoveT > R("loveTime", 60f)) { text = key + $"Feed {fn} (breed)"; act = () => Feed(best, rec, k, now, tag); }
        else if (food) { text = key + $"Lead the {best.Kind} ({fn})"; act = () => { _led[best] = now + R("leadTime", 60f); best.SetHome(null, null); best.Lead(p); Ui.Note($"The {best.Kind} follows you: walk it into a fenced pen and close the gate", Ui.Dim); }; }
        else text = rec != null ? $"{name} (yours){(rec.Baby ? ", young" : k.Produce != "" ? ", nothing to collect yet" : "")}" : $"{name}: carry {fn} to lead it";
        p.Prompt = text;
        if (act != null && Input.IsActionJustPressed(Inputs.Use)) act();
    }
    static void Feed(ILivestock an, AnimalSave rec, Kind k, float now, string tag)
    {
        var m = Game.D.Mats; if (m.GetValueOrDefault(k.Food) <= 0) return;
        m[k.Food]--; if (m[k.Food] <= 0) m.Remove(k.Food); rec.LoveT = now; Game.Notify(); Sfx.Play("heal", .5f, 1.3f);
        var pos = an.Body.GlobalPosition; float range = R("breedRange", 6f), love = R("loveTime", 60f);
        foreach (var (id, other) in _live)
        {
            if (other == an || !GodotObject.IsInstanceValid(other.Body) || other.Kind != an.Kind || Rec(id) is not { } o || o.Baby) continue;
            if (now - o.LoveT > love || other.Body.GlobalPosition.DistanceTo(pos) > range) continue;
            rec.BreedT = o.BreedT = now; rec.LoveT = o.LoveT = -99999f;
            int count = Stead.S.Animals.Count(a => a.World == tag && new Vector3(a.Hx, a.Hy, a.Hz).DistanceTo(new Vector3(rec.Hx, rec.Hy, rec.Hz)) < 1f);
            if (count >= (int)R("maxPerPen", 12)) { Ui.Note("The pen is too crowded for a young one", Ui.Dim); return; }
            var at = (pos + other.Body.GlobalPosition) * .5f;
            if (Spawner?.Invoke(an.Kind, at, true) is not { } baby) { Ui.Note($"The {an.Kind}s nuzzle each other", Ui.Dim); return; }
            var b = new AnimalSave { Id = "a" + (++Stead.S.AnimalSeq), Kind = an.Kind, World = tag, Baby = true, Born = now, ProdT = now, BreedT = now, LoveT = -99999f, X = at.X, Y = at.Y, Z = at.Z, Hx = rec.Hx, Hy = rec.Hy, Hz = rec.Hz };
            Stead.S.Animals.Add(b); baby.AnimalId = b.Id; baby.Baby = true; baby.SetHome(new Vector3(b.Hx, b.Hy, b.Hz), PenAt(at)); _live[b.Id] = baby;
            LifeSkills.Award(Farm.Skill, Farm.N("xp.breed", 12)); Ui.Note($"A young {an.Kind} is born!", UiTheme.Highlight); Sfx.Play("level", .6f);
            return;
        }
        Ui.Note($"The {an.Kind} is ready to breed: feed another grown {an.Kind} beside it", Ui.Dim);
    }
}
