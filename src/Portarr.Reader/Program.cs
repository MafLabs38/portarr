namespace Portarr.Reader;

/// <summary>Entry point. Not a top-level program, whose generated <c>Program</c> type would clash with Portarr's own in the tests.</summary>
internal static class ReaderEntryPoint
{
    private static void Main(string[] args)
    {
        ReaderApp.Build(args).Run();
    }
}
