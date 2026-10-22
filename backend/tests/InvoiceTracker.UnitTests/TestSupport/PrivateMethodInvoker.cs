using System.Reflection;

namespace InvoiceTracker.UnitTests.TestSupport;

/// <summary>
/// The three Kafka BackgroundServices under test each keep their actual scan/relay/process logic
/// private, with only the infinite polling-loop <c>ExecuteAsync</c> exposed (see each class's own
/// XML doc). Invoking that private logic directly via reflection — rather than running the whole
/// poll-and-delay loop in a test — lets these tests assert on one pass of real behavior without
/// coupling to timing/interval configuration, and without touching any production code to expose
/// an internal seam purely for testability.
/// </summary>
internal static class PrivateMethodInvoker
{
    public static Task InvokeAsync(object target, string methodName, params object?[] args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException(target.GetType().FullName, methodName);

        var result = method.Invoke(target, args);
        return result switch
        {
            Task task => task,
            _ => throw new InvalidOperationException($"{methodName} did not return a Task.")
        };
    }

    /// <summary>
    /// Same as <see cref="InvokeAsync"/>, but for a private method whose declared return type is
    /// <see cref="Task{T}"/> and whose result the caller needs (e.g. asserting on the bool a
    /// consume-loop uses to decide whether to commit a Kafka offset).
    /// </summary>
    public static async Task<T> InvokeAsync<T>(object target, string methodName, params object?[] args)
    {
        var task = InvokeAsync(target, methodName, args);
        await task;
        var resultProperty = task.GetType().GetProperty("Result")
            ?? throw new InvalidOperationException($"{methodName}'s returned Task has no Result property.");
        return (T)resultProperty.GetValue(task)!;
    }
}
