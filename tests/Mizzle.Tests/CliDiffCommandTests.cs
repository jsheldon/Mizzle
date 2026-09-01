using Mizzle.Cli.Commands;

namespace Mizzle.Tests;

public sealed class CliDiffCommandTests
{
    [Fact]
    public void Same_named_tables_in_different_schemas_do_not_collide()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mizzle-diff-schema-collision-" + Guid.NewGuid()));
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "DboUsers.cs"), """
                using Mizzle.SqlServer;

                public sealed class DboUsers : SqlTable<DboUsers>
                {
                    public DboUsers() : base("users", "dbo") { }
                    public SqlColumn<int> Id { get; } = Int("id").PrimaryKey();
                    public SqlColumn<string> Email { get; } = NVarChar("email", 255);
                }
                """);
            File.WriteAllText(Path.Combine(root.FullName, "ArchiveUsers.cs"), """
                using Mizzle.SqlServer;

                public sealed class ArchiveUsers : SqlTable<ArchiveUsers>
                {
                    public ArchiveUsers() : base("users", "archive") { }
                    public SqlColumn<int> Id { get; } = Int("id").PrimaryKey();
                }
                """);

            var declared = DiffCommand.ParseDeclared(root.FullName);

            Assert.True(declared.TryGetValue(("dbo", "users"), out var dboColumns));
            Assert.Contains("email", dboColumns);

            Assert.True(declared.TryGetValue(("archive", "users"), out var archiveColumns));
            Assert.DoesNotContain("email", archiveColumns);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Table_key_lookup_is_case_insensitive()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mizzle-diff-case-" + Guid.NewGuid()));
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "Users.cs"), """
                using Mizzle.SqlServer;

                public sealed class Users : SqlTable<Users>
                {
                    public Users() : base("Users", "DBO") { }
                    public SqlColumn<int> Id { get; } = Int("id").PrimaryKey();
                }
                """);

            var declared = DiffCommand.ParseDeclared(root.FullName);

            Assert.True(declared.ContainsKey(("dbo", "users")));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Chained_alias_calls_are_not_mistaken_for_declared_columns()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mizzle-diff-alias-" + Guid.NewGuid()));
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "Users.cs"), """
                using Mizzle.SqlServer;

                public sealed class Users : SqlTable<Users>
                {
                    public Users() : base("users", "dbo") { }
                    public SqlColumn<int> Id { get; } = Int("id").PrimaryKey();
                }

                public static class Q
                {
                    public static void Run(Users u) => u.Id.As("PatientId");
                }
                """);

            var declared = DiffCommand.ParseDeclared(root.FullName);

            Assert.True(declared.TryGetValue(("dbo", "users"), out var columns));
            Assert.DoesNotContain("PatientId", columns);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Table_declared_without_an_explicit_schema_matches_any_live_schema()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mizzle-diff-no-schema-" + Guid.NewGuid()));
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "Users.cs"), """
                using Mizzle.SqlServer;

                public sealed class Users : SqlTable<Users>
                {
                    public Users() : base("users") { }
                    public SqlColumn<int> Id { get; } = Int("id").PrimaryKey();
                }
                """);

            var declared = DiffCommand.ParseDeclared(root.FullName);

            Assert.True(declared.ContainsKey(("", "users")));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
