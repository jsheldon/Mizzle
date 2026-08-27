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

`WithAlias` returns a new table instance. The original table keeps its declared
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
