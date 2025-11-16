using System.Diagnostics;
using Xunit.Abstractions;

/// <summary>
/// Helper for selectively logging performance test metrics without
/// polluting normal automated test runs.
///
/// USAGE:
///     • Calls to Report(...) are only included in the compiled output
///       when OMNIWORKS_PERF_LOG is defined in the test project.
///     • Output is written via xUnit's ITestOutputHelper so it always
///       shows up in the per-test log in Test Explorer.
/// </summary>
internal static class PerfTestLog
{
    /// <summary>
    /// Logs a formatted message about a performance test result.
    /// </summary>
    [Conditional("OMNIWORKS_PERF_LOG")]
    public static void Report(
        ITestOutputHelper output,
        string testName,
        double averageMillisecondsPerTick)
    {
        if (output == null)
        {
            return;
        }

        string message =
            $"[Perf] {testName}: average = {averageMillisecondsPerTick:F4} ms/tick";

        output.WriteLine(message);
    }
}