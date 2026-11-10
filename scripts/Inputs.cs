using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// Registers all input actions in code so project.godot stays clean. Every action in <see cref="Rebindable"/> can be changed in
/// Settings > Controls; changes are saved in settings.json (Settings.Binds) as "k:physical keycode" or "m:mouse button".
/// </summary>
public static class Inputs
{
    public const string Left = "move_left", Right = "move_right", Forward = "move_forward", Back = "move_back";
    public const string Jump = "jump", Sprint = "sprint", Roll = "roll", Pause = "pause";
    public const string Spell1 = "spell1", Spell2 = "spell2", Spell3 = "spell3", Spell4 = "spell4";
    public const string PotHp = "pot_hp", PotMp = "pot_mp", Use = "interact", Realm = "realm", Mount = "mount";
    public const string Pack = "ui_pack", Journal = "ui_journal", Map = "ui_map", Tree = "ui_tree";
    public const string Lock = "lock_on", Unlock = "lock_off", SkillsUi = "ui_skills";
    public const string Attack = "attack", Guard = "guard";

    /// <summary>Actions shown in the Controls menu, in order, with their names.</summary>
    public static readonly (string id, string name)[] Rebindable =
    {
        (Forward, "Move forward"), (Back, "Move back"), (Left, "Move left"), (Right, "Move right"),
        (Sprint, "Sprint"), (Jump, "Jump"), (Roll, "Roll"), (Attack, "Attack (hold for power)"), (Guard, "Guard with shield"),
        (Spell1, "Skill 1"), (Spell2, "Skill 2"), (Spell3, "Skill 3"), (Spell4, "Skill 4"), ("spell5", "Skill 5"), ("spell6", "Skill 6"),
        (PotHp, "Health potion"), (PotMp, "Mana potion"), (Use, "Interact"), (Realm, "Shift realm"), (Mount, "Horse"),
        (Lock, "Lock on / next target"), (Unlock, "Release lock-on"),
        (Pack, "Pack"), (Journal, "Journal"), (Map, "Map"), (Tree, "Skill tree"), (SkillsUi, "Skills"), (Pause, "Menu"),
    };

    static bool _done;
    static readonly Dictionary<string, InputEvent> _defaults = new();
    public static void Setup()
    {
        if (_done) return; _done = true;
        Bind(Left, Key.A); Bind(Right, Key.D); Bind(Forward, Key.W); Bind(Back, Key.S);
        Bind(Jump, Key.Space); Bind(Sprint, Key.Shift); Bind(Roll, Key.C); Bind(Pause, Key.Escape);
        Bind(Spell1, Key.Key1); Bind(Spell2, Key.Key2); Bind(Spell3, Key.Key3); Bind(Spell4, Key.Key4); Bind("spell5", Key.Key5); Bind("spell6", Key.Key6);
        Bind(PotHp, Key.H); Bind(PotMp, Key.G); Bind(Use, Key.E); Bind(Realm, Key.R); Bind(Mount, Key.T);
        Bind(Pack, Key.I); Bind(Journal, Key.J); Bind(Map, Key.M); Bind(Tree, Key.K);
        Bind(Lock, Key.Tab); Bind(Unlock, Key.Q); Bind(SkillsUi, Key.L);
        BindMouse(Attack, MouseButton.Left); BindMouse(Guard, MouseButton.Right);
        InputMap.ActionAddEvent(Lock, new InputEventMouseButton { ButtonIndex = MouseButton.Middle });   // extra default, dropped once rebound
        ApplyBinds();
    }

    static void Bind(string action, Key key) => Add(action, new InputEventKey { PhysicalKeycode = key });
    static void BindMouse(string action, MouseButton b) => Add(action, new InputEventMouseButton { ButtonIndex = b });
    static void Add(string action, InputEvent ev)
    {
        if (!InputMap.HasAction(action)) InputMap.AddAction(action);
        InputMap.ActionAddEvent(action, ev); _defaults.TryAdd(action, ev);
    }

    // ---- saving bindings as short strings
    public static string Encode(InputEvent ev) => ev switch
    {
        InputEventKey k => "k:" + (long)(k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode),
        InputEventMouseButton m => "m:" + (long)m.ButtonIndex,
        _ => "",
    };
    public static InputEvent? Decode(string s)
    {
        if (s.Length < 3 || !long.TryParse(s[2..], out long v)) return null;
        return s[0] == 'k' ? new InputEventKey { PhysicalKeycode = (Key)v } : s[0] == 'm' ? new InputEventMouseButton { ButtonIndex = (MouseButton)v } : null;
    }

    /// <summary>Puts the saved bindings (Settings.Binds) into the input map; anything not saved keeps its default.</summary>
    public static void ApplyBinds()
    {
        if (!_done) return; _keyCache.Clear();
        foreach (var (id, _) in Rebindable)
        {
            if (Game.Set.Binds.TryGetValue(id, out var s) && Decode(s) is InputEvent ev) { InputMap.ActionEraseEvents(id); InputMap.ActionAddEvent(id, ev); }
        }
    }
    /// <summary>The event currently bound to an action (the first one).</summary>
    public static InputEvent? Current(string action) { if (!InputMap.HasAction(action)) return null; var l = InputMap.ActionGetEvents(action); return l.Count > 0 ? l[0] : null; }

    /// <summary>Binds an action to a key or mouse button. Another action already on that input gets this action's old input (a swap).
    /// Returns the name of the action that was swapped, or null.</summary>
    public static string? Rebind(string action, InputEvent ev)
    {
        string code = Encode(ev); if (code == "") return null; string? swapped = null; _keyCache.Clear();
        var old = Current(action);
        foreach (var (id, name) in Rebindable)
        {
            if (id == action || Current(id) is not InputEvent cur || Encode(cur) != code) continue;
            InputMap.ActionEraseEvents(id); if (old != null) { InputMap.ActionAddEvent(id, old); Game.Set.Binds[id] = Encode(old); } else Game.Set.Binds.Remove(id);
            swapped = name;
        }
        InputMap.ActionEraseEvents(action); InputMap.ActionAddEvent(action, Decode(code)!); Game.Set.Binds[action] = code;
        Game.SaveSettings(); return swapped;
    }
    /// <summary>Every action back to its default key.</summary>
    public static void ResetAll(bool save = true)
    {
        Game.Set.Binds.Clear(); _keyCache.Clear();
        foreach (var (id, _) in Rebindable) if (_defaults.TryGetValue(id, out var ev)) { InputMap.ActionEraseEvents(id); InputMap.ActionAddEvent(id, ev); }
        InputMap.ActionAddEvent(Lock, new InputEventMouseButton { ButtonIndex = MouseButton.Middle });
        if (save) Game.SaveSettings();
    }

    /// <summary>Readable name of an input: "E", "Space", "Left mouse".</summary>
    public static string Name(InputEvent? ev)
    {
        switch (ev)
        {
            case InputEventKey k:
                var key = k.PhysicalKeycode != Key.None ? DisplayServer.KeyboardGetKeycodeFromPhysical(k.PhysicalKeycode) : k.Keycode;
                if (key == Key.None) key = k.PhysicalKeycode;
                return OS.GetKeycodeString(key);
            case InputEventMouseButton m:
                return m.ButtonIndex switch
                {
                    MouseButton.Left => "Left mouse", MouseButton.Right => "Right mouse", MouseButton.Middle => "Middle mouse",
                    MouseButton.WheelUp => "Wheel up", MouseButton.WheelDown => "Wheel down", MouseButton.Xbutton1 => "Mouse 4", MouseButton.Xbutton2 => "Mouse 5",
                    _ => "Mouse " + (int)m.ButtonIndex,
                };
            default: return "-";
        }
    }
    /// <summary>Short name of the key bound to an action, for hints ("I pack").</summary>
    public static string KeyOf(string action)
    {
        if (_keyCache.TryGetValue(action, out var c)) return c;   // the HUD asks every frame
        var n = Name(Current(action)); n = n == "Left mouse" ? "LMB" : n == "Right mouse" ? "RMB" : n == "Middle mouse" ? "MMB" : n;
        _keyCache[action] = n; return n;
    }
    static readonly Dictionary<string, string> _keyCache = new();
}
