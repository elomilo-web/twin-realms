using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>data/pets.json "duel": Soul Duel tuning, element wheel, statuses, items, ladder ranks, trainer names, Pet Trader and trade limits.</summary>
public static class DuelDb
{
    public static JsonElement D => PetDb.Root.GetProperty("duel");
    public static float F(string k, float d) => JsonUtil.F(D, k, d);
    public static string S(string k, string d = "") => JsonUtil.S(D, k, d);
    public static JsonElement Sub(string k) => D.GetProperty(k);
    public static int RankCount => D.GetProperty("ranks").GetArrayLength();
    public static JsonElement Rank(int r) { var a = D.GetProperty("ranks"); return a[Math.Clamp(r, 0, a.GetArrayLength() - 1)]; }
    public static string RankTitle(int r) => JsonUtil.S(Rank(r), "title", "Duelist");
    public static List<string> Strings(string k) { var l = new List<string>(); if (D.TryGetProperty(k, out var a)) foreach (var x in a.EnumerateArray()) l.Add(x.GetString() ?? ""); return l; }
    /// <summary>Element multiplier of an attack element against a defender element: the wheel (x strong), its reverse and the same element (x weak); physical is neutral.</summary>
    public static float Elem(string atk, string def)
    {
        if (atk == "physical" || def == "physical") return 1f;
        var w = D.GetProperty("wheel");
        if (JsonUtil.S(w, atk) == def) return F("strong", 1.5f);
        if (JsonUtil.S(w, def) == atk || atk == def) return F("weak", .67f);
        return 1f;
    }
    public static JsonElement? Status(string id) => D.GetProperty("status").TryGetProperty(id, out var s) ? s : null;
    public static float StatusF(string id, string k, float d) => Status(id) is { } s ? JsonUtil.F(s, k, d) : d;
    public static string StatusName(string id) => Status(id) is { } s ? JsonUtil.S(s, "name", id) : id;
    public static Color StatusColor(string id) => Status(id) is { } s ? Color.FromHtml(JsonUtil.S(s, "color", "#dddddd")) : Colors.White;
    public static JsonElement Item(string id) => D.GetProperty("items").GetProperty(id);
}

/// <summary>One pet in a Soul Duel: its stats at its level, duel hp and focus, statuses (id -> turns left), guard and attack buff, and its model.</summary>
public class DuelPet
{
    public PetState S = null!; public PetStats St; public int MaxHp, Hp, Focus, GuardT, BuffT; public float BuffMul = 1;
    public readonly Dictionary<string, int> Status = new();
    public Node3D? Root; public Enemy? Body; public float H = 1.4f;
    public bool Ko => Hp <= 0;
    public string Name => S.Name();
    public float HpF => MaxHp > 0 ? Hp / (float)MaxHp : 0;
    public DuelPet(PetState s)
    {
        S = s; St = s.Stats(); MaxHp = Math.Max(10, Mathf.RoundToInt(St.Hp * DuelDb.F("hpMul", .6f))); Hp = MaxHp; Focus = (int)DuelDb.F("focusStart", 2);
        if (s.Skills.Count == 0) s.LearnUpTo();
    }
    public List<PetSkillDef> Moves() { var l = new List<PetSkillDef>(); foreach (var id in S.Skills.Take(4)) if (PetDb.Skill(id) is { } k) l.Add(k); if (l.Count == 0 && PetDb.Skill(DuelDb.S("fallback", "strike")) is { } f) l.Add(f); return l; }
    public float Spd => St.Spd * (Status.ContainsKey("chill") ? DuelDb.StatusF("chill", "spdMul", .5f) : 1f);
    public void Reset() { Status.Clear(); GuardT = 0; BuffT = 0; BuffMul = 1; }
}

/// <summary>A team in the duel: the hero's (IsPlayer) or a Duelist of the Pit's, with items left.</summary>
public class DuelSide
{
    public string Name = "", Title = ""; public bool IsPlayer; public List<DuelPet> Team = new(); public int Cur, Potions, Revives; public float Smart = 1;
    public DuelPet Active => Team[Cur];
    public bool Lost => Team.All(p => p.Ko);
    public int Alive => Team.Count(p => !p.Ko);
}

/// <summary>
/// Soul Duels: turn-based pet battles in the Crimson Arena against generated Duelists of the Pit on a ranked ladder (data/pets.json "duel").
/// Started by Arenas.Start("souls", team). Both pets stand on the sand facing each other (their Enemy PetBody models), framed by the
/// duel's own camera; the hero watches behind their pet (Player.Frozen). Each turn: a skill, swap, item or forfeit; swaps and items go
/// first, then skill priority, then speed. KO collapses the pet and forces a swap. Wins pay valor, gold, pet xp, cubes and pet gear,
/// and move PlayerStats.DuelRank up. A turn runs as a queue of timed steps (_q) so the attacks, numbers and texts play out in order.
/// </summary>
public partial class SoulDuel : CanvasLayer
{
    public static SoulDuel? I;
    public static bool Active => I != null && IsInstanceValid(I);
    public string Phase = "intro";            // intro / command / resolve / forced / over
    public int Turn;
    public DuelSide Me = null!, Foe = null!;
    bool _dev; int _rank; readonly Random _rn = new();
    Camera3D _cam = null!; Camera3D? _prev; Node3D _stage = null!; Npc? _trainer;
    Vector3 _c, _myAt, _foeAt;
    readonly Queue<Func<float>> _q = new(); float _wait;
    struct Act { public string Kind; public PetSkillDef? Sk; public int Arg; }
    readonly Dictionary<DuelSide, Act> _acts = new();
    readonly Dictionary<DuelSide, (int dmg, float mul, bool crit, bool miss)> _pend = new();

    // ------------------------------------------------------------------ start / stop
    /// <summary>From Arenas.Start("souls"): the hero's team (pet ids; up to 3). dev: a capture run (no rewards, no saving).</summary>
    public static void Begin(List<string> team, bool dev)
    {
        Stop();
        var d = Game.D; var pets = team.Select(id => d.Pets.Find(p => p.Id == id)).Where(p => p != null).Take(3).ToList();
        if (pets.Count == 0) pets = d.Pets.Take(3).ToList();
        if (pets.Count == 0 || Game.Root == null) { Ui.Note("You need a bound soul to duel.", new Color(1, .6f, .5f)); Arenas.End(false); return; }
        var sd = new SoulDuel { Name = "SoulDuel", _dev = dev };
        sd.Me = new DuelSide { Name = d.Name, IsPlayer = true, Potions = (int)JsonUtil.F(DuelDb.Item("potion"), "count", 2), Revives = (int)JsonUtil.F(DuelDb.Item("revive"), "count", 1) };
        foreach (var p in pets) sd.Me.Team.Add(new DuelPet(p!));
        I = sd; Game.Root.AddChild(sd);
    }
    public static void Stop() { if (Active) I!.Cleanup(); I = null; }

    public override void _Ready()
    {
        Layer = 15; ProcessMode = ProcessModeEnum.Pausable;
        _rank = Math.Clamp(Game.D.DuelRank, 0, DuelDb.RankCount - 1);
        Foe = MakeTrainer(_rank);
        _c = Builder.ArenaPos; _c.Y = World.GroundAt(_c.X, _c.Z);
        _myAt = World.OnGround(_c.X - 3.4f, _c.Z, 0); _foeAt = World.OnGround(_c.X + 3.4f, _c.Z, 0);
        _stage = new Node3D { Name = "SoulDuelStage" }; Game.Root!.AddChild(_stage);
        _prev = GetViewport().GetCamera3D();
        _cam = new Camera3D { Fov = 50f, Far = 800f }; _stage.AddChild(_cam);
        _cam.GlobalPosition = _c + new Vector3(-.6f, 3.1f, 8.8f); _cam.LookAt(_c + new Vector3(0, 1.0f, 0), Vector3.Up); _cam.Current = true;
        var p = Player.Instance; if (p != null) p.Teleport(World.OnGround(_c.X - 7.5f, _c.Z + 1.6f, .3f), -Mathf.Pi / 2);
        _trainer = new Npc { Id = "duelist" + _rank, NpcName = Foe.Name, Role = "arena", Position = World.OnGround(_c.X + 7.5f, _c.Z - 1.4f, 0) }; _stage.AddChild(_trainer);
        BuildUi();
        Spawn(Me, true); Spawn(Foe, false);
        Phase = "intro"; _wait = LoadScreen.Covering ? 1.2f : .4f;
        Step(() => { Say($"{Foe.Name}, {Foe.Title}, wants to duel!"); Sfx.Play("roar", .6f); return 1.6f; });
        Step(() => { Say($"Go, {Me.Active.Name}!"); Sfx.Play("portal", .6f); return .9f; });
        Step(() => { Command(); return 0; });
        Refresh();
    }

    void Cleanup()
    {
        Player.Frozen = false;
        if (_prev != null && IsInstanceValid(_prev)) _prev.Current = true;
        if (IsInstanceValid(_stage)) _stage.QueueFree();
        QueueFree();
        if (I == this) I = null;
    }

    DuelSide MakeTrainer(int rank)
    {
        var r = DuelDb.Rank(rank); var fn = DuelDb.Strings("firstNames"); var ln = DuelDb.Strings("lastNames"); var pool = DuelDb.Strings("pool");
        string name = (fn.Count > 0 ? fn[_rn.Next(fn.Count)] : "Duelist") + " " + (ln.Count > 0 ? ln[_rn.Next(ln.Count)] : "");
        var s = new DuelSide { Name = name.Trim(), Title = JsonUtil.S(r, "title", "Duelist"), Potions = (int)JsonUtil.F(r, "potions"), Smart = JsonUtil.F(r, "smart", .5f) };
        int avg = Mathf.RoundToInt((float)Me.Team.Average(p => p.S.Level)), lvl = Math.Max(1, avg + (int)JsonUtil.F(r, "lvl"));
        int size = Math.Clamp((int)JsonUtil.F(r, "size", 1), 1, 3);
        for (int i = 0; i < size; i++)
        {
            string sp = pool.Count > 0 ? pool[_rn.Next(pool.Count)] : "wolf";
            var p = PetDb.New(sp, Math.Max(1, lvl - _rn.Next(2)), "iron"); p.Ot = s.Name; p.Bond = 30;
            s.Team.Add(new DuelPet(p));
        }
        return s;
    }

    // ------------------------------------------------------------------ models
    void Spawn(DuelSide side, bool mine)
    {
        foreach (var dp in side.Team) if (dp.Root != null && IsInstanceValid(dp.Root)) { dp.Root.QueueFree(); dp.Root = null; dp.Body = null; }
        var a = side.Active; var at = mine ? _myAt : _foeAt; var to = mine ? _foeAt : _myAt;
        var root = new Node3D { Name = mine ? "MyPet" : "FoePet" }; _stage.AddChild(root); root.GlobalPosition = at;
        var d = to - at; root.Rotation = new Vector3(0, Util.YawTo(d.X, d.Z), 0);
        var vis = new Node3D(); root.AddChild(vis);
        var body = new Enemy { Type = a.S.Species, PetBody = true, Level = a.S.Level, AnyRealm = true }; vis.AddChild(body);
        float h = Mathf.Max(.3f, body.HeightM()), k = Mathf.Clamp(1.5f / h, .2f, 1f); vis.Scale = Vector3.One * k; a.H = h * k;
        a.Root = root; a.Body = body;
        if (a.Ko) body.PetDown(true);
        Fx.Burst(at + Vector3.Up * .8f, PetDb.Cube(a.S.Cube).Col.Lerp(new Color(1f, .85f, .5f), .5f), 18, 3f, .6f, .1f, 2f, "magic");
    }
    Vector3 Chest(DuelPet p) => (p.Root?.GlobalPosition ?? _c) + Vector3.Up * Mathf.Max(.5f, p.H * .55f);
    Vector3 Head(DuelPet p) => (p.Root?.GlobalPosition ?? _c) + Vector3.Up * (p.H + .35f);

    // ------------------------------------------------------------------ step queue
    void Step(Func<float> f) => _q.Enqueue(f);
    public override void _Process(double delta)
    {
        float dt = (float)delta;
        Player.Frozen = true; if (Phase is "command" or "forced" or "over") Input.MouseMode = Input.MouseModeEnum.Visible;
        _bottom.Visible = _menuShown && !Ui.AnyOpen;
        // a slow breathing sway of the duel camera
        _camT += dt; _cam.GlobalPosition = _c + new Vector3(-.6f + Mathf.Sin(_camT * .25f) * .5f, 3.6f + Mathf.Sin(_camT * .4f) * .08f, 11f); _cam.LookAt(_c + new Vector3(0, 1.0f, 0), Vector3.Up);
        if (LoadScreen.Covering) return;
        if (_wait > 0) { _wait -= dt; return; }
        while (_q.Count > 0 && _wait <= 0) { _wait = _q.Dequeue()(); Refresh(); }
    }
    float _camT;

    // ------------------------------------------------------------------ choosing
    void Command()
    {
        Phase = "command"; _menu = ""; Say($"What will {Me.Active.Name} do?"); Menu();
    }
    /// <summary>The hero's choice for this turn (buttons, the capture tool): a skill index, "swap" + team index, "item" + 0 potion / 1 revive, "forfeit".</summary>
    public void Choose(string kind, int arg)
    {
        if (Phase == "forced")
        {
            if (kind != "swap" || arg < 0 || arg >= Me.Team.Count || Me.Team[arg].Ko) return;
            Me.Cur = arg; Spawn(Me, true); Say($"Go, {Me.Active.Name}!"); Sfx.Play("portal", .6f); Phase = "resolve"; HideMenu();
            Step(() => { Command(); return 0; }); _wait = .8f; return;
        }
        if (Phase != "command") return;
        var a = new Act { Kind = kind, Arg = arg };
        if (kind == "skill") { var mv = Me.Active.Moves(); if (arg < 0 || arg >= mv.Count) return; a.Sk = mv[arg]; if (a.Sk.Cost > Me.Active.Focus) { Say("Not enough focus."); return; } }
        if (kind == "swap" && (arg < 0 || arg >= Me.Team.Count || arg == Me.Cur || Me.Team[arg].Ko)) return;
        if (kind == "item" && (arg == 0 ? Me.Potions <= 0 : Me.Revives <= 0 || !Me.Team.Any(p => p.Ko))) return;
        if (kind == "forfeit") { HideMenu(); Phase = "resolve"; Step(() => { Say($"{Me.Name} forfeits the duel."); return 1.4f; }); Step(() => { Finish(false); return 0; }); return; }
        HideMenu(); Phase = "resolve"; Resolve(a);
    }
    public void DevChoose(int skill) => Choose("skill", Math.Clamp(skill, 0, Me.Active.Moves().Count - 1));

    Act Ai(DuelSide me, DuelSide foe)
    {
        var a = me.Active; var f = foe.Active; bool best = _rn.NextDouble() < me.Smart;
        var mv = a.Moves().Where(m => m.Cost <= a.Focus).ToList();
        if (mv.Count == 0) return new Act { Kind = "skill", Sk = PetDb.Skill(DuelDb.S("fallback", "strike")) ?? a.Moves()[0] };
        float hp = a.HpF;
        if (hp < .3f && me.Potions > 0 && best) return new Act { Kind = "item", Arg = 0 };
        if (hp < .38f && mv.FirstOrDefault(m => m.Kind == "heal") is { } heal && (best || _rn.NextDouble() < .5)) return new Act { Kind = "skill", Sk = heal };
        // out-matched: the foe's element beats ours and we cannot hurt it well; a bench pet that fares better comes in
        if (best && hp > .25f && !a.Status.ContainsKey("root") && me.Alive > 1)
        {
            float threat = BestMul(f, a.S.Elem()), mine = BestMul(a, f.S.Elem());
            if (threat > 1.01f && mine <= 1f)
                for (int i = 0; i < me.Team.Count; i++)
                {
                    var b = me.Team[i]; if (i == me.Cur || b.Ko || b.HpF < .4f) continue;
                    if (BestMul(f, b.S.Elem()) <= 1f && BestMul(b, f.S.Elem()) >= 1f && _rn.NextDouble() < .7) return new Act { Kind = "swap", Arg = i };
                }
        }
        if (!best) { var r = mv[_rn.Next(mv.Count)]; return new Act { Kind = "skill", Sk = r }; }
        PetSkillDef? pick = null; float score = -1;
        foreach (var m in mv)
        {
            float s = m.Kind switch
            {
                "attack" => m.Power * m.Acc / 100f * DuelDb.Elem(m.Elem, f.S.Elem()) * (m.Elem == "physical" ? a.St.Atk : a.St.Mag) / Mathf.Max(1f, m.Elem == "physical" ? a.St.Atk : a.St.Mag)
                            + (m.Effect != "" && !f.Status.ContainsKey(m.Effect) ? m.Chance * 25 : 0) - m.Cost * 2,
                "heal" => hp < .5f ? 70 * (1 - hp) : -1,
                "shield" => hp < .55f && a.GuardT <= 0 ? 38 : -1,
                "buff" => a.BuffT <= 0 && hp > .6f ? 46 : -1,
                "debuff" => m.Effect != "" && !f.Status.ContainsKey(m.Effect) ? 36 : -1,
                _ => -1,
            };
            // finishing blow: prefer the strongest hit when the foe is nearly down
            if (m.Kind == "attack" && f.HpF < .25f) s += m.Power * .3f;
            if (s > score) { score = s; pick = m; }
        }
        return new Act { Kind = "skill", Sk = pick ?? mv[0] };
    }
    static float BestMul(DuelPet att, string defElem)
    {
        float b = 0; foreach (var m in att.Moves()) if (m.Kind == "attack") b = Mathf.Max(b, DuelDb.Elem(m.Elem, defElem));
        return b <= 0 ? 1 : b;
    }

    // ------------------------------------------------------------------ resolving a turn
    void Resolve(Act mine)
    {
        Turn++; _acts[Me] = mine; _acts[Foe] = Ai(Foe, Me);
        int Pri(DuelSide s) { var a = _acts[s]; return a.Kind is "swap" or "item" ? 100 : a.Sk?.Priority ?? 0; }
        DuelSide first = Me, second = Foe;
        int pm = Pri(Me), pf = Pri(Foe);
        if (pf > pm || pf == pm && (Foe.Active.Spd > Me.Active.Spd || Mathf.IsEqualApprox(Foe.Active.Spd, Me.Active.Spd) && _rn.Next(2) == 0)) { first = Foe; second = Me; }
        foreach (var s in new[] { first, second })
        {
            var side = s; var actor = side.Active;
            Step(() => Perform(side, actor));
            Step(() => Land(side, actor));
        }
        Step(EndOfTurn);
    }
    DuelSide Other(DuelSide s) => s == Me ? Foe : Me;

    float Perform(DuelSide side, DuelPet actor)
    {
        _pend.Remove(side);
        if (actor.Ko || actor != side.Active) return 0;
        var act = _acts[side]; var foe = Other(side).Active;
        switch (act.Kind)
        {
            case "swap":
                {
                    var old = side.Active; side.Cur = act.Arg; old.Reset(); Spawn(side, side == Me);
                    Say(side == Me ? $"Come back, {old.Name}! Go, {side.Active.Name}!" : $"{side.Name} calls back {old.Name} and sends out {side.Active.Name}!");
                    Sfx.Play("portal", .6f); return 1.1f;
                }
            case "item":
                {
                    if (act.Arg == 0) { side.Potions--; var p = side.Active; int n = Mathf.RoundToInt(p.MaxHp * JsonUtil.F(DuelDb.Item("potion"), "heal", .5f)); p.Hp = Math.Min(p.MaxHp, p.Hp + n);
                        Say($"{(side == Me ? "You" : side.Name)} used {JsonUtil.S(DuelDb.Item("potion"), "name", "a potion")} on {p.Name}."); FloatText.Spawn(_stage, Head(p), "+" + n, new Color(.5f, 1f, .6f), 1.1f); Vfx.HealPillarAt(p.Root!.GlobalPosition, new Color(.5f, 1f, .6f), 1f); Sfx.Play("heal", .7f); }
                    else { var p = side.Team.FirstOrDefault(x => x.Ko); if (p != null) { side.Revives--; p.Hp = Mathf.RoundToInt(p.MaxHp * JsonUtil.F(DuelDb.Item("revive"), "heal", .5f)); p.Reset(); Say($"{JsonUtil.S(DuelDb.Item("revive"), "name", "A revive")} rekindles {p.Name}!"); Sfx.Play("level", .6f); } }
                    return 1.2f;
                }
        }
        var sk = act.Sk!;
        // losing the action to a status
        foreach (var (id, _) in actor.Status.ToList())
        {
            float skip = DuelDb.StatusF(id, "skip", 0);
            if (skip > 0 && _rn.NextDouble() < skip) { Say($"{actor.Name} is {DuelDb.StatusName(id).ToLower()} and cannot act!"); FloatText.Spawn(_stage, Head(actor), DuelDb.StatusName(id), DuelDb.StatusColor(id), .9f); if (id == "stun") actor.Status.Remove(id); return 1.0f; }
        }
        actor.Focus = Math.Max(0, actor.Focus - sk.Cost);
        Say($"{(side == Me ? "" : "The foe's ")}{actor.Name} uses {sk.Name}!");
        var col = sk.Elem == "physical" ? new Color(1f, .85f, .7f) : Dmg.TypeColor(sk.Elem);
        switch (sk.Kind)
        {
            case "heal": Vfx.HealPillarAt(actor.Root!.GlobalPosition, new Color(.5f, 1f, .6f), 1f); Sfx.Play("heal", .7f); break;
            case "shield": Fx.Ring(actor.Root!.GlobalPosition + Vector3.Up * .1f, 1.6f, new Color(.7f, .85f, 1f), .6f); Sfx.Play("block", .6f); break;
            case "buff": Fx.Ring(actor.Root!.GlobalPosition + Vector3.Up * .1f, 2.2f, new Color(1f, .7f, .35f), .6f); Sfx.Play("roar", .5f, 1.4f); actor.Body?.PetSwing(.3f); break;
            default:
                actor.Body?.PetSwing(.3f);
                if (sk.Kind == "attack") _pend[side] = Calc(actor, foe, sk);
                if (sk.Fx is "bolt" or "breath") Bolt(Chest(actor), Chest(foe), col, .35f, sk.Fx == "breath");
                else if (sk.Fx == "nova") Fx.Ring(actor.Root!.GlobalPosition + Vector3.Up * .1f, 2.5f, col, .45f);
                else Lunge(actor, foe);
                Sfx.Play(sk.Fx == "bolt" ? "zap" : "swing", .6f);
                break;
        }
        return .45f;
    }

    (int dmg, float mul, bool crit, bool miss) Calc(DuelPet a, DuelPet d, PetSkillDef sk)
    {
        if (_rn.NextDouble() * 100 > sk.Acc) return (0, 1, false, true);
        bool phys = sk.Elem == "physical";
        float A = phys ? a.St.Atk : a.St.Mag, D = phys ? d.St.Def : (d.St.Def + d.St.Mag) / 2f;
        float b = (DuelDb.F("levelK", .4f) * a.S.Level + 2) * sk.Power * A / Mathf.Max(1f, D) / DuelDb.F("dmgDiv", 50) + 2;
        float mul = DuelDb.Elem(sk.Elem, d.S.Elem()); bool crit = _rn.NextDouble() < DuelDb.F("critChance", .06f);
        float k = mul * Mathf.Lerp(DuelDb.F("rollMin", .85f), 1f, (float)_rn.NextDouble()) * (crit ? DuelDb.F("critMul", 1.5f) : 1) * (a.BuffT > 0 ? a.BuffMul : 1) * (d.GuardT > 0 ? .5f : 1);
        foreach (var id in a.Status.Keys) k *= DuelDb.StatusF(id, "dmgMul", 1);
        foreach (var id in d.Status.Keys) k *= DuelDb.StatusF(id, "takenMul", 1);
        return (Math.Max(1, Mathf.RoundToInt(b * k)), mul, crit, false);
    }

    float Land(DuelSide side, DuelPet actor)
    {
        var act = _acts[side]; if (act.Kind != "skill" || actor.Ko || actor != side.Active) return 0;
        var sk = act.Sk!; var foe = Other(side).Active; var col = sk.Elem == "physical" ? new Color(1f, .85f, .7f) : Dmg.TypeColor(sk.Elem);
        switch (sk.Kind)
        {
            case "heal":
                { int n = Mathf.RoundToInt(actor.MaxHp * sk.Power / 100f); actor.Hp = Math.Min(actor.MaxHp, actor.Hp + n); FloatText.Spawn(_stage, Head(actor), "+" + n, new Color(.5f, 1f, .6f), 1.1f); Say($"{actor.Name} mends its wounds."); return .9f; }
            case "shield": actor.GuardT = Math.Max(1, sk.Turns); Say($"{actor.Name} braces itself."); return .8f;
            case "buff": actor.BuffT = Math.Max(1, sk.Turns); actor.BuffMul = sk.BuffMul; Say($"{actor.Name}'s attack rises!"); FloatText.Spawn(_stage, Head(actor), "ATK UP", new Color(1f, .75f, .4f), .9f); return .8f;
            case "debuff":
                if (foe.Ko) return 0;
                if (_rn.NextDouble() * 100 > sk.Acc || sk.Effect == "" || _rn.NextDouble() > Mathf.Max(sk.Chance, .01f)) { Say("It had no effect."); FloatText.Spawn(_stage, Head(foe), DuelDb.S("missText", "Missed"), new Color(.75f, .75f, .8f), .9f); return .8f; }
                foe.Status[sk.Effect] = Math.Max(1, sk.Turns); Say($"{foe.Name} is {DuelDb.StatusName(sk.Effect).ToLower()}!"); FloatText.Spawn(_stage, Head(foe), DuelDb.StatusName(sk.Effect), DuelDb.StatusColor(sk.Effect), 1f); foe.Body?.PetFlinch(actor.Root!.GlobalPosition); return .9f;
        }
        if (foe.Ko) return 0;
        if (!_pend.TryGetValue(side, out var r)) return 0;
        if (r.miss) { FloatText.Spawn(_stage, Head(foe), DuelDb.S("missText", "Missed"), new Color(.75f, .75f, .8f), 1f); Say($"{actor.Name}'s attack misses."); return .8f; }
        foe.Hp = Math.Max(0, foe.Hp - r.dmg);
        var hit = Chest(foe);
        if (sk.Elem != "physical") Vfx.ElementImpact(sk.Elem, hit, 1.1f); else Fx.Burst(hit, col, 10, 3.5f, .35f, .1f);
        FloatText.Spawn(_stage, Head(foe), (r.crit ? "-" + r.dmg + "!" : "-" + r.dmg), r.mul > 1 ? new Color(1f, .55f, .25f) : r.mul < 1 ? new Color(.7f, .75f, .85f) : new Color(1f, .9f, .8f), r.crit || r.mul > 1 ? 1.4f : 1.1f);
        foe.Body?.PetFlinch(actor.Root!.GlobalPosition); Sfx.Play("hit", .6f, 1.1f);
        string msg = r.mul > 1 ? DuelDb.S("strongText", "Searing!") : r.mul < 1 ? DuelDb.S("weakText", "Resisted...") : "";
        if (r.crit) msg = (msg + " A telling blow!").Trim();
        if (msg != "") { Say(msg); if (r.mul != 1) FloatText.Spawn(_stage, Head(foe) + Vector3.Up * .45f, r.mul > 1 ? DuelDb.S("strongText", "Searing!") : DuelDb.S("weakText", "Resisted..."), r.mul > 1 ? new Color(1f, .6f, .3f) : new Color(.7f, .75f, .9f), .9f); }
        if (sk.Effect != "" && !foe.Ko && foe.Hp > 0 && _rn.NextDouble() < sk.Chance) { foe.Status[sk.Effect] = Math.Max(1, sk.Turns); Say($"{msg} {foe.Name} is {DuelDb.StatusName(sk.Effect).ToLower()}!".Trim()); }
        if (foe.Hp <= 0) Collapse(foe);
        return foe.Hp <= 0 ? 1.4f : 1.0f;
    }
    void Collapse(DuelPet p)
    {
        p.Body?.PetDown(true); p.Reset(); Say($"{p.Name} collapses!"); Sfx.Play("die", .6f);
        Fx.Burst(Chest(p), PetDb.Cube(p.S.Cube).Col.Lerp(new Color(1f, .85f, .5f), .5f), 20, 3f, .8f, .1f, 2f, "magic");
    }

    float EndOfTurn()
    {
        foreach (var s in new[] { Me, Foe })
        {
            var p = s.Active; if (p.Ko) continue;
            foreach (var (id, _) in p.Status.ToList())
            {
                float dot = DuelDb.StatusF(id, "dot", 0);
                if (dot > 0) { int n = Math.Max(1, Mathf.RoundToInt(p.MaxHp * dot)); p.Hp = Math.Max(0, p.Hp - n); FloatText.Spawn(_stage, Head(p), "-" + n, DuelDb.StatusColor(id), .9f); }
                if (--p.Status[id] <= 0) p.Status.Remove(id);
            }
            if (p.GuardT > 0) p.GuardT--; if (p.BuffT > 0 && --p.BuffT <= 0) p.BuffMul = 1;
            p.Focus = Math.Min((int)DuelDb.F("focusMax", 6), p.Focus + (int)DuelDb.F("focusRegen", 1));
            if (p.Hp <= 0) Collapse(p);
        }
        Step(AfterKo);
        return .5f;
    }
    float AfterKo()
    {
        if (Me.Lost || Foe.Lost) { bool won = Foe.Lost && !Me.Lost; Step(() => { Finish(won); return 0; }); return .6f; }
        if (Foe.Active.Ko)
        {
            int best = -1; float bs = -99;
            for (int i = 0; i < Foe.Team.Count; i++) { var b = Foe.Team[i]; if (b.Ko) continue; float s = BestMul(b, Me.Active.S.Elem()) - BestMul(Me.Active, b.S.Elem()) + b.HpF; if (s > bs) { bs = s; best = i; } }
            Foe.Cur = best; Spawn(Foe, false); Say($"{Foe.Name} sends out {Foe.Active.Name}!"); Sfx.Play("portal", .6f);
            Step(AfterKo); return 1.2f;
        }
        if (Me.Active.Ko) { Phase = "forced"; _menu = "swap"; Say($"Choose who fights next."); Menu(); return 0; }
        Command(); return 0;
    }

    void Finish(bool won)
    {
        Phase = "over"; var d = Game.D; var lines = new List<string>(); var r = DuelDb.Rank(_rank);
        if (won)
        {
            Say($"You defeated {Foe.Name}, {Foe.Title}!"); Sfx.Play("level"); Ui.Banner("Victory", Foe.Title + " falls");
            if (!_dev)
            {
                int v = (int)JsonUtil.F(r, "valor"), g = (int)JsonUtil.F(r, "gold"), xp = (int)JsonUtil.F(r, "petXp", 50);
                Valor.Give(v); d.Gold += g; lines.Add($"+{v} valor, +{g} gold");
                foreach (var p in Me.Team) { int ups = p.S.GainXp(p.Ko ? xp / 2 : xp); lines.Add($"{p.Name} +{(p.Ko ? xp / 2 : xp)} xp" + (ups > 0 ? $"  (level {p.S.Level}!)" : "")); }
                if (_rn.NextDouble() < JsonUtil.F(r, "cubeChance")) { string c = JsonUtil.S(r, "cube", "iron"); d.Cubes[c] = Cubes.Count(c) + 1; lines.Add($"+1 {PetDb.Cube(c).Name}"); }
                if (_rn.NextDouble() < JsonUtil.F(r, "gearChance")) { var it = PetDb.RollGear(Mathf.RoundToInt((float)Me.Team.Average(p => p.S.Level)), 0); if (d.Inv.Count < 28) d.Inv.Add(it); else d.Stash.Add(it); lines.Add("Pet gear: " + it.Name); }
                d.SoulWins++; d.DuelRank = Math.Min(DuelDb.RankCount - 1, _rank + 1);
                lines.Add(_rank + 1 < DuelDb.RankCount ? $"Ladder: you rise to face a {DuelDb.RankTitle(d.DuelRank)}" : "Ladder: Champion of the Pit. The champions keep coming.");
                Game.Save(); Game.Notify();
            }
        }
        else
        {
            Say(Me.Lost ? "Your souls are spent. The crowd jeers." : "You yield the pit."); Sfx.Play("die", .5f);
            if (!_dev)
            {
                int xp = (int)(JsonUtil.F(r, "petXp", 50) * .25f); foreach (var p in Me.Team) p.S.GainXp(xp); lines.Add($"Each pet +{xp} xp for the trying");
                d.SoulLosses++; Game.Save(); Game.Notify();
            }
        }
        _result = string.Join("\n", lines); _menu = "over"; Menu();
    }
    string _result = "";

    /// <summary>After a win: the next Duelist on the ladder, the same team rested.</summary>
    void Again()
    {
        foreach (var p in Me.Team) { p.Hp = p.MaxHp; p.Reset(); p.Focus = (int)DuelDb.F("focusStart", 2); p.St = p.S.Stats(); }
        Me.Cur = 0; Me.Potions = (int)JsonUtil.F(DuelDb.Item("potion"), "count", 2); Me.Revives = (int)JsonUtil.F(DuelDb.Item("revive"), "count", 1);
        _rank = Math.Clamp(Game.D.DuelRank, 0, DuelDb.RankCount - 1); Foe = MakeTrainer(_rank); Turn = 0;
        if (_trainer != null && IsInstanceValid(_trainer)) _trainer.QueueFree();
        _trainer = new Npc { Id = "duelist" + _rank + _rn.Next(99), NpcName = Foe.Name, Role = "arena", Position = World.OnGround(_c.X + 7.5f, _c.Z - 1.4f, 0) }; _stage.AddChild(_trainer);
        Spawn(Me, true); Spawn(Foe, false); HideMenu(); Phase = "intro";
        Step(() => { Say($"{Foe.Name}, {Foe.Title}, steps into the pit!"); Sfx.Play("roar", .6f); return 1.6f; });
        Step(() => { Command(); return 0; });
    }

    // ------------------------------------------------------------------ effects
    void Lunge(DuelPet a, DuelPet f)
    {
        if (a.Root == null || f.Root == null) return; var home = a.Root.GlobalPosition; var dir = (f.Root.GlobalPosition - home) with { Y = 0 };
        var tw = a.Root.CreateTween(); tw.TweenProperty(a.Root, "global_position", home + dir.Normalized() * Mathf.Min(2.2f, dir.Length() * .45f), .18f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tw.TweenProperty(a.Root, "global_position", home, .3f).SetDelay(.12f);
    }
    void Bolt(Vector3 a, Vector3 b, Color col, float t, bool big)
    {
        var orb = new MeshInstance3D { Mesh = new SphereMesh { Radius = big ? .3f : .17f, Height = big ? .6f : .34f, RadialSegments = 10, Rings = 5 }, MaterialOverride = new StandardMaterial3D { AlbedoColor = col, EmissionEnabled = true, Emission = col, EmissionEnergyMultiplier = 3f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _stage.AddChild(orb); orb.GlobalPosition = a;
        var tw = orb.CreateTween(); tw.TweenProperty(orb, "global_position", b, t); tw.TweenCallback(Callable.From(orb.QueueFree));
    }

    // ------------------------------------------------------------------ interface
    Control _root = null!; PanelContainer _bottom = null!; Label _msg = null!; Card _mine = null!, _theirs = null!; VBoxContainer _menuBox = null!;
    string _menu = ""; bool _forfeitAsk;
    class Card { public Label Name = null!, Hp = null!, Focus = null!, St = null!, Team = null!; public ProgressBar Bar = null!; }
    static readonly Color Parch = new(.89f, .83f, .70f), Gold = new(.85f, .68f, .32f), DimC = new(.6f, .58f, .52f);

    void BuildUi()
    {
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore }; _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); AddChild(_root);
        _mine = MakeCard(false); _theirs = MakeCard(true);
        _msg = L("", 24, Parch); _msg.HorizontalAlignment = HorizontalAlignment.Center; _msg.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _msg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop); _msg.OffsetLeft = -380; _msg.OffsetRight = 380; _msg.OffsetTop = 212; _msg.OffsetBottom = 282; _root.AddChild(_msg);
        _bottom = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        _bottom.AddThemeStyleboxOverride("panel", Box(new Color(.04f, .035f, .03f, .9f), Gold with { A = .6f }, 14));
        _bottom.AnchorLeft = .5f; _bottom.AnchorRight = .5f; _bottom.AnchorTop = 1; _bottom.AnchorBottom = 1;
        void Place() { var s = GetViewport().GetVisibleRect().Size; float w = Mathf.Min(1060, s.X - 48); _bottom.OffsetLeft = -w / 2; _bottom.OffsetRight = w / 2; _bottom.OffsetTop = -214; _bottom.OffsetBottom = -18; }
        Place(); GetViewport().SizeChanged += Place; _root.TreeExiting += () => GetViewport().SizeChanged -= Place;
        _root.AddChild(_bottom);
        _menuBox = new VBoxContainer(); _menuBox.AddThemeConstantOverride("separation", 6); _bottom.AddChild(_menuBox);
        _bottom.Visible = false;
    }
    static StyleBoxFlat Box(Color bg, Color border, int pad) => new() { BgColor = bg, BorderColor = border, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1, ContentMarginLeft = pad, ContentMarginRight = pad, ContentMarginTop = pad * .7f, ContentMarginBottom = pad * .7f, CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3 };
    static Label L(string t, int size, Color c, Node? parent = null)
    {
        var l = new Label { Text = t, MouseFilter = Control.MouseFilterEnum.Ignore }; l.AddThemeFontSizeOverride("font_size", size); l.AddThemeColorOverride("font_color", c);
        l.AddThemeColorOverride("font_outline_color", Colors.Black); l.AddThemeConstantOverride("outline_size", 6); parent?.AddChild(l); return l;
    }
    Card MakeCard(bool right)
    {
        var pc = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(360, 0) };
        pc.AddThemeStyleboxOverride("panel", Box(new Color(.04f, .035f, .03f, .82f), (right ? new Color(.85f, .35f, .3f) : Gold) with { A = .7f }, 12));
        if (right) { pc.AnchorLeft = 1; pc.AnchorRight = 1; pc.OffsetLeft = -384; pc.OffsetRight = -24; } else { pc.OffsetLeft = 24; pc.OffsetRight = 384; }
        pc.OffsetTop = 24; _root.AddChild(pc);
        var v = new VBoxContainer(); v.AddThemeConstantOverride("separation", 3); pc.AddChild(v);
        var c = new Card { Team = L("", 14, DimC, v), Name = L("", 20, Parch, v) };
        c.Bar = new ProgressBar { MinValue = 0, MaxValue = 1, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 12), MouseFilter = Control.MouseFilterEnum.Ignore };
        c.Bar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(.1f, .08f, .07f) }); v.AddChild(c.Bar);
        c.Hp = L("", 14, Parch, v); c.Focus = L("", 15, new Color(.55f, .8f, 1f), v); c.St = L("", 14, Parch, v);
        return c;
    }
    void FillCard(Card c, DuelSide s)
    {
        var p = s.Active; var sp = p.S.Sp();
        c.Team.Text = (s.IsPlayer ? s.Name : $"{s.Name}, {s.Title}") + "   " + string.Join(" ", s.Team.Select(x => x.Ko ? "◇" : "◆")) + (s.IsPlayer ? $"   draughts {s.Potions}  embers {s.Revives}" : "");
        c.Name.Text = $"{p.Name}   Lv {p.S.Level}" + (sp.Elem != "physical" ? $"   [{sp.Elem}]" : "");
        c.Name.AddThemeColorOverride("font_color", sp.Elem == "physical" ? Parch : Dmg.TypeColor(sp.Elem).Lerp(Parch, .35f));
        c.Bar.Value = p.HpF; var hc = p.HpF > .5f ? new Color(.35f, .8f, .35f) : p.HpF > .2f ? new Color(.9f, .75f, .25f) : new Color(.85f, .25f, .2f);
        c.Bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = hc });
        c.Hp.Text = $"HP {p.Hp} / {p.MaxHp}";
        int fm = (int)DuelDb.F("focusMax", 6); c.Focus.Text = "Focus " + new string('●', Math.Min(p.Focus, fm)) + new string('○', Math.Max(0, fm - p.Focus));
        var st = p.Status.Select(kv => $"{DuelDb.StatusName(kv.Key)} {kv.Value}").ToList(); if (p.GuardT > 0) st.Add($"Guard {p.GuardT}"); if (p.BuffT > 0) st.Add($"Fury {p.BuffT}");
        c.St.Text = st.Count > 0 ? string.Join("  ", st) : " ";
        c.St.AddThemeColorOverride("font_color", p.Status.Count > 0 ? DuelDb.StatusColor(p.Status.Keys.First()) : Parch);
    }
    void Refresh() { if (Me == null || Foe == null || _mine == null) return; FillCard(_mine, Me); FillCard(_theirs, Foe); }
    void Say(string t) { if (_msg != null) _msg.Text = t; }
    void HideMenu() { _menuShown = false; if (_bottom != null) _bottom.Visible = false; }
    bool _menuShown;

    Button Big(Node parent, string text, Action a, Color? col = null, bool ok = true, float w = 0, float h = 58)
    {
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.All, Disabled = !ok, CustomMinimumSize = new Vector2(w, h), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        b.AddThemeFontSizeOverride("font_size", 17);
        b.AddThemeStyleboxOverride("normal", Box(new Color(.09f, .075f, .06f, .95f), Gold with { A = .35f }, 10));
        b.AddThemeStyleboxOverride("hover", Box(new Color(.16f, .12f, .07f, .98f), Gold, 10));
        b.AddThemeStyleboxOverride("focus", Box(new Color(.16f, .12f, .07f, .98f), new Color(1f, .85f, .4f), 10));
        b.AddThemeStyleboxOverride("pressed", Box(new Color(.25f, .18f, .08f, 1f), Gold, 10));
        b.AddThemeStyleboxOverride("disabled", Box(new Color(.06f, .05f, .045f, .8f), Gold with { A = .12f }, 10));
        var c = col ?? Parch; b.AddThemeColorOverride("font_color", c); b.AddThemeColorOverride("font_hover_color", c.Lightened(.2f)); b.AddThemeColorOverride("font_focus_color", c.Lightened(.2f)); b.AddThemeColorOverride("font_disabled_color", new Color(.42f, .4f, .36f));
        b.MouseEntered += Sfx.UiHover; b.Pressed += () => { Sfx.Play("click"); a(); };
        parent.AddChild(b); return b;
    }

    /// <summary>The command panel: skills (2 x 2) with Swap / Item / Forfeit beside them, or a sub-list (swap, items) with Back, or the result.</summary>
    void Menu()
    {
        foreach (var c in _menuBox.GetChildren()) c.QueueFree();
        _menuShown = true; _bottom.Visible = !Ui.AnyOpen; Button? first = null;
        if (_menu == "over")
        {
            L(_result == "" ? (_dev ? "(capture run: no rewards)" : " ") : _result, 16, Parch, _menuBox).AutowrapMode = TextServer.AutowrapMode.WordSmart;
            var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 10); _menuBox.AddChild(h);
            if (Foe.Lost && !Me.Lost) first = Big(h, $"Next duelist ({DuelDb.RankTitle(Game.D.DuelRank)})", Again);
            var lv = Big(h, "Leave the pit", () => Arenas.Leave()); first ??= lv;
        }
        else if (_menu == "swap")
        {
            L(Phase == "forced" ? "Who fights next?" : "Swap: who steps in? (the swap comes before any attack)", 15, DimC, _menuBox);
            var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 8); _menuBox.AddChild(h);
            for (int i = 0; i < Me.Team.Count; i++)
            {
                int k = i; var p = Me.Team[i]; bool ok = !p.Ko && (i != Me.Cur || Phase == "forced" && !p.Ko && i != Me.Cur);
                var b = Big(h, $"{p.Name}  Lv{p.S.Level}\n{(p.Ko ? "spent" : $"HP {p.Hp}/{p.MaxHp}")}{(i == Me.Cur ? "  (fighting)" : "")}", () => Choose("swap", k), null, ok && i != Me.Cur, 0, 70);
                if (ok && i != Me.Cur) first ??= b;
            }
            if (Phase != "forced") { var bk = Big(_menuBox, "Back", () => { _menu = ""; Menu(); }, DimC, true, 0, 40); first ??= bk; }
        }
        else if (_menu == "item")
        {
            L("Items (using one takes the turn, before any attack)", 15, DimC, _menuBox);
            var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 8); _menuBox.AddChild(h);
            var po = DuelDb.Item("potion"); var rv = DuelDb.Item("revive");
            var b1 = Big(h, $"{JsonUtil.S(po, "name", "Potion")}  x{Me.Potions}\nheals {JsonUtil.F(po, "heal", .5f) * 100:0}% of {Me.Active.Name}", () => Choose("item", 0), new Color(.6f, 1f, .65f), Me.Potions > 0, 0, 70);
            var b2 = Big(h, $"{JsonUtil.S(rv, "name", "Revive")}  x{Me.Revives}\nrekindles a spent pet ({JsonUtil.F(rv, "heal", .5f) * 100:0}%)", () => Choose("item", 1), new Color(1f, .8f, .45f), Me.Revives > 0 && Me.Team.Any(p => p.Ko), 0, 70);
            if (!b1.Disabled) first = b1; else if (!b2.Disabled) first = b2;
            var bk = Big(_menuBox, "Back", () => { _menu = ""; Menu(); }, DimC, true, 0, 40); first ??= bk;
        }
        else
        {
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); _menuBox.AddChild(row);
            var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; grid.AddThemeConstantOverride("h_separation", 8); grid.AddThemeConstantOverride("v_separation", 8); row.AddChild(grid);
            var a = Me.Active; var mv = a.Moves(); var foeE = Foe.Active.S.Elem();
            for (int i = 0; i < 4; i++)
            {
                if (i >= mv.Count) { var e = Big(grid, "-", () => { }, DimC, false, 300, 70); continue; }
                int k = i; var m = mv[i]; float mul = m.Kind == "attack" ? DuelDb.Elem(m.Elem, foeE) : 1;
                string info = m.Kind switch { "attack" => $"{m.Elem}  {m.Power:0} pow  {m.Acc:0}%", "heal" => $"heals {m.Power:0}%", "shield" => "guard: half damage", "buff" => "raises attack", "debuff" => DuelDb.StatusName(m.Effect).ToLower() + " the foe", _ => m.Kind };
                string tag = mul > 1 ? "  ▲" : mul < 1 ? "  ▼" : "";
                var b = Big(grid, $"{m.Name}{tag}\n{info}   focus {m.Cost}", () => Choose("skill", k), m.Elem == "physical" ? Parch : Dmg.TypeColor(m.Elem).Lerp(Parch, .3f), m.Cost <= a.Focus, 300, 70);
                b.TooltipText = m.Desc; if (!b.Disabled) first ??= b;
            }
            var side = new VBoxContainer { CustomMinimumSize = new Vector2(210, 0) }; side.AddThemeConstantOverride("separation", 6); row.AddChild(side);
            bool rooted = a.Status.ContainsKey("root");
            Big(side, rooted ? "Swap (rooted)" : "Swap pet", () => { _menu = "swap"; Menu(); }, null, !rooted && Me.Team.Where((p, i) => i != Me.Cur && !p.Ko).Any(), 0, 44);
            Big(side, "Item", () => { _menu = "item"; Menu(); }, null, Me.Potions > 0 || Me.Revives > 0 && Me.Team.Any(p => p.Ko), 0, 44);
            Big(side, _forfeitAsk ? "Really forfeit?" : "Forfeit", () => { if (!_forfeitAsk) { _forfeitAsk = true; Menu(); return; } _forfeitAsk = false; Choose("forfeit", 0); }, new Color(.9f, .5f, .45f), true, 0, 44);
        }
        if (first != null) { var f = first; Callable.From(() => { if (IsInstanceValid(f) && f.IsVisibleInTree()) f.GrabFocus(); }).CallDeferred(); }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!(Phase is "command") || _menu == "" || !e.IsActionPressed("ui_cancel")) return;
        _menu = ""; Menu(); GetViewport().SetInputAsHandled();
    }
}
