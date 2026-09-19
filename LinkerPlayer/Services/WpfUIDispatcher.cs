using System.Windows;

namespace LinkerPlayer.Services;

public interface IUiDispatcher
{
    /// <summary>
    /// Executes an action on the UI thread
    /// </summary>
    /// <param name="action">Action to execute</param>
    Task InvokeAsync(Action action);

    /// <summary>
    /// Executes a function on the UI thread and returns the result
    /// </summary>
    /// <typeparam name="T">Return type</typeparam>
    /// <param name="func">Function to execute</param>
    /// <returns>Result of the function</returns>
    Task<T> InvokeAsync<T>(Func<T> func);

    /// <summary>
    /// Executes an async action on the UI thread
    /// </summary>
    /// <param name="asyncAction">Async action to execute</param>
    Task InvokeAsync(Func<Task> asyncAction);

    /// <summary>
    /// Executes an async function on the UI thread and returns the result
    /// </summary>
    /// <typeparam name="T">Return type</typeparam>
    /// <param name="asyncFunc">Async function to execute</param>
    /// <returns>Result of the function</returns>
    Task<T> InvokeAsync<T>(Func<Task<T>> asyncFunc);

    /// <summary>
    /// Checks if the current thread is the UI thread
    /// </summary>
    bool CheckAccess();
}

public class WpfUiDispatcher : IUiDispatcher
{
    public async Task InvokeAsync(Action action)
    {
        System.Windows.Threading.Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            throw new InvalidOperationException("Application dispatcher is not available");
        }

        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            await dispatcher.InvokeAsync(action);
        }
    }

    public async Task<T> InvokeAsync<T>(Func<T> func)
    {
        System.Windows.Threading.Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            throw new InvalidOperationException("Application dispatcher is not available");
        }

        if (dispatcher.CheckAccess())
        {
            return func();
        }
        else
        {
            return await dispatcher.InvokeAsync(func);
        }
    }

    public async Task InvokeAsync(Func<Task> asyncAction)
    {
        System.Windows.Threading.Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            throw new InvalidOperationException("Application dispatcher is not available");
        }

        if (dispatcher.CheckAccess())
        {
            await asyncAction();
        }
        else
        {
            await dispatcher.InvokeAsync(asyncAction);
        }
    }

    public async Task<T> InvokeAsync<T>(Func<Task<T>> asyncFunc)
    {
        System.Windows.Threading.Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            throw new InvalidOperationException("Application dispatcher is not available");
        }

        if (dispatcher.CheckAccess())
        {
            return await asyncFunc();
        }
        else
        {
            Task<T>? result = await dispatcher.InvokeAsync(asyncFunc);
            return await result;
        }
    }

    public bool CheckAccess()
    {
        return Application.Current?.Dispatcher?.CheckAccess() ?? false;
    }
}
