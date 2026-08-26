# Mizzle

Fluent, type-safe SQL for .NET. Inspired by [Drizzle ORM](https://orm.drizzle.team/): you declare your schema in C#, build queries against typed columns, and Mizzle compiles them to dialect-correct SQL for PostgreSQL and SQL Server. No raw SQL strings, no LINQ translation layer, no reflection on the hot path.

> **Status: experimental.** This is an early alpha. The API will change between releases. PostgreSQL and SQL Server are the only supported databases.

## Packages

| Package | What's in it |
|---|---|
| `Mizzle` | Query IR, fluent builders, operators, paging, transactions contracts |
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

// The generator emits a `User` record and an ordinal mapper for every table.
var found = await db.Select(users.Id, users.Email)
    .From(users.ToFrom())
    .Where(users.Email, "a@b.com")
    .ToListAsync(UsersMapper.Read);

var id = await db.InsertInto(users)
    .Value(users.Email, "new@example.com")
    .Returning(users.Id)
    .ToListAsync(r => r.GetInt32(0));

await db.Transaction(async tx =>
{
    await tx.LockAsync("invoice:123");
    // queries in here run on the transaction's connection;
    // nested Transaction calls become savepoints
});
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

## How queries execute

Every query builds an immutable IR graph. Before any SQL is written, a capability
pass checks the target dialect and throws `UnsupportedFeatureException` for anything
it can't do — `ILike` on SQL Server, for example — instead of silently emitting
something different.

Statically-visible query chains are compiled at build time: a source generator
reconstructs the query, bakes the exact SQL string, and intercepts the call site so
runtime skips IR construction and emission entirely. Dynamic queries fall back to
the runtime pipeline with a shape cache. Setting
`<MizzleQueryMode>Strict</MizzleQueryMode>` in your project turns any
non-compilable query into a build error.

## What it deliberately doesn't do

- **No raw SQL.** If a construct isn't in the IR, it isn't expressible. This is a
  guarantee, not a gap — but check that the surface covers your needs before
  adopting.
- No LINQ / `IQueryable`, no sync APIs, no migrations, no MySQL (yet).

## Building

```bash
dotnet test Mizzle.slnx
```

Unit and generator tests run anywhere; the integration tests use
[Testcontainers](https://dotnet.testcontainers.org/) and need Docker.

## License

MIT
