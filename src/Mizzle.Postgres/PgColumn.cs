using Mizzle.Schema;

namespace Mizzle.Postgres;

/// <summary>
///     A column on a PostgreSQL table. The type argument is the storage type; nullability
///     comes from <see cref="NotNull"/>, not from the type argument.
/// </summary>
/// <typeparam name="T">The CLR type the column reads as.</typeparam>
public sealed class PgColumn<T> : Column<T>
{
    internal PgColumn(string name) : base(name, DialectKind.Postgres)
    {
    }

    /// <summary>
    ///     Marks this column as the optimistic-concurrency token. An update that does
    ///     not constrain it in <c>Where</c> is rejected before it reaches the database.
    /// </summary>
    public PgColumn<T> Version()
    {
        MarkVersion();
        return this;
    }

    /// <summary>
    ///     Marks this column as the table's primary key. Implies <see cref="NotNull"/>,
    ///     so projections read it as non-nullable.
    /// </summary>
    public PgColumn<T> PrimaryKey()
    {
        MarkPrimaryKey();
        return this;
    }

    /// <summary>
    ///     Declares that the database column is <c>NOT NULL</c>. This, not the type
    ///     argument, is what decides nullability: a column without it projects as
    ///     <c>T?</c> and reads through an <c>IsDBNull</c> check.
    /// </summary>
    /// <remarks>
    ///     A column on the nullable side of a <c>LEFT JOIN</c> still projects as
    ///     nullable, whatever this says.
    /// </remarks>
    public PgColumn<T> NotNull()
    {
        MarkNotNull();
        return this;
    }

    /// <summary>
    ///     Records the column's database default, so an insert may legitimately omit it.
    /// </summary>
    /// <remarks>
    ///     Mizzle emits no DDL; this declares what the database already does. Reserved
    ///     for insert-completeness validation -- a NOT NULL column with neither a value
    ///     nor a default -- and not yet read.
    /// </remarks>
    public PgColumn<T> Default(T value)
    {
        MarkDefault(value);
        return this;
    }

    /// <summary>
    ///     Records a foreign key from this column to <paramref name="column"/>.
    /// </summary>
    /// <remarks>
    ///     Mizzle emits no DDL; this declares a relationship that already exists.
    ///     Reserved for join validation -- checking that an <c>On</c> condition matches
    ///     a declared relationship -- and not yet read.
    /// </remarks>
    public PgColumn<T> References(IColumn column)
    {
        MarkReferences(column);
        return this;
    }

    internal PgColumn<T> WithLength(int length)
    {
        SetLength(length);
        return this;
    }

    /// <summary>
    ///     Excludes this column from <c>MizzleTrimStrings</c>, for values where trailing
    ///     whitespace is meaningful -- fixed-width codes, base64, hashes.
    /// </summary>
    public PgColumn<T> Untrimmed()
    {
        MarkUntrimmed();
        return this;
    }

    /// <summary>
    ///     Binds this column to a projection member with a different name, and emits a
    ///     SQL alias to match. Use it when the schema and the domain type disagree.
    /// </summary>
    /// <param name="name">The projection member to fill, e.g. <c>"PatientId"</c>.</param>
    /// <returns>
    ///     A copy carrying the alias. The table's own instance is shared across queries
    ///     and is left unchanged.
    /// </returns>
    /// <example>
    ///     <code>db.Select(persons.PersonId.As("PatientId")).From(persons)</code>
    /// </example>
    public PgColumn<T> As(string name)
    {
        var column = new PgColumn<T>(Name);
        column.CopyFrom(this);
        column.SetProjectionName(name);
        return column;
    }

    /// <summary>
    ///     Converts between a legacy storage representation and a domain type, so the
    ///     column reads as <typeparamref name="TResult"/> while the database still holds
    ///     <typeparamref name="T"/>.
    /// </summary>
    /// <param name="read">Storage to domain. Must be a static method reference.</param>
    /// <param name="write">Domain to storage. Must be a static method reference.</param>
    /// <remarks>
    ///     Both arguments must be static method references, not lambdas, so the
    ///     generators can bake the conversion into generated mappers; a lambda is
    ///     <c>MIZ008</c>. Neither is ever called with null -- Mizzle short-circuits
    ///     NULL on both sides -- so <typeparamref name="TResult"/> must be
    ///     non-nullable, and a nullable one is <c>MIZ009</c>. Express nullability by
    ///     omitting <see cref="NotNull"/> instead.
    /// </remarks>
    public PgColumn<TResult> Map<TResult>(Func<T, TResult> read, Func<TResult, T> write)
    {
        var column = new PgColumn<TResult>(Name);
        column.CopyMetadataFrom(this);
        column.SetConverter(typeof(T), value => read((T)value!), value => write((TResult)value!));
        return column;
    }
}
