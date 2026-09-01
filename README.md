# Mizzle

[![CI](https://github.com/jsheldon/Mizzle/actions/workflows/ci.yml/badge.svg)](https://github.com/jsheldon/Mizzle/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Mizzle.svg)](https://www.nuget.org/packages/Mizzle)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![Status](https://img.shields.io/badge/status-alpha-orange.svg)]()

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
| `Mizzle.Cli` | `dotnet` tool for inspecting databases, scaffolding tables, and explaining query support |

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
    public Users() : base("users", "public") { }

    public PgColumn<int> Id { get; } = Identity("id").PrimaryKey();
    public PgColumn<string> Email { get; } = Text("email").NotNull().Unique();
    public PgColumn<string> DisplayName { get; } = Varchar("display_name", 120);
    public PgColumn<bool> IsActive { get; } = Boolean("is_active").NotNull();
    public PgColumn<Guid> PublicId { get; } = Uuid("public_id").NotNull();
    public PgColumn<DateTimeOffset> CreatedAt { get; } = Timestamptz("created_at").NotNull();
    public PgColumn<DateOnly> Birthday { get; } = Date("birthday");
    public PgColumn<long> LoginCount { get; } = BigInt("login_count");
}
```

The table alias defaults to the table name. Use `WithAlias` at the query site
when one table needs another name for a self-join or repeated lookup.

PostgreSQL tables include factories such as `Text`, `Varchar`, `Char`,
`Integer`, `BigInt`, `Boolean`, `Uuid`, `Date`, `Timestamptz`, and `Identity`.
SQL Server tables include `NVarChar`, `NVarCharMax`, `VarChar`, `Char`, `Text`,
`NText`, `Int`, `SmallInt`, `TinyInt`, `BigInt`, `Decimal`, `Numeric`, `Real`,
`Float`, `Bit`, `UniqueIdentifier`, `Date`, `DateTime`, `DateTime2`,
`Timestamp`, and `Identity`.

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
    .SingleAsync<int>();

var inserted = await db.InsertInto(users)
    .Value(users.Email, "new@example.com")
    .Returning(users.Id, users.Email)
    .SingleAsync<UserRow>();

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
    .From(users)
    .OrderBy(users.Email)
    .Page(2, 25)
    .ToPageAsync<UserRow>(includeTotal: true);
```

## Storage Conversions

Some databases store values in a shape you do not want in your application:
GUIDs in `char(36)`, dates in `char(8)`, or flags as `'Y'` and `'N'`. Declare
that conversion on the column:

```csharp
public SqlColumn<Guid> AccountId { get; } =
    Char("account_id", 36)
        .Map(AccountConvert.ToGuid, AccountConvert.FromGuid)
        .PrimaryKey();
```

Queries use the mapped type:

```csharp
await db.Select(accounts.AccountId, accounts.Email)
    .From(accounts)
    .Where(accounts.AccountId.Eq(accountId))
    .SingleAsync<AccountRow>();
```

The write side receives the storage value, so the predicate above sends a
string to the database. Generated mappers call the read converter directly.
Converters must be static method references, not lambdas, so the generator can
see and bake the call. Lambdas report `MIZ008`.

## Projecting into domain types

Typed terminators come in two modes.

If the result type does not exist, Mizzle generates a record from the selected
columns:

```csharp
var rows = await db.Select(users.Id, users.Email)
    .From(users)
    .ToListAsync<UserRow>();
```

If the result type already exists, Mizzle maps into it by normalized member
name. Underscores and casing are ignored, so `display_name` can match
`DisplayName`.

When a column name and member name do not line up, use `As` at the select site:

```csharp
var row = await db.Select(
        books.BookId.As("Id"),
        books.DisplayTitle.As("Title"),
        authors.DisplayName.As("Author"))
    .From(books)
    .InnerJoin(authors).On(books.AuthorId.Eq(authors.AuthorId))
    .SingleAsync<BookSummary>(ct);
```

`As` returns a copy of the column. The table's column stays unchanged, and SQL
gets a real alias such as `AS [Title]`. Projection diagnostics use the aliased
name, so a typo reports `MIZ003` and a type mismatch reports `MIZ010` at the
call site.

`As` also accepts `nameof(...)`, which keeps the alias in sync if the target
member is renamed:

```csharp
books.BookId.As(nameof(BookSummary.Id))
```

Anything else -- a field, a variable, string concatenation -- is not a
compile-time constant and falls back to the runtime path.

The delegate overloads are always runtime mapped:

```csharp
var rows = await db.Select(users.Id, users.Email)
    .From(users)
    .ToListAsync(r => new UserRow(r.GetInt32(0), r.GetString(1)));
```

The delegate-free typed terminators need a statically visible query chain so
the source generator can intercept the call. If you pass around a dynamic
`SelectBuilder`, use the delegate overload.

## Reusing Tables

Use `WithAlias` when one table appears more than once in a query. It works for
self-joins and for lookup tables used in several roles:

```csharp
var primaryTag = new Tags().WithAlias("primary_tag");
var secondaryTag = new Tags().WithAlias("secondary_tag");

var rows = await db.Select(
        posts.Title,
        primaryTag.Label.As("PrimaryTag"),
        secondaryTag.Label.As("SecondaryTag"))
    .From(posts)
    .LeftJoin(primaryTag).On(posts.PrimaryTagId.Eq(primaryTag.TagId))
    .LeftJoin(secondaryTag).On(posts.SecondaryTagId.Eq(secondaryTag.TagId))
    .ToListAsync<PostTagRow>();
```

`WithAlias` returns a new table instance. The original table keeps its default
alias and can still be shared. If two table instances in one generated query
use the same alias, Mizzle reports `MIZ011`.

## Trimming Strings

Databases with fixed-width text columns often return padded strings. You can opt
generated mappers into trimming string reads:

```xml
<PropertyGroup>
  <MizzleTrimStrings>true</MizzleTrimStrings>
</PropertyGroup>
```

Trimming applies to string storage reads before any `Map` converter. It does
not run on writes. Exclude a column when trailing whitespace is meaningful:

```csharp
public SqlColumn<string> Signature { get; } = VarChar("signature", 500).Untrimmed();
```

## Common table expressions

`With` and `WithRecursive` attach a CTE to any statement -- select, insert,
update or delete:

```csharp
var stale = db.Select(o.OrderId).From(o).Where(o.Status.Eq("abandoned")).Build();

await db.DeleteFrom(o)
    .With(CteBuilder.Named("stale", stale))
    .Where(o.Status.Eq("abandoned"))
    .ExecuteAsync(ct);
```

A CTE whose body is a statically visible chain is baked along with the outer
query, so CTE queries stay on the interceptor path instead of falling back to
runtime compilation.

To join a CTE with typed columns, declare its shape as a table whose name is the
CTE name and whose schema is omitted:

```csharp
public sealed class RxNorm : PgTable<RxNorm>
{
    public RxNorm() : base("rxnorm") { }
    public PgColumn<string> Ndc { get; } = Text("ndc").NotNull();
    public PgColumn<string> Code { get; } = Text("code").NotNull();
}

var rows = await db.Select(o.OrderId, rx.Code.As("Code"))
    .With(CteBuilder.Named("rxnorm", body))
    .From(o)
    .LeftJoin(rx).On(o.Ndc.Eq(rx.Ndc))
    .ToListAsync<OrderCode>();
```

The CTE then behaves like any other table: typed columns, `As(...)`, left-join
nullability, and the projection diagnostics. Note that nothing checks the
declared columns against the CTE body's select list -- a mismatch surfaces at the
database, not at build time.

## Returning rows from writes

Insert, update and delete expose the same typed terminators as select --
`ToListAsync<T>`, `FirstAsync<T>`, `FirstOrDefaultAsync<T>`, `SingleAsync<T>`,
`SingleOrDefaultAsync<T>` -- over their `Returning(...)` columns, and `As(...)`
works there too:

```csharp
var updated = await db.Update(o)
    .Set(o.Status, "shipped")
    .Where(o.OrderId.Eq(id))
    .Returning(o.OrderId.As("Id"), o.Status)
    .SingleAsync<ShippedOrder>(ct);
```

Write projections are mapped at runtime rather than baked, but the projection
diagnostics still run at build time, so a returning-into-T mismatch is a
compile error rather than a runtime throw.

## How queries execute

Every query builds an immutable IR graph. That is just a small object model for
the query: selected columns, source table, joins, predicates, ordering, limits,
and values. Each builder call returns the next query shape instead of mutating
the old one.

Before any SQL is written, a capability pass checks the target dialect and
throws `UnsupportedFeatureException` for anything it cannot do. For example,
`ILike` is valid on PostgreSQL but not SQL Server. Mizzle reports that instead
of emitting a different query.

What that means in practice:

- SQL and parameters stay separate until execution.
- PostgreSQL and SQL Server share the same fluent surface where they can.
- Dialect-only behavior fails clearly instead of turning into surprise SQL.
- The same query shape can run dynamically or be picked up by the source generator.
- Statically-visible list and single-row queries can skip runtime SQL emission.

Statically-visible query chains are compiled at build time. A source generator
reconstructs the query, bakes the SQL string, generates the projection mapper,
and intercepts the call site. Dynamic queries fall back to the runtime pipeline
with a shape cache. Typed paging uses the generated mapper with the normal paging
executor so `includeTotal` and cursor behavior stay in one place.

Setting `<MizzleQueryMode>Strict</MizzleQueryMode>` in your project turns any
non-compilable query into a build error.

## What it deliberately doesn't do

- **No raw SQL.** If a construct isn't in the IR, it isn't expressible. This is a
  guarantee, not a gap. Check that the current surface covers your needs before
  adopting it.
- No LINQ / `IQueryable`, no sync APIs, no migrations, no MySQL (yet).

## CLI

`Mizzle.Cli` ships as a .NET tool. It is installed through NuGet and runs as
`mizzle`:

```bash
dotnet tool install --global Mizzle.Cli --prerelease
```

```bash
mizzle version
mizzle version --verbose
mizzle type-map --provider postgres
mizzle inspect --connection "Host=localhost;Database=app;Username=postgres;Password=..." --schema public --all
mizzle scaffold --connection "Host=localhost;Database=app;Username=postgres;Password=..." --schema public --tables users,posts --namespace MyApp.Data --output ./Data/Tables
mizzle doctor
mizzle doctor --project ./MyApp.csproj
mizzle doctor --solution ./MyApp.slnx
mizzle diff --connection "Host=localhost;Database=app;Username=postgres;Password=..." --schema public --source ./Data/Tables
mizzle explain --provider postgres --sql-file ./query.sql
mizzle translate-query --provider postgres --sql-file ./query.sql
```

Database commands infer `postgres` or `sqlserver` from common connection string
shapes. Pass `--provider` when the connection string is ambiguous.

`doctor` is intentionally read-only. If you run it from a directory with one
solution, it checks the projects in that solution. If there is no solution, it
uses the single project in the current directory. You can also pass `--project`
or `--solution` explicitly.

Project and solution paths must be inside the current working directory.
Inherited project files are read only up to that directory:
`Directory.Build.props`, `Directory.Build.targets`, and
`Directory.Packages.props`. The command checks dialect references, generator
references, nullable settings, `MizzleQueryMode`, old constructor alias syntax,
non-literal column names, lambda column maps, and mismatched Mizzle package
versions. Test and benchmark projects are shown when they use Mizzle, but their
app configuration checks are skipped.

The CLI stops on unsupported database types or SQL shapes with `MZCLI###`
messages. That is intentional: it should point at what Mizzle needs to learn
next, not generate code that quietly guesses.

Current commands:

- `version`: show the installed Mizzle CLI version. Pass `--verbose` to include build metadata.
- `type-map`: show the database types Mizzle knows how to scaffold.
- `inspect`: list tables, columns, database types, nullability, keys, and unsupported mappings.
- `scaffold`: generate `PgTable<>` or `SqlTable<>` classes from an existing database.
- `doctor`: check a project or solution for common Mizzle setup problems.
- `diff`: compare live database columns with existing Mizzle table classes.
- `explain`: summarize SQL features and likely Mizzle support.
- `translate-query`: translate a small SQL subset into Mizzle query syntax.

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
