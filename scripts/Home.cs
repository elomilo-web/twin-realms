using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>Storage chest at the hero's home in Oakhaven. Opens the Home Storage window (pack on the left, 56 stash slots on the right).</summary>
public partial class StashChest : Node3D, IInteractable
{
    public string Prompt => "Open home storage";
    public bool CanInteract => true;
    public const int Capacity = 56;
    Node3D _lid = null!;

    public override void _Ready()
    {
        AddToGroup("interact");
        var wood = Gfx.Tex(World.Mat(new Color(.45f, .3f, .17f), new Color(.18f, .14f, .22f)), "planks", .9f);
        var iron = World.Mat(new Color(.25f, .25f, .27f), null, .45f, .75f); var brass = World.Mat(new Color(.85f, .68f, .3f), null, .35f, .85f);
        var body = new StaticBody3D { CollisionLayer = 1 }; AddChild(body);
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.3f, .8f, .8f) }, Position = new Vector3(0, .4f, 0) });
        void P(Node3D par, Mesh m, Material mt, Vector3 pos, Vector3? rot = null) => par.AddChild(new MeshInstance3D { Mesh = m, MaterialOverride = mt, Position = pos, Rotation = rot ?? Vector3.Zero });
        P(this, new BoxMesh { Size = new Vector3(1.3f, .62f, .8f) }, wood, new Vector3(0, .31f, 0));
        foreach (float x in new[] { -.5f, .5f }) P(this, new BoxMesh { Size = new Vector3(.08f, .64f, .84f) }, iron, new Vector3(x, .32f, 0));
        _lid = new Node3D { Position = new Vector3(0, .62f, .4f) }; AddChild(_lid);
        P(_lid, new CylinderMesh { TopRadius = .4f, BottomRadius = .4f, Height = 1.3f, RadialSegments = 12 }, wood, new Vector3(0, 0, -.4f), new Vector3(0, 0, Mathf.Pi / 2));
        foreach (float x in new[] { -.5f, .5f }) P(_lid, new CylinderMesh { TopRadius = .42f, BottomRadius = .42f, Height = .08f, RadialSegments = 12 }, iron, new Vector3(x, 0, -.4f), new Vector3(0, 0, Mathf.Pi / 2));
        P(this, new BoxMesh { Size = new Vector3(.16f, .2f, .05f) }, brass, new Vector3(0, .55f, -.42f));
        // the lid bulge sits half inside the body; clip the bottom with the body box (visually fine from outside)
        AddChild(new Label3D { Text = "Your Home", FontSize = 30, OutlineSize = 8, PixelSize = .005f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = new Vector3(0, 1.7f, 0), Modulate = new Color(1f, .9f, .6f),
            VisibilityRangeEnd = 22f, VisibilityRangeEndMargin = 3f, VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self });
        World.Lamp(this, new Vector3(0, 1.4f, -.6f), new Color(1f, .78f, .45f), 7f, .8f);
    }

    public void Interact()
    {
        Sfx.Play("click"); _lid.CreateTween().TweenProperty(_lid, "rotation:x", -1.1f, .25f);
        GetTree().CreateTimer(1.5f, true).Timeout += () => { if (IsInstanceValid(_lid)) _lid.CreateTween().TweenProperty(_lid, "rotation:x", 0f, .3f); };
        Ui.I?.OpenStash();
    }
}

public partial class Ui
{
    /// <summary>Home Storage: click an item in your pack to store it, click a stored item to take it back. Hover shows the tooltip.</summary>
    public void OpenStash()
    {
        var d = Game.D; var v = Frame("stash", "Home Storage", new Vector2(1040, 600), OpenStash);
        var top = new HBoxContainer(); v.AddChild(top); top.AddThemeConstantOverride("separation", 16);
        Lbl($"Pack {d.Inv.Count}/28", 16, d.Inv.Count >= 28 ? new Color(1, .6f, .5f) : Gold, top);
        Lbl($"Storage {d.Stash.Count}/{StashChest.Capacity}", 16, d.Stash.Count >= StashChest.Capacity ? new Color(1, .6f, .5f) : Gold, top);
        Lbl("Click to move between pack and storage.", 14, Dim, top).SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        top.AddChild(Btn("Store all", () => { while (d.Inv.Count > 0 && d.Stash.Count < StashChest.Capacity) { d.Stash.Add(d.Inv[0]); d.Inv.RemoveAt(0); } Sfx.Play("click"); Game.Notify(); OpenStash(); }, true));
        top.AddChild(Btn("Sort storage", () => { d.Stash.Sort((a, b) => b.Rar != a.Rar ? b.Rar.CompareTo(a.Rar) : string.Compare(a.Slot, b.Slot, System.StringComparison.Ordinal)); Sfx.Play("click"); OpenStash(); }, true));
        var cols = new HBoxContainer(); cols.AddThemeConstantOverride("separation", 24); v.AddChild(cols);
        GridContainer Grid(Node parent, string title, int columns) { var b = new VBoxContainer(); parent.AddChild(b); Lbl(title, 15, UiTheme.Accent, b); var g = new GridContainer { Columns = columns }; g.AddThemeConstantOverride("h_separation", 5); g.AddThemeConstantOverride("v_separation", 5); b.AddChild(g); return g; }
        var pg = Grid(cols, "PACK", 4); var sg = Grid(cols, "STORAGE", 8);
        for (int i = 0; i < 28; i++)
        {
            int k = i; var it = i < d.Inv.Count ? d.Inv[i] : null; var cell = new ItemSlot { Item = it, CustomMinimumSize = new Vector2(58, 58) }; pg.AddChild(cell); if (it == null) continue;
            cell.Hover += on => ShowTip(on ? it : null, cell, d.Eq.GetValueOrDefault(it.Slot));
            cell.Clicked += _ => { HideTip(); if (d.Stash.Count >= StashChest.Capacity) { Note("Storage is full", new Color(1, .5f, .5f)); return; } d.Inv.RemoveAt(k); d.Stash.Add(it); Sfx.Play("click"); Game.Notify(); OpenStash(); };
        }
        for (int i = 0; i < StashChest.Capacity; i++)
        {
            int k = i; var it = i < d.Stash.Count ? d.Stash[i] : null; var cell = new ItemSlot { Item = it, CustomMinimumSize = new Vector2(58, 58) }; sg.AddChild(cell); if (it == null) continue;
            cell.Hover += on => ShowTip(on ? it : null, cell, d.Eq.GetValueOrDefault(it.Slot));
            cell.Clicked += _ => { HideTip(); if (d.Inv.Count >= 28) { Note("Pack is full", new Color(1, .5f, .5f)); return; } d.Stash.RemoveAt(k); d.Inv.Add(it); Sfx.Play("click"); Game.Notify(); OpenStash(); };
        }
    }
}
