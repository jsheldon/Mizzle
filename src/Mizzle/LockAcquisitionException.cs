namespace Mizzle;

/// <summary>
///     A lock request did not succeed: <c>sp_getapplock</c> (or an equivalent) returned a
///     negative result code -- timed out, was chosen as a deadlock victim, was cancelled, or
///     hit another failure -- rather than acquiring the lock. Thrown instead of silently
///     continuing without the lock, which would defeat the reason the lock was taken.
/// </summary>
public sealed class LockAcquisitionException : Exception
{
    public string Resource { get; }
    public int ResultCode { get; }

    public LockAcquisitionException(string resource, int resultCode)
        : base($"Could not acquire lock on '{resource}': {Describe(resultCode)} (result code {resultCode}).")
    {
        Resource = resource;
        ResultCode = resultCode;
    }

    private static string Describe(int resultCode) => resultCode switch
    {
        -1 => "timed out waiting for the lock",
        -2 => "the request was cancelled",
        -3 => "the caller was chosen as a deadlock victim",
        -999 => "an invalid parameter or other error occurred",
        _ => "the lock could not be acquired"
    };
}
