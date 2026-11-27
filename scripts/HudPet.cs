using Godot;

namespace TwinRealms;

/// <summary>
/// The pet frame (A-20): on the right, just above the minimap (nothing else is drawn there). The active pet's portrait badge, name and
/// level, its health bar, a stamina bar while you ride it (or the horse), "IN ITS CUBE 42 s" while it is knocked out, its order
/// (follow / stay), and a note when it has pet points to spend. Hidden with no active pet and no mount (pets.json rules.hudPet 0 = off).
/// </summary>
public partial class HudCanvas
{
    void DrawPet(Player p, Vector2 sz, float u, float pad, int f)
    {
        if (PetDb.R("hudPet", 1) <= 0 || Arenas.Active) return;
        var a = PetWorld.Active; bool horse = p.Mounted && !p.MountIsPet;
        if (a == null && !horse) return;
        float w = Mathf.Max(200, u * 15), h = f * 2.2f + 26, x = sz.X - pad - w, y = sz.Y - pad - u * 14 - 14 - h;
        Glass(new Rect2(x, y, w, h), false);
        float br = h * .36f; var bc = new Vector2(x + 10 + br, y + h / 2);
        string name, sub; Color col; float hp = 1; bool ko = false;
        if (a != null)
        {
            var sp = a.Sp(); col = PetDb.RarityColor(sp.Rarity); name = a.Name(); ko = a.Ko();
            var o = PetWorld.Out; hp = ko ? 0 : o != null && IsInstanceValid(o) && o.S == a && o.MaxHp > 0 ? Mathf.Clamp(o.Hp / o.MaxHp, 0, 1) : Mathf.Clamp(a.HpFrac, 0, 1);
            int free = PetTree.Free(a);
            sub = ko ? $"IN ITS CUBE  {Mathf.CeilToInt(a.KoT)} s" : PetWorld.Riding ? $"RIDING  ·  {Inputs.KeyOf(Inputs.PetStay)} attack" : free > 0 ? $"{free} pet point{(free == 1 ? "" : "s")}  ({Inputs.KeyOf(Inputs.PetsUi)})" : a.Stay ? "STAY" : a.Aggro.ToUpper();
        }
        else { col = new Color(.75f, .6f, .4f); name = "Horse"; sub = "RIDING"; }
        DrawCircle(bc, br, new Color(.06f, .05f, .045f)); DrawCircle(bc, br - 3, col.Darkened(.45f)); DrawArc(bc, br - 1, 0, Mathf.Tau, 24, ko ? UiTheme.Dim : col, 2f, true);
        string letter = name.Length > 0 ? name[..1].ToUpper() : "?"; int lf = (int)(br * 1.1f); var ls = F.GetStringSize(letter, HorizontalAlignment.Left, -1, lf);
        DrawString(F, bc + new Vector2(-ls.X / 2, ls.Y * .32f), letter, HorizontalAlignment.Left, -1, lf, new Color(.95f, .9f, .8f, ko ? .5f : 1f));
        float tx = bc.X + br + 10, tw = x + w - 10 - tx;
        Txt(new Vector2(tx, y + f + 5), name, f, ko ? UiTheme.Dim : Parch, HorizontalAlignment.Left, tw);
        if (a != null) Txt(new Vector2(tx, y + f + 5), $"Lv {a.Level}", Mathf.Max(10, f - 2), UiTheme.Accent, HorizontalAlignment.Right, tw);
        float by = y + f + 11;
        if (a != null) { SlantBar(new Rect2(tx, by, tw, 7), hp, hp > .5f ? new Color("5be37d") : hp > .25f ? new Color("f2d14b") : new Color("c0281f"), 4, 0); by += 10; }
        if (p.Mounted && p.MountStMax > 0) { SlantBar(new Rect2(tx, by, tw * (a != null ? .8f : 1f), 5), p.MountSt / p.MountStMax, p.MountFlies ? new Color("6aa8e8") : new Color("b8a04a"), 3, 0); by += 8; }
        Txt(new Vector2(tx, y + h - 5), sub, Mathf.Max(10, f - 3), ko ? new Color(1f, .6f, .5f) : UiTheme.Dim, HorizontalAlignment.Left, tw);
    }
}
