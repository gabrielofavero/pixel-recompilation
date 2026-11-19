using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Port.Hooks;

internal sealed record MenuTarget(string Id, Rectangle Bounds, Keys Action, int? Selection = null);

internal static class MenuTargets
{
    internal static List<MenuTarget> Build(string state, string substate, object? component, bool paused)
    {
        List<MenuTarget> targets = [];
        if (component is null) return targets;
        void Sprite(string id, object sprite, Keys action, int? selection = null, int maxHeight = int.MaxValue) =>
            targets.Add(new(id, GameAccess.SpriteBounds(sprite, maxHeight), action, selection));
        void Region(string id, Keys action, Rectangle bounds) => targets.Add(new(id, bounds, action));
        if (state is "START" or "TITLE")
        {
            if (substate == "START")
                Sprite("start", GameAccess.Get<object[]>(component, "m_sprPressStart")[0], Keys.Space);
            else if (substate == "SELECTION")
            {
                var sprites = GameAccess.Get<object[]>(component, "m_sprMenuChoix");
                for (int i = 0; i < sprites.Length; i++)
                {
                    if (i == 1 && !GameAccess.Get<bool>(component, "m_continue")) continue;
                    if (i == 3 && !GameAccess.Get<bool>(component, "m_isTrialMode")) continue;
                    Sprite("title-" + i, sprites[i], Keys.A, i, 42);
                }
                Region("quit", Keys.Escape, new(60, 590, 210, 64));
            }
            else if (substate == "CONFIRM")
            {
                foreach (var (name, key) in new[] { ("m_sprBoutonA", Keys.A), ("m_sprBoutonB", Keys.B) })
                {
                    var sprite = GameAccess.Get(component, name)!;
                    targets.Add(new(name, new((int)GameAccess.Get<float>(sprite, "m_sprX") - 32,
                        (int)GameAccess.Get<float>(sprite, "m_sprY") - 26, 240, 52), key));
                }
            }
        }
        else if (state == "CONTINUE" && substate == "SELECTION")
        {
            var sprites = GameAccess.Get<object[]>(component, "m_sprMenuChoix");
            int maximum = GameAccess.Get<int>(component, "m_menuChoixMax");
            for (int i = 0; i <= maximum; i++) Sprite("level-" + i, sprites[i], Keys.A, i, 42);
        }
        else if (state == "GAME" && paused)
        {
            // Existing pause artwork: Main Menu on the left, Continue on the right.
            Region("resume", Keys.Space, new(660, 470, 380, 96));
            Region("return-title", Keys.Escape, new(240, 470, 380, 96));
        }
        else if (state == "INTRO" && substate == "PAGE2" && GameAccess.Get<int>(component, "m_timer") == 0)
            Sprite("lets-go", GameAccess.Get(component, "m_sprButton")!, Keys.A);
        else if ((state == "OUTRO" && substate is "PAGE1" or "PAGE2" ||
                  state.StartsWith("END_", StringComparison.Ordinal) && substate is "END1" or "END2") &&
                 GameAccess.Get<int>(component, "m_timer") == 0)
            Sprite("continue", GameAccess.Get(component, "m_sprButton")!, Keys.A);
        return targets;
    }
}
