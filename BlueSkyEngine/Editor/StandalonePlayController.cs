using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using BlueSky.Networking;

namespace BlueSky.Editor;

/// <summary>
/// Manages standalone play mode - launching BlueSkyRuntime as separate processes.
/// Supports launching multiple instances for local multiplayer testing.
/// </summary>
public class StandalonePlayController
{
    private readonly List<Process> _runtimeProcesses = new();
    private readonly object _lock = new();
    private readonly Action<string> _log;

    public bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                return _runtimeProcesses.Count > 0 && _runtimeProcesses.Any(p => !p.HasExited);
            }
        }
    }

    public IReadOnlyList<int> ProcessIds
    {
        get
        {
            lock (_lock)
                return _runtimeProcesses.Where(p => !p.HasExited).Select(p => p.Id).ToList();
        }
    }

    public int? ProcessId
    {
        get
        {
            lock (_lock)
                return _runtimeProcesses.Count > 0 ? _runtimeProcesses[^1].Id : null;
        }
    }

    public StandalonePlayController(Action<string> log) { _log = log; }

    // ══════════════════════════════════════════════════════════════════════════
    //  SINGLE INSTANCE LAUNCH (backward-compatible)
    // ══════════════════════════════════════════════════════════════════════════

    public bool Launch(string scenePath, int width = 1280, int height = 720, bool fullscreen = false, bool vsync = true)
        => Launch(new StandaloneLaunchOptions { ScenePath = scenePath, Width = width, Height = height, Fullscreen = fullscreen, VSync = vsync });

    public bool Launch(StandaloneLaunchOptions options) => LaunchInternal(options, 0, 0);

    // ══════════════════════════════════════════════════════════════════════════
    //  MULTI-INSTANCE LAUNCH
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Launch multiple standalone instances for local multiplayer.
    /// All instances start in Offline mode — user chooses Host/Join inside each window.
    /// Windows are tiled on screen so they don't overlap.
    /// </summary>
    public bool LaunchMultiple(StandaloneLaunchOptions baseOptions, int instanceCount)
    {
        if (IsRunning)
        {
            _log("[Standalone] Already running! Stop first.");
            return false;
        }

        instanceCount = Math.Clamp(instanceCount, 1, 4);

        string runtimePath = GetRuntimeExecutablePath();
        if (!File.Exists(runtimePath))
        {
            _log("[Standalone] Runtime not found - building...");
            if (!BuildRuntime())
            {
                _log("[Standalone] Failed to build runtime!");
                return false;
            }
        }

        string sessionName = !string.IsNullOrWhiteSpace(baseOptions.Multiplayer?.SessionName)
            ? baseOptions.Multiplayer!.SessionName
            : Path.GetFileNameWithoutExtension(baseOptions.ScenePath);

        int successCount = 0;
        int halfW = baseOptions.Width / 2 + 20;
        int halfH = baseOptions.Height / 2 + 60;

        for (int i = 0; i < instanceCount; i++)
        {
            int col = i % 2;
            int row = i / 2;
            int xOff = col * halfW;
            int yOff = row * halfH;

            var opts = new StandaloneLaunchOptions
            {
                ScenePath = baseOptions.ScenePath,
                Width = baseOptions.Width,
                Height = baseOptions.Height,
                Fullscreen = false,
                VSync = baseOptions.VSync,
                Multiplayer = new MultiplayerConfig
                {
                    Mode = MultiplayerMode.Offline,
                    ScenePath = baseOptions.ScenePath,
                }
            };

            _log($"[Standalone] Instance {i + 1}/{instanceCount}: {opts.Multiplayer.Mode} ({opts.Multiplayer.LocalPlayerName})");

            if (LaunchInternal(opts, xOff, yOff))
                successCount++;
        }

        return successCount > 0;
    }

    /// <summary>
    /// Stop all running standalone processes.
    /// </summary>
    public void Stop()
    {
        lock (_lock)
        {
            if (_runtimeProcesses.Count == 0) return;
            _log("[Standalone] Stopping all instances...");
            foreach (var p in _runtimeProcesses)
            {
                try
                {
                    if (!p.HasExited)
                    {
                        p.Kill();
                        p.WaitForExit(2000);
                    }
                }
                catch (Exception ex) { _log($"[Standalone] Error stopping PID {p.Id}: {ex.Message}"); }
            }
            _runtimeProcesses.Clear();
            _log("[Standalone] All instances stopped");
        }
    }

    public void Dispose() { Stop(); }

    // ══════════════════════════════════════════════════════════════════════════
    //  INTERNAL HELPERS
    // ══════════════════════════════════════════════════════════════════════════

    private bool LaunchInternal(StandaloneLaunchOptions options, int xOffset, int yOffset)
    {
        string runtimePath = GetRuntimeExecutablePath();
        if (!File.Exists(runtimePath))
        {
            _log($"[Standalone] Runtime not found at: {runtimePath}");
            return false;
        }

        string args = BuildCommandLineArgs(options);
        args += $" --window-offset-x {xOffset} --window-offset-y {yOffset}";

        _log($"[Standalone] Launching: {args}");

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{runtimePath}\" {args}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = false,
                WorkingDirectory = Path.GetDirectoryName(runtimePath)
            };

            var process = new Process { StartInfo = startInfo };
            process.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) _log($"[Standalone] {e.Data}"); };
            process.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) _log($"[Standalone ERROR] {e.Data}"); };
            process.EnableRaisingEvents = true;

            bool started = process.Start();
            if (started)
            {
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                lock (_lock) { _runtimeProcesses.Add(process); }
                _log($"[Standalone] Launched (PID: {process.Id})");
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            _log($"[Standalone] Exception: {ex.Message}");
            return false;
        }
    }

    private string BuildCommandLineArgs(StandaloneLaunchOptions options)
    {
        string args = $"--scene \"{options.ScenePath}\" --width {options.Width} --height {options.Height}";
        if (options.Fullscreen) args += " --fullscreen";
        if (options.VSync) args += " --vsync";

        if (options.Multiplayer != null && options.Multiplayer.Enabled)
        {
            args += $" --multiplayer {options.Multiplayer.Mode.ToString().ToLowerInvariant()}";
            if (!string.IsNullOrWhiteSpace(options.Multiplayer.SessionName))
                args += $" --session-name \"{options.Multiplayer.SessionName}\"";
            if (!string.IsNullOrWhiteSpace(options.Multiplayer.LocalPlayerName))
                args += $" --player-name \"{options.Multiplayer.LocalPlayerName}\"";
            if (!string.IsNullOrWhiteSpace(options.Multiplayer.JoinSessionId))
                args += $" --join-session \"{options.Multiplayer.JoinSessionId}\"";

            var eos = options.Multiplayer.Eos;
            if (eos != null)
            {
                if (!string.IsNullOrWhiteSpace(eos.ProductId)) args += $" --eos-product-id \"{eos.ProductId}\"";
                if (!string.IsNullOrWhiteSpace(eos.SandboxId)) args += $" --eos-sandbox-id \"{eos.SandboxId}\"";
                if (!string.IsNullOrWhiteSpace(eos.DeploymentId)) args += $" --eos-deployment-id \"{eos.DeploymentId}\"";
                if (!string.IsNullOrWhiteSpace(eos.ClientId)) args += $" --eos-client-id \"{eos.ClientId}\"";
                if (!string.IsNullOrWhiteSpace(eos.ClientSecret)) args += $" --eos-client-secret \"{eos.ClientSecret}\"";
                if (!string.IsNullOrWhiteSpace(eos.ArtifactId)) args += $" --eos-artifact-id \"{eos.ArtifactId}\"";
            }
        }

        return args;
    }

    private string GetRuntimeExecutablePath()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string runtimeDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "BlueSkyRuntime"));
        return Path.Combine(runtimeDir, "bin", "Debug", "net8.0", "BlueSkyRuntime.dll");
    }

    private bool BuildRuntime()
    {
        try
        {
            string runtimeDir = Path.GetDirectoryName(GetRuntimeExecutablePath())!;
            runtimeDir = Path.GetFullPath(Path.Combine(runtimeDir, "..", "..", ".."));
            var buildProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = "build",
                    WorkingDirectory = runtimeDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            buildProcess.Start();
            buildProcess.WaitForExit();
            return buildProcess.ExitCode == 0;
        }
        catch { return false; }
    }
}