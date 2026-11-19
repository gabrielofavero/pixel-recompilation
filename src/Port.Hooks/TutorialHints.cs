using System.Runtime.CompilerServices;

namespace Port.Hooks;

internal static class TutorialHints
{
    private sealed class TextState { internal string Original = "", Rendered = ""; }
    private static readonly ConditionalWeakTable<object, TextState> Texts = new();

    internal static string Translate(string text, bool keyboard) => !keyboard ? text : text
        .Replace("[A]", "[Space]").Replace("[B]", "[E]").Replace("[X]", "[Shift]")
        .Replace("[Y]", "[Q]").Replace("[LT]", "[Tab]").Replace("\"Left Trigger\"", "[Tab]")
        .Replace("Left Trigger", "Tab")
        .Replace("How about holding the [Shift] button to run?",
            "How about holding the [Shift]\nbutton to run?");

    internal static void Update(object level, bool keyboard)
    {
        var font = GameAccess.Get(level, "m_fntTextBox")!;
        if (GameAccess.Get(level, "m_state")?.ToString() != "SIGN") return;
        RefreshText(font, keyboard);
    }

    internal static void RefreshText(object font, bool keyboard)
    {
        var state = Texts.GetValue(font, _ => new());
        var text = GameAccess.Get<string>(font, "m_fntText");
        if (text != state.Rendered) state.Original = text;
        var translated = Translate(state.Original, keyboard);
        if (translated != text) font.GetType().GetMethod("Ark_FntSetText")!.Invoke(font, [translated]);
        state.Rendered = translated;
    }
}
