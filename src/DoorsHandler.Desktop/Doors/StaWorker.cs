using System.Collections.Concurrent;

namespace DoorsHandler.Desktop.Doors;

/// <summary>
/// A single background STA thread. COM objects created on it are only ever used from it,
/// which keeps DOORS calls off the UI thread without cross-apartment marshalling surprises.
/// </summary>
internal sealed class StaWorker : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

    public StaWorker(string name)
    {
        _thread = new Thread(Run) { IsBackground = true, Name = name };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public Task<T> InvokeAsync<T>(Func<T> work, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Action action = () =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                tcs.TrySetCanceled(cancellationToken);
                return;
            }

            try
            {
                tcs.TrySetResult(work());
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        };

        try
        {
            _queue.Add(action, CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            // Dispose() already called CompleteAdding.
            tcs.TrySetException(new ObjectDisposedException(nameof(StaWorker)));
        }

        return tcs.Task;
    }

    private void Run()
    {
        foreach (var action in _queue.GetConsumingEnumerable())
        {
            action();
        }
    }

    /// <summary>
    /// Stops accepting work without waiting: queued work still runs, then the thread exits.
    /// A call stuck inside DOORS therefore never freezes the caller. The queue itself is left
    /// for the garbage collector because the thread may still be reading from it.
    /// </summary>
    public void Dispose()
    {
        if (!_queue.IsAddingCompleted)
        {
            _queue.CompleteAdding();
        }
    }
}
