using Godot;

namespace TwinRealms;

public partial class Ui
{
    /// <summary>A-35: the open trader's curios (WeaponFinds.Stock): a pedlar's odd things that are weapons if you swing them hard
    /// enough, or now and then one named piece at another trader. Called from OpenShop's buy tab.</summary>
    void CurioRows(Control list, string tab)
    {
        var d = Game.D; var keeper = NpcDb.Get(_shopNpc); bool pedlar = keeper?.Role == "pedlar";
        var stock = WeaponFinds.Stock(_shopNpc, keeper?.Role ?? "", d.Level); if (stock.Count == 0) return;
        Row(list, pedlar ? "Curios (the pedlar swears every one of these is a weapon)" : "Something special", new Color("ffd27a"), 14);
        for (int i = 0; i < stock.Count; i++)
        {
            var it = stock[i]; int price = Mathf.Max(1, Mathf.RoundToInt(BuyPrice(it) * (pedlar ? WeaponFinds.PedlarPriceMul : 1f)));
            ItemRow(list, it, price + "g", () =>
            {
                if (d.Gold < price) { Sfx.UiError(); Note("Not enough gold", new Color(1, .5f, .5f)); return; }
                if (d.Inv.Count >= 28) { Note("Pack is full", new Color(1, .5f, .5f)); return; }
                d.Gold -= price; d.Inv.Add(it); stock.Remove(it); Traded(); Sfx.Play("coin"); OpenShop(tab);
            }, d.Gold >= price);
        }
    }
}
