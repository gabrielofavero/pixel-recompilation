using Microsoft.Xna.Framework.Input;

namespace Port.Hooks;

// Translate authored desktop bindings to the original input reader's key vocabulary.
// The original reader continues to own pressed/held edges, analog values, and pad merging.
public static class KeyboardScheme
{
    public static KeyboardState Translate(KeyboardState source, bool gameplay, bool paused, bool startScreen)
    {
        List<Keys> output = [];
        bool Down(params Keys[] keys) => keys.Any(source.IsKeyDown);
        void Map(Keys target, params Keys[] keys) { if (Down(keys)) output.Add(target); }
        Map(Keys.Left, Keys.A, Keys.Left);
        Map(Keys.Right, Keys.D, Keys.Right);
        Map(Keys.Up, Keys.W, Keys.Up);
        Map(Keys.Down, Keys.S, Keys.Down);
        Map(Keys.A, Keys.Space, Keys.Enter);
        Map(Keys.B, Keys.E, Keys.Back);
        Map(Keys.X, Keys.LeftShift, Keys.RightShift);
        Map(Keys.Y, Keys.Q);
        Map(Keys.Subtract, Keys.Tab);
        Map(Keys.PageUp, Keys.PageUp);
        Map(Keys.PageDown, Keys.PageDown);
        if (gameplay)
        {
            Map(Keys.Space, Keys.Escape, Keys.P);
            if (paused) Map(Keys.Escape, Keys.Back);
        }
        else
        {
            // The legacy reader names Xbox Back/Select "Escape". Both desktop
            // back keys cancel menus; gameplay keeps its separate pause binding.
            if (Down(Keys.Back, Keys.Escape)) { output.Add(Keys.B); output.Add(Keys.Escape); }
            if (startScreen && Down(Keys.Enter, Keys.Space)) output.Add(Keys.Space);
        }
        return new KeyboardState(output.Distinct().ToArray());
    }
}
