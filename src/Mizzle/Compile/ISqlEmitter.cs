using Mizzle.Ir;

namespace Mizzle.Compile;

public interface ISqlEmitter
{
    CompiledSql Emit(Query query, IReadOnlyList<object?> values);
}
