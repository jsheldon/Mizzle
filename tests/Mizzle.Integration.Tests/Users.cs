using Mizzle.Schema;

namespace Mizzle.Integration.Tests;

internal sealed class Users : PgTable<Users>
{
    public Users() : base("users", "public") { }

    public PgColumn<int> Id { get; } = Identity("id");
    public PgColumn<string> Email { get; } = Text("email");
}
