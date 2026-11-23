using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace TransfertPhotos.Services;

public sealed class StaWorker : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

    public StaWorker()
    {
        _thread = new Thread(RunLoop)
        {
            IsBackground = true,
            Name = "TransfertPhotos-STA",
            ApartmentState = ApartmentState.STA
        };
        _thread.Start();
    }

    private void RunLoop()
    {
        foreach (var action in _queue.GetConsumingEnumerable())
        {
            try { action(); }
            catch
            {
                // Les exceptions sont transmises via le TaskCompletionSource de l'appelant.
            }
        }
    }

    public T Invoke<T>(Func<T> func)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() =>
        {
            try { completion.SetResult(func()); }
            catch (Exception ex) { completion.SetException(ex); }
        });
        return completion.Task.GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
    }
}
