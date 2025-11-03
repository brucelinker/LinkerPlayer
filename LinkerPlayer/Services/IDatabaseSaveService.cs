namespace LinkerPlayer.Services;

/// <summary>
/// Service for debouncing database saves to prevent blocking on every user action.
/// Saves are deferred and batched to improve performance.
/// </summary>
public interface IDatabaseSaveService : IDisposable
{
    /// <summary>
    /// Requests a deferred database save. The save will be batched and executed
    /// after a short delay to avoid excessive database writes.
    /// </summary>
    void RequestSave();

    /// <summary>
    /// Forces an immediate save to the database, bypassing the debounce delay.
    /// Use this for critical operations like app shutdown or playlist deletion.
    /// </summary>
    void SaveImmediately();
}
