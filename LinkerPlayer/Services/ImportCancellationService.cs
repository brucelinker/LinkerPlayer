namespace LinkerPlayer.Services;

public interface IImportCancellationService
{
    /// <summary>
    /// Whether an import is currently active.
    /// </summary>
    bool IsImporting { get; }

    /// <summary>
    /// Gets the <see cref="CancellationToken"/> for the current import session.
    /// </summary>
    CancellationToken Token { get; }

    /// <summary>
    /// Begins a new import session, resetting any previous cancellation state.
    /// </summary>
    void BeginImport();

    /// <summary>
    /// Requests a cooperative pause. The import loop should call <see cref="WaitIfPausedAsync"/> between files.
    /// </summary>
    void RequestPause();

    /// <summary>
    /// Resumes a paused import.
    /// </summary>
    void Resume();

    /// <summary>
    /// Cancels the current import session.
    /// </summary>
    void Cancel();

    /// <summary>
    /// Marks the import session as finished.
    /// </summary>
    void EndImport();

    /// <summary>
    /// Blocks cooperatively if the import is paused. Returns false if cancelled while paused.
    /// </summary>
    Task<bool> WaitIfPausedAsync(CancellationToken token);
}

public class ImportCancellationService : IImportCancellationService
{
    private CancellationTokenSource _cts = new();
    private SemaphoreSlim _pauseGate = new(1, 1);
    private volatile bool _isPaused;

    public bool IsImporting { get; private set; }

    public CancellationToken Token => _cts.Token;

    public void BeginImport()
    {
        _cts.Dispose();
        _cts = new CancellationTokenSource();
        _isPaused = false;
        if (_pauseGate.CurrentCount == 0)
        {
            _pauseGate.Release();
        }
        IsImporting = true;
    }

    public void RequestPause()
    {
        if (!_isPaused && IsImporting)
        {
            _isPaused = true;
            _pauseGate.Wait(0); // drain the semaphore to block waiters
        }
    }

    public void Resume()
    {
        if (_isPaused)
        {
            _isPaused = false;
            if (_pauseGate.CurrentCount == 0)
            {
                _pauseGate.Release();
            }
        }
    }

    public void Cancel()
    {
        _cts.Cancel();
        // Also release the pause gate so the loop can exit
        Resume();
        IsImporting = false;
    }

    public void EndImport()
    {
        Resume();
        IsImporting = false;
    }

    public async Task<bool> WaitIfPausedAsync(CancellationToken token)
    {
        try
        {
            await _pauseGate.WaitAsync(token).ConfigureAwait(false);
            _pauseGate.Release();
            return !token.IsCancellationRequested;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
