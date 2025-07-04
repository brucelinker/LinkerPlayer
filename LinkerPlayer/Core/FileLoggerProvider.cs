using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading;

namespace LinkerPlayer.Core;

public class FileLoggerProvider : ILoggerProvider
{
    private readonly string _filePath;
    private readonly Func<LogEntry, string> _formatLogEntry;

    public FileLoggerProvider(string filePath, Func<LogEntry, string> formatLogEntry)
    {
        _filePath = filePath;
        _formatLogEntry = formatLogEntry;
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new FileLogger(_filePath, _formatLogEntry);
    }

    public void Dispose() { }
}

public class FileLogger : ILogger
{
    private readonly string _filePath;
    private readonly Func<LogEntry, string> _formatLogEntry;
    private readonly Lock _lock = new();

    public FileLogger(string filePath, Func<LogEntry, string> formatLogEntry)
    {
        _filePath = filePath;
        _formatLogEntry = formatLogEntry;
    }

    IDisposable ILogger.BeginScope<TState>(TState state)
    {
        return null!; // Scopes not supported, return null
    }

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            LogLevel = logLevel,
            Message = formatter(state, exception),
            Exception = exception
        };

        string logMessage = _formatLogEntry(entry);
        lock (_lock)
        {
            File.AppendAllText(_filePath, logMessage + Environment.NewLine);
        }
    }
}

public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public LogLevel LogLevel { get; set; }
    public string Message { get; set; } = string.Empty;
    public Exception? Exception { get; set; }
}

public static class FileLoggerExtensions
{
    public static ILoggingBuilder AddFile(this ILoggingBuilder builder, string filePath, Action<FileLoggerOptions> configure)
    {
        var options = new FileLoggerOptions();
        configure(options);
        builder.AddProvider(new FileLoggerProvider(filePath, options.FormatLogEntry));
        return builder;
    }
}

public class FileLoggerOptions
{
    public Func<LogEntry, string> FormatLogEntry { get; set; } = entry =>
        $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{entry.LogLevel}] {entry.Message}{entry.Exception}";
}