namespace Ageledger;

internal static class TaskObservation
{
    /// <summary>
    /// Marks a fault of this task as observed. Used for tasks a caller may stop waiting for (timeout, cancel): a later fault must not reach
    /// <see cref="TaskScheduler.UnobservedTaskException"/>, where platform exception text could end up in an app's crash or telemetry hook.
    /// </summary>
    public static void ObserveFaults(this Task task) =>
        _ = task.ContinueWith(static x => _ = x.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}
