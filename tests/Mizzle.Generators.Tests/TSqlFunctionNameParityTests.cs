using System.Reflection;
using Mizzle.Ir;
using Mizzle.SqlServer;

namespace Mizzle.Generators.Tests;

// TSqlFunctionNames is the baker's copy of the function names TSql builds into
// CallExpr. A TSql function added without a case there just stops baking, which
// in Strict mode is a MIZ002 on code that looks fine -- so pin them together.
public sealed class TSqlFunctionNameParityTests
{
    private static IEnumerable<MethodInfo> CallFactories()
        => typeof(TSql)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.ReturnType == typeof(CallExpr))
            .Where(m => m.GetParameters().All(p => p.ParameterType == typeof(Expr)));

    public static TheoryData<string> Factories()
    {
        var data = new TheoryData<string>();
        foreach (var method in CallFactories())
        {
            data.Add(method.Name + "/" + method.GetParameters().Length);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Factories))]
    public void Every_TSql_function_has_a_matching_baker_name(string key)
    {
        var parts = key.Split('/');
        var arity = int.Parse(parts[1]);
        var method = CallFactories().Single(m => m.Name == parts[0] && m.GetParameters().Length == arity);

        object[] arguments = arity == 0 ? [] : [new ValueExpr("x", typeof(string))];
        var runtime = (CallExpr)method.Invoke(null, arguments)!;

        Assert.Equal(runtime.Name, TSqlFunctionNames.For(parts[0], arity));
    }

    [Fact]
    public void Convert_is_not_treated_as_a_call()
    {
        // TSql.Convert produces a ConvertExpr, and ResolveConvertSql owns it.
        Assert.Null(TSqlFunctionNames.For("Convert", 2));
        Assert.Null(TSqlFunctionNames.For("Convert", 3));
    }

    [Fact]
    public void An_unknown_member_has_no_baker_name()
    {
        Assert.Null(TSqlFunctionNames.For("Soundex", 1));
        Assert.Null(TSqlFunctionNames.For("GetDate", 1));
    }
}
