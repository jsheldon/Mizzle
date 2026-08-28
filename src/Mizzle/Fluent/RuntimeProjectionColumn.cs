using System.Data.Common;
using Mizzle.Schema;

namespace Mizzle.Fluent;

internal sealed record RuntimeProjectionColumn(
    string MemberName,
    Type ClrType,
    Func<DbDataReader, int, object?> Read)
{
    public static RuntimeProjectionColumn From(IColumn column)
        => new(
            column.ProjectionName ?? column.Name,
            column.ClrType,
            (reader, ordinal) => column is IRuntimeReadableColumn readable
                ? readable.ReadValue(reader, ordinal)
                : reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal));
}
