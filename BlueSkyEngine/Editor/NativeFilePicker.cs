using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BlueSky.Editor;

/// <summary>
/// Cross-platform native file picker using OS dialogs.
/// </summary>
public static class NativeFilePicker
{
    /// <summary>
    /// Open a file picker dialog and return the selected file path.
    /// Returns null if cancelled.
    /// </summary>
    public static string? OpenFile(string title = "Select File", string filter = "All Files|*.*")
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return OpenFileMacOS(title, filter);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return OpenFileWindows(title, filter);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return OpenFileLinux(title, filter);
        }

        Console.WriteLine("[NativeFilePicker] Unsupported platform");
        return null;
    }

    private static string? OpenFileMacOS(string title, string filter)
    {
        try
        {
            // Use osascript to show native macOS file picker
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "osascript",
                    Arguments = $"-e 'POSIX path of (choose file with prompt \"{title}\")'",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
            {
                return output;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NativeFilePicker] macOS picker error: {ex.Message}");
        }

        return null;
    }

    private static string? OpenFileWindows(string title, string filter)
    {
        try
        {
            // Use PowerShell to show Windows file picker
            var filterParts = filter.Split('|');
            string psFilter = filterParts.Length >= 2 ? filterParts[1] : "*.*";

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-Command \"Add-Type -AssemblyName System.Windows.Forms; $f = New-Object System.Windows.Forms.OpenFileDialog; $f.Title = '{title}'; $f.Filter = '{filter}'; $f.ShowDialog() | Out-Null; $f.FileName\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            if (!string.IsNullOrEmpty(output))
            {
                return output;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NativeFilePicker] Windows picker error: {ex.Message}");
        }

        return null;
    }

    private static string? OpenFileLinux(string title, string filter)
    {
        try
        {
            // Try zenity first (most common)
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "zenity",
                    Arguments = $"--file-selection --title=\"{title}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
            {
                return output;
            }
        }
        catch
        {
            // Zenity not available, try kdialog
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "kdialog",
                        Arguments = $"--getopenfilename . --title \"{title}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();

                if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                {
                    return output;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NativeFilePicker] Linux picker error: {ex.Message}");
            }
        }

        return null;
    }
}
