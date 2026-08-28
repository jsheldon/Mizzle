namespace Mizzle.Cli.Infrastructure;

internal sealed class CliFailure : Exception
{
    public CliFailure(string code, string message, string? hint = null, int exitCode = 2)
        : base(message)
    {
        Code = code;
        Hint = hint;
        ExitCode = exitCode;
    }

    public string Code { get; }
    public string? Hint { get; }
    public int ExitCode { get; }
}
