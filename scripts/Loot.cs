using Godot;

namespace TwinRealms;

/// <summary>Pickups: gold, potions and items. Walk close to collect them.</summary>
public partial class Loot : Node3D
{
    public int Gold; public Item? Item; public string? Pot;   // "hp" or "mp"
    public string? Cube;   // a Soul Cube of this tier (data/pets.json "cubes")
    public string? Gem;    // a loose gem key (Gems.cs, data/gems.json drop)
    // multiplayer: a drop the host keeps track of (NetId > 0). Owner 0 = anyone may take it.
    public int NetId; public new long Owner; public string OwnerName = "";
    public bool Hold;   // dropped by us: not picked up again until we have stepped away from it once
    float _t, _claimT;
    static Vector3 _dropPos; static ulong _dropMs;

    // chests set this while dropping: loot arcs out from here instead of appearing in place
    public static Vector3? PopFrom; static int _pops;
    float _toss = -1; Vector3 _ta, _tb;

    public static void Drop(Vector3 pos, int gold = 0, Item? item = null, string? pot = null, bool hold = false)
    {
        var l = new Loot { Gold = gold, Item = item, Pot = pot, Hold = hold };
        Game.Root!.AddChild(l);
        if (PopFrom is Vector3 from && _pops < 8)
        {
            _pops++;
            float a = GD.Randf() * Mathf.Tau, r = 1f + GD.Randf() * .5f;
            l._ta = from; l._tb = from + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r); l._toss = 0; l.GlobalPosition = from;
            return;
        }
        l.GlobalPosition = pos + new Vector3((GD.Randf() - .5f) * 1.5f, 0.6f, (GD.Randf() - .5f) * 1.5f);
    }

    /// <summary>Drop everything inside the action as a chest pop: small parabolic arcs to 1-1.5 m around the chest.</summary>
    public static void Pop(Vector3 from, System.Action drop) { PopFrom = from; _pops = 0; try { drop(); } finally { PopFrom = null; } }

    /// <summary>Right-click in the pack: put an item on the ground in front of us, for a friend to take (or for us to pick up again).</summary>
    public static void DropFromPack(Item it)
    {
        var p = Player.Instance; if (p == null) return;
        var at = p.GlobalPosition + p.Facing() * 1.4f;
        _dropPos = at; _dropMs = Time.GetTicksMsec();
        if (Net.Active) Net.I!.DropItem(it, at); else Drop(at, item: it, hold: true);
        Ui.Note($"Dropped {it.Name}", Ui.Dim); Sfx.Play("click");
    }

    /// <summary>The pickup filter (Pack > Pickup filter): set pieces always; otherwise the item's rarity and slot must pass.</summary>
    public static bool Wanted(Item it)
    {
        var d = Game.D; if (it.Set != null || it.Unique) return true;
        return it.Rar >= d.PickRar && !d.PickSkip.Contains(it.Slot);
    }

    public static void DropFrom(Vector3 pos, int level, float itemChance, float bonus, int gold, float potChance)
    {
        // luck (pooled across the party) finds more and rarer items; gold find is our own
        float luck = Luck.Party(); itemChance = Luck.Chance(itemChance, luck); bonus = Luck.Bonus(bonus, luck);
        gold = Mathf.RoundToInt(gold * (1 + Mathf.Max(0, Game.TS["goldP"]) / 100f));
        if (gold > 0) Drop(pos, gold: gold);
        if (GD.Randf() < itemChance) Drop(pos, item: LootTables.Roll(level, bonus));   // in a dungeon: that dungeon's own drop table
        if (GD.Randf() < potChance) Drop(pos, pot: GD.Randf() < .6f ? "hp" : "mp");
        // Soul Cubes (data/pets.json rules): now and then from monsters, more in dungeons, often from bosses
        float cc = gold > 30 && itemChance >= 1 ? PetDb.R("bossCubeDropChance", .6f) : Dungeon.Active ? PetDb.R("dungeonCubeDropChance", .08f) : PetDb.R("cubeDropChance", .03f);
        if (GD.Randf() < cc) { var l = new Loot { Cube = PetDb.RollCube() }; Game.Root!.AddChild(l); l.GlobalPosition = pos + new Vector3((GD.Randf() - .5f) * 1.5f, 0.6f, (GD.Randf() - .5f) * 1.5f); }
        // gems (data/gems.json drop): now and then, more in dungeons and better the deeper you go; bosses often
        if (GemDb.RollDrop(gold > 30 && itemChance >= 1) is { } gem) { var l = new Loot { Gem = gem }; Game.Root!.AddChild(l); l.GlobalPosition = pos + new Vector3((GD.Randf() - .5f) * 1.5f, 0.6f, (GD.Randf() - .5f) * 1.5f); }
    }
    /// <summary>A pet's auto-loot brings it in (same rules as walking over it).</summary>
    public void PetPickup(Player pl) { if (_t > .4f && !Hold) Collect(pl); }

    public override void _Ready()
    {
        AddToGroup("loot");
        if (NetId > 0 && Time.GetTicksMsec() - _dropMs < 5000 && GlobalPosition.DistanceTo(_dropPos) < 3f) Hold = true;   // the copy of what we just dropped
        Color c = Gem != null ? GemDb.Col(Gem).Lightened(.1f) : Cube != null ? PetDb.Cube(Cube).Col.Lightened(.15f) : Gold > 0 ? new Color(1, .85f, .2f) : Pot == "hp" ? new Color(.9f, .2f, .25f) : Pot == "mp" ? new Color(.25f, .45f, 1f) : (Item?.Color ?? Colors.White);
        var mat = new StandardMaterial3D { AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = 1.5f };
        Mesh mesh = Gold > 0 ? new SphereMesh { Radius = .15f, Height = .3f, RadialSegments = 8, Rings = 4 } : Pot != null ? new CylinderMesh { TopRadius = .1f, BottomRadius = .13f, Height = .3f } : new BoxMesh { Size = new Vector3(.3f, .3f, .3f) };
        AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = mat });
        AddChild(new OmniLight3D { LightColor = c, OmniRange = 4f, LightEnergy = .6f });
        // rarity beam for rare+ items: thin additive column, fades after 6 s (or goes with the pickup)
        if (Item != null && (Item.Rar >= 2 || Item.Set != null || Item.Unique))
        {
            var bm = new StandardMaterial3D { AlbedoColor = new Color(c, .35f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
            var beam = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = .05f, BottomRadius = .09f, Height = 3f, RadialSegments = 8, Rings = 1 }, MaterialOverride = bm, Position = new Vector3(0, 1.3f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(beam);
            var tw = beam.CreateTween(); tw.TweenInterval(5f); tw.TweenProperty(bm, "albedo_color:a", 0f, 1f); tw.TweenCallback(Callable.From(beam.QueueFree));
        }
        // a drop reserved for someone else shows whose it is
        if (NetId > 0 && Owner != 0 && Owner != Net.MyId)
            AddChild(new Label3D { Text = OwnerName + "'s", FontSize = 26, OutlineSize = 7, PixelSize = .005f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = new Vector3(0, .55f, 0), Modulate = new Color(.8f, .85f, 1f, .85f) });
    }

    public override void _PhysicsProcess(double delta)
    {
        _t += (float)delta; RotateY((float)delta * 2f);
        if (_toss >= 0)
        {
            _toss += (float)delta / .55f; float k = Mathf.Min(_toss, 1f);
            GlobalPosition = _ta.Lerp(_tb, k) + Vector3.Up * Mathf.Sin(k * Mathf.Pi) * 1.1f;
            if (_toss >= 1f) _toss = -1;
            return;
        }
        var pl = Player.Instance; if (pl == null || pl.Dead) return;
        var p = GlobalPosition; p.Y = pl.GlobalPosition.Y;
        // settle onto the ground
        var q = PhysicsRayQueryParameters3D.Create(GlobalPosition + Vector3.Up * 2, GlobalPosition + Vector3.Down * 6, 1);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(q);
        if (Water.SurfaceAt(GlobalPosition) is { } ws && (hit.Count == 0 || ((Vector3)hit["position"]).Y < ws - .1f))
        {   // in water (F-20): float at the surface with a slow bob and drift with the current until the shore
            float dt = (float)delta; var gp = GlobalPosition; var nx = gp + Water.Current(gp.X, gp.Z) * dt;
            if (Water.SurfaceAt(new Vector3(nx.X, ws, nx.Z)) is { } ns && Mathf.Abs(ns - ws) < .5f && Water.DepthAt(nx.X, nx.Z, ns) > .3f) gp = nx;
            float fy = ws + .12f + Mathf.Sin(_t * 2.2f) * .06f;
            GlobalPosition = new Vector3(gp.X, Mathf.Lerp(gp.Y, fy, .12f), gp.Z);
            Rotation = new Vector3(Mathf.Sin(_t * 1.7f) * .18f, Rotation.Y, Mathf.Cos(_t * 1.3f) * .15f);   // rocking on the ripples
        }
        else if (hit.Count > 0) { var gy = ((Vector3)hit["position"]).Y + .45f + Mathf.Sin(_t * 3f) * .08f; GlobalPosition = new Vector3(GlobalPosition.X, Mathf.Lerp(GlobalPosition.Y, gy, .2f), GlobalPosition.Z); }
        float dist = p.DistanceTo(pl.GlobalPosition);
        if (Hold) { if (dist > 3.5f) Hold = false; return; }
        if (_t > .4f && dist < 2.3f) Collect(pl);
    }

    void Collect(Player pl)
    {
        // pickup filter: leave unwanted items lying (or break them into scraps, if chosen)
        bool scrap = false;
        if (Item != null && !Wanted(Item)) { if (!Game.D.PickScrap) return; scrap = true; }
        if (NetId > 0)
        {
            // shared drop: ask the host (once a second while standing on it); it removes the drop for everyone and pays us
            _claimT -= (float)GetPhysicsProcessDeltaTime(); if (_claimT > 0) return; _claimT = 1f;
            if (Net.Active) Net.I!.Claim(this); else QueueFree();
            return;
        }
        var D = Game.D;
        if (Gem != null) { GemDb.Give(D, Gem); Ui.Note(GemDb.Name(Gem) + ": " + GemDb.StatText(Gem), GemDb.Col(Gem).Lightened(.2f)); Sfx.Play("pickup"); }
        else if (Cube != null) { Cubes.Give(Cube, 1); Sfx.Play("pickup"); }
        else if (Gold > 0) { D.Gold += Gold; Ui.Note("+" + Gold + " gold", new Color(1, .85f, .3f)); Sfx.Play("coin"); }
        else if (Pot != null) { if (Pot == "hp") D.HpPot++; else D.MpPot++; Ui.Note(Pot == "hp" ? "Health potion" : "Mana potion", Pot == "hp" ? new Color(1, .5f, .5f) : new Color(.5f, .7f, 1f)); Sfx.Play("pickup"); }
        else if (Item != null && scrap) Ui.Note($"Scrapped {Item.Name}: {LootTables.Disassemble(Item)}", Ui.Dim);
        else if (Item != null)
        {
            if (D.Inv.Count >= 28) { if (_t % 3f < .05f) Ui.Note("Pack is full", new Color(1, .5f, .5f)); return; }
            D.Inv.Add(Item); Ui.Note(Item.Describe(), Item.Color); Sfx.Play("pickup");
        }
        Game.Notify(); QueueFree();
    }
}
