using LinkerPlayer.Services;

namespace LinkerPlayer.Tests.Mocks;

/// <summary>
/// Mock implementation of IUIDispatcher for unit testing.
/// Executes all operations synchronously on the current thread.
/// </summary>
public class MockUIDispatcher : IUiDispatcher
{
    public Task InvokeAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    public Task<T> InvokeAsync<T>(Func<T> func)
    {
        T result = func();
        return Task.FromResult(result);
    }

    public Task InvokeAsync(Func<Task> asyncAction)
    {
        return asyncAction();
    }

    public async Task<T> InvokeAsync<T>(Func<Task<T>> asyncFunc)
    {
        return await asyncFunc();
    }

    public bool CheckAccess()
    {
        return true; // Always return true for testing
    }
}