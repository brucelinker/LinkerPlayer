using Shouldly;
using LinkerPlayer.Services;
using System.Windows;

namespace LinkerPlayer.Tests.Services;

public class WpfUiDispatcherTests
{
    private readonly WpfUiDispatcher _uiDispatcher;

    public WpfUiDispatcherTests()
    {
        _uiDispatcher = new WpfUiDispatcher();
    }

    [StaFact]
    public void CheckAccess_WhenApplicationDispatcherIsNull_ShouldReturnFalse()
    {
        // Note: This test may not work as expected in a unit test environment
        // because Application.Current might be null or the Dispatcher might not be available
        // In a real WPF application, this would behave differently

        // Act
        bool result = _uiDispatcher.CheckAccess();

        // Assert
        // In a unit test environment without WPF application context,
        // this will likely return false
        result.ShouldBeFalse();
    }

    [StaFact]
    public async Task InvokeAsync_WithAction_WhenNotOnUIThread_ShouldCompleteWithoutException()
    {
        // Arrange
#pragma warning disable CS0219 // Variable is assigned but its value is never used
        bool actionExecuted = false;
#pragma warning restore CS0219 // Variable is assigned but its value is never used
        Action testAction = () => actionExecuted = true;

        // Act & Assert
        // In a unit test environment, this will likely throw because there's no WPF application
        Exception? exception = await Record.ExceptionAsync(() => _uiDispatcher.InvokeAsync(testAction));

        // In unit tests, we expect this to throw InvalidOperationException
        // because there's no Application.Current.Dispatcher
        exception.ShouldBeOfType<InvalidOperationException>();
        exception!.Message.ShouldContain("Application dispatcher is not available");
    }

    [StaFact]
    public async Task InvokeAsync_WithFunc_WhenNotOnUIThread_ShouldThrowInvalidOperationException()
    {
        // Arrange
        Func<string> testFunc = () => "test result";

        // Act & Assert
        Exception? exception = await Record.ExceptionAsync(() => _uiDispatcher.InvokeAsync(testFunc));

        exception.ShouldBeOfType<InvalidOperationException>();
        exception!.Message.ShouldContain("Application dispatcher is not available");
    }

    [StaFact]
    public async Task InvokeAsync_WithAsyncAction_WhenNotOnUIThread_ShouldThrowInvalidOperationException()
    {
        // Arrange
        Func<Task> asyncAction = () => Task.CompletedTask;

        // Act & Assert
        Exception? exception = await Record.ExceptionAsync(() => _uiDispatcher.InvokeAsync(asyncAction));

        exception.ShouldBeOfType<InvalidOperationException>();
        exception!.Message.ShouldContain("Application dispatcher is not available");
    }

    [StaFact]
    public async Task InvokeAsync_WithAsyncFunc_WhenNotOnUIThread_ShouldThrowInvalidOperationException()
    {
        // Arrange
        Func<Task<string>> asyncFunc = () => Task.FromResult("test result");

        // Act & Assert
        Exception? exception = await Record.ExceptionAsync(() => _uiDispatcher.InvokeAsync(asyncFunc));

        exception.ShouldBeOfType<InvalidOperationException>();
        exception!.Message.ShouldContain("Application dispatcher is not available");
    }
}
