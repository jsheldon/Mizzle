using System.Reflection;
using Mizzle.SqlServer;

namespace Mizzle.Generators.Tests;

// The baker never runs Mizzle.SqlServer -- it rebuilds a SqlType's text from the
// member name in the syntax tree -- so SqlTypeNames.For is a second copy of
// SqlType.Name. These pin the copies together; a member added to one and not the
// other would otherwise just drop the query off the baked path with no signal.
public sealed class SqlTypeNameParityTests
{
    private const int SampleLength = 20;

    public static TheoryData<string> Properties()
    {
        var data = new TheoryData<string>();
        foreach (var property in typeof(SqlType).GetProperties(BindingFlags.Public | BindingFlags.Static))
        {
            if (property.PropertyType == typeof(SqlType))
            {
                data.Add(property.Name);
            }
        }

        return data;
    }

    public static TheoryData<string> LengthFactories()
    {
        var data = new TheoryData<string>();
        foreach (var method in typeof(SqlType).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (method.ReturnType == typeof(SqlType)
                && method.GetParameters() is [{ ParameterType: var p }]
                && p == typeof(int))
            {
                data.Add(method.Name);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Properties))]
    public void Every_SqlType_property_has_a_matching_baker_name(string member)
    {
        var runtime = (SqlType)typeof(SqlType)
            .GetProperty(member, BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;

        Assert.Equal(runtime.Name, SqlTypeNames.For(member, null));
    }

    [Theory]
    [MemberData(nameof(LengthFactories))]
    public void Every_SqlType_length_factory_has_a_matching_baker_name(string member)
    {
        var runtime = (SqlType)typeof(SqlType)
            .GetMethod(member, BindingFlags.Public | BindingFlags.Static, [typeof(int)])!
            .Invoke(null, [SampleLength])!;

        Assert.Equal(runtime.Name, SqlTypeNames.For(member, SampleLength));
    }

    [Fact]
    public void The_baker_rejects_a_length_the_runtime_would_refuse_to_build()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SqlType.VarChar(0));
        Assert.Null(SqlTypeNames.For("VarChar", 0));
        Assert.Null(SqlTypeNames.For("VarChar", -5));
    }

    [Fact]
    public void An_unknown_member_has_no_baker_name()
    {
        Assert.Null(SqlTypeNames.For("Geography", null));
    }
}
