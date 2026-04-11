using System;
using System.Diagnostics;

namespace BlueSky.Core.Platform.Detection
{
    internal static class ShellCommandRunner
    {
        /// <summary>
        /// Executes a command with the given arguments and returns stdout, or null on failure.
        /// Uses separate FileName/Arguments to prevent shell injection.
        /// </summary>
        internal static string? Run(string fileName, string arguments, int timeoutMs = 5000)
        {
            try
            {
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                process.Start();

                // Read stdout/stderr asynchronously to avoid deadlocks
                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(timeoutMs))
                {
                    try { process.Kill(true); } catch { }
                    Console.WriteLine($"[GPU] Command timed out: {fileName} {arguments}");
                    return null;
                }

                var stdout = stdoutTask.GetAwaiter().GetResult();

                if (process.ExitCode != 0)
                {
                    Console.WriteLine($"[GPU] Command failed (exit {process.ExitCode}): {fileName} {arguments}");
                    return null;
                }

                return stdout;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Command not found
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GPU] Command error ({fileName}): {ex.Message}");
                return null;
            }
        }
    }
}
