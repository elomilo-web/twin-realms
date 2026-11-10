using System;
using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>Multiplayer windows: host or join, the lobby (players, party, loot rules, duels), yes/no requests and the party chest.</summary>
public partial class Ui
{
    /// <summary>Title screen hooks set by Main: host with your hero, or join a friend's address.</summary>
    public static Action<int>? OnHost; public static Action<string, int>? OnJoin;
    static string _joinAddr = "127.0.0.1", _port = Net.DefaultPort.ToString();
    public static string PickedClass => _pickClass; public static string PickedRace => _pickRace; public static string PickedName => _pickName; public static string PickedBody => _pickBody;

    const string HelpText =
        "How friends connect:\n" +
        "  Host a game, press Copy next to a join code and send it (Discord, text). Your friend pastes it under Join a friend.\n" +
        "  Same house or Wi-Fi: use the Same Wi-Fi code.\n" +
        "  Over the internet: use the internet code. The game asks your router to open the port by itself (UPnP). If the menu says that failed, forward UDP port " + "24565" + " to this computer in your router settings, " +
        "or have both of you install a free virtual network such as Tailscale or Radmin VPN (easiest when the router cannot be changed) and use the Same Wi-Fi / VPN code it shows.\n" +
        "Everyone keeps their own hero: level, skills, pack and home storage come along and are saved on their own computer.";

    public void OpenMultiplayer()
    {
        var v = Frame("multiplayer", "Multiplayer", new Vector2(980, 640), OpenMultiplayer);
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 520) }; v.AddChild(scroll);
        var body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; body.AddThemeConstantOverride("separation", 8); scroll.AddChild(body);
        if (!Net.Active) OfflinePage(body); else LobbyPage(body);
        if (!InGame) v.AddChild(Btn("Back", ShowTitle));
    }

    void OfflinePage(VBoxContainer v)
    {
        var sp = Game.Peek(Game.Slot);
        string who = sp != null && !InGame ? $"{sp.Name}, level {sp.Level} (slot {Game.Slot + 1}; pick another hero on the title screen)" : InGame ? $"{Game.D.Name}, level {Game.D.Level}" : $"a new {Races.Get(_pickRace).Name} {Classes.Get(_pickClass).Name} named {_pickName}";
        Row(v, $"Play together with friends. You bring {who}.", Colors.White, 16);
        if (Net.Status != "") Row(v, Net.Status, UiTheme.Highlight, 15);
        if (Net.LastNote != "") Row(v, Net.LastNote, new Color(1f, .7f, .5f), 15);
        // ---- host
        Lbl("HOST YOUR WORLD", 15, UiTheme.Accent, v);
        var hr = new HBoxContainer(); hr.AddThemeConstantOverride("separation", 8); v.AddChild(hr);
        Lbl("Port", 15, Dim, hr); var port = new LineEdit { Text = _port, CustomMinimumSize = new Vector2(110, 34) }; hr.AddChild(port); port.TextChanged += s => _port = s;
        hr.AddChild(Btn(InGame ? "Open my world to friends" : "Host a game", () =>
        {
            int p = int.TryParse(_port, out var x) ? x : Net.DefaultPort; Net.LastNote = "";
            if (InGame) { Net.I?.Host(p); OpenMultiplayer(); } else { CloseNoUnpause(); OnHost?.Invoke(p); }
        }));
        // ---- join
        Lbl("JOIN A FRIEND", 15, UiTheme.Accent, v);
        if (InGame) Row(v, "To join a friend's world, use Quit to title in the pause menu, then Multiplayer.", Dim, 14);
        else
        {
            var jr = new HBoxContainer(); jr.AddThemeConstantOverride("separation", 8); v.AddChild(jr);
            Lbl("Join code or address", 15, Dim, jr); var addr = new LineEdit { Text = _joinAddr, PlaceholderText = "e.g. 4K7QM-2XH9D or 192.168.1.20", CustomMinimumSize = new Vector2(280, 34) }; jr.AddChild(addr); addr.TextChanged += s => _joinAddr = s;
            jr.AddChild(Btn("Paste", () => { _joinAddr = DisplayServer.ClipboardGet().Trim(); OpenMultiplayer(); }, true));
            Lbl("Port", 15, Dim, jr); var jp = new LineEdit { Text = _port, CustomMinimumSize = new Vector2(90, 34) }; jr.AddChild(jp); jp.TextChanged += s => _port = s;
            jr.AddChild(Btn("Join", () =>
            {
                int p = int.TryParse(_port, out var x) ? x : Net.DefaultPort; string a = _joinAddr.Trim(); Net.LastNote = "";
                if (JoinCode.TryRead(a, out var ip, out var cp)) { a = ip; p = cp; }   // a join code carries the address and port
                OnJoin?.Invoke(a, p); OpenMultiplayer();
            }));
            Row(v, "Paste the join code your friend copied from their Multiplayer menu (the port box is only needed when typing an address).", Dim, 13);
            v.AddChild(Btn("Refresh status", OpenMultiplayer, true));
        }
        Row(v, HelpText, Dim, 13);
    }

    void LobbyPage(VBoxContainer v)
    {
        var net = Net.I!; var me = Net.MyId;
        Row(v, Net.IsHost ? "You are hosting. The world, its enemies, loot, the party chest and duels run on your computer." : "You are in a friend's world. Your hero and pack are saved on your computer.", Colors.White, 15);
        if (Net.IsHost)
        {
            var addrs = Net.LocalAddresses(); int port = Net.HostPort;
            // join codes: friends paste a short code instead of typing an address and port
            Lbl("JOIN CODES (send one to your friend)", 15, UiTheme.Accent, v);
            void CodeRow(string label, string ip)
            {
                var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 10); v.AddChild(h);
                Lbl(label, 15, Dim, h).CustomMinimumSize = new Vector2(250, 0);
                string code = JoinCode.Make(ip, port); var cl = Lbl(code, 22, UiTheme.Highlight, h); cl.CustomMinimumSize = new Vector2(190, 0);
                h.AddChild(Btn("Copy", () => { DisplayServer.ClipboardSet(code); Note("Copied " + code + ". Paste it to your friend.", Gold); }, true));
                Lbl($"({ip}:{port})", 13, Dim, h);
            }
            if (Net.PublicAddress != "") CodeRow("Over the internet:", Net.PublicAddress);
            else Row(v, "Finding your internet address ... press Refresh in a moment.", Dim, 14);
            foreach (var a in addrs) CodeRow("Same Wi-Fi / VPN:", a);
            if (Net.UpnpStatus != "") Row(v, Net.UpnpStatus, Net.UpnpStatus.StartsWith("Router port") ? new Color(.6f, 1f, .7f) : new Color(1f, .8f, .55f), 14);
        }
        // ---- players
        Lbl($"PLAYERS ({net.Players.Count}/{Net.MaxPlayers})", 15, UiTheme.Accent, v);
        var grid = new GridContainer { Columns = 5 }; grid.AddThemeConstantOverride("h_separation", 14); grid.AddThemeConstantOverride("v_separation", 4); v.AddChild(grid);
        var ids = new List<long>(net.Players.Keys); ids.Sort();
        foreach (var id in ids)
        {
            var p = net.Players[id]; bool self = id == me, party = net.SameParty(me, id);
            string where = p.Zone >= 1000 ? "in the party dungeon" : p.Zone == 1 ? "in a dungeon" : p.Zone == 2 ? "in the arena" : "in the world";
            Lbl(p.Name + (self ? " (you)" : "") + (id == 1 ? "  [host]" : ""), 16, self ? UiTheme.Highlight : party ? new Color(.55f, 1f, .6f) : Colors.White, grid).CustomMinimumSize = new Vector2(230, 0);
            Lbl($"Lv {p.Level} {Races.Get(p.Race).Name} {Classes.Get(p.Class).Name}", 14, Dim, grid).CustomMinimumSize = new Vector2(250, 0);
            Lbl(where + (party ? ", party" : ""), 14, Dim, grid).CustomMinimumSize = new Vector2(150, 0);
            if (self) { grid.AddChild(new Control()); grid.AddChild(new Control()); continue; }
            long tid = id;
            var inv = Btn(party ? "In party" : "Invite to party", () => { net.Invite(tid); Note("Invite sent to " + p.Name, Gold); }, true); inv.Disabled = party; grid.AddChild(inv);
            var du = Btn("Challenge to duel", () => { net.Challenge(tid); Note("Challenge sent to " + p.Name, Gold); }, true); du.Disabled = net.Duel != null; grid.AddChild(du);
        }
        // ---- party
        var pt = net.MyParty;
        Lbl("PARTY", 15, UiTheme.Accent, v);
        if (pt == null) Row(v, "You are not in a party. Invite someone above. A party shares one dungeon (any dungeon door takes you to where your party is; monsters are the party's average level + 3), splits 90% of each kill's experience evenly between members nearby, finds slightly more loot, and splits gold.", Dim, 14);
        else
        {
            var names = new List<string>(); foreach (var m in pt.Members) names.Add(net.NameOf(m) + (m == pt.Leader ? " (leader)" : ""));
            Row(v, string.Join(",   ", names), Colors.White, 15);
            Row(v, "Item drops: " + Net.LootModeName(pt.Loot) + (pt.Leader == me ? "" : "   (the leader can change this)"), UiTheme.Highlight, 14);
            if (pt.Leader == me)
            {
                var lr = new HBoxContainer(); lr.AddThemeConstantOverride("separation", 6); v.AddChild(lr);
                foreach (var m in Net.LootModes) { string mode = m; var b = Btn(Net.LootModeName(m), () => net.SetLootMode(mode), true); if (m == pt.Loot) b.AddThemeColorOverride("font_color", UiTheme.Highlight); lr.AddChild(b); }
            }
            v.AddChild(Btn("Leave party", () => net.LeaveParty(), true));
        }
        Row(v, "Gold from party kills is split evenly. Drops reserved for someone become free for all after a minute. Personal loot is only visible to its owner. In party dungeons everyone always gets their own personal loot.", Dim, 13);
        // ---- leave
        var bot = new HBoxContainer(); bot.AddThemeConstantOverride("separation", 8); v.AddChild(bot);
        bot.AddChild(Btn("Refresh", OpenMultiplayer, true));
        bot.AddChild(Btn(Net.IsHost ? "Stop hosting" : "Leave game (back to title)", () =>
        {
            if (Net.IsHost) { Net.I!.Shutdown(); Note("You stopped hosting", Dim); OpenMultiplayer(); return; }
            Game.Save(); Net.I!.Shutdown(); GetTree().Paused = false; InGame = false; GetTree().ReloadCurrentScene();
        }, true));
        if (Net.IsHost) Row(v, HelpText, Dim, 13);
    }

    /// <summary>A yes/no request (party invite, duel challenge). It never pauses the game.</summary>
    public void Ask(string text, string yes, string no, Action<bool> answer)
    {
        var v = Frame("ask", "Request", new Vector2(560, 250), () => { });
        Row(v, text, Colors.White, 17).CustomMinimumSize = new Vector2(500, 0);
        var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 12); v.AddChild(h);
        var y = Btn(yes, () => { Close(); answer(true); }); y.CustomMinimumSize = new Vector2(200, 42); h.AddChild(y);
        var n = Btn(no, () => { Close(); answer(false); }); n.CustomMinimumSize = new Vector2(200, 42); h.AddChild(n);
        Sfx.Play("portal");
    }

    /// <summary>E on a friend's hero: party invite or duel challenge.</summary>
    public void OpenPlayerMenu(long peer)
    {
        var net = Net.I; if (net == null || !net.Players.TryGetValue(peer, out var p)) return;
        var v = Frame("player", p.Name, new Vector2(460, 300), () => OpenPlayerMenu(peer));
        Row(v, $"Level {p.Level} {Races.Get(p.Race).Name} {Classes.Get(p.Class).Name}", Dim, 15);
        bool party = net.SameParty(Net.MyId, peer);
        var inv = Btn(party ? "Already in your party" : "Invite to party", () => { net.Invite(peer); Note("Invite sent", Gold); Close(); }); inv.Disabled = party; v.AddChild(inv);
        var du = Btn("Challenge to a duel", () => { net.Challenge(peer); Note("Challenge sent", Gold); Close(); }); du.Disabled = net.Duel != null; v.AddChild(du);
        v.AddChild(Btn("Multiplayer menu", OpenMultiplayer));
    }

    /// <summary>The party chest: shared by everyone in the lobby and kept by the host. Click to move items between your pack and the chest.</summary>
    public void OpenPartyChest()
    {
        var d = Game.D; var net = Net.I!; var chest = net.PartyChest;
        var v = Frame("partychest", "Party Chest", new Vector2(1040, 600), OpenPartyChest);
        var top = new HBoxContainer(); v.AddChild(top); top.AddThemeConstantOverride("separation", 16);
        Lbl($"Pack {d.Inv.Count}/28", 16, d.Inv.Count >= 28 ? new Color(1, .6f, .5f) : Gold, top);
        Lbl($"Party chest {chest.Count}/{Net.ChestCap}", 16, chest.Count >= Net.ChestCap ? new Color(1, .6f, .5f) : Gold, top);
        Lbl(Net.Active ? "Everyone in the lobby can use this chest. The host keeps it." : "Solo, this is a spare chest. When you host, your friends share it.", 14, Dim, top).SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var cols = new HBoxContainer(); cols.AddThemeConstantOverride("separation", 24); v.AddChild(cols);
        GridContainer Grid(string title, int columns) { var b = new VBoxContainer(); cols.AddChild(b); Lbl(title, 15, UiTheme.Accent, b); var g = new GridContainer { Columns = columns }; g.AddThemeConstantOverride("h_separation", 5); g.AddThemeConstantOverride("v_separation", 5); b.AddChild(g); return g; }
        var pg = Grid("PACK", 4); var sg = Grid("PARTY CHEST", 8);
        for (int i = 0; i < 28; i++)
        {
            int k = i; var it = i < d.Inv.Count ? d.Inv[i] : null; var cell = new ItemSlot { Item = it, CustomMinimumSize = new Vector2(58, 58) }; pg.AddChild(cell); if (it == null) continue;
            cell.Hover += on => ShowTip(on ? it : null, cell, d.Eq.GetValueOrDefault(it.Slot));
            cell.Clicked += _ => { HideTip(); net.ChestPut(k); Sfx.Play("click"); OpenPartyChest(); };
        }
        for (int i = 0; i < Net.ChestCap; i++)
        {
            int k = i; var it = i < chest.Count ? chest[i] : null; var cell = new ItemSlot { Item = it, CustomMinimumSize = new Vector2(58, 58) }; sg.AddChild(cell); if (it == null) continue;
            cell.Hover += on => ShowTip(on ? it : null, cell, d.Eq.GetValueOrDefault(it.Slot));
            cell.Clicked += _ => { HideTip(); net.ChestTake(k); Sfx.Play("click"); OpenPartyChest(); };
        }
    }
}

/// <summary>The party chest on the village plaza, next to your home storage.</summary>
public partial class PartyChestNode : Node3D, IInteractable
{
    public string Prompt => Net.Active ? "Open the party chest (shared)" : "Open the party chest";
    public bool CanInteract => true;
    public override void _Ready()
    {
        AddToGroup("interact");
        var wood = Gfx.Tex(World.Mat(new Color(.3f, .34f, .45f), new Color(.15f, .14f, .25f)), "planks", .9f); var gold = World.Mat(new Color(.95f, .75f, .3f), null, .3f, .9f);
        var body = new StaticBody3D { CollisionLayer = 1 }; AddChild(body);
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.5f, .9f, .9f) }, Position = new Vector3(0, .45f, 0) });
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.5f, .7f, .9f) }, MaterialOverride = wood, Position = new Vector3(0, .35f, 0) });
        AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = .45f, BottomRadius = .45f, Height = 1.5f, RadialSegments = 12 }, MaterialOverride = wood, Position = new Vector3(0, .7f, 0), Rotation = new Vector3(0, 0, Mathf.Pi / 2), Scale = new Vector3(1, 1, .9f) });
        foreach (float x in new[] { -.6f, 0f, .6f }) AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(.08f, 1.2f, 1f) }, MaterialOverride = gold, Position = new Vector3(x, .55f, 0) });
        AddChild(new Label3D { Text = "Party Chest", FontSize = 30, OutlineSize = 8, PixelSize = .005f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = new Vector3(0, 1.8f, 0), Modulate = new Color(.6f, .85f, 1f),
            VisibilityRangeEnd = 22f, VisibilityRangeEndMargin = 3f, VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self });
        World.Lamp(this, new Vector3(0, 1.5f, -.7f), new Color(.55f, .75f, 1f), 7f, .8f);
    }
    public void Interact() { Sfx.Play("click"); Net.I?.OpenChest(); }
}
