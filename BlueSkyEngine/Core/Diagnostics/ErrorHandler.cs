using System.Diagnostics;
using System.Text;

namespace BlueSky.Core.Diagnostics;

/// <summary>
/// Centralized error handling and logging system.
/// Makes debugging easy and prevents crashes.
/// </summary>
public static class ErrorHandler
{
    private static readonly List<ErrorLog> _errors = new();
    private static readonly object _lock = new();
    private static bool _throwOnError = false;

    public static bool ThrowOnError
    {
        get => _throwOnError;
        set => _throwOnError = value;
    }

    public static IReadOnlyList<ErrorLog> Errors
    {
        get
        {
            lock (_lock)
            {
                return _errors.ToList();
            }
        }
    }

    /// <summary>
    /// Log an error (non-fatal, continues execution).
    /// </summary>
    public static void LogError(string message, Exception? exception = null, string? context = null)
    {
        var error = new ErrorLog
        {
            Level = ErrorLevel.Error,
            Message = message,
            Exception = exception,
            Context = context ?? GetCallerContext(),
            Timestamp = DateTime.Now,
            StackTrace = new StackTrace(1, true).ToString()
        };

        lock (_lock)
        {
            _errors.Add(error);
        }

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[ERROR] {error.Context}: {message}");
        if (exception != null)
        {
            Console.WriteLine($"  Exception: {exception.Message}");
        }
        Console.ResetColor();

        if (_throwOnError && exception != null)
        {
            throw exception;
        }
    }

    /// <summary>
    /// Log a warning (potential issue, but not critical).
    /// </summary>
    public static void LogWarning(string message, string? context = null)
    {
        var error = new ErrorLog
        {
            Level = ErrorLevel.Warning,
            Message = message,
            Context = context ?? GetCallerContext(),
            Timestamp = DateTime.Now
        };

        lock (_lock)
        {
            _errors.Add(error);
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[WARNING] {error.Context}: {message}");
        Console.ResetColor();
    }

    /// <summary>
    /// Log info (general information, not an error).
    /// </summary>
    public static void LogInfo(string message, string? context = null)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"[INFO] {context ?? GetCallerContext()}: {message}");
        Console.ResetColor();
    }

    /// <summary>
    /// Assert a condition (throws if false in debug mode).
    /// </summary>
    public static void Assert(bool condition, string message, string? context = null)
    {
        if (!condition)
        {
            var error = $"Assertion failed: {message}";
            LogError(error, new AssertionException(error), context);

            #if DEBUG
            if (Debugger.IsAttached)
            {
                Debugger.Break();
            }
            #endif
        }
    }

    /// <summary>
    /// Try to execute an action and log any errors.
    /// </summary>
    public static bool TryExecute(Action action, string context)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex)
        {
            LogError($"Failed to execute: {context}", ex, context);
            return false;
        }
    }

    /// <summary>
    /// Try to execute a function and return result or default.
    /// </summary>
    public static T? TryExecute<T>(Func<T> func, string context, T? defaultValue = default)
    {
        try
        {
            return func();
        }
        catch (Exception ex)
        {
            LogError($"Failed to execute: {context}", ex, context);
            return defaultValue;
        }
    }

    /// <summary>
    /// Clear all logged errors.
    /// </summary>
    public static void Clear()
    {
        lock (_lock)
        {
            _errors.Clear();
        }
    }

    /// <summary>
    /// Get error summary report.
    /// </summary>
    public static string GetReport()
    {
        lock (_lock)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Error Report ===");
            sb.AppendLine($"Total Errors: {_errors.Count(e => e.Level == ErrorLevel.Error)}");
            sb.AppendLine($"Total Warnings: {_errors.Count(e => e.Level == ErrorLevel.Warning)}");
            sb.AppendLine();

            var recentErrors = _errors.TakeLast(20).ToList();
            foreach (var error in recentErrors)
            {
                sb.AppendLine($"[{error.Level}] {error.Timestamp:HH:mm:ss} - {error.Context}");
                sb.AppendLine($"  {error.Message}");
                if (error.Exception != null)
                {
                    sb.AppendLine($"  Exception: {error.Exception.Message}");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Save error report to file.
    /// </summary>
    public static void SaveReport(string path)
    {
        try
        {
            File.WriteAllText(path, GetReport());
            Console.WriteLine($"[ErrorHandler] Saved error report to: {path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ErrorHandler] Failed to save report: {ex.Message}");
        }
    }

    private static string GetCallerContext()
    {
        var stackTrace = new StackTrace(2, true);
        var frame = stackTrace.GetFrame(0);
        if (frame != null)
        {
            var method = frame.GetMethod();
            if (method != null)
            {
                return $"{method.DeclaringType?.Name}.{method.Name}";
            }
        }
        return "Unknown";
    }
}

public class ErrorLog
{
    public ErrorLevel Level { get; set; }
    public string Message { get; set; } = "";
    public Exception? Exception { get; set; }
    public string Context { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string? StackTrace { get; set; }
}

public enum ErrorLevel
{
    Info,
    Warning,
    Error,
    Fatal
}

public class AssertionException : Exception
{
    public AssertionException(string message) : base(message) { }
}
