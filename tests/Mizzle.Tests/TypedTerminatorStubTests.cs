namespace Mizzle.Tests;

public sealed class TypedTerminatorStubTests
{
    private const string ExpectedMessage =
        "Query shape is not statically visible. Use the delegate overload or restructure the chain.";

    private static SelectBuilder Builder()
        => new SelectBuilder()
            .Select(new ColumnRef("u", "email", typeof(string)))
            .From(new FromSource("users", "public", "u"));

    [Fact]
    public async Task All_typed_terminators_throw_when_not_intercepted()
    {
        var b = Builder();
        var ex1 = await Assert.ThrowsAsync<InvalidOperationException>(() => b.ToListAsync<string>());
        var ex2 = await Assert.ThrowsAsync<InvalidOperationException>(() => b.FirstAsync<string>());
        var ex3 = await Assert.ThrowsAsync<InvalidOperationException>(() => b.FirstOrDefaultAsync<string>());
        var ex4 = await Assert.ThrowsAsync<InvalidOperationException>(() => b.SingleAsync<string>());
        var ex5 = await Assert.ThrowsAsync<InvalidOperationException>(() => b.SingleOrDefaultAsync<string>());
        Assert.All(
            [ex1.Message, ex2.Message, ex3.Message, ex4.Message, ex5.Message],
            m => Assert.Equal(ExpectedMessage, m));
    }
}
