using System.Reflection;

namespace Mizzle.Fluent;

internal static class RuntimeProjectionMapper
{
    public static T Read<T>(IReadOnlyList<RuntimeProjectionColumn> columns, System.Data.Common.DbDataReader reader)
    {
        if (columns.Count == 0)
        {
            throw new InvalidOperationException("A typed projection requires at least one returned column.");
        }

        var target = typeof(T);
        if (columns.Count == 1 && CanAssign(Read(columns[0], reader, 0), target, out var scalar))
        {
            return (T)scalar!;
        }

        var normalized = columns
            .Select((column, ordinal) => (Name: Normalize(column.MemberName), Column: column, Ordinal: ordinal))
            .ToList();
        var duplicate = normalized.GroupBy(c => c.Name).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Projection column '{duplicate.First().Column.MemberName}' is ambiguous after name normalization.");
        }

        var byName = normalized.ToDictionary(c => c.Name, StringComparer.Ordinal);
        var ctor = target.GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .Where(c => !IsCopyConstructor(c, target))
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault();
        if (ctor is null)
        {
            throw new InvalidOperationException($"Projection target '{target.Name}' needs a public constructor.");
        }

        var used = new HashSet<string>(StringComparer.Ordinal);
        var parameters = ctor.GetParameters();
        var args = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            if (!byName.TryGetValue(Normalize(parameter.Name ?? ""), out var match))
            {
                if (parameter.HasDefaultValue)
                {
                    args[i] = parameter.DefaultValue;
                    continue;
                }

                throw new InvalidOperationException($"Required constructor parameter '{parameter.Name}' has no returned column.");
            }

            args[i] = ConvertValue(Read(match.Column, reader, match.Ordinal), parameter.ParameterType, parameter.Name ?? target.Name);
            used.Add(match.Name);
        }

        var result = ctor.Invoke(args);
        foreach (var property in target.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .Where(p => p.SetMethod is not null && p.GetIndexParameters().Length == 0))
        {
            var key = Normalize(property.Name);
            if (used.Contains(key) || !byName.TryGetValue(key, out var match))
            {
                continue;
            }

            property.SetValue(result, ConvertValue(Read(match.Column, reader, match.Ordinal), property.PropertyType, property.Name));
            used.Add(match.Name);
        }

        var unmapped = normalized.FirstOrDefault(c => !used.Contains(c.Name));
        if (unmapped.Column is not null)
        {
            throw new InvalidOperationException($"Returned column '{unmapped.Column.MemberName}' has no matching member on '{target.Name}'.");
        }

        return (T)result;
    }

    private static object? Read(RuntimeProjectionColumn column, System.Data.Common.DbDataReader reader, int ordinal)
        => column.Read(reader, ordinal);

    private static object? ConvertValue(object? value, Type target, string member)
    {
        if (value is null)
        {
            if (Nullable.GetUnderlyingType(target) is not null || !target.IsValueType)
            {
                return null;
            }

            throw new InvalidOperationException($"Returned NULL cannot be assigned to non-nullable member '{member}'.");
        }

        if (CanAssign(value, target, out var assigned))
        {
            return assigned;
        }

        throw new InvalidOperationException($"Returned value for '{member}' is '{value.GetType().Name}', not assignable to '{target.Name}'.");
    }

    private static bool CanAssign(object? value, Type target, out object? assigned)
    {
        assigned = value;
        if (value is null)
        {
            return Nullable.GetUnderlyingType(target) is not null || !target.IsValueType;
        }

        var destination = Nullable.GetUnderlyingType(target) ?? target;
        return destination.IsInstanceOfType(value);
    }

    private static bool IsCopyConstructor(ConstructorInfo ctor, Type target)
    {
        var parameters = ctor.GetParameters();
        return parameters.Length == 1 && parameters[0].ParameterType == target;
    }

    private static string Normalize(string name)
        => name.Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
}
