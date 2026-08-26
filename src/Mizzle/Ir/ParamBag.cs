namespace Mizzle.Ir;

public sealed class ParamBag
{
    private readonly List<object?> _values = [];

    public IReadOnlyList<object?> Values => _values;

    public ParamRef Add(object? value, Type clrType)
    {
        var slot = _values.Count;
        _values.Add(value);
        return new ParamRef(slot, clrType);
    }
}
