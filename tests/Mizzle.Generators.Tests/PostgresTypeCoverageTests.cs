using Microsoft.CodeAnalysis;

namespace Mizzle.Generators.Tests;

public sealed class PostgresTypeCoverageTests
{
    private const string Tables = """
        using System;
        using Mizzle.Postgres;

        namespace Demo;

        public sealed class Readings : PgTable<Readings>
        {
            public Readings() : base("readings", "public") { }
            public PgColumn<Guid> ReadingId { get; } = Uuid("reading_id").NotNull();
            public PgColumn<short> Bucket { get; } = SmallInt("bucket").NotNull();
            public PgColumn<decimal> Amount { get; } = Numeric("amount").NotNull();
            public PgColumn<decimal> Price { get; } = Money("price").NotNull();
            public PgColumn<float> Ratio { get; } = Real("ratio").NotNull();
            public PgColumn<double> Precise { get; } = DoublePrecision("precise").NotNull();
            public PgColumn<DateTime> TakenAt { get; } = Timestamp("taken_at").NotNull();
            public PgColumn<TimeOnly> TakenTime { get; } = Time("taken_time").NotNull();
            public PgColumn<byte[]> Blob { get; } = Bytea("blob").NotNull();
            public PgColumn<string> Payload { get; } = Jsonb("payload").NotNull();
        }
        """;

    [Fact]
    public void All_postgres_factories_project_their_clr_types()
    {
        const string callSite = """
            using System;
            using System.Threading.Tasks;
            using Mizzle.Fluent;
            using Mizzle.Postgres;

            namespace Demo;

            public static class PgTypesQ
            {
                public static async Task Run(PostgresDb db)
                {
                    var r = new Readings();
                    var rows = await db.Select(
                            r.ReadingId, r.Bucket, r.Amount, r.Price, r.Ratio,
                            r.Precise, r.TakenAt, r.TakenTime, r.Blob, r.Payload)
                        .From(r)
                        .ToListAsync<ReadingRow>();
                }
            }
            """;

        var (result, diagnostics) = GeneratorTestHost.RunAndCompile(Tables, callSite);
        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var generated = GeneratorTestHost.Generated(result);
        Assert.Contains("short Bucket", generated, StringComparison.Ordinal);
        Assert.Contains("decimal Amount", generated, StringComparison.Ordinal);
        Assert.Contains("float Ratio", generated, StringComparison.Ordinal);
        Assert.Contains("double Precise", generated, StringComparison.Ordinal);
        Assert.Contains("System.DateTime TakenAt", generated, StringComparison.Ordinal);
        Assert.Contains("global::System.TimeOnly TakenTime", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Timestamp_means_datetime_on_postgres_and_rowversion_on_sql_server()
    {
        // Same factory name, different storage. Only the converter path reads the
        // factory name, so both need a Map column to exercise it.
        const string pg = """
            using System;
            using Mizzle.Postgres;

            namespace Demo;

            internal static class PgConv
            {
                public static long ToTicks(DateTime value) => value.Ticks;
                public static DateTime FromTicks(long value) => new(value);
            }

            public sealed class PgEvents : PgTable<PgEvents>
            {
                public PgEvents() : base("events", "public") { }
                public PgColumn<long> At { get; } = Timestamp("at").Map(PgConv.ToTicks, PgConv.FromTicks).NotNull();
            }
            """;
        const string sql = """
            using System;
            using Mizzle.SqlServer;

            namespace Demo;

            internal static class SqlConv
            {
                public static string ToText(byte[] value) => Convert.ToBase64String(value);
                public static byte[] FromText(string value) => Convert.FromBase64String(value);
            }

            public sealed class SqlEvents : SqlTable<SqlEvents>
            {
                public SqlEvents() : base("events", "dbo") { }
                public SqlColumn<string> Version { get; } = Timestamp("row_version").Map(SqlConv.ToText, SqlConv.FromText).NotNull();
            }
            """;

        var pgGenerated = GeneratorTestHost.Generated(GeneratorTestHost.Run(pg));
        Assert.Contains("PgConv.ToTicks(r.GetDateTime(0))", pgGenerated, StringComparison.Ordinal);

        var sqlGenerated = GeneratorTestHost.Generated(GeneratorTestHost.Run(sql));
        Assert.Contains("SqlConv.ToText(r.GetFieldValue<byte[]>(0))", sqlGenerated, StringComparison.Ordinal);
    }
}
