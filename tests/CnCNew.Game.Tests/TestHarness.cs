namespace CnCNew.Game.Tests;

internal sealed class TestHarness(string suite)
{
    public int Failures { get; private set; }

    /// <summary>When set, only the tests whose name starts with this text run (a development aid for the long suites).</summary>
    /// <summary>Only the tests whose name begins so run; the default is the environment variable <c>CNCNEW_TEST_PREFIX</c> (none: every test).</summary>
    public string? OnlyPrefix { get; init; } = Environment.GetEnvironmentVariable("CNCNEW_TEST_PREFIX") is { Length: > 0 } prefix ? prefix : null;

    public void Run(string name, Action test)
    {
        if (OnlyPrefix is not null && !name.StartsWith(OnlyPrefix, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            test();
            Console.WriteLine($"PASS {suite}.{name}");
        }
        catch (Exception exception)
        {
            Failures++;
            Console.WriteLine($"FAIL {suite}.{name}: {exception.Message}");
        }
    }

    public static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Throws<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException($"{message}: expected {typeof(TException).Name}");
    }
}
