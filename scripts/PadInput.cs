using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// Autoload: turns Xbox / PlayStation / generic controller state into the game's input actions every frame, using the controller
/// bindings (Settings.PadBinds, defaults in <see cref="Inputs.PadDefaults"/>). Keyboard bindings stay in the InputMap untouched.
/// - left stick: analog movement (radial deadzone, rescaled), emitted as move action strengths
/// - "L+" codes fire only while the left trigger is held; a button with an L+ combo decides plain or combo when it is pressed
/// - a press that starts inside a menu never reaches the game (B closing a window does not also roll); menu toggles always pass
/// - sprint: the Sprint button toggles a run that ends when the stick is let go; holding the Roll button also sprints (dodge into a run)
/// - tracks <see cref="Inputs.UsingPad"/>: true after pad input, false after keyboard or mouse input
/// </summary>
public partial class PadInput : Node
{
    readonly Dictionary<string, bool> _raw = new(), _claimMod = new();
    readonly HashSet<string> _menuHeld = new(), _chorded = new(), _down = new(), _blocked = new();
    readonly Dictionary<string, float> _moveS = new();
    bool _trigR, _trigL, _latch, _sprintOn; float _rollHeld, _idleT, _refresh;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always; ProcessPriority = -100;   // before the player and camera read the actions
        Input.JoyConnectionChanged += OnJoy; Inputs.DeviceChanged += OnDevice; Refresh();
    }
    public override void _ExitTree() { Input.JoyConnectionChanged -= OnJoy; Inputs.DeviceChanged -= OnDevice; }
    // switching between pad and mouse: menus gain or drop focus after this event is done (so the press that switched does not also click)
    static void OnDevice() { if (Ui.I is Ui u) Callable.From(u.PadFocus).CallDeferred(); }
    void OnJoy(long device, bool connected)
    {
        Refresh();
        if (connected) { Inputs.Detect((int)device); Ui.Note($"Controller connected: {Inputs.PadLabel((int)device)}", new Color(.85f, .8f, .6f)); }
        else if (Inputs.Pads.Length == 0) { Inputs.SetUsingPad(false); ReleaseAll(); }
    }
    void Refresh()
    {
        var a = Input.GetConnectedJoypads(); var arr = new int[a.Count]; for (int i = 0; i < a.Count; i++) arr[i] = a[i]; Inputs.Pads = arr;
        if (arr.Length > 0 && !Inputs.UsingPad) Inputs.Detect(arr[0]);
    }

    static bool InGame => Input.MouseMode == Input.MouseModeEnum.Captured && !Ui.AnyOpen;

    public override void _Input(InputEvent e)
    {
        switch (e)
        {
            case InputEventJoypadButton jb:
                if (jb.Pressed) { Inputs.SetUsingPad(true, jb.Device); if (!InGame) _menuHeld.Add("b:" + (int)jb.ButtonIndex); }
                break;
            case InputEventJoypadMotion jm:
                if (Mathf.Abs(jm.AxisValue) > .5f)
                {
                    Inputs.SetUsingPad(true, jm.Device);
                    if (!InGame && jm.Axis is JoyAxis.TriggerLeft or JoyAxis.TriggerRight) _menuHeld.Add("a:" + (int)jm.Axis);
                }
                break;
            case InputEventKey { Pressed: true }: case InputEventMouseButton { Pressed: true }: Inputs.SetUsingPad(false); break;
            case InputEventMouseMotion mm: if (mm.Relative.LengthSquared() > 25f) Inputs.SetUsingPad(false); break;
        }
    }

    bool Raw(string c)
    {
        if (c.Length < 3 || !int.TryParse(c[2..], out int v)) return false;
        if (c[0] == 'b') return Inputs.PadButton((JoyButton)v);
        if (c[0] != 'a') return false;
        float x = Inputs.PadAxis((JoyAxis)v);
        if ((JoyAxis)v == JoyAxis.TriggerRight) { _trigR = _trigR ? x > .35f : x > .55f; return _trigR; }
        return x > .55f;
    }

    void Emit(string id, bool pressed, float strength = 1f) => Input.ParseInputEvent(new InputEventAction { Action = id, Pressed = pressed, Strength = pressed ? strength : 0f });

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _refresh -= dt; if (_refresh <= 0) { _refresh = 2f; Refresh(); }
        if (!Inputs.IsSetup || Inputs.Pads.Length == 0) return;
        bool game = InGame;
        float lta = Inputs.PadAxis(JoyAxis.TriggerLeft); _trigL = _trigL ? lta > .3f : lta > .5f; bool lt = _trigL;

        // ---- movement: left stick
        var ls = game ? Inputs.Stick(JoyAxis.LeftX, JoyAxis.LeftY) : Vector2.Zero;
        Move(Inputs.Left, Mathf.Max(0, -ls.X)); Move(Inputs.Right, Mathf.Max(0, ls.X)); Move(Inputs.Forward, Mathf.Max(0, -ls.Y)); Move(Inputs.Back, Mathf.Max(0, ls.Y));

        // ---- buttons: raw state per code, then which binding (plain / LT combo) owns each press
        _chorded.Clear(); var codes = _codes; codes.Clear();
        foreach (var (id, _) in Inputs.Rebindable)
        {
            if (Inputs.IsMove(id)) continue; string code = Inputs.PadCode(id); if (code == "") continue;
            codes.Add((id, code)); if (code.StartsWith("L+")) _chorded.Add(code[2..]);
        }
        var seen = _seen; seen.Clear();
        foreach (var (_, code) in codes)
        {
            string c = code.StartsWith("L+") ? code[2..] : code; if (!seen.Add(c)) continue;
            bool r = Raw(c), was = _rawPrev.GetValueOrDefault(c);
            if (r && !was) _claimMod[c] = lt;
            if (!r) _menuHeld.Remove(c);
            _raw[c] = r; _rawPrev[c] = r;
        }
        string rollCode = "";
        foreach (var (id, code) in codes)
        {
            bool mod = code.StartsWith("L+"); string c = mod ? code[2..] : code;
            bool r = _raw.GetValueOrDefault(c);
            bool down = r && (_claimMod.GetValueOrDefault(c) == mod || (!mod && !_chorded.Contains(c)));
            if (id == Inputs.Roll && !mod) rollCode = c;
            bool was = _down.Contains(id);
            if (down && !was)
            {
                _down.Add(id);
                bool menu = Inputs.IsMenuAction(id);
                if (!menu && (!game || _menuHeld.Contains(c))) _blocked.Add(id);
                else if (id == Inputs.Sprint) _latch = !_latch;   // the sprint button toggles a run
                else Emit(id, true);
            }
            else if (!down && was)
            {
                _down.Remove(id);
                if (!_blocked.Remove(id) && id != Inputs.Sprint) Emit(id, false);
            }
        }

        // ---- sprint: toggled run (ends when the stick returns to rest) or holding the roll button
        bool rollHeld = rollCode != "" && _down.Contains(Inputs.Roll) && !_blocked.Contains(Inputs.Roll);
        _rollHeld = rollHeld ? _rollHeld + dt : 0f;
        if (ls.LengthSquared() < .04f) _idleT += dt; else _idleT = 0f;
        if (!game || _idleT > .25f) _latch = false;
        bool want = game && (_latch || _rollHeld > .35f);
        if (want != _sprintOn) { _sprintOn = want; Emit(Inputs.Sprint, want); }
    }
    readonly Dictionary<string, bool> _rawPrev = new();
    readonly List<(string id, string code)> _codes = new(); readonly HashSet<string> _seen = new();

    void Move(string id, float s)
    {
        float prev = _moveS.GetValueOrDefault(id);
        if (s <= 0f) { if (prev > 0f) { _moveS[id] = 0f; Emit(id, false); } return; }
        if (Mathf.Abs(s - prev) < .02f) return;
        _moveS[id] = s; Emit(id, true, s);
    }

    void ReleaseAll()
    {
        foreach (var id in _down) if (!_blocked.Contains(id) && id != Inputs.Sprint) Emit(id, false);
        _down.Clear(); _blocked.Clear(); _menuHeld.Clear(); _latch = false;
        if (_sprintOn) { _sprintOn = false; Emit(Inputs.Sprint, false); }
        foreach (var k in new List<string>(_moveS.Keys)) if (_moveS[k] > 0) { _moveS[k] = 0; Emit(k, false); }
    }
}
