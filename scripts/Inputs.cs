using Godot;

namespace TwinRealms;

/// <summary>Registers all input actions in code so project.godot stays clean.</summary>
public static class Inputs
{
    public const string Left = "move_left", Right = "move_right", Forward = "move_forward", Back = "move_back";
    public const string Jump = "jump", Sprint = "sprint", Roll = "roll", Pause = "pause";
    public const string Spell1 = "spell1", Spell2 = "spell2", Spell3 = "spell3", Spell4 = "spell4";
    public const string PotHp = "pot_hp", PotMp = "pot_mp", Use = "interact", Realm = "realm", Mount = "mount";
    public const string Pack = "ui_pack", Journal = "ui_journal", Map = "ui_map", Tree = "ui_tree";

    static bool _done;
    public static void Setup()
    {
        if (_done) return; _done = true;
        Bind(Left, Key.A); Bind(Right, Key.D); Bind(Forward, Key.W); Bind(Back, Key.S);
        Bind(Jump, Key.Space); Bind(Sprint, Key.Shift); Bind(Roll, Key.C); Bind(Pause, Key.Escape);
        Bind(Spell1, Key.Key1); Bind(Spell2, Key.Key2); Bind(Spell3, Key.Key3); Bind(Spell4, Key.Key4); Bind("spell5", Key.Key5); Bind("spell6", Key.Key6);
        Bind(PotHp, Key.H); Bind(PotMp, Key.G); Bind(Use, Key.E); Bind(Realm, Key.R); Bind(Mount, Key.T);
        Bind(Pack, Key.I); Bind(Journal, Key.J); Bind(Map, Key.M); Bind(Tree, Key.K);
        Bind(Lock, Key.Tab); Bind(Unlock, Key.Q); Bind(SkillsUi, Key.L);
        if (!InputMap.HasAction(Unlock)) InputMap.AddAction(Unlock);
        InputMap.ActionAddEvent(Lock, new InputEventMouseButton { ButtonIndex = MouseButton.Middle });
    }
    public const string Lock = "lock_on", Unlock = "lock_off", SkillsUi = "ui_skills";

    static void Bind(string action, Key key)
    {
        if (!InputMap.HasAction(action)) InputMap.AddAction(action);
        InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
    }
}
