namespace Mizzle.Paging;

public sealed record Page<T>(IReadOnlyList<T> Items, bool HasMore, int? TotalCount);
