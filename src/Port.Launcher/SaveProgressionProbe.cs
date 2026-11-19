using System.Reflection;
using Microsoft.Xna.Framework;
using SDL3;

// Acceptance-only fixture: invokes the original exit event, then observes the unmodified
// game completion, saving, next-level loading, and Continue selection. It does not play the level.
internal sealed class SaveProgressionProbe(Game game, string mode, Action<SDL.SDL_GamepadButton, string> press)
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly HashSet<int> completed = [];
    private bool checkedSelection, checkedContinue;
    public bool Ready { get; private set; }

    private static object? Field(object instance, string name) => instance.GetType().GetField(name, Fields)!.GetValue(instance);

    public bool Handle(string? state, string? substate, object? component)
    {
        if (state == "START" && substate == "SELECTION" && !checkedSelection)
        {
            var expected = mode == "continue";
            if (Field(component!, "m_continue") is not bool actual || actual != expected)
                throw new InvalidDataException($"Save fixture expected Continue={expected}.");
            checkedSelection = true;
            Console.WriteLine($"SAVE_SCRIPT_TITLE continue={actual} selected={Field(component!, "m_menuChoix")}");
        }
        if (state == "CONTINUE" && substate == "SELECTION")
        {
            if (mode != "continue" || Field(component!, "m_menuChoixMax") is not 1 || Field(component!, "m_menuChoix") is not 1)
                throw new InvalidDataException("Saved progress did not unlock and select Cave in Continue.");
            checkedContinue = true;
            Console.WriteLine("SAVE_SCRIPT_CONTINUE unlocked=grassland,cave selected=cave");
            press(SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH, "continue saved Cave level");
            return true;
        }
        if (state != "GAME") return false;
        if (Field(component!, "m_timerIntro") is not 0 || Field(component!, "m_level") is not object level) return true;
        int index = (int)Field(component!, "m_levelIndex")!;
        if (mode == "complete" && index < 2)
        {
            if (completed.Add(index))
            {
                level.GetType().GetMethod("OnExitReached", Fields)!.Invoke(level, null);
                Console.WriteLine($"SAVE_SCRIPT_FIXTURE level={index} action=invoke-original-exit-event");
            }
            return true;
        }
        if (index != 2) throw new InvalidDataException($"Expected Cave (level 2), got {index}.");
        var data = game.GetType().Assembly.GetType("LowLevel.CLASS_SAVE")!.GetField("m_saveData")!.GetValue(null)!;
        var levels = (bool[])Field(data, "m_savLevels")!;
        if (!levels.SequenceEqual(new[] { true, true, false, false, false, false, false }))
            throw new InvalidDataException("Original level completion did not persist the expected unlocks.");
        Ready = true;
        Console.WriteLine($"SAVE_SCRIPT_GAME mode={mode} level={index} unlocks=1100000");
        return true;
    }

    public void Verify()
    {
        if (!Ready || !checkedSelection || (mode == "continue" ? !checkedContinue : !completed.SetEquals([0, 1])))
            throw new InvalidOperationException("Save progression script did not complete.");
        Console.WriteLine($"SAVE_SCRIPT_RESULT mode={mode} progression=ok");
    }
}
