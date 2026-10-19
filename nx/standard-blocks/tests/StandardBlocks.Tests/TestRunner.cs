using System.Reflection;

namespace StandardBlocks.Tests;

// Runs every public static void method in classes whose name ends in "Tests".
// Pass a word on the command line to run only tests whose name contains it.
public static class TestRunner
{
    public static int Main(string[] args)
    {
        string filter = args.Length > 0 ? args[0] : null;
        var tests = typeof(TestRunner).Assembly.GetTypes()
            .Where(t => t.IsClass && t.Name.EndsWith("Tests", StringComparison.Ordinal))
            .OrderBy(t => t.Name)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.ReturnType == typeof(void) && m.GetParameters().Length == 0)
                .Select(m => (Name: t.Name + "." + m.Name, Method: m)))
            .Where(x => filter == null || x.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        int failed = 0;
        foreach (var (name, method) in tests)
        {
            try
            {
                method.Invoke(null, null);
                Console.WriteLine("  ok    " + name);
            }
            catch (TargetInvocationException ex)
            {
                failed++;
                Console.WriteLine("  FAIL  " + name);
                Console.WriteLine("        " + ex.InnerException!.Message.Replace("\n", "\n        "));
            }
        }

        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? $"All {tests.Count} tests passed."
            : $"{failed} of {tests.Count} tests FAILED.");
        return failed == 0 ? 0 : 1;
    }
}

public sealed class AssertException(string message) : Exception(message);

public static class Assert
{
    public static void True(bool condition, string what)
    {
        if (!condition) throw new AssertException("Expected true: " + what);
    }

    public static void False(bool condition, string what)
    {
        if (condition) throw new AssertException("Expected false: " + what);
    }

    public static void Equal<T>(T expected, T actual, string what = "")
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new AssertException($"{what}\nExpected: {expected}\nActual:   {actual}".Trim());
    }

    public static void Near(double expected, double actual, double tol = 1e-9, string what = "")
    {
        if (Math.Abs(expected - actual) > tol)
            throw new AssertException($"{what}\nExpected: {expected} (±{tol})\nActual:   {actual}".Trim());
    }

    public static void Near(Core.Vec3 expected, Core.Vec3 actual, double tol = 1e-9, string what = "")
    {
        if ((expected - actual).Length > tol)
            throw new AssertException($"{what}\nExpected: {expected}\nActual:   {actual}".Trim());
    }

    public static void Contains(string expectedPart, string actual, string what = "")
    {
        if (actual == null || !actual.Contains(expectedPart, StringComparison.Ordinal))
            throw new AssertException($"{what}\nExpected text containing: {expectedPart}\nActual: {actual}".Trim());
    }

    public static T Throws<T>(Action action, string what = "") where T : Exception
    {
        try
        {
            action();
        }
        catch (T ex)
        {
            return ex;
        }
        catch (Exception ex)
        {
            throw new AssertException($"{what}\nExpected {typeof(T).Name} but got {ex.GetType().Name}: {ex.Message}".Trim());
        }
        throw new AssertException($"{what}\nExpected {typeof(T).Name} but nothing was thrown.".Trim());
    }
}
