# Mizzle

Fluent, type-safe SQL for .NET, inspired by [Drizzle ORM](https://orm.drizzle.team/).

Declare your schema in C#, build queries against typed columns, and let Mizzle
emit SQL for PostgreSQL or SQL Server. It does not use raw SQL strings, LINQ
translation, or reflection in the query path.

> **Status: experimental.** This is an early alpha. The API will change between releases. PostgreSQL and SQL Server are the only supported databases.

## Packages

| Package | What's in it |
|---|---|
| `Mizzle` | Query IR, fluent builders, operators, paging, transaction contracts |
| `Mizzle.Postgres` | Postgres schema types, SQL emitter, Npgsql execution, `AddMizzlePostgres` |
| `Mizzle.SqlServer` | SQL Server schema types, T-SQL emitter, SqlClient execution, `AddMizzleSqlServer` |
| `Mizzle.Generators` | Source generators: record/mapper generation, compiled query interceptors, Strict-mode analyzer |

Installing a dialect package brings in everything you need:

```bash
dotnet add package Mizzle.Postgres
```

## Quick start

Declare a table:

```csharp
using Mizzle.Postgres;

public sealed class Users : PgTable<Users>
{
    public Users() : base("users", "public", "u") { }

    public PgColumn<int> Id { get; } = Identity("id").PrimaryKey();
    public PgColumn<string> Email { get; } = Text("email").NotNull().Unique();
}
```

Register and query:

```csharp
services.AddMizzlePostgres(connectionString);
```

```csharp
var users = new Users();

// If UserRow does not exist, the generator declares it from the select shape.
var found = await db.Select(users.Id, users.Email)
    .From(users)
    .Where(users.Email.Eq("a@b.com"))
    .ToListAsync<UserRow>();

// Existing DTOs are mapped by normalized member name.
var profile = await db.Select(users.Id, users.Email)
    .From(users)
    .Where(users.Id.Eq(42))
    .FirstOrDefaultAsync<MyExistingDto>();

var id = await db.InsertInto(users)
    .Value(users.Email, "new@example.com")
    .Returning(users.Id)
    .ToListAsync(r => r.GetInt32(0));

await db.Transaction(async tx =>
{
    await tx.LockAsync("invoice:123");
    // Queries here run on the transaction connection.
    // Nested Transaction calls become savepoints.
});
```

Joins keep the same style. Conditions are typed, and chained `Where` calls are
combined with `AND`:

```csharp
var rows = await db.Select(authors.DisplayName, tags.Label)
    .From(authors)
    .LeftJoin(tags).On(authors.FavoriteTagId.Eq(tags.TagId), tags.Kind.Eq("topic"))
    .Where(authors.BlogId.Eq(blogId))
    .OrderBy(authors.DisplayName)
    .ToListAsync<AuthorTagRow>();
```

Guarded updates for optimistic concurrency:

```csharp
await db.Update(users)
    .Set(users.Email, "renamed@example.com")
    .Where(users.Id, 42)
    .Expect(1)          // throws ConcurrencyException if affected rows != 1
    .ExecuteAsync();
```

Paging on any ordered query:

```csharp
var page = await db.Select(users.Id, users.Email)
    .From(users.ToFrom())
    .OrderBy(users.Email.ToRef())
    .Page(2, 25)
    .ToPageAsync(UsersMapper.Read, includeTotal: true);
```

## Legacy storage types

When the database stores something in a shape your domain shouldn't see (GUIDs
in `char(36)`, dates in `char(8)`, `'Y'/'N'` booleans), declare the conversion
once on the column with static method references:

```csharp
public SqlColumn<Guid> PersonId { get; } =
    Char("person_id", 36).Map(EhrConvert.ToGuid, EhrConvert.FromGuid).PrimaryKey();
```

Queries bind converted values (`Where(t.PersonId.Eq(guid))` sends a string, so
indexes still seek), and generated mappers call the converter directly — no
reflection, no runtime registry. The converters must be static method
references (not lambdas) so the source generator can bake them; a lambda is a
build error (`MIZ008`).

## Projecting into domain types

A typed terminator matches columns to members by name, ignoring `_` and case.
When the names differ, alias the column at the select site:

```csharp
var profile = await db.Select(
        persons.PersonId.As("PatientId"),
        persons.Zip.As("PostalCode"),
        persons.FirstName)                 // already matches
    .From(persons)
    .FirstOrDefaultAsync<PatientProfile>(ct);
```

`As` returns a copy, so the table's own column is unchanged, and it emits a real
SQL alias: `SELECT [a].[person_id] AS [PatientId]`. The existing projection
diagnostics report the aliased name, so a typo is `MIZ003` and a type mismatch
is `MIZ010`, both pointing at your call site.

One table can appear in a query more than once -- a lookup table joined for
several coded fields, or a self-join. Each instance needs its own alias:

```csharp
internal static class Ehr
{
    public static readonly Persons   Person      = new();
    public static readonly MstrLists Language    = new MstrLists().WithAlias("lang");
    public static readonly MstrLists ContactPref = new MstrLists().WithAlias("cpref");
}
```

`WithAlias` returns a new instance, so the original keeps its declared alias and
stays shareable. It works the same on a local variable. Two tables sharing an
alias in one query is a build error (`MIZ011`) rather than SQL the database
rejects.

Legacy schemas often store blank-padded `CHAR`. Opt the whole compilation into
trimming string reads:

```xml
<PropertyGroup>
  <MizzleTrimStrings>true</MizzleTrimStrings>
</PropertyGroup>
```

Trimming applies to string storage reads only, before any `Map` converter, and
never on write — so converters stop needing to defend against padding. Exclude a
column where trailing whitespace is meaningful:

```csharp
public SqlColumn<string> Signature { get; } = VarChar("signature", 500).Untrimmed();
```

## How queries execute

Every query builds an immutable IR graph. Before any SQL is written, a capability
pass checks the target dialect and throws `UnsupportedFeatureException` for anything
it cannot do. For example, `ILike` is valid on PostgreSQL but not SQL Server.
Mizzle reports that instead of emitting a different query.

Statically-visible query chains are compiled at build time: a source generator
reconstructs the query, bakes the exact SQL string, and intercepts the call site so
runtime skips IR construction and emission entirely. Dynamic queries fall back to
the runtime pipeline with a shape cache. Setting
`<MizzleQueryMode>Strict</MizzleQueryMode>` in your project turns any
non-compilable query into a build error.

## What it deliberately doesn't do

- **No raw SQL.** If a construct isn't in the IR, it isn't expressible. This is a
  guarantee, not a gap. Check that the current surface covers your needs before
  adopting it.
- No LINQ / `IQueryable`, no sync APIs, no migrations, no MySQL (yet).

## Breaking changes in 0.1.0-alpha.3

- `ParamBag` is gone. Expressions carry their values (`col.Eq(value)`); a
  deterministic parameterization pass extracts them at compile time. Every
  bag-taking overload and the builders' `Parameters` property were removed.
- `IQueryExecutor` signatures changed (no bag parameter; the precompiled
  entry point takes the built query). Custom executors must be updated.
- The schema metadata property `IColumn.IsNotNull` is now `IsRequired`
  (freeing `IsNotNull()` for the SQL operator).

## Building

```bash
dotnet test Mizzle.slnx
```

Unit and generator tests run anywhere; the integration tests use
[Testcontainers](https://dotnet.testcontainers.org/) and need Docker.

## Benchmarks

Benchmarks live in `benchmarks/Mizzle.Benchmarks`.

They compare Mizzle's runtime and source-generated query paths with raw Npgsql
and Dapper. The goal is to show the overhead of Mizzle's typed query API, not to
claim a universal winner.

```bash
dotnet run -c Release --project benchmarks/Mizzle.Benchmarks
```

By default the benchmarks start PostgreSQL with Testcontainers. To use an
existing database instead, set `MIZZLE_BENCH_POSTGRES` to a PostgreSQL
connection string.

## License

MIT
