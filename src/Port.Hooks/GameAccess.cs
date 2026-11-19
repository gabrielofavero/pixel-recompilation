using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Xna.Framework;

namespace Port.Hooks;

internal static class GameAccess
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly ConcurrentDictionary<(Type, string), MemberInfo> Members = new();
    internal static object? Get(object instance, string name)
    {
        var member = Members.GetOrAdd((instance.GetType(), name), key =>
            (MemberInfo?)key.Item1.GetField(key.Item2, Flags) ?? key.Item1.GetProperty(key.Item2, Flags)
            ?? throw new MissingMemberException(key.Item1.FullName, key.Item2));
        return member is FieldInfo field ? field.GetValue(instance) : ((PropertyInfo)member).GetValue(instance);
    }
    internal static T Get<T>(object instance, string name) => (T)Get(instance, name)!;
    internal static void Set(object instance, string name, object value) =>
        ((FieldInfo)Members.GetOrAdd((instance.GetType(), name), key =>
            key.Item1.GetField(key.Item2, Flags) ?? throw new MissingFieldException(key.Item1.FullName, key.Item2))).SetValue(instance, value);
    internal static void Select(object component, int index) => component.GetType()
        .GetMethod("SelectMenu", Flags)!.Invoke(component, [index]);

    internal static Rectangle SpriteBounds(object sprite, int maxHeight = int.MaxValue)
    {
        float x = Get<float>(sprite, "m_sprX"), y = Get<float>(sprite, "m_sprY");
        float sx = Get<float>(sprite, "m_sprScaleX"), sy = Get<float>(sprite, "m_sprScaleY");
        int width = (int)(Get<int>(sprite, "m_sprFrmWidth") * sx);
        int height = Math.Min(maxHeight, (int)(Get<int>(sprite, "m_sprFrmHeight") * sy));
        return new Rectangle((int)(x - Get<float>(sprite, "m_sprOriginX") * sx),
            maxHeight == int.MaxValue ? (int)(y - Get<float>(sprite, "m_sprOriginY") * sy) : (int)y - height / 2,
            width, height);
    }
}
