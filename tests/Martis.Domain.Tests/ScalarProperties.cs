using System.Reflection;

namespace Martis.Domain.Tests;

internal static class ScalarProperties
{
    public static IReadOnlyList<string> WritableDoubles(Type type)
    {
        return type
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.PropertyType == typeof(double) && property.CanWrite)
            .Select(property => property.Name)
            .ToArray();
    }

    public static void Set(object target, string name, double value)
    {
        var property = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        property!.SetValue(target, value);
    }
}
