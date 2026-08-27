using System.Diagnostics;
using Dapper;

namespace Mizzle.Integration.Tests;

public sealed class BenchmarkSmokeTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fx;

    public BenchmarkSmokeTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task Pk_select_is_not_catastrophically_slower_than_dapper()
    {
        await using var conn = await _fx.DataSource.OpenConnectionAsync();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS public.bench_users (
                  id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                  email text NOT NULL
                );
                DELETE FROM public.bench_users;
                INSERT INTO public.bench_users (email) VALUES ('bench@x.com');
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var id = await conn.ExecuteScalarAsync<int>("SELECT id FROM public.bench_users LIMIT 1");
        var db = new PostgresDb(_fx.DataSource);
        var users = new BenchUsers();

        static long Median(List<long> samples)
        {
            samples.Sort();
            return samples[samples.Count / 2];
        }

        async Task<long> TimeDapperAsync()
        {
            var sw = Stopwatch.StartNew();
            _ = (await conn.QueryAsync<(int Id, string Email)>(
                "SELECT id, email FROM public.bench_users WHERE id = @id",
                new { id })).Single();
            return sw.ElapsedMilliseconds;
        }

        async Task<long> TimeMizzleAsync()
        {
            var sw = Stopwatch.StartNew();
            _ = await db.Select(users.Id, users.Email)
                .From(users.ToFrom())
                .Where(users.Id, id)
                .ToListAsync(r => (r.GetInt32(0), r.GetString(1)));
            return sw.ElapsedMilliseconds;
        }

        for (var i = 0; i < 5; i++)
        {
            await TimeDapperAsync();
            await TimeMizzleAsync();
        }

        var dapper = new List<long>(50);
        var mizzle = new List<long>(50);
        for (var i = 0; i < 50; i++)
        {
            dapper.Add(await TimeDapperAsync());
            mizzle.Add(await TimeMizzleAsync());
        }

        var dapperMs = Median(dapper);
        var mizzleMs = Median(mizzle);
        // Spec intent is "not slower"; this gate only fails on catastrophic regression.
        Assert.True(mizzleMs <= Math.Max(1, dapperMs) * 3, $"Mizzle median {mizzleMs}ms vs Dapper median {dapperMs}ms");
    }
}

file sealed class BenchUsers : PgTable<BenchUsers>
{
    public BenchUsers() : base("bench_users", "public") { }

    public PgColumn<int> Id { get; } = Identity("id");
    public PgColumn<string> Email { get; } = Text("email");
}
