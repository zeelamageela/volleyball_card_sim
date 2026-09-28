using System;
using System.Threading;

/// <summary>
/// Thread-safe request/response mailbox connecting a background-thread IStrategy
/// (HumanStrategy, running inside Core's Rally.Play() call stack) to the main
/// thread (RallyController, driven by Update()).
///
/// The background thread calls Post(request) and blocks; the main thread polls
/// TryTakePending()/Resolve(response) once per frame. Deliberately carries plain
/// C# objects only -- no UnityEngine type ever needs to cross this boundary,
/// since the background thread must never touch a UnityEngine API directly.
/// </summary>
public sealed class HumanDecisionChannel
{
    private readonly object _lock = new();
    private readonly SemaphoreSlim _requestReady = new(0, 1);
    private readonly SemaphoreSlim _responseReady = new(0, 1);

    private object _pendingRequest;
    private object _pendingResponse;
    private volatile bool _cancelled;

    /// <summary>Called from the background thread. Blocks until the main thread resolves it,
    /// or throws OperationCanceledException if Cancel() was called first.</summary>
    public TResponse Post<TResponse>(object request)
    {
        if (_cancelled)
        {
            throw new OperationCanceledException("HumanDecisionChannel cancelled");
        }
        lock (_lock)
        {
            _pendingRequest = request;
        }
        _requestReady.Release();
        _responseReady.Wait();
        if (_cancelled)
        {
            throw new OperationCanceledException("HumanDecisionChannel cancelled");
        }
        lock (_lock)
        {
            return (TResponse)_pendingResponse;
        }
    }

    /// <summary>Main thread only. True if a request is waiting and hasn't been taken yet.</summary>
    public bool TryTakePending(out object request)
    {
        if (_requestReady.Wait(0))
        {
            lock (_lock)
            {
                request = _pendingRequest;
            }
            return true;
        }
        request = null;
        return false;
    }

    /// <summary>Main thread only. Call once the player has answered the currently-taken request.</summary>
    public void Resolve(object response)
    {
        lock (_lock)
        {
            _pendingResponse = response;
        }
        _responseReady.Release();
    }

    /// <summary>
    /// Unblocks any current or future Post() call with a cancellation instead of an
    /// answer -- call this on shutdown (e.g. OnDestroy/OnApplicationQuit) so a
    /// background thread blocked mid-rally doesn't leak past Play mode stopping.
    /// </summary>
    public void Cancel()
    {
        _cancelled = true;
        ReleaseIfNotFull(_requestReady);
        ReleaseIfNotFull(_responseReady);
    }

    private static void ReleaseIfNotFull(SemaphoreSlim semaphore)
    {
        try
        {
            semaphore.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already signaled -- fine, whoever is waiting will see it.
        }
    }
}
