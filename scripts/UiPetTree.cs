using Godot;

namespace TwinRealms;

/// <summary>
/// The Tree tab of the pets menu (A-20, PetTree.cs): the pet's family tree in three branches, a point per level to spend, respec for gold.
/// </summary>
public partial class Ui
{
    void PetTreeTab(VBoxContainer v, PetState p)
    {
        var fam = PetTree.Family(p);
        if (fam == null) { Row(v, "This pet has no skill tree (data/pets.json tree).", Dim, 14); return; }
        int free = PetTree.Free(p), spent = PetTree.Spent(p), cost = PetTree.RespecCost(p);
        var top = new HBoxContainer(); top.AddThemeConstantOverride("separation", 10); v.AddChild(top);
        var tl = Lbl($"{fam.Name} tree   ·   {free} point{(free == 1 ? "" : "s")} to spend   ·   {spent} spent", 15, free > 0 ? Gold : Parch, top); tl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var rb = Btn($"Respec ({cost}g)", () =>
        {
            if (Game.D.Gold < cost) { Sfx.UiError(); Note("Not enough gold", new Color(1, .5f, .5f)); return; }
            if (PetTree.Respec(p)) { Sfx.Play("coin"); Note($"{p.Name()}: all pet points returned", Gold); Game.Save(); }
            OpenPets(null);
        }, true);
        rb.Disabled = spent <= 0; rb.TooltipText = "Returns every point. Skills the tree taught are forgotten."; top.AddChild(rb);
        if (fam.Desc != "") Row(v, fam.Desc + "  One point for every level after the first.", Dim, 13);
        foreach (var b in fam.Branches)
        {
            int inB = PetTree.SpentIn(p, b.Id);
            Header($"{b.Name}  ({inB})", v);
            foreach (var n in b.Nodes)
            {
                var node = n; int rank = PetTree.Rank(p, n); string? why = PetTree.Why(p, n); bool maxed = rank >= n.Max, locked = inB < n.Req;
                string text = $"{(n.Cap ? "★ " : "")}{n.Name}   {rank}/{n.Max}{(n.Req > 0 ? $"   (needs {n.Req} in {b.Name})" : "")}\n{PetTree.Effects(n)}{(n.Desc != "" ? "  ·  " + n.Desc : "")}";
                var col = maxed ? Gold : rank > 0 ? Parch : locked ? Dim : Parch.Darkened(.12f);
                AddRow(v, text, col, maxed ? "Mastered" : locked ? "Locked" : "Learn", () =>
                {
                    if (PetTree.Buy(p, node)) { Sfx.Play("click"); Game.Save(); } else Note(PetTree.Why(p, node) ?? "", new Color(1, .6f, .5f));
                    OpenPets(null);
                }, why == null);
            }
        }
    }
}
