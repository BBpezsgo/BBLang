using System.Reflection;
using System.Reflection.Emit;

namespace LanguageCore.IL;

[SuppressMessage("Quality", "MY003")]
public static class Extensions
{
    public static T? GetValue<T>(this FieldInfo field, object? obj) => (T?)field.GetValue(obj);

    public static T CreateDelegate<T>(this DynamicMethod dynamicMethod) where T : Delegate => (T)dynamicMethod.CreateDelegate(typeof(T));
}
