namespace CnCNew.Game.Tests;

internal sealed class TestHarness(string suite)
{
    public int Failures { get; private set; }

    public void Run(string name, Action test)
    {
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
