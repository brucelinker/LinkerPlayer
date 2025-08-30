using System;
using System.Threading.Tasks;
using System.Windows;

namespace LinkerPlayer.Services;

public class WpfUIDispatcher : IUIDispatcher
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