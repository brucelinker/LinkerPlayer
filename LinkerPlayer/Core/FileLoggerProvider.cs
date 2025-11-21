using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.IO;

namespace LinkerPlayer.Core;

public class FileLoggerProvider : ILoggerProvider
{
    private readonly string _filePath;
    private readonly Func<LogEntry, string> _formatLogEntry;
    private readonly BackgroundLogWriter _writer;

    public FileLoggerProvider(string filePath, Func<LogEntry, string> formatLogEntry)
    {
        _filePath = filePath;
        _formatLogEntry = formatLogEntry;
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        _writer = new BackgroundLogWriter(filePath);
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new FileLogger(_writer, _formatLogEntry);
    }

    public void Dispose()
    {
        _writer?.Dispose();
    }
}

public class BackgroundLogWriter : IDisposable
{
    private readonly BlockingCollection<string> _logQueue = new();
    private readonly Task _writerTask;
    private readonly StreamWriter _writer;
    private bool _disposed;

    public BackgroundLogWriter(string filePath)
    {
        // Open file once and keep it open for performance
        _writer = new StreamWriter(filePath, append: true, System.Text.Encoding.UTF8)
        {
            AutoFlush = false // Manual flush for better performance
        };

        // Start background thread
        _writerTask = Task.Run(ProcessQueue);
    }

    public void Enqueue(string logMessage)
    {
        if (!_disposed)
        {
            _logQueue.Add(logMessage);
        }
    }

    private async Task ProcessQueue()
    {
        try
        {
            // Flush every 100ms OR when queue has 10+ items
            using Timer flushTimer = new Timer(_ => FlushWriter(), null, 100, 100);

            foreach (string message in _logQueue.GetConsumingEnumerable())
            {
                await _writer.WriteAsync(message);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Log writer error: {ex.Message}");
        }
    }

    private void FlushWriter()
    {
        if (!_disposed)
        {
            _writer.Flush();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _logQueue.CompleteAdding();
        _writerTask.Wait(TimeSpan.FromSeconds(2)); // Wait for queue to finish
        _writer.Flush();
        _writer.Dispose();
        _logQueue.Dispose();
    }
}

public class FileLogger : ILogger
{
    private readonly BackgroundLogWriter _writer;
    private readonly Func<LogEntry, string> _formatLogEntry;

    public FileLogger(BackgroundLogWriter writer, Func<LogEntry, string> formatLogEntry)
    {
        _writer = writer;
        _formatLogEntry = formatLogEntry;
    }

    IDisposable ILogger.BeginScope<TState>(TState state)
    {
        return null!; // Scopes not supported, return null
    }

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        LogEntry entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            LogLevel = logLevel,
            Message = formatter(state, exception),
            Exception = exception
        };

        string logMessage = _formatLogEntry(entry);

        // NEW: Queue the log message - NO BLOCKING!
        _writer.Enqueue(logMessage);
    }
}

public class LogEntry
{
    public DateTime Timestamp
    {
        get; set;
    }
    public LogLevel LogLevel
    {
        get; set;
    }
    public string Message { get; set; } = string.Empty;
    public Exception? Exception
    {
        get; set;
    }
}

public static class FileLoggerExtensions
{
    public static ILoggingBuilder AddFile(this ILoggingBuilder builder, string filePath, Action<FileLoggerOptions> configure)
    {
        FileLoggerOptions options = new FileLoggerOptions();
        configure(options);
        builder.AddProvider(new FileLoggerProvider(filePath, options.FormatLogEntry));
        return builder;
    }
}

public class FileLoggerOptions
{
    public Func<LogEntry, string> FormatLogEntry
    {
        get; set;
    } = entry =>
        $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{entry.LogLevel}] {entry.Message}{entry.Exception}";
}
