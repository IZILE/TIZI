namespace Tizi.Desktop;

/// <summary>
/// A dialog lifetime is cancellable while preparing an edit. BeginCommit is the
/// atomic boundary: after it succeeds, user dismissal stays locked until the
/// submit attempt has actually finished. Use Token only before that boundary.
/// </summary>
internal sealed class EditorSession : IDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cancellation;
    private bool _attempt, _committing, _cancelRequested, _disposed;
    internal CancellationToken Token { get; }
    internal bool IsCommitting { get { lock (_gate) return _committing; } }
    internal bool IsWorking { get { lock (_gate) return _attempt; } }
    internal bool IsCancellationRequested { get { lock (_gate) return _cancelRequested || Token.IsCancellationRequested; } }
    internal event Action? Changed;

    internal EditorSession(CancellationToken cancellationToken = default)
    {
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Token = _cancellation.Token;
    }

    internal void BeginAttempt()
    {
        lock (_gate)
        {
            ThrowIfCancellationRequestedLocked();
            if (_attempt) throw new InvalidOperationException("Another operation is in progress.");
            _attempt = true;
        }
        Changed?.Invoke();
    }

    internal void BeginCommit()
    {
        lock (_gate)
        {
            if (_committing) return;
            ThrowIfCancellationRequestedLocked();
            _committing = true;
        }
        Changed?.Invoke();
    }

    internal async Task CommitAsync(Func<Task> apply)
    {
        BeginCommit();
        await apply();
        // Do not unlock here. The dialog still needs to record Applied and close;
        // cancellation between those steps would falsely report a saved edit as cancelled.
    }

    internal bool TryCancel()
    {
        lock (_gate)
        {
            if (_committing) return false;
            if (_cancelRequested || _disposed) return true;
            _cancelRequested = true;
        }
        _cancellation.Cancel();
        Changed?.Invoke();
        return true;
    }

    internal void ThrowIfCancellationRequested()
    {
        lock (_gate) ThrowIfCancellationRequestedLocked();
    }

    private void ThrowIfCancellationRequestedLocked()
    {
        if (_cancelRequested || _disposed) throw new OperationCanceledException(Token);
        Token.ThrowIfCancellationRequested();
    }

    internal void FinishAttempt()
    {
        lock (_gate) { _attempt = false; _committing = false; }
        Changed?.Invoke();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            // Successful atomic commits are already complete when their dialog closes.
            if (!_committing) _cancelRequested = true;
        }
        if (!IsCommitting) _cancellation.Cancel();
        _cancellation.Dispose();
    }
}
