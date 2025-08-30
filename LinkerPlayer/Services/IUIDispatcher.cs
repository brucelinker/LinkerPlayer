using System;
using System.Threading.Tasks;

namespace LinkerPlayer.Services;

public interface IUIDispatcher
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