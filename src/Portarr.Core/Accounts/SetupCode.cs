using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Portarr.Core.Configuration;

namespace Portarr.Core.Accounts;

/// <summary>
/// The one-time code that guards the first-run screen (F-SET-07): without it, the first visitor to reach a fresh
/// install would become its Admin. Issued at startup while no user exists, printed in the log and written to the data
/// folder, gone once the first administrator exists.
/// </summary>
public interface ISetupCode
{
    /// <summary>Whether the setup screen needs a code right now.</summary>
    bool Required { get; }

    /// <summary>Issues a code when one is needed (fresh install, no unattended first run) and tells the operator where to find it.</summary>
    void Issue();

    /// <summary>Whether the presented code is the issued one (letters in any case, dashes and spaces ignored).</summary>
    bool Matches(string? presented);

    /// <summary>Forgets the code and deletes its file: the first administrator exists.</summary>
    void Clear();
}

public sealed partial class SetupCode : ISetupCode
{
    public const string FileName = "setup-code";

    private readonly PortarrOptions _portarr;
    private readonly AuthOptions _auth;
    private readonly ILogger<SetupCode> _logger;
    private volatile string? _code;

    public SetupCode(IOptions<PortarrOptions> portarr, IOptions<AuthOptions> auth, ILogger<SetupCode> logger)
    {
        _portarr = portarr.Value;
        _auth = auth.Value;
        _logger = logger;
    }

    public bool Required => _code is not null;

    private string FilePath => Path.Combine(_portarr.DataFolder, FileName);

    public void Issue()
    {
        if (!_auth.RequireSetupCode)
        {
            LogNotRequired();
            return;
        }

        var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
        var code = string.Join('-', Enumerable.Range(0, 4).Select(i => raw.Substring(i * 4, 4)));
        _code = Normalize(code);
        try
        {
            Directory.CreateDirectory(_portarr.DataFolder);
            File.WriteAllText(FilePath, code + Environment.NewLine);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(FilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogFileFailed(exception.GetType().Name);
        }

        LogIssued(code, FilePath);
    }

    public bool Matches(string? presented)
    {
        var expected = _code;
        return expected is not null
            && presented is { Length: <= 64 }
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Normalize(presented)), Encoding.UTF8.GetBytes(expected));
    }

    public void Clear()
    {
        if (Interlocked.Exchange(ref _code, null) is null)
        {
            return;
        }

        try
        {
            File.Delete(FilePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogFileFailed(exception.GetType().Name);
        }
    }

    private static string Normalize(string code) => code.Replace("-", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Portarr is not set up yet. Open it and enter the setup code {Code} to create the first administrator (also in {File}). The code is gone once the administrator exists.")]
    private partial void LogIssued(string code, string file);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Portarr is not set up yet and Auth:RequireSetupCode is off: the first visitor to the setup screen becomes the administrator")]
    private partial void LogNotRequired();

    [LoggerMessage(Level = LogLevel.Error, Message = "The setup code file could not be written or deleted ({Error}); the code in the log still works")]
    private partial void LogFileFailed(string error);
}
