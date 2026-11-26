using System;
using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// Registers all input actions in code so project.godot stays clean. Every action in <see cref="Rebindable"/> can be changed in
/// Settings > Controls; changes are saved in settings.json (Settings.Binds) as "k:physical keycode" or "m:mouse button".
/// Gamepads (Xbox, PlayStation, generic) have their own binding set (Settings.PadBinds, Settings > Controller) that never touches the
/// keyboard one: "b:button", "a:axis" (a trigger), with "L+" in front for a left-trigger combo. <see cref="PadInput"/> turns pad state into
/// these actions every frame.
/// </summary>
public static class Inputs
{
    public const string Left = "move_left", Right = "move_right", Forward = "move_forward", Back = "move_back";
    public const string Jump = "jump", Sprint = "sprint", Roll = "roll", Pause = "pause";
    public const string Spell1 = "spell1", Spell2 = "spell2", Spell3 = "spell3", Spell4 = "spell4";
    public const string PotHp = "pot_hp", PotMp = "pot_mp", Use = "interact", Realm = "realm", Mount = "mount";
    public const string Pack = "ui_pack", Journal = "ui_journal", Map = "ui_map", Tree = "ui_tree";
    public const string Lock = "lock_on", Unlock = "lock_off", SkillsUi = "ui_skills", AttacksUi = "ui_attacks", ModsUi = "ui_mods", SpellcraftUi = "ui_spellcraft";
    public const string Attack = "attack", Guard = "guard", Heavy = "heavy_attack";
    public const string Macro1 = "macro1", Macro2 = "macro2";   // skill chains (PlayerMacros.cs; set up in Skills > Macros)
    public const string Flag = "playtest_flag";   // F8: screenshot + report for later review (scripts/Flag.cs)
    public const string BuildMode = "build_mode", BlockRotate = "block_rotate";   // the block world (PlayerBlocks.cs)
    public const string PetsUi = "ui_pets", ThrowCube = "throw_cube", CubeNext = "cube_next", PetStay = "pet_stay";   // pets (UiPets.cs, Cube.cs, Pet.cs)

    /// <summary>Actions shown in the Controls menu, in order, with their names.</summary>
    public static readonly (string id, string name)[] Rebindable =
    {
        (Forward, "Move forward"), (Back, "Move back"), (Left, "Move left"), (Right, "Move right"),
        (Sprint, "Sprint"), (Jump, "Jump"), (Roll, "Roll"), (Attack, "Attack (hold to keep the chain going)"), (Heavy, "Heavy attack"), (Guard, "Guard with shield"),
        (Spell1, "Skill 1"), (Spell2, "Skill 2"), (Spell3, "Skill 3"), (Spell4, "Skill 4"), ("spell5", "Skill 5"), ("spell6", "Skill 6"),
        (Macro1, "Macro 1 (tap: play once, hold: loop)"), (Macro2, "Macro 2 (tap: play once, hold: loop)"),
        (PotHp, "Health potion"), (PotMp, "Mana potion"), (Use, "Interact"), (Realm, "Shift realm"), (Mount, "Horse"),
        (Lock, "Lock on / next target"), (Unlock, "Release lock-on"),
        (Pack, "Pack"), (Journal, "Journal"), (Map, "Map"), (Tree, "Skill tree"), (SkillsUi, "Skills"), (AttacksUi, "Basic attacks"), (ModsUi, "Modifier tree"), (SpellcraftUi, "Spellcraft"), (Pause, "Menu"),
        (Flag, "Flag for review (screenshot + report)"),
        (BuildMode, "Build mode (dig and place blocks)"), (BlockRotate, "Rotate block (build mode)"),
        (PetsUi, "Pets"), (ThrowCube, "Throw Soul Cube"), (CubeNext, "Next Soul Cube tier"), (PetStay, "Pet: follow / stay"),
    };

    static bool _done;
    public static bool IsSetup => _done;
    static readonly Dictionary<string, InputEvent> _defaults = new();
    public static void Setup()
    {
        if (_done) return; _done = true;
        Bind(Left, Key.A); Bind(Right, Key.D); Bind(Forward, Key.W); Bind(Back, Key.S);
        Bind(Jump, Key.Space); Bind(Sprint, Key.Shift); Bind(Roll, Key.C); Bind(Pause, Key.Escape);
        Bind(Macro1, Key.Z); Bind(Macro2, Key.X); Bind(Spell1, Key.Key1); Bind(Spell2, Key.Key2); Bind(Spell3, Key.Key3); Bind(Spell4, Key.Key4); Bind("spell5", Key.Key5); Bind("spell6", Key.Key6);
        Bind(PotHp, Key.H); Bind(PotMp, Key.G); Bind(Use, Key.E); Bind(Realm, Key.R); Bind(Mount, Key.T);
        Bind(Pack, Key.I); Bind(Journal, Key.J); Bind(Map, Key.M); Bind(Tree, Key.K);
        Bind(Lock, Key.Tab); Bind(Unlock, Key.Q); Bind(SkillsUi, Key.L); Bind(AttacksUi, Key.O); Bind(ModsUi, Key.U); Bind(SpellcraftUi, Key.B); Bind(Flag, Key.F8);
        Bind(BuildMode, Key.N); Bind(BlockRotate, Key.Y);
        Bind(PetsUi, Key.P); Bind(ThrowCube, Key.V); Bind(CubeNext, Key.Comma); Bind(PetStay, Key.Period);
        BindMouse(Attack, MouseButton.Left); BindMouse(Heavy, MouseButton.Right); Bind(Guard, Key.F);
        InputMap.ActionAddEvent(Lock, new InputEventMouseButton { ButtonIndex = MouseButton.Middle });   // extra default, dropped once rebound
        // the analog stick arrives already dead-zoned and rescaled (PadInput), so the move actions keep only a tiny deadzone of their own
        foreach (var m in new[] { Left, Right, Forward, Back }) InputMap.ActionSetDeadzone(m, .05f);
        // menus with a pad: A presses, B backs out (Godot's built-in ui_* actions). The D-pad and left stick do NOT sit in ui_up/down/left/right:
        // a real stick sends a stream of motion events and every one past the deadzone counted as a new press, so focus flew several rows
        // per push (and jittered while held). PadInput turns D-pad + stick into clean ui_* presses with a hold-to-repeat instead.
        PadUi("ui_accept", Btn(JoyButton.A)); PadUi("ui_cancel", Btn(JoyButton.B));
        foreach (var ui in new[] { "ui_up", "ui_down", "ui_left", "ui_right" })
            if (InputMap.HasAction(ui)) foreach (var ev in InputMap.ActionGetEvents(ui)) if (ev is InputEventJoypadButton or InputEventJoypadMotion) InputMap.ActionEraseEvent(ui, ev);
        ApplyBinds();
    }

    static void Bind(string action, Key key) => Add(action, new InputEventKey { PhysicalKeycode = key });
    static void BindMouse(string action, MouseButton b) => Add(action, new InputEventMouseButton { ButtonIndex = b });
    static void Add(string action, InputEvent ev)
    {
        if (!InputMap.HasAction(action)) InputMap.AddAction(action);
        InputMap.ActionAddEvent(action, ev); _defaults.TryAdd(action, ev);
    }
    static InputEventJoypadButton Btn(JoyButton b) => new() { ButtonIndex = b, Device = -1 };
    static InputEventJoypadMotion Ax(JoyAxis a, float v) => new() { Axis = a, AxisValue = v, Device = -1 };
    static void PadUi(string action, InputEvent ev)
    {
        if (!InputMap.HasAction(action)) return;
        if (!InputMap.ActionHasEvent(action, ev)) InputMap.ActionAddEvent(action, ev);
    }

    // ---- saving bindings as short strings
    public static string Encode(InputEvent ev) => ev switch
    {
        InputEventKey k => "k:" + (long)(k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode),
        InputEventMouseButton m => "m:" + (long)m.ButtonIndex,
        InputEventJoypadButton j => "b:" + (long)j.ButtonIndex,
        InputEventJoypadMotion j => "a:" + (long)j.Axis,
        _ => "",
    };
    public static InputEvent? Decode(string s)
    {
        if (s.StartsWith("L+")) s = s[2..];
        if (s.Length < 3 || !long.TryParse(s[2..], out long v)) return null;
        return s[0] switch
        {
            'k' => new InputEventKey { PhysicalKeycode = (Key)v },
            'm' => new InputEventMouseButton { ButtonIndex = (MouseButton)v },
            'b' => Btn((JoyButton)v),
            'a' => Ax((JoyAxis)v, 1f),
            _ => null,
        };
    }

    /// <summary>Puts the saved bindings (Settings.Binds) into the input map; anything not saved keeps its default.</summary>
    public static void ApplyBinds()
    {
        if (!_done) return; _keyCache.Clear();
        foreach (var (id, _) in Rebindable)
        {
            if (Game.Set.Binds.TryGetValue(id, out var s) && Decode(s) is InputEvent ev && ev is InputEventKey or InputEventMouseButton) { InputMap.ActionEraseEvents(id); InputMap.ActionAddEvent(id, ev); }
        }
    }
    /// <summary>The event currently bound to an action (the first one).</summary>
    public static InputEvent? Current(string action) { if (!InputMap.HasAction(action)) return null; var l = InputMap.ActionGetEvents(action); return l.Count > 0 ? l[0] : null; }

    /// <summary>Binds an action to a key or mouse button. Another action already on that input gets this action's old input (a swap).
    /// Returns the name of the action that was swapped, or null.</summary>
    public static string? Rebind(string action, InputEvent ev)
    {
        string code = Encode(ev); if (code == "" || code[0] is 'b' or 'a') return null; string? swapped = null; _keyCache.Clear();
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
    /// <summary>Every action back to its default key (the controller bindings are left alone: see <see cref="ResetPad"/>).</summary>
    public static void ResetAll(bool save = true)
    {
        Game.Set.Binds.Clear(); _keyCache.Clear();
        foreach (var (id, _) in Rebindable) if (_defaults.TryGetValue(id, out var ev)) { InputMap.ActionEraseEvents(id); InputMap.ActionAddEvent(id, ev); }
        InputMap.ActionAddEvent(Lock, new InputEventMouseButton { ButtonIndex = MouseButton.Middle });
        if (save) Game.SaveSettings();
    }

    /// <summary>Readable name of an input: "E", "Space", "Left mouse", "RB" / "R1".</summary>
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
            case InputEventJoypadButton or InputEventJoypadMotion: return PadName(Encode(ev!), true);
            default: return "-";
        }
    }
    /// <summary>Short name of the key bound to an action, for hints ("I pack"). With a controller in hand: its button ("RB", "Cross").</summary>
    public static string KeyOf(string action)
    {
        if (_keyCache.TryGetValue(action, out var c)) return c;   // the HUD asks every frame
        string n;
        if (UsingPad && PadCode(action) is string pc && pc != "") n = PadName(pc);
        else { n = Name(Current(action)); n = n == "Left mouse" ? "LMB" : n == "Right mouse" ? "RMB" : n == "Middle mouse" ? "MMB" : n; }
        _keyCache[action] = n; return n;
    }
    static readonly Dictionary<string, string> _keyCache = new();
    public static void ClearKeyCache() => _keyCache.Clear();

    // ================================================================ gamepad
    static string B(JoyButton b) => "b:" + (int)b;
    static string LT(JoyButton b) => "L+b:" + (int)b;
    /// <summary>Default controller layout (souls-like). Move is always the left stick and the camera the right stick.</summary>
    public static readonly Dictionary<string, string> PadDefaults = new()
    {
        [Jump] = B(JoyButton.A), [Roll] = B(JoyButton.B), [Use] = B(JoyButton.X), [PotHp] = B(JoyButton.Y),
        [Attack] = B(JoyButton.RightShoulder), [Heavy] = "a:" + (int)JoyAxis.TriggerRight, [Guard] = B(JoyButton.LeftShoulder),
        [Sprint] = B(JoyButton.LeftStick), [Lock] = B(JoyButton.RightStick), [Unlock] = LT(JoyButton.RightStick),
        [Spell1] = B(JoyButton.DpadUp), [Spell2] = B(JoyButton.DpadRight), [Spell3] = B(JoyButton.DpadDown), [Spell4] = B(JoyButton.DpadLeft),
        ["spell5"] = LT(JoyButton.DpadUp), ["spell6"] = LT(JoyButton.DpadRight), [Macro1] = LT(JoyButton.DpadDown), [Macro2] = LT(JoyButton.DpadLeft),
        [PotMp] = LT(JoyButton.Y), [Realm] = LT(JoyButton.X), [Mount] = LT(JoyButton.A), [SkillsUi] = LT(JoyButton.B),
        [Pause] = B(JoyButton.Start), [Pack] = B(JoyButton.Back), [Map] = LT(JoyButton.Back), [Journal] = LT(JoyButton.Start),
        [BuildMode] = LT(JoyButton.LeftStick), [BlockRotate] = LT(JoyButton.LeftShoulder),
        [ThrowCube] = LT(JoyButton.RightShoulder), [PetsUi] = "L+a:" + (int)JoyAxis.TriggerRight,
    };
    static readonly HashSet<string> _moves = new() { Left, Right, Forward, Back };
    public static bool IsMove(string id) => _moves.Contains(id);
    /// <summary>Actions that open or close menus: they work while a menu is open (everything else waits until the button is let go).</summary>
    public static bool IsMenuAction(string id) => id is Pause or Pack or Journal or Map or Tree or SkillsUi or AttacksUi or ModsUi or SpellcraftUi or PetsUi;
    /// <summary>The controller input of an action ("" = none).</summary>
    public static string PadCode(string action) => Game.Set.PadBinds.TryGetValue(action, out var c) ? c : PadDefaults.GetValueOrDefault(action, "");

    /// <summary>Binds an action to a pad button / trigger (optionally an "L+" combo); an action already on it takes this one's old input.</summary>
    public static string? RebindPad(string action, string code)
    {
        if (code == "" || IsMove(action)) return null; string? swapped = null; _keyCache.Clear();
        string old = PadCode(action);
        foreach (var (id, name) in Rebindable)
        {
            if (id == action || IsMove(id) || PadCode(id) != code) continue;
            Game.Set.PadBinds[id] = old; swapped = name;
        }
        Game.Set.PadBinds[action] = code; Game.SaveSettings(); return swapped;
    }
    public static void ResetPad(bool save = true) { Game.Set.PadBinds.Clear(); _keyCache.Clear(); if (save) Game.SaveSettings(); }

    /// <summary>True after the last input came from a controller, false after keyboard or mouse input.</summary>
    public static bool UsingPad { get; private set; }
    /// <summary>"xbox", "playstation" or "generic" (the connected pad; the Settings > Controller "Button names" choice wins).</summary>
    public static string PadStyle => Game.Set.PadGlyphs == 1 ? "xbox" : Game.Set.PadGlyphs == 2 ? "playstation" : _detected;
    static string _detected = "xbox";
    public static int Device { get; private set; }
    /// <summary>Bumped whenever the input device (and so the shown button names) changes.</summary>
    public static int Ver { get; private set; }
    public static int[] Pads = Array.Empty<int>();
    public static bool HasPad => Pads.Length > 0;
    public static event Action? DeviceChanged;

    public static void SetUsingPad(bool on, int device = -1)
    {
        if (on && device >= 0 && device != Device) { Device = device; Detect(device); _keyCache.Clear(); Ver++; }
        if (on == UsingPad) return;
        UsingPad = on; _keyCache.Clear(); Ver++;
        if (on && device >= 0) Detect(device);
        DeviceChanged?.Invoke();
    }
    /// <summary>Works out the controller family from its name and SDL GUID (Sony vendor 054c, Microsoft 045e).</summary>
    public static string Detect(int device)
    {
        string n = Input.GetJoyName(device).ToLowerInvariant(), g = Input.GetJoyGuid(device).ToLowerInvariant();
        _detected = n.Contains("ps3") || n.Contains("ps4") || n.Contains("ps5") || n.Contains("playstation") || n.Contains("dualshock") || n.Contains("dualsense") || n.Contains("sony") || g.Contains("4c05") ? "playstation"
            : n.Contains("xbox") || n.Contains("xinput") || n.Contains("microsoft") || g.Contains("5e04") || g.StartsWith("xinput") ? "xbox" : "generic";
        _keyCache.Clear(); return _detected;
    }
    public static string PadLabel(int device) => Input.GetJoyName(device);

    /// <summary>Name of a pad code for prompts: "A", "RB", "LT+Up" (Xbox / generic) or "Cross", "R1", "L2+Up" (PlayStation).
    /// longForm spells the D-pad out ("D-pad Up").</summary>
    public static string PadName(string code, bool longForm = false)
    {
        if (code == "") return "-";
        bool ps = PadStyle == "playstation"; string pre = "";
        if (code.StartsWith("L+")) { pre = ps ? "L2+" : "LT+"; code = code[2..]; }
        if (code.Length < 3 || !int.TryParse(code[2..], out int v)) return "-";
        if (code[0] == 'a')
            return pre + (JoyAxis)v switch
            {
                JoyAxis.TriggerLeft => ps ? "L2" : "LT", JoyAxis.TriggerRight => ps ? "R2" : "RT",
                JoyAxis.LeftX or JoyAxis.LeftY => "Left stick", JoyAxis.RightX or JoyAxis.RightY => "Right stick", _ => "Axis " + v,
            };
        string d = longForm ? "D-pad " : "";
        return pre + (JoyButton)v switch
        {
            JoyButton.A => ps ? "Cross" : "A", JoyButton.B => ps ? "Circle" : "B", JoyButton.X => ps ? "Square" : "X", JoyButton.Y => ps ? "Triangle" : "Y",
            JoyButton.LeftShoulder => ps ? "L1" : "LB", JoyButton.RightShoulder => ps ? "R1" : "RB",
            JoyButton.LeftStick => ps ? "L3" : "LS", JoyButton.RightStick => ps ? "R3" : "RS",
            JoyButton.Back => ps ? "Share" : "View", JoyButton.Start => ps ? "Options" : "Menu", JoyButton.Guide => ps ? "PS" : "Home",
            JoyButton.DpadUp => d + "Up", JoyButton.DpadDown => d + "Down", JoyButton.DpadLeft => d + "Left", JoyButton.DpadRight => d + "Right",
            JoyButton.Touchpad => "Touchpad", JoyButton.Misc1 => ps ? "Mic" : "Share",
            JoyButton.Paddle1 => "P1", JoyButton.Paddle2 => "P2", JoyButton.Paddle3 => "P3", JoyButton.Paddle4 => "P4",
            _ => "Button " + v,
        };
    }

    // ---- reading the pads (every connected pad counts; the one moved most wins)
    public static bool PadButton(JoyButton b) { foreach (int d in Pads) if (Input.IsJoyButtonPressed(d, b)) return true; return false; }
    public static float PadAxis(JoyAxis a) { float best = 0; foreach (int d in Pads) { float v = Input.GetJoyAxis(d, a); if (Mathf.Abs(v) > Mathf.Abs(best)) best = v; } return best; }
    /// <summary>A stick with the radial deadzone (Settings > Controller) taken off and the rest rescaled to 0..1.</summary>
    public static Vector2 Stick(JoyAxis x, JoyAxis y)
    {
        Vector2 best = Vector2.Zero;
        foreach (int d in Pads) { var v = new Vector2(Input.GetJoyAxis(d, x), Input.GetJoyAxis(d, y)); if (v.LengthSquared() > best.LengthSquared()) best = v; }
        float dz = Mathf.Clamp(Game.Set.PadDeadzone, 0f, .6f), len = best.Length();
        if (len <= dz) return Vector2.Zero;
        return best / len * Mathf.Clamp((len - dz) / (1f - dz), 0f, 1f);
    }

    /// <summary>Controller rumble (Settings > Controller: vibration on/off and strength). Only while playing on the pad.</summary>
    public static void Rumble(float weak, float strong, float secs)
    {
        if (!UsingPad || !Game.Set.Vibration || !HasPad) return;
        float s = Mathf.Clamp(Game.Set.VibStrength, 0f, 1f); if (s <= 0) return;
        Input.StartJoyVibration(Device, Mathf.Clamp(weak * s, 0f, 1f), Mathf.Clamp(strong * s, 0f, 1f), secs);
    }
}
