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
        if (Application.Current?.Dispatcher == null)
        {
            throw new InvalidOperationException("Application dispatcher is not available");
        }

        if (Application.Current.Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            await Application.Current.Dispatcher.InvokeAsync(action);
        }
    }

    public async Task<T> InvokeAsync<T>(Func<T> func)
    {
        if (Application.Current?.Dispatcher == null)
        {
            throw new InvalidOperationException("Application dispatcher is not available");
        }

        if (Application.Current.Dispatcher.CheckAccess())
        {
            return func();
        }
        else
        {
            return await Application.Current.Dispatcher.InvokeAsync(func);
        }
    }

    public async Task InvokeAsync(Func<Task> asyncAction)
    {
        if (Application.Current?.Dispatcher == null)
        {
            throw new InvalidOperationException("Application dispatcher is not available");
        }

        if (Application.Current.Dispatcher.CheckAccess())
        {
            await asyncAction();
        }
        else
        {
            await Application.Current.Dispatcher.InvokeAsync(asyncAction);
        }
    }

    public async Task<T> InvokeAsync<T>(Func<Task<T>> asyncFunc)
    {
        if (Application.Current?.Dispatcher == null)
        {
            throw new InvalidOperationException("Application dispatcher is not available");
        }

        if (Application.Current.Dispatcher.CheckAccess())
        {
            return await asyncFunc();
        }
        else
        {
            Task<T>? result = await Application.Current.Dispatcher.InvokeAsync(asyncFunc);
            return await result;
        }
    }

    public bool CheckAccess()
    {
        return Application.Current?.Dispatcher?.CheckAccess() ?? false;
    }
}
