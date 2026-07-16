namespace HistoryClipboard.App.Windows;

public sealed class SingleInstanceController : IDisposable
{
    private const string ActivationEventName = @"Local\HistoryClipboard.Activate";
    private const string InstanceMutexName = @"Local\HistoryClipboard.SingleInstance";

    private readonly EventWaitHandle activationEvent;
    private readonly Action activationRequested;
    private readonly Task listenerTask;
    private readonly Mutex markerMutex;
    private readonly CancellationTokenSource stopListening = new();
    private bool isDisposed;

    private SingleInstanceController(
        Mutex markerMutex,
        EventWaitHandle activationEvent,
        Action activationRequested)
    {
        this.markerMutex = markerMutex;
        this.activationEvent = activationEvent;
        this.activationRequested = activationRequested;
        listenerTask = Task.Run(ListenForActivation);
    }

    public static SingleInstanceController? TryStart(Action activationRequested)
    {
        ArgumentNullException.ThrowIfNull(activationRequested);

        var activationEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            ActivationEventName);
        Mutex? markerMutex = null;
        try
        {
            markerMutex = new Mutex(
                initiallyOwned: false,
                InstanceMutexName,
                out var createdNew);
            if (createdNew)
            {
                return new SingleInstanceController(
                    markerMutex,
                    activationEvent,
                    activationRequested);
            }

            activationEvent.Set();
            markerMutex.Dispose();
            activationEvent.Dispose();
            return null;
        }
        catch
        {
            markerMutex?.Dispose();
            activationEvent.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        stopListening.Cancel();
        listenerTask.GetAwaiter().GetResult();
        stopListening.Dispose();
        activationEvent.Dispose();
        markerMutex.Dispose();
        GC.SuppressFinalize(this);
    }

    private void ListenForActivation()
    {
        var waitHandles = new[]
        {
            activationEvent,
            stopListening.Token.WaitHandle,
        };

        while (WaitHandle.WaitAny(waitHandles) == 0)
        {
            activationRequested();
        }
    }
}
