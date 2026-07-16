namespace HistoryClipboard.App.Startup;

internal static class StartupFailureCoordinator
{
    public static void Handle(
        Exception exception,
        IReadOnlyList<Action> cleanupActions,
        Action<StartupFailureNotice> presentFailure,
        Action<int> shutdown)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(cleanupActions);
        ArgumentNullException.ThrowIfNull(presentFailure);
        ArgumentNullException.ThrowIfNull(shutdown);

        var notice = StartupFailureClassifier.Classify(exception);
        foreach (var cleanupAction in cleanupActions)
        {
            try
            {
                cleanupAction();
            }
            catch
            {
                // Startup is already failing. Continue releasing the remaining owned resources.
            }
        }

        try
        {
            presentFailure(notice);
        }
        catch
        {
            // A desktop/session UI failure must not prevent a nonzero process exit.
        }
        finally
        {
            shutdown(1);
        }
    }
}
