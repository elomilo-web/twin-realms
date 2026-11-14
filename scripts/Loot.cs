using Godot;

namespace TwinRealms;

/// <summary>Pickups: gold, potions and items. Walk close to collect them.</summary>
public partial class Loot : Node3D
{
    public int Gold; public Item? Item; public string? Pot;   // "hp" or "mp"
    // multiplayer: a drop the host keeps track of (NetId > 0). Owner 0 = anyone may take it.
    public int NetId; public long Owner; public string OwnerName = "";
    public bool Hold;   // dropped by us: not picked up again until we have stepped away from it once
    float _t, _claimT;
    static Vector3 _dropPos; static ulong _dropMs;

    public static void Drop(Vector3 pos, int gold = 0, Item? item = null, string? pot = null, bool hold = false)
    {
        var l = new Loot { Gold = gold, Item = item, Pot = pot, Hold = hold };
        Game.Root!.AddChild(l);
        l.GlobalPosition = pos + new Vector3((GD.Randf() - .5f) * 1.5f, 0.6f, (GD.Randf() - .5f) * 1.5f);
    }

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
    }

    public override void _Ready()
    {
        AddToGroup("loot");
        if (NetId > 0 && Time.GetTicksMsec() - _dropMs < 5000 && GlobalPosition.DistanceTo(_dropPos) < 3f) Hold = true;   // the copy of what we just dropped
        Color c = Gold > 0 ? new Color(1, .85f, .2f) : Pot == "hp" ? new Color(.9f, .2f, .25f) : Pot == "mp" ? new Color(.25f, .45f, 1f) : (Item?.Color ?? Colors.White);
        var mat = new StandardMaterial3D { AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = 1.5f };
        Mesh mesh = Gold > 0 ? new SphereMesh { Radius = .15f, Height = .3f, RadialSegments = 8, Rings = 4 } : Pot != null ? new CylinderMesh { TopRadius = .1f, BottomRadius = .13f, Height = .3f } : new BoxMesh { Size = new Vector3(.3f, .3f, .3f) };
        AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = mat });
        AddChild(new OmniLight3D { LightColor = c, OmniRange = 4f, LightEnergy = .6f });
        // a drop reserved for someone else shows whose it is
        if (NetId > 0 && Owner != 0 && Owner != Net.MyId)
            AddChild(new Label3D { Text = OwnerName + "'s", FontSize = 26, OutlineSize = 7, PixelSize = .005f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = new Vector3(0, .55f, 0), Modulate = new Color(.8f, .85f, 1f, .85f) });
    }

    public override void _PhysicsProcess(double delta)
    {
        _t += (float)delta; RotateY((float)delta * 2f);
        var pl = Player.Instance; if (pl == null || pl.Dead) return;
        var p = GlobalPosition; p.Y = pl.GlobalPosition.Y;
        // settle onto the ground
        var q = PhysicsRayQueryParameters3D.Create(GlobalPosition + Vector3.Up * 2, GlobalPosition + Vector3.Down * 6, 1);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(q);
        if (hit.Count > 0) { var gy = ((Vector3)hit["position"]).Y + .45f + Mathf.Sin(_t * 3f) * .08f; GlobalPosition = new Vector3(GlobalPosition.X, Mathf.Lerp(GlobalPosition.Y, gy, .2f), GlobalPosition.Z); }
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
        if (Gold > 0) { D.Gold += Gold; Ui.Note("+" + Gold + " gold", new Color(1, .85f, .3f)); Sfx.Play("coin"); }
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
