using System;
using BlueSky.Ramanujan;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlueSky.Core.Assets;
using BlueSky.Meridian;
using BlueSky.SkyCore;

namespace BlueSky.Editor;

public enum AvalancheSigningMode
{
    AdHoc,
    DeveloperId,
    Auto
}

/// <summary>
/// Configuration settings for building a standalone macOS application bundle (.app) via Avalanche.
/// </summary>
public sealed class AvalancheBuildSettings
{
    public string AppName { get; set; } = "BlueSkyGame";
    public string BundleIdentifier { get; set; } = "com.bluesky.game";
    public string Version { get; set; } = "1.0.0";
    public string OutputDirectory { get; set; } = "";
    public string ProjectDirectory { get; set; } = "";
    public string StartupScenePath { get; set; } = "";
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 720;
    public bool Fullscreen { get; set; } = false;
    public bool VSync { get; set; } = true;
    public bool ShowDiagnosticsHud { get; set; } = false;
    public bool IncludeAllProjectAssets { get; set; } = true;
    public bool AdHocCodeSign { get; set; } = true;
    public AvalancheSigningMode SigningMode { get; set; } = AvalancheSigningMode.Auto;
    public string SigningIdentity { get; set; } = "";
    /// <summary>Copy a private .NET runtime into the app so players do not need .NET installed (adds about 75 MB).</summary>
    public bool BundleDotnetRuntime { get; set; } = true;
}

/// <summary>
/// Runtime manifest written to Contents/Resources/avalanche.json inside a built .app bundle.
/// </summary>
public sealed class AvalancheManifest
{
    [JsonPropertyName("appName")]
    public string AppName { get; set; } = "BlueSkyGame";

    [JsonPropertyName("bundleIdentifier")]
    public string BundleIdentifier { get; set; } = "com.bluesky.game";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("startupScene")]
    public string StartupScene { get; set; } = "Assets/StartupScene.bsscene";

    [JsonPropertyName("windowWidth")]
    public int WindowWidth { get; set; } = 1280;

    [JsonPropertyName("windowHeight")]
    public int WindowHeight { get; set; } = 720;

    [JsonPropertyName("fullscreen")]
    public bool Fullscreen { get; set; } = false;

    [JsonPropertyName("vsync")]
    public bool VSync { get; set; } = true;

    [JsonPropertyName("showDiagnosticsHud")]
    public bool ShowDiagnosticsHud { get; set; } = false;

    [JsonPropertyName("targetPlatform")]
    public string TargetPlatform { get; set; } = "macOS";

    [JsonPropertyName("buildTimestampUtc")]
    public DateTime BuildTimestampUtc { get; set; } = DateTime.UtcNow;

    public static AvalancheManifest? Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AvalancheManifest>(json);
        }
        catch
        {
            return null;
        }
    }

    public void Save(string path)
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json, Encoding.UTF8);
    }
}

/// <summary>
/// Detailed report returned after an Avalanche packaging run.
/// </summary>
public sealed class AvalancheBuildReport
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = "";
    public string AppName { get; set; } = "";
    public string AppBundlePath { get; set; } = "";
    public string ExecutablePath { get; set; } = "";
    public string ManifestPath { get; set; } = "";
    public string GameDataDirectory { get; set; } = "";
    public string StartupSceneRelativePath { get; set; } = "";
    public int BundledSceneCount { get; set; }
    public int StartupSceneEntityCount { get; set; }
    /// <summary>Version of the .NET runtime bundled inside the app, or empty if players need .NET installed.</summary>
    public string BundledDotnetRuntimeVersion { get; set; } = "";
    public int BundledAssetCount { get; set; }
    public int RelativizedReferenceCount { get; set; }
    public long TotalBundleBytes { get; set; }
    public double DurationMs { get; set; }
    public List<string> StepsLog { get; } = new();
}

/// <summary>
/// Avalanche Game Packager — builds double-clickable macOS (.app) bundles that carry their own .NET runtime,
/// containing the BlueSky runtime, Metal shaders, native physics/EOS libraries, and cooked GameData.
/// </summary>
public static class AvalanchePackager
{
    public static string SanitizeBundleExecutableName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
            return "BlueSkyGame";

        var sb = new StringBuilder(rawName.Length);
        foreach (char c in rawName.Trim())
        {
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                sb.Append(c);
            else if (c == ' ')
                sb.Append('_');
        }

        string cleaned = sb.ToString().Trim('_');
        return string.IsNullOrEmpty(cleaned) ? "BlueSkyGame" : cleaned;
    }

    public static string SanitizeBundleIdentifier(string rawId, string fallbackAppName)
    {
        if (string.IsNullOrWhiteSpace(rawId))
        {
            string slug = SanitizeBundleExecutableName(fallbackAppName).ToLowerInvariant().Replace('_', '-');
            return $"com.bluesky.{slug}";
        }

        var sb = new StringBuilder(rawId.Length);
        foreach (char c in rawId.Trim())
        {
            if (char.IsLetterOrDigit(c) || c == '.' || c == '-')
                sb.Append(c);
        }

        string result = sb.ToString().Trim('.');
        return string.IsNullOrEmpty(result) ? "com.bluesky.game" : result;
    }

    /// <summary>
    /// Packages a complete macOS Application Bundle (<AppName>.app) ready to launch on macOS.
    /// </summary>
    public static AvalancheBuildReport BuildMacOSApp(
        AvalancheBuildSettings settings,
        World? fallbackWorld = null,
        Action<string>? logger = null)
    {
        var sw = Stopwatch.StartNew();
        var report = new AvalancheBuildReport();

        void Step(string msg)
        {
            report.StepsLog.Add(msg);
            logger?.Invoke(msg);
        }

        try
        {
            string displayAppName = string.IsNullOrWhiteSpace(settings.AppName)
                ? "BlueSkyGame"
                : settings.AppName.Trim();
            string exeName = SanitizeBundleExecutableName(displayAppName);
            string bundleId = SanitizeBundleIdentifier(settings.BundleIdentifier, displayAppName);
            string version = string.IsNullOrWhiteSpace(settings.Version) ? "1.0.0" : settings.Version.Trim();

            string projectDir = !string.IsNullOrWhiteSpace(settings.ProjectDirectory) && Directory.Exists(settings.ProjectDirectory)
                ? Path.GetFullPath(settings.ProjectDirectory)
                : (!string.IsNullOrWhiteSpace(ProjectManager.CurrentProjectDir) && Directory.Exists(ProjectManager.CurrentProjectDir)
                    ? Path.GetFullPath(ProjectManager.CurrentProjectDir)
                    : Directory.GetCurrentDirectory());

            string outputDir = !string.IsNullOrWhiteSpace(settings.OutputDirectory)
                ? Path.GetFullPath(settings.OutputDirectory)
                : Path.Combine(projectDir, "Builds", "MacOS");

            Directory.CreateDirectory(outputDir);

            // Prevent invalid path chars in the .app folder name while preserving spaces
            string appFolderName = string.Join("_", displayAppName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
            if (string.IsNullOrEmpty(appFolderName))
                appFolderName = exeName;
            if (!appFolderName.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                appFolderName += ".app";

            string appBundlePath = Path.Combine(outputDir, appFolderName);
            string contentsDir = Path.Combine(appBundlePath, "Contents");
            string macosDir = Path.Combine(contentsDir, "MacOS");
            string resourcesDir = Path.Combine(contentsDir, "Resources");
            string gameDataDir = Path.Combine(resourcesDir, "GameData");
            string gameAssetsDir = Path.Combine(gameDataDir, "Assets");
            string gameConfigDir = Path.Combine(gameDataDir, "Config");

            Step($"[Avalanche] Packaging '{displayAppName}' ({bundleId} v{version}) -> {appBundlePath}");

            // Clean any existing bundle at target path
            if (Directory.Exists(appBundlePath))
            {
                Directory.Delete(appBundlePath, recursive: true);
            }

            Directory.CreateDirectory(macosDir);
            Directory.CreateDirectory(resourcesDir);
            Directory.CreateDirectory(gameDataDir);
            Directory.CreateDirectory(gameAssetsDir);
            Directory.CreateDirectory(gameConfigDir);

            // 1. Write Info.plist and PkgInfo
            string infoPlistPath = Path.Combine(contentsDir, "Info.plist");
            File.WriteAllText(infoPlistPath, GenerateInfoPlistXml(displayAppName, exeName, bundleId, version), Encoding.UTF8);
            File.WriteAllText(Path.Combine(contentsDir, "PkgInfo"), "APPLBSKY", Encoding.ASCII);
            Step("[Avalanche] Generated Contents/Info.plist and Contents/PkgInfo");

            // 2. Generate AppIcon.png (and AppIcon.icns on macOS if iconutil/sips available)
            GenerateAppIconBundleResources(resourcesDir, Step);

            // 3. Stage Runtime Assemblies, Native Dylibs, Font, and Shaders into Contents/MacOS
            StageRuntimeBinariesAndShaders(macosDir, resourcesDir, exeName, Step);

            // 3b. Bundle a private .NET runtime so the app runs on Macs that do not have .NET installed.
            if (settings.BundleDotnetRuntime)
                report.BundledDotnetRuntimeVersion = StagePrivateDotnetRuntime(macosDir, resourcesDir, Step);

            // 4. Stage and Cook Project GameData (Assets, Startup Scene, Config, Relativized Paths)
            string startupSceneRel = StageAndCookGameData(
                settings,
                projectDir,
                gameDataDir,
                gameAssetsDir,
                gameConfigDir,
                displayAppName,
                fallbackWorld,
                report,
                Step);

            // 5. Write Contents/Resources/avalanche.json Manifest
            string arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "macOS-arm64" : "macOS-x64";
            var manifest = new AvalancheManifest
            {
                AppName = displayAppName,
                BundleIdentifier = bundleId,
                Version = version,
                StartupScene = startupSceneRel,
                WindowWidth = Math.Clamp(settings.WindowWidth, 640, 7680),
                WindowHeight = Math.Clamp(settings.WindowHeight, 360, 4320),
                Fullscreen = settings.Fullscreen,
                VSync = settings.VSync,
                ShowDiagnosticsHud = settings.ShowDiagnosticsHud,
                TargetPlatform = arch,
                BuildTimestampUtc = DateTime.UtcNow
            };

            string manifestPath = Path.Combine(resourcesDir, "avalanche.json");
            manifest.Save(manifestPath);
            Step($"[Avalanche] Wrote manifest Contents/Resources/avalanche.json (StartupScene='{startupSceneRel}')");

            // 6. Write Launcher Executable in Contents/MacOS/<exeName> and set +x permissions
            string launcherPath = Path.Combine(macosDir, exeName);
            File.WriteAllText(launcherPath, GenerateMacOSLauncherScript(displayAppName), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            EnsureExecutablePermissions(launcherPath);

            string runtimeApphost = Path.Combine(macosDir, "BlueSkyRuntimeBin");
            if (File.Exists(runtimeApphost))
                EnsureExecutablePermissions(runtimeApphost);

            string engineApphost = Path.Combine(macosDir, "BlueSkyEngineBin");
            if (File.Exists(engineApphost))
                EnsureExecutablePermissions(engineApphost);

            // 7. Codesign bundle on macOS (Developer ID with Hardened Runtime & Entitlements, or Ad-Hoc)
            if (OperatingSystem.IsMacOS() && (settings.AdHocCodeSign || settings.SigningMode != AvalancheSigningMode.AdHoc))
            {
                CodeSignBundle(appBundlePath, macosDir, resourcesDir, settings, Step);
            }

            sw.Stop();
            report.Success = true;
            report.AppName = displayAppName;
            report.AppBundlePath = appBundlePath;
            report.ExecutablePath = launcherPath;
            report.ManifestPath = manifestPath;
            report.GameDataDirectory = gameDataDir;
            report.StartupSceneRelativePath = startupSceneRel;
            report.TotalBundleBytes = ComputeDirectorySize(appBundlePath);
            report.DurationMs = sw.Elapsed.TotalMilliseconds;

            Step($"✓ [Avalanche] Built '{Path.GetFileName(appBundlePath)}' ({FormatBytes(report.TotalBundleBytes)}, {report.BundledAssetCount} assets, {report.BundledSceneCount} scenes) in {report.DurationMs:F0} ms");
            return report;
        }
        catch (Exception ex)
        {
            sw.Stop();
            report.Success = false;
            report.ErrorMessage = ex.Message;
            report.DurationMs = sw.Elapsed.TotalMilliseconds;
            Step($"✗ [Avalanche] Build failed: {ex.Message}");
            return report;
        }
    }

    /// <summary>
    /// Launches a packaged macOS .app bundle using macOS `open` or direct executable invocation.
    /// </summary>
    public static bool LaunchBuiltMacOSApp(string appBundlePath, Action<string>? logger = null)
    {
        if (string.IsNullOrWhiteSpace(appBundlePath) || !Directory.Exists(appBundlePath))
        {
            logger?.Invoke($"✗ [Avalanche] App bundle not found: {appBundlePath}");
            return false;
        }

        try
        {
            if (OperatingSystem.IsMacOS() && File.Exists("/usr/bin/open"))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/usr/bin/open",
                    Arguments = $"-n \"{appBundlePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(3000);
                if (proc is { ExitCode: 0 })
                {
                    logger?.Invoke($"✓ [Avalanche] Launched {Path.GetFileName(appBundlePath)}");
                    return true;
                }
            }

            // Fallback: find executable inside Contents/MacOS and launch directly
            string macosDir = Path.Combine(appBundlePath, "Contents", "MacOS");
            if (Directory.Exists(macosDir))
            {
                var scripts = Directory.GetFiles(macosDir)
                    .Where(f => !f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
                                !f.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) &&
                                !f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                                !f.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase) &&
                                !f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                string? mainExe = scripts.FirstOrDefault(f => !f.EndsWith("Bin", StringComparison.Ordinal)) ?? scripts.FirstOrDefault();
                if (!string.IsNullOrEmpty(mainExe))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = mainExe,
                        WorkingDirectory = macosDir,
                        UseShellExecute = false
                    };
                    Process.Start(psi);
                    logger?.Invoke($"✓ [Avalanche] Launched executable {Path.GetFileName(mainExe)}");
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            logger?.Invoke($"✗ [Avalanche] Failed to launch app: {ex.Message}");
        }

        return false;
    }

    public static void RevealInFinder(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (OperatingSystem.IsMacOS() && File.Exists("/usr/bin/open"))
            {
                string args = Directory.Exists(path) && path.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
                    ? $"-R \"{path}\""
                    : $"\"{path}\"";
                Process.Start(new ProcessStartInfo
                {
                    FileName = "/usr/bin/open",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
        }
        catch { }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  INTERNAL PACKAGING STAGES
    // ══════════════════════════════════════════════════════════════════════════

    private static void StageRuntimeBinariesAndShaders(string macosDir, string resourcesDir, string exeName, Action<string> step)
    {
        string engineBinDir = AppContext.BaseDirectory;
        EnsureRuntimeIsCurrent(engineBinDir, step);
        string? runtimeBinDir = LocateRuntimeBinDirectory(engineBinDir);

        // 1. Copy BlueSkyRuntime files first if available
        if (!string.IsNullOrEmpty(runtimeBinDir) && Directory.Exists(runtimeBinDir))
        {
            CopyRuntimeOutputFiles(runtimeBinDir, macosDir);
            step($"[Avalanche] Staged BlueSkyRuntime binaries from {runtimeBinDir}");
        }

        // 2. Overlay latest BlueSkyEngine.dll, TeaScript.dll, managed dependencies, font, and dylibs from engineBinDir
        if (Directory.Exists(engineBinDir))
        {
            CopyRuntimeOutputFiles(engineBinDir, macosDir);
        }

        // Rename apphost binaries so they never collide with our launcher script named <exeName>
        string rawRuntimeHost = Path.Combine(macosDir, "BlueSkyRuntime");
        string renamedRuntimeHost = Path.Combine(macosDir, "BlueSkyRuntimeBin");
        if (File.Exists(rawRuntimeHost))
        {
            File.Move(rawRuntimeHost, renamedRuntimeHost, overwrite: true);
        }

        string rawEngineHost = Path.Combine(macosDir, "BlueSkyEngine");
        string renamedEngineHost = Path.Combine(macosDir, "BlueSkyEngineBin");
        if (File.Exists(rawEngineHost))
        {
            File.Move(rawEngineHost, renamedEngineHost, overwrite: true);
        }

        // Ensure libjoltc.dylib is present both in runtimes/osx/native/ AND right in Contents/MacOS/
        // so P/Invoke DllImport("joltc") resolves immediately regardless of working directory.
        string joltNativeInRuntimes = Path.Combine(macosDir, "runtimes", "osx", "native", "libjoltc.dylib");
        string joltNativeRoot = Path.Combine(macosDir, "libjoltc.dylib");
        if (File.Exists(joltNativeInRuntimes) && !File.Exists(joltNativeRoot))
        {
            File.Copy(joltNativeInRuntimes, joltNativeRoot, overwrite: true);
        }

        // Every native library the runtime declares for this platform (Jolt, miniaudio, ...) must be in the bundle.
        VerifyNativeLibrariesStaged(macosDir, step);

        // Ensure roboto.ttf is present in Contents/MacOS and Contents/Resources
        string[] fontCandidates =
        {
            Path.Combine(engineBinDir, "roboto.ttf"),
            Path.Combine(Directory.GetCurrentDirectory(), "BlueSkyEngine", "roboto.ttf"),
            Path.Combine(Directory.GetCurrentDirectory(), "roboto.ttf")
        };
        string? foundFont = Array.Find(fontCandidates, File.Exists);
        if (!string.IsNullOrEmpty(foundFont))
        {
            File.Copy(foundFont, Path.Combine(macosDir, "roboto.ttf"), overwrite: true);
            File.Copy(foundFont, Path.Combine(resourcesDir, "roboto.ttf"), overwrite: true);
        }

        // 3. Stage Metal & HLSL/GLSL Shaders into both Contents/MacOS/Editor/Shaders and Contents/Resources/Editor/Shaders
        string macosShadersDir = Path.Combine(macosDir, "Editor", "Shaders");
        string resShadersDir = Path.Combine(resourcesDir, "Editor", "Shaders");
        Directory.CreateDirectory(macosShadersDir);
        Directory.CreateDirectory(resShadersDir);

        string[] shaderSourceDirs =
        {
            Path.Combine(engineBinDir, "Editor", "Shaders"),
            Path.Combine(Directory.GetCurrentDirectory(), "BlueSkyEngine", "Editor", "Shaders"),
            Path.Combine(Directory.GetCurrentDirectory(), "Editor", "Shaders")
        };

        int shaderCount = 0;
        foreach (string sDir in shaderSourceDirs)
        {
            if (!Directory.Exists(sDir)) continue;
            foreach (string file in Directory.GetFiles(sDir))
            {
                string ext = Path.GetExtension(file);
                if (ext.Equals(".air", StringComparison.OrdinalIgnoreCase))
                    continue;

                string name = Path.GetFileName(file);
                string dst1 = Path.Combine(macosShadersDir, name);
                string dst2 = Path.Combine(resShadersDir, name);

                // Always keep the newest shader source/metallib
                if (!File.Exists(dst1) || File.GetLastWriteTimeUtc(file) > File.GetLastWriteTimeUtc(dst1))
                {
                    File.Copy(file, dst1, overwrite: true);
                    File.Copy(file, dst2, overwrite: true);
                    shaderCount++;
                }
            }
        }

        step($"[Avalanche] Bundled {shaderCount} shader files (.metallib & .metal) and native libraries");
    }

    private static void CopyRuntimeOutputFiles(string sourceDir, string targetMacosDir)
    {
        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string name = Path.GetFileName(file);
            string ext = Path.GetExtension(file);

            // Copy managed assemblies, runtime configs, native dylibs, font, and apphosts
            bool shouldCopy =
                ext.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".dylib", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".ttf", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("BlueSkyRuntime", StringComparison.Ordinal) ||
                name.Equals("BlueSkyEngine", StringComparison.Ordinal);

            if (!shouldCopy)
                continue;

            string dest = Path.Combine(targetMacosDir, name);
            if (!File.Exists(dest) || File.GetLastWriteTimeUtc(file) >= File.GetLastWriteTimeUtc(dest))
            {
                File.Copy(file, dest, overwrite: true);
            }
        }

        // Copy macOS native runtime folder (runtimes/osx/native/libjoltc.dylib)
        string osxRuntimeSrc = Path.Combine(sourceDir, "runtimes", "osx", "native");
        if (Directory.Exists(osxRuntimeSrc))
        {
            string osxRuntimeDst = Path.Combine(targetMacosDir, "runtimes", "osx", "native");
            Directory.CreateDirectory(osxRuntimeDst);
            foreach (string f in Directory.GetFiles(osxRuntimeSrc))
            {
                File.Copy(f, Path.Combine(osxRuntimeDst, Path.GetFileName(f)), overwrite: true);
            }
        }

        // NuGet puts some native libraries under the architecture-specific folder. Miniaudio-CS ships
        // runtimes/osx-arm64/native/libminiaudio.dylib there. If it is not bundled, the managed wrapper still
        // loads but every sound is silently disabled ("Unable to load shared library 'miniaudio'").
        string archRid = GetMacRuntimeIdentifier();
        string archRuntimeSrc = Path.Combine(sourceDir, "runtimes", archRid, "native");
        if (Directory.Exists(archRuntimeSrc))
        {
            string archRuntimeDst = Path.Combine(targetMacosDir, "runtimes", archRid, "native");
            Directory.CreateDirectory(archRuntimeDst);
            foreach (string f in Directory.GetFiles(archRuntimeSrc))
            {
                string libName = Path.GetFileName(f);
                File.Copy(f, Path.Combine(archRuntimeDst, libName), overwrite: true);

                // Also place the library next to the executable, which is the first place dlopen looks.
                // This is how libjoltc.dylib is already handled.
                if (libName.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase))
                    File.Copy(f, Path.Combine(targetMacosDir, libName), overwrite: true);
            }
        }
    }

    /// <summary>
    /// Copies the .NET runtime this editor runs on into Contents/Resources/dotnet: the dotnet host, hostfxr and the
    /// Microsoft.NETCore.App framework the game targets. The launcher starts the game with that copy, so it runs
    /// on Macs without .NET. This needs no download, unlike a self-contained publish, which fetches a runtime pack.
    /// It lives in Resources because codesign treats the framework folder "Microsoft.NETCore.App" as a nested app
    /// bundle anywhere it looks for nested code (such as Contents/MacOS), and refuses to sign the app.
    /// Returns the bundled framework version, or "" (with a warning) if no usable runtime was found.
    /// </summary>
    private static string StagePrivateDotnetRuntime(string macosDir, string resourcesDir, Action<string> step)
    {
        int targetMajor = ReadTargetFrameworkMajor(macosDir) ?? Environment.Version.Major;

        // The editor's own framework lives at <dotnet root>/shared/Microsoft.NETCore.App/<version>/.
        string? frameworkDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
        string? dotnetRoot = frameworkDir == null ? null : Directory.GetParent(frameworkDir)?.Parent?.Parent?.FullName;
        string muxer = dotnetRoot == null ? "" : Path.Combine(dotnetRoot, "dotnet");
        string frameworksRoot = dotnetRoot == null ? "" : Path.Combine(dotnetRoot, "shared", "Microsoft.NETCore.App");
        string fxrRoot = dotnetRoot == null ? "" : Path.Combine(dotnetRoot, "host", "fxr");

        if (dotnetRoot == null || !File.Exists(muxer) || !Directory.Exists(frameworksRoot) || !Directory.Exists(fxrRoot))
        {
            step("[Avalanche] ⚠️ Could not find a .NET installation to bundle. Players will need .NET installed to run this app.");
            return "";
        }

        static (string Path, Version Version)? Newest(string root, Func<Version, bool> accept) =>
            Directory.GetDirectories(root)
                .Select(d => (Path: d, Ok: Version.TryParse(System.IO.Path.GetFileName(d), out var v), Version: v!))
                .Where(x => x.Ok && accept(x.Version))
                .OrderByDescending(x => x.Version)
                .Select(x => ((string, Version)?)(x.Path, x.Version))
                .FirstOrDefault();

        var framework = Newest(frameworksRoot, v => v.Major == targetMajor);
        var fxr = Newest(fxrRoot, v => v.Major >= targetMajor);
        if (framework == null || fxr == null)
        {
            step($"[Avalanche] ⚠️ No .NET {targetMajor} runtime found under {dotnetRoot}. Players will need .NET {targetMajor} installed to run this app.");
            return "";
        }

        string bundledRoot = Path.Combine(resourcesDir, "dotnet");
        if (Directory.Exists(bundledRoot))
            Directory.Delete(bundledRoot, recursive: true);
        Directory.CreateDirectory(bundledRoot);

        File.Copy(muxer, Path.Combine(bundledRoot, "dotnet"), overwrite: true);
        EnsureExecutablePermissions(Path.Combine(bundledRoot, "dotnet"));

        string fxrVersion = Path.GetFileName(fxr.Value.Path);
        CopyDirectoryRecursive(fxr.Value.Path, Path.Combine(bundledRoot, "host", "fxr", fxrVersion), skipBuildsFolder: false);

        string frameworkVersion = Path.GetFileName(framework.Value.Path);
        string bundledFramework = Path.Combine(bundledRoot, "shared", "Microsoft.NETCore.App", frameworkVersion);
        CopyDirectoryRecursive(framework.Value.Path, bundledFramework, skipBuildsFolder: false);
        string createdump = Path.Combine(bundledFramework, "createdump");
        if (File.Exists(createdump))
            EnsureExecutablePermissions(createdump);

        // .NET is MIT licensed; its license and third-party notices travel with the copy.
        foreach (string notice in new[] { "LICENSE.txt", "ThirdPartyNotices.txt" })
        {
            string src = Path.Combine(dotnetRoot, notice);
            if (File.Exists(src))
                File.Copy(src, Path.Combine(bundledRoot, notice), overwrite: true);
        }

        step($"[Avalanche] Bundled .NET {frameworkVersion} runtime ({FormatBytes(ComputeDirectorySize(bundledRoot))}) so players do not need .NET installed");
        return frameworkVersion;
    }

    /// <summary>Major version of the framework BlueSkyRuntime targets, from its runtimeconfig.json.</summary>
    private static int? ReadTargetFrameworkMajor(string macosDir)
    {
        try
        {
            string configPath = Path.Combine(macosDir, "BlueSkyRuntime.runtimeconfig.json");
            if (!File.Exists(configPath))
                return null;

            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(configPath));
            string? version = doc.RootElement.GetProperty("runtimeOptions").GetProperty("framework").GetProperty("version").GetString();
            return Version.TryParse(version, out var parsed) ? parsed.Major : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The executables and native libraries of the bundled .NET runtime, which must be signed like the app's own.</summary>
    private static IEnumerable<string> BundledRuntimeCodeFiles(string resourcesDir)
    {
        string runtimeRoot = Path.Combine(resourcesDir, "dotnet");
        if (!Directory.Exists(runtimeRoot))
            return Array.Empty<string>();

        return Directory.GetFiles(runtimeRoot, "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase) ||
                        Path.GetFileName(f) is "dotnet" or "createdump");
    }

    /// <summary>The .NET runtime identifier for the machine that is packaging (matches the manifest's target platform).</summary>
    private static string GetMacRuntimeIdentifier() =>
        RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";

    /// <summary>Which build configuration the running editor was built in, from its output folder.</summary>
    private static string GetBuildConfiguration(string engineBinDir) =>
        engineBinDir.Replace('\\', '/').Contains("/Release/", StringComparison.OrdinalIgnoreCase) ? "Release" : "Debug";

    /// <summary>
    /// Fails the build if the runtime declares a native library for this platform that did not make it into the
    /// bundle. A missing library does not stop the app from starting; it silently disables a feature (audio) or
    /// crashes later (physics), so it is far better to find out here.
    /// </summary>
    private static void VerifyNativeLibrariesStaged(string macosDir, Action<string> step)
    {
        string depsPath = Path.Combine(macosDir, "BlueSkyRuntime.deps.json");
        if (!File.Exists(depsPath))
            depsPath = Path.Combine(macosDir, "BlueSkyEngine.deps.json");
        if (!File.Exists(depsPath))
            return;

        string archRid = GetMacRuntimeIdentifier();
        var missing = new List<string>();
        int checkedCount = 0;

        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(depsPath));
        if (!doc.RootElement.TryGetProperty("targets", out var targets))
            return;

        foreach (var target in targets.EnumerateObject())
        {
            foreach (var library in target.Value.EnumerateObject())
            {
                if (!library.Value.TryGetProperty("runtimeTargets", out var runtimeTargets))
                    continue;

                foreach (var asset in runtimeTargets.EnumerateObject())
                {
                    if (!asset.Value.TryGetProperty("assetType", out var assetType) || assetType.GetString() != "native")
                        continue;
                    if (!asset.Value.TryGetProperty("rid", out var ridElement))
                        continue;

                    string? rid = ridElement.GetString();
                    if (rid != "osx" && rid != archRid)
                        continue;

                    checkedCount++;
                    string expected = Path.Combine(macosDir, asset.Name.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(expected))
                        missing.Add(asset.Name);
                }
            }
        }

        if (missing.Count > 0)
        {
            throw new FileNotFoundException(
                "The packaged app would be missing native libraries it needs: " + string.Join(", ", missing) +
                ". Rebuild BlueSkyRuntime so they are copied to its output folder.");
        }

        step($"[Avalanche] Verified {checkedCount} native libraries for {archRid} are bundled");
    }

    /// <summary>
    /// Builds BlueSkyRuntime so the bundle's runtime matches the engine that is running. Packaging a runtime that was
    /// compiled against an older engine produces an app that dies at startup with "Method not found".
    /// Skipped when there is no source checkout (nothing to build). Uses --no-dependencies so the engine
    /// binaries this editor is running from are never overwritten.
    /// </summary>
    private static void EnsureRuntimeIsCurrent(string engineBinDir, Action<string> step)
    {
        string[] projectCandidates =
        {
            Path.GetFullPath(Path.Combine(engineBinDir, "..", "..", "..", "..", "BlueSkyRuntime", "BlueSkyRuntime.csproj")),
            Path.Combine(Directory.GetCurrentDirectory(), "BlueSkyRuntime", "BlueSkyRuntime.csproj")
        };
        string? project = Array.Find(projectCandidates, File.Exists);
        if (project == null)
            return;

        string configuration = GetBuildConfiguration(engineBinDir);

        string[] dotnetCandidates =
        {
            Path.Combine(Environment.GetEnvironmentVariable("DOTNET_ROOT") ?? "", "dotnet"),
            "/usr/local/share/dotnet/dotnet",
            "/opt/homebrew/bin/dotnet",
            "/opt/homebrew/opt/dotnet@8/libexec/dotnet"
        };
        string dotnet = Array.Find(dotnetCandidates, c => !string.IsNullOrEmpty(c) && File.Exists(c)) ?? "dotnet";

        step($"[Avalanche] Building BlueSkyRuntime ({configuration}) so it matches the engine...");

        var psi = new ProcessStartInfo
        {
            FileName = dotnet,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(project)!
        };
        foreach (string arg in new[] { "build", project, "-c", configuration, "--no-dependencies", "--nologo", "-v", "quiet" })
            psi.ArgumentList.Add(arg);

        Process? proc;
        try
        {
            proc = Process.Start(psi);
        }
        catch (Exception ex)
        {
            step($"[Avalanche] ⚠️ Could not run 'dotnet' to rebuild BlueSkyRuntime ({ex.Message}). If the app fails to start with 'Method not found', run: dotnet build BlueSkyRuntime");
            return;
        }

        if (proc == null)
            return;

        using (proc)
        {
            var outputTask = proc.StandardOutput.ReadToEndAsync();
            var errorTask = proc.StandardError.ReadToEndAsync();
            if (!proc.WaitForExit(300_000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                throw new TimeoutException("Building BlueSkyRuntime took longer than 5 minutes.");
            }

            if (proc.ExitCode != 0)
            {
                string errors = string.Join(Environment.NewLine,
                    (outputTask.Result + Environment.NewLine + errorTask.Result)
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Where(l => l.Contains("error", StringComparison.OrdinalIgnoreCase))
                        .Take(5));
                throw new InvalidOperationException(
                    $"BlueSkyRuntime failed to build, so the app would not match this engine. {errors}".Trim());
            }
        }
    }

    private static string? LocateRuntimeBinDirectory(string engineBinDir)
    {
        // Prefer the runtime built in the same configuration as this editor, so the two always come from one build.
        string preferred = GetBuildConfiguration(engineBinDir);
        string other = preferred == "Release" ? "Debug" : "Release";
        string[] candidates =
        {
            Path.GetFullPath(Path.Combine(engineBinDir, "..", "..", "..", "..", "BlueSkyRuntime", "bin", preferred, "net8.0")),
            Path.GetFullPath(Path.Combine(engineBinDir, "..", "..", "..", "..", "BlueSkyRuntime", "bin", other, "net8.0")),
            Path.Combine(Directory.GetCurrentDirectory(), "BlueSkyRuntime", "bin", preferred, "net8.0"),
            Path.Combine(Directory.GetCurrentDirectory(), "BlueSkyRuntime", "bin", other, "net8.0")
        };

        return Array.Find(candidates, d => Directory.Exists(d) && File.Exists(Path.Combine(d, "BlueSkyRuntime.dll")));
    }

    private static string StageAndCookGameData(
        AvalancheBuildSettings settings,
        string projectDir,
        string gameDataDir,
        string gameAssetsDir,
        string gameConfigDir,
        string displayAppName,
        World? fallbackWorld,
        AvalancheBuildReport report,
        Action<string> step)
    {
        // 1. Write project marker .BlueSkyProj
        string projMarker = Path.Combine(gameDataDir, $"{SanitizeBundleExecutableName(displayAppName)}.BlueSkyProj");
        File.WriteAllText(projMarker, "{ \"version\": \"1.0\", \"packagedBy\": \"Avalanche\" }", Encoding.UTF8);

        // 2. Copy Config/EOS.ini
        string[] eosCandidates =
        {
            Path.Combine(projectDir, "Config", "EOS.ini"),
            Path.Combine(Directory.GetCurrentDirectory(), "Config", "EOS.ini")
        };
        string? eosSrc = Array.Find(eosCandidates, File.Exists);
        if (!string.IsNullOrEmpty(eosSrc))
        {
            File.Copy(eosSrc, Path.Combine(gameConfigDir, "EOS.ini"), overwrite: true);
        }
        else
        {
            File.WriteAllText(Path.Combine(gameConfigDir, "EOS.ini"),
                "[EOS]\nProductName=" + displayAppName + "\nProductVersion=" + settings.Version + "\n",
                Encoding.UTF8);
        }

        // 3. Copy project Assets/ directory if present
        string srcAssetsDir = Path.Combine(projectDir, "Assets");
        if (settings.IncludeAllProjectAssets && Directory.Exists(srcAssetsDir))
        {
            CopyDirectoryRecursive(srcAssetsDir, gameAssetsDir, skipBuildsFolder: true);
        }

        // 3b. Copy the project's Scenes/ folder. Editor Save As writes scenes there, and scripts reach them
        // with loadScene("Scenes/<name>.blueskyscene"), so the bundle keeps the same relative layout.
        string srcScenesDir = Path.Combine(projectDir, "Scenes");
        string gameScenesDir = Path.Combine(gameDataDir, "Scenes");
        if (settings.IncludeAllProjectAssets && Directory.Exists(srcScenesDir))
        {
            CopyDirectoryRecursive(srcScenesDir, gameScenesDir, skipBuildsFolder: true);
            step($"[Avalanche] Bundled project Scenes/ folder ({Directory.GetFiles(gameScenesDir, "*.*", SearchOption.AllDirectories).Length} files)");
        }

        // 4. Determine Startup Scene and ensure it exists inside GameData/Assets/
        string startupSceneRel = "Assets/StartupScene.bsscene";
        string requestedScene = settings.StartupScenePath;

        if (!string.IsNullOrWhiteSpace(requestedScene))
        {
            requestedScene = AssetPathResolver.Resolve(requestedScene);
        }

        if (!string.IsNullOrWhiteSpace(requestedScene) && File.Exists(requestedScene))
        {
            string fullScene = Path.GetFullPath(requestedScene);
            if (Directory.Exists(srcAssetsDir) &&
                fullScene.StartsWith(Path.GetFullPath(srcAssetsDir), StringComparison.OrdinalIgnoreCase))
            {
                string relInsideAssets = Path.GetRelativePath(srcAssetsDir, fullScene).Replace('\\', '/');
                startupSceneRel = "Assets/" + relInsideAssets;
                string destScenePath = Path.Combine(gameAssetsDir, relInsideAssets);
                Directory.CreateDirectory(Path.GetDirectoryName(destScenePath)!);
                File.Copy(fullScene, destScenePath, overwrite: true);
            }
            else if (Directory.Exists(srcScenesDir) &&
                     fullScene.StartsWith(Path.GetFullPath(srcScenesDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                string relInsideScenes = Path.GetRelativePath(srcScenesDir, fullScene).Replace('\\', '/');
                startupSceneRel = "Scenes/" + relInsideScenes;
                string destScenePath = Path.Combine(gameScenesDir, relInsideScenes);
                Directory.CreateDirectory(Path.GetDirectoryName(destScenePath)!);
                File.Copy(fullScene, destScenePath, overwrite: true);
            }
            else
            {
                string fileName = Path.GetFileName(fullScene);
                startupSceneRel = "Assets/" + fileName;
                string destScenePath = Path.Combine(gameAssetsDir, fileName);
                File.Copy(fullScene, destScenePath, overwrite: true);
            }
        }
        else if (fallbackWorld != null)
        {
            // Serialize the live Editor World directly into GameData/Assets/StartupScene.bsscene
            var sceneData = SceneConverter.WorldToSceneData(fallbackWorld, displayAppName);
            string destScenePath = Path.Combine(gameAssetsDir, "StartupScene.bsscene");
            if (!SceneSerializer.SaveScene(sceneData, destScenePath))
                throw new IOException($"Could not write the startup scene to {destScenePath}");
            step("[Avalanche] Serialized active Editor World as Assets/StartupScene.bsscene");
        }
        else
        {
            // Look for any existing scene in GameData/Assets
            var existingScenes = Directory.GetFiles(gameAssetsDir, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".bsscene", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".blueskyscene", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (existingScenes.Count > 0)
            {
                string rel = Path.GetRelativePath(gameDataDir, existingScenes[0]).Replace('\\', '/');
                startupSceneRel = rel;
            }
            else
            {
                // Create a valid default scene so the bundle always boots cleanly
                var defaultWorld = new World();
                var camEntity = defaultWorld.CreateEntity();
                defaultWorld.AddComponent(camEntity, new BlueSky.SkyCore.Builtin.TransformComponent
                {
                    Position = new BlueSky.Ramanujan.Vector3(0, 1, 0),
                    Rotation = BlueSky.Ramanujan.Quaternion.Identity,
                    Scale = BlueSky.Ramanujan.Vector3.One
                });
                var sceneData = SceneConverter.WorldToSceneData(defaultWorld, displayAppName);
                string destScenePath = Path.Combine(gameAssetsDir, "StartupScene.bsscene");
                bool wroteDefault = SceneSerializer.SaveScene(sceneData, destScenePath);
                defaultWorld.Dispose();
                if (!wroteDefault)
                    throw new IOException($"Could not write the default startup scene to {destScenePath}");
            }
        }

        // 5. Cook & relativize all scenes, prefabs, and .blueskyasset Strata slot links inside GameData/Assets
        CookAndRelativizeGameData(projectDir, gameDataDir, gameAssetsDir, report, step);

        // 6. Verify the startup scene really is in the bundle and can be read back.
        string stagedStartupScene = Path.Combine(gameDataDir, startupSceneRel.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(stagedStartupScene))
            throw new FileNotFoundException($"The startup scene was not embedded in the bundle: {startupSceneRel}");
        var stagedScene = SceneSerializer.LoadScene(stagedStartupScene)
            ?? throw new InvalidDataException($"The embedded startup scene could not be read back: {startupSceneRel}");
        report.StartupSceneEntityCount = stagedScene.Entities.Count;
        step($"[Avalanche] Embedded startup scene '{startupSceneRel}' ('{stagedScene.Name}', {stagedScene.Entities.Count} entities)");

        // Count final bundled scenes and assets
        var allBundledFiles = Directory.GetFiles(gameAssetsDir, "*.*", SearchOption.AllDirectories);
        report.BundledAssetCount = allBundledFiles.Length;
        var bundledSceneCandidates = allBundledFiles.Concat(Directory.Exists(gameScenesDir)
            ? Directory.GetFiles(gameScenesDir, "*.*", SearchOption.AllDirectories)
            : Array.Empty<string>());
        report.BundledSceneCount = bundledSceneCandidates.Count(f =>
            f.EndsWith(".bsscene", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".blueskyscene", StringComparison.OrdinalIgnoreCase));

        return startupSceneRel;
    }

    private static void CookAndRelativizeGameData(
        string sourceProjectDir,
        string gameDataDir,
        string gameAssetsDir,
        AvalancheBuildReport report,
        Action<string> step)
    {
        string bundledExternalDir = Path.Combine(gameAssetsDir, "Bundled");
        var copiedExternals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string RelativizeAndStageAsset(string? rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
                return string.Empty;

            string trimmed = rawPath.Trim();

            // Already a clean relative path under Assets/ that exists in gameDataDir?
            string normalizedRel = trimmed.Replace('\\', '/');
            if (!Path.IsPathRooted(trimmed))
            {
                string candidateInBundle = Path.Combine(gameDataDir, normalizedRel);
                if (File.Exists(candidateInBundle))
                    return normalizedRel;
            }

            // Try to resolve the source file on disk
            string resolved = AssetPathResolver.Resolve(trimmed);
            if (!File.Exists(resolved) && !Path.IsPathRooted(trimmed))
            {
                string inSourceProj = Path.Combine(sourceProjectDir, trimmed);
                if (File.Exists(inSourceProj))
                    resolved = Path.GetFullPath(inSourceProj);
            }

            if (!File.Exists(resolved))
            {
                // Even if not found on disk, strip any machine-specific prefix before "/Assets/"
                int assetsIdx = normalizedRel.LastIndexOf("/Assets/", StringComparison.OrdinalIgnoreCase);
                if (assetsIdx >= 0)
                {
                    report.RelativizedReferenceCount++;
                    return normalizedRel[(assetsIdx + 1)..];
                }
                return trimmed;
            }

            string fullResolved = Path.GetFullPath(resolved);

            // Check if it's already inside gameDataDir
            if (fullResolved.StartsWith(Path.GetFullPath(gameDataDir), StringComparison.OrdinalIgnoreCase))
            {
                report.RelativizedReferenceCount++;
                return Path.GetRelativePath(gameDataDir, fullResolved).Replace('\\', '/');
            }

            // Check if it was inside sourceProjectDir/Assets
            string srcAssets = Path.Combine(sourceProjectDir, "Assets");
            if (Directory.Exists(srcAssets) &&
                fullResolved.StartsWith(Path.GetFullPath(srcAssets), StringComparison.OrdinalIgnoreCase))
            {
                string relInAssets = Path.GetRelativePath(srcAssets, fullResolved).Replace('\\', '/');
                string targetInGameAssets = Path.Combine(gameAssetsDir, relInAssets);
                if (!File.Exists(targetInGameAssets))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(targetInGameAssets)!);
                    File.Copy(fullResolved, targetInGameAssets, overwrite: true);
                }
                report.RelativizedReferenceCount++;
                return "Assets/" + relInAssets;
            }

            // External file outside project Assets/ — stage it into GameData/Assets/Bundled/
            if (copiedExternals.TryGetValue(fullResolved, out string? existingRel))
            {
                report.RelativizedReferenceCount++;
                return existingRel;
            }

            Directory.CreateDirectory(bundledExternalDir);
            string fileName = Path.GetFileName(fullResolved);
            string destBundled = Path.Combine(bundledExternalDir, fileName);
            int suffix = 1;
            while (File.Exists(destBundled) &&
                   !string.Equals(ComputeFileHashSafe(fullResolved), ComputeFileHashSafe(destBundled), StringComparison.OrdinalIgnoreCase))
            {
                string stem = Path.GetFileNameWithoutExtension(fileName);
                string ext = Path.GetExtension(fileName);
                destBundled = Path.Combine(bundledExternalDir, $"{stem}_{suffix++}{ext}");
            }

            File.Copy(fullResolved, destBundled, overwrite: true);
            string relBundled = Path.GetRelativePath(gameDataDir, destBundled).Replace('\\', '/');
            copiedExternals[fullResolved] = relBundled;
            report.RelativizedReferenceCount++;
            return relBundled;
        }

        // First pass: inspect all scenes (.bsscene, .blueskyscene) and prefabs (.bsprefab, .bseprefab)
        // in both Assets/ and Scenes/. The editor saves prefabs as .bseprefab.
        string gameScenesRoot = Path.Combine(gameDataDir, "Scenes");
        var sceneAndPrefabFiles = Directory.GetFiles(gameAssetsDir, "*.*", SearchOption.AllDirectories)
            .Concat(Directory.Exists(gameScenesRoot)
                ? Directory.GetFiles(gameScenesRoot, "*.*", SearchOption.AllDirectories)
                : Array.Empty<string>())
            .Where(f => f.EndsWith(".bsscene", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".blueskyscene", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".bsprefab", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".bseprefab", StringComparison.OrdinalIgnoreCase))
            .ToList();

        for (int s = 0; s < sceneAndPrefabFiles.Count; s++)
        {
            string sceneFile = sceneAndPrefabFiles[s];
            var sceneData = SceneSerializer.LoadScene(sceneFile);
            if (sceneData == null) continue;

            bool modified = false;
            foreach (var entity in sceneData.Entities)
            {
                foreach (var comp in entity.Components)
                {
                    switch (comp)
                    {
                        case StaticMeshComponentData sm when !string.IsNullOrEmpty(sm.MeshAssetId):
                        {
                            string rel = RelativizeAndStageAsset(sm.MeshAssetId);
                            if (rel != sm.MeshAssetId) { sm.MeshAssetId = rel; modified = true; }
                            break;
                        }
                        case SkeletalMeshComponentData sk when !string.IsNullOrEmpty(sk.MeshAssetPath):
                        {
                            string rel = RelativizeAndStageAsset(sk.MeshAssetPath);
                            if (rel != sk.MeshAssetPath) { sk.MeshAssetPath = rel; modified = true; }
                            break;
                        }
                        case TerrainComponentData tc when !string.IsNullOrEmpty(tc.TerrainAssetPath):
                        {
                            string rel = RelativizeAndStageAsset(tc.TerrainAssetPath);
                            if (rel != tc.TerrainAssetPath) { tc.TerrainAssetPath = rel; modified = true; }
                            break;
                        }
                        case PhysicsComponentData pc when !string.IsNullOrEmpty(pc.PhysicsAssetPath):
                        {
                            string rel = RelativizeAndStageAsset(pc.PhysicsAssetPath);
                            if (rel != pc.PhysicsAssetPath) { pc.PhysicsAssetPath = rel; modified = true; }
                            break;
                        }
                        case TeaScriptComponentData ts when !string.IsNullOrEmpty(ts.ScriptAssetId):
                        {
                            string rel = RelativizeAndStageAsset(ts.ScriptAssetId);
                            if (rel != ts.ScriptAssetId) { ts.ScriptAssetId = rel; modified = true; }
                            break;
                        }
                        case NetworkManagerComponentData nm when !string.IsNullOrEmpty(nm.PrefabPath):
                        {
                            string rel = RelativizeAndStageAsset(nm.PrefabPath);
                            if (rel != nm.PrefabPath)
                            {
                                nm.PrefabPath = rel;
                                modified = true;
                                string stagedPrefab = Path.Combine(gameDataDir, rel);
                                if (File.Exists(stagedPrefab) && !sceneAndPrefabFiles.Contains(stagedPrefab, StringComparer.OrdinalIgnoreCase))
                                {
                                    sceneAndPrefabFiles.Add(stagedPrefab);
                                }
                            }
                            break;
                        }
                        case AudioSourceComponentData aud when !string.IsNullOrEmpty(aud.ClipPath):
                        {
                            string rel = RelativizeAndStageAsset(aud.ClipPath);
                            if (rel != aud.ClipPath) { aud.ClipPath = rel; modified = true; }
                            break;
                        }
                    }
                }
            }

            if (modified && !SceneSerializer.SaveScene(sceneData, sceneFile))
                throw new IOException($"Could not write the cooked scene '{Path.GetFileName(sceneFile)}' into the bundle");
        }

        // Second pass: inspect all .blueskyasset files in GameData/Assets and relativize strataSlot0..63 metadata links
        var blueAssets = Directory.GetFiles(gameAssetsDir, "*.blueskyasset", SearchOption.AllDirectories);
        foreach (string assetPath in blueAssets)
        {
            var asset = BlueAsset.Load(assetPath);
            if (asset == null || asset.Metadata == null) continue;

            bool assetChanged = false;
            var keys = asset.Metadata.Keys.Where(k => k.StartsWith("strataSlot", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (string key in keys)
            {
                string? slotPath = asset.Metadata[key];
                if (!string.IsNullOrWhiteSpace(slotPath))
                {
                    string relSlot = RelativizeAndStageAsset(slotPath);
                    if (relSlot != slotPath)
                    {
                        asset.Metadata[key] = relSlot;
                        assetChanged = true;
                    }
                }
            }

            // Meshes cite their .stratapack in "sourcePack". Skeleton, skin, vertex AO and sky probes are
            // sliced straight from that file at runtime, so it has to ship with the build when any of them are used.
            // Otherwise only the file name is kept, so the build does not carry the author's machine path.
            if (asset.Metadata.TryGetValue("sourcePack", out var packRef) && !string.IsNullOrWhiteSpace(packRef))
            {
                if (MeshReadsSourcePack(asset.Metadata, packRef))
                {
                    string relPack = RelativizeAndStageAsset(packRef);
                    if (relPack != packRef)
                    {
                        asset.Metadata["sourcePack"] = relPack;
                        assetChanged = true;
                    }

                    if (File.Exists(Path.Combine(gameDataDir, relPack.Replace('/', Path.DirectorySeparatorChar))))
                        step($"[Avalanche] Bundled source pack '{Path.GetFileName(packRef)}' for '{asset.AssetName}' (skeleton / AO / sky probes)");
                    else
                        step($"[Avalanche] ⚠️ '{asset.AssetName}' reads its skeleton/AO/probes from '{Path.GetFileName(packRef)}', but that file was not found. Those features will be missing in this build.");
                }
                else
                {
                    string nameOnly = Path.GetFileName(packRef);
                    if (nameOnly != packRef)
                    {
                        asset.Metadata["sourcePack"] = nameOnly;
                        assetChanged = true;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(asset.SourceFile))
            {
                asset.SourceFile = Path.GetFileName(asset.SourceFile);
                assetChanged = true;
            }

            if (assetChanged)
            {
                asset.Save(assetPath);
            }
        }

        if (report.RelativizedReferenceCount > 0)
        {
            step($"[Avalanche] Relativized {report.RelativizedReferenceCount} asset/scene/material path references for portable bundle execution");
        }
    }

    /// <summary>
    /// True when a mesh asset reads data from its source .stratapack at runtime: a skeleton and skin stream,
    /// vertex AO, or sky probes in the pack header. Plain static meshes carry everything they need in the
    /// .blueskyasset, so their pack is not worth shipping.
    /// </summary>
    private static bool MeshReadsSourcePack(Dictionary<string, string> metadata, string packRef)
    {
        if (metadata.ContainsKey("skinOffset") || metadata.ContainsKey("aoOffset"))
            return true;

        try
        {
            string resolved = AssetPathResolver.Resolve(packRef);
            if (!File.Exists(resolved))
                return false;

            var decoded = BlueSky.BSR.Strata.StrataPack.DecodeHeader(resolved);
            return decoded.Header.Probes is { Count: > 0 };
        }
        catch
        {
            return false;
        }
    }

    private static string GenerateInfoPlistXml(string displayAppName, string exeName, string bundleId, string version)
    {
        static string Esc(string s) => s
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
                <key>CFBundleName</key>
                <string>{Esc(displayAppName)}</string>
                <key>CFBundleDisplayName</key>
                <string>{Esc(displayAppName)}</string>
                <key>CFBundleIdentifier</key>
                <string>{Esc(bundleId)}</string>
                <key>CFBundleVersion</key>
                <string>{Esc(version)}</string>
                <key>CFBundleShortVersionString</key>
                <string>{Esc(version)}</string>
                <key>CFBundlePackageType</key>
                <string>APPL</string>
                <key>CFBundleSignature</key>
                <string>BSKY</string>
                <key>CFBundleExecutable</key>
                <string>{Esc(exeName)}</string>
                <key>CFBundleIconFile</key>
                <string>AppIcon</string>
                <key>LSMinimumSystemVersion</key>
                <string>11.0</string>
                <key>NSHighResolutionCapable</key>
                <true/>
                <key>NSPrincipalClass</key>
                <string>NSApplication</string>
                <key>NSSupportsAutomaticGraphicsSwitching</key>
                <true/>
            </dict>
            </plist>
            """;
    }

    private static string GenerateMacOSLauncherScript(string displayAppName)
    {
        return $$"""
            #!/bin/bash
            # Avalanche macOS Application Bundle Launcher — {{displayAppName}}
            set -e

            MACOS_DIR="$(cd "$(dirname "$0")" && pwd)"
            CONTENTS_DIR="$(cd "$MACOS_DIR/.." && pwd)"
            RESOURCES_DIR="$CONTENTS_DIR/Resources"
            GAMEDATA_DIR="$RESOURCES_DIR/GameData"
            MANIFEST_PATH="$RESOURCES_DIR/avalanche.json"

            # Strip macOS Finder -psn_* process serial number argument if present
            CLEAN_ARGS=()
            for arg in "$@"; do
                if [[ "$arg" != -psn_* ]]; then
                    CLEAN_ARGS+=("$arg")
                fi
            done

            # Locate .NET 8 runtime across Apple Silicon Homebrew, Intel Homebrew, user ~/.dotnet, and system paths
            if [ -z "$DOTNET_ROOT" ] || [ ! -x "$DOTNET_ROOT/dotnet" ]; then
                for candidate in \
                    "/opt/homebrew/opt/dotnet@8/libexec" \
                    "/opt/homebrew/opt/dotnet/libexec" \
                    "/usr/local/share/dotnet" \
                    "$HOME/.dotnet"; do
                    if [ -x "$candidate/dotnet" ]; then
                        export DOTNET_ROOT="$candidate"
                        break
                    fi
                done

                if [ -z "$DOTNET_ROOT" ] && [ -d "/opt/homebrew/Cellar/dotnet@8" ]; then
                    LATEST_CELLAR="$(ls -1d /opt/homebrew/Cellar/dotnet@8/*/libexec 2>/dev/null | tail -n 1)"
                    if [ -n "$LATEST_CELLAR" ] && [ -x "$LATEST_CELLAR/dotnet" ]; then
                        export DOTNET_ROOT="$LATEST_CELLAR"
                    fi
                fi
            fi

            if [ -n "$DOTNET_ROOT" ]; then
                export PATH="$DOTNET_ROOT:$PATH"
            fi
            export PATH="$PATH:/opt/homebrew/bin:/usr/local/bin:$HOME/.dotnet"
            export DYLD_LIBRARY_PATH="$MACOS_DIR:$MACOS_DIR/runtimes/osx/native:${DYLD_LIBRARY_PATH}"

            if [ -d "$GAMEDATA_DIR" ]; then
                cd "$GAMEDATA_DIR"
            else
                cd "$MACOS_DIR"
            fi

            # Prefer the .NET runtime bundled inside the app, so players do not need .NET installed.
            PRIVATE_DOTNET_ROOT="$RESOURCES_DIR/dotnet"
            if [ -x "$PRIVATE_DOTNET_ROOT/dotnet" ] && [ -f "$MACOS_DIR/BlueSkyRuntime.dll" ]; then
                export DOTNET_ROOT="$PRIVATE_DOTNET_ROOT"
                unset DOTNET_ROOT_ARM64 DOTNET_ROOT_X64
                exec "$PRIVATE_DOTNET_ROOT/dotnet" "$MACOS_DIR/BlueSkyRuntime.dll" --avalanche-manifest "$MANIFEST_PATH" "${CLEAN_ARGS[@]}"
            fi

            if command -v dotnet >/dev/null 2>&1 && [ -f "$MACOS_DIR/BlueSkyRuntime.dll" ]; then
                exec dotnet "$MACOS_DIR/BlueSkyRuntime.dll" --avalanche-manifest "$MANIFEST_PATH" "${CLEAN_ARGS[@]}"
            elif [ -x "$MACOS_DIR/BlueSkyRuntimeBin" ]; then
                exec "$MACOS_DIR/BlueSkyRuntimeBin" --avalanche-manifest "$MANIFEST_PATH" "${CLEAN_ARGS[@]}"
            elif command -v dotnet >/dev/null 2>&1 && [ -f "$MACOS_DIR/BlueSkyEngine.dll" ]; then
                exec dotnet "$MACOS_DIR/BlueSkyEngine.dll" --avalanche-manifest "$MANIFEST_PATH" "${CLEAN_ARGS[@]}"
            elif [ -x "$MACOS_DIR/BlueSkyEngineBin" ]; then
                exec "$MACOS_DIR/BlueSkyEngineBin" --avalanche-manifest "$MANIFEST_PATH" "${CLEAN_ARGS[@]}"
            else
                /usr/bin/osascript -e 'display alert "BlueSky Avalanche Runtime Error" message "Could not locate the .NET 8 runtime or BlueSkyRuntime.dll inside the application bundle." as critical' >/dev/null 2>&1 || true
                exit 1
            fi
            """;
    }

    private static void GenerateAppIconBundleResources(string resourcesDir, Action<string> step)
    {
        try
        {
            string pngPath = Path.Combine(resourcesDir, "AppIcon.png");
            byte[] pngBytes = SynthesizeAvalancheIconPng(256, 256);
            File.WriteAllBytes(pngPath, pngBytes);

            // On macOS, if iconutil and sips are available, also build AppIcon.icns
            if (OperatingSystem.IsMacOS() && File.Exists("/usr/bin/iconutil") && File.Exists("/usr/bin/sips"))
            {
                string iconsetDir = Path.Combine(resourcesDir, "AppIcon.iconset");
                Directory.CreateDirectory(iconsetDir);
                try
                {
                    File.WriteAllBytes(Path.Combine(iconsetDir, "icon_256x256.png"), pngBytes);
                    File.WriteAllBytes(Path.Combine(iconsetDir, "icon_128x128@2x.png"), pngBytes);

                    int[] sizes = { 16, 32, 64, 128 };
                    foreach (int sz in sizes)
                    {
                        byte[] sizedBytes = SynthesizeAvalancheIconPng(sz, sz);
                        File.WriteAllBytes(Path.Combine(iconsetDir, $"icon_{sz}x{sz}.png"), sizedBytes);
                        if (sz >= 32)
                        {
                            File.WriteAllBytes(Path.Combine(iconsetDir, $"icon_{sz / 2}x{sz / 2}@2x.png"), sizedBytes);
                        }
                    }

                    string icnsPath = Path.Combine(resourcesDir, "AppIcon.icns");
                    using var proc = Process.Start(new ProcessStartInfo
                    {
                        FileName = "/usr/bin/iconutil",
                        Arguments = $"-c icns \"{iconsetDir}\" -o \"{icnsPath}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    });
                    proc?.WaitForExit(3000);
                }
                finally
                {
                    if (Directory.Exists(iconsetDir))
                        Directory.Delete(iconsetDir, recursive: true);
                }
            }

            step("[Avalanche] Generated custom Avalanche mountain-crest app icon");
        }
        catch
        {
            // Non-fatal if icon generation fails
        }
    }

    /// <summary>
    /// Synthesizes a valid RGBA8 PNG image featuring the BlueSky / Avalanche mountain crest.
    /// Zero external dependencies required.
    /// </summary>
    internal static byte[] SynthesizeAvalancheIconPng(int width, int height)
    {
        byte[] rawScanlines = new byte[height * (1 + width * 4)];
        float invW = 1f / Math.Max(1, width - 1);
        float invH = 1f / Math.Max(1, height - 1);

        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * (1 + width * 4);
            rawScanlines[rowOffset] = 0; // Filter type 0 (None)
            float v = y * invH;

            for (int x = 0; x < width; x++)
            {
                float u = x * invW;
                float dx = MathF.Abs(u - 0.5f) - 0.30f;
                float dy = MathF.Abs(v - 0.5f) - 0.30f;
                float cornerDist = MathF.Sqrt(MathF.Max(dx, 0f) * MathF.Max(dx, 0f) + MathF.Max(dy, 0f) * MathF.Max(dy, 0f));

                // Rounded macOS squircle mask (radius ~ 0.18)
                float alpha = Math.Clamp((0.185f - cornerDist) * width * 0.45f, 0f, 1f);
                if (alpha <= 0.001f)
                {
                    int px0 = rowOffset + 1 + x * 4;
                    rawScanlines[px0] = 0;
                    rawScanlines[px0 + 1] = 0;
                    rawScanlines[px0 + 2] = 0;
                    rawScanlines[px0 + 3] = 0;
                    continue;
                }

                // Deep cobalt-to-cyan sky gradient
                float r = 14f + (1f - v) * 28f;
                float g = 28f + (1f - v) * 95f;
                float b = 68f + (1f - v) * 165f;

                // Stylized twin Avalanche mountain peaks
                float mainPeakY = 0.28f + MathF.Abs(u - 0.46f) * 1.15f;
                float secondPeakY = 0.38f + MathF.Abs(u - 0.66f) * 1.10f;
                float mountainY = MathF.Min(mainPeakY, secondPeakY);

                if (v >= mountainY && v <= 0.84f)
                {
                    bool sunlitFace = (u < 0.46f && mainPeakY <= secondPeakY) || (u >= 0.46f && u < 0.66f && secondPeakY < mainPeakY);
                    float snowFactor = Math.Clamp(1f - (v - mountainY) * 3.2f, 0f, 1f);
                    if (sunlitFace)
                    {
                        r = 215f + snowFactor * 38f;
                        g = 232f + snowFactor * 22f;
                        b = 250f;
                    }
                    else
                    {
                        r = 72f + snowFactor * 95f;
                        g = 128f + snowFactor * 80f;
                        b = 198f + snowFactor * 45f;
                    }
                }

                int px = rowOffset + 1 + x * 4;
                rawScanlines[px] = (byte)Math.Clamp((int)r, 0, 255);
                rawScanlines[px + 1] = (byte)Math.Clamp((int)g, 0, 255);
                rawScanlines[px + 2] = (byte)Math.Clamp((int)b, 0, 255);
                rawScanlines[px + 3] = (byte)Math.Clamp((int)(alpha * 255f), 0, 255);
            }
        }

        using var ms = new MemoryStream();
        // PNG Signature
        ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

        // IHDR Chunk
        byte[] ihdr = new byte[13];
        WriteBigEndianInt32(ihdr, 0, width);
        WriteBigEndianInt32(ihdr, 4, height);
        ihdr[8] = 8;  // 8 bits per channel
        ihdr[9] = 6;  // Color type 6: RGBA
        ihdr[10] = 0; // Compression
        ihdr[11] = 0; // Filter
        ihdr[12] = 0; // Interlace
        WritePngChunk(ms, "IHDR", ihdr);

        // IDAT Chunk (zlib stream wrapping Deflate)
        byte[] idatPayload;
        using (var zStream = new MemoryStream())
        {
            using (var zlib = new ZLibStream(zStream, CompressionLevel.Optimal, leaveOpen: true))
            {
                zlib.Write(rawScanlines, 0, rawScanlines.Length);
            }
            idatPayload = zStream.ToArray();
        }
        WritePngChunk(ms, "IDAT", idatPayload);

        // IEND Chunk
        WritePngChunk(ms, "IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    private static void WritePngChunk(Stream s, string type, byte[] data)
    {
        Span<byte> lenBuf = stackalloc byte[4];
        WriteBigEndianInt32(lenBuf, 0, data.Length);
        s.Write(lenBuf);

        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        if (data.Length > 0)
            s.Write(data);

        uint crc = ComputeCrc32(typeBytes, data);
        Span<byte> crcBuf = stackalloc byte[4];
        WriteBigEndianInt32(crcBuf, 0, (int)crc);
        s.Write(crcBuf);
    }

    private static void WriteBigEndianInt32(Span<byte> buf, int offset, int value)
    {
        buf[offset] = (byte)((value >> 24) & 0xFF);
        buf[offset + 1] = (byte)((value >> 16) & 0xFF);
        buf[offset + 2] = (byte)((value >> 8) & 0xFF);
        buf[offset + 3] = (byte)(value & 0xFF);
    }

    private static uint ComputeCrc32(byte[] typeBytes, byte[] data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in typeBytes)
            crc = UpdateCrcByte(crc, b);
        foreach (byte b in data)
            crc = UpdateCrcByte(crc, b);
        return ~crc;
    }

    private static uint UpdateCrcByte(uint crc, byte b)
    {
        crc ^= b;
        for (int k = 0; k < 8; k++)
            crc = (crc & 1) != 0 ? (0xEDB88320u ^ (crc >> 1)) : (crc >> 1);
        return crc;
    }

    private static void EnsureExecutablePermissions(string filePath)
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
            return;

        try
        {
            File.SetUnixFileMode(filePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        catch { }
    }

    private static void CodeSignBundle(string appBundlePath, string macosDir, string resourcesDir, AvalancheBuildSettings settings, Action<string> step)
    {
        if (!File.Exists("/usr/bin/codesign"))
            return;

        try
        {
            string? identity = null;
            bool useHardenedRuntime = false;

            if (settings.SigningMode == AvalancheSigningMode.AdHoc)
            {
                identity = "-";
            }
            else if (settings.SigningMode == AvalancheSigningMode.DeveloperId)
            {
                identity = !string.IsNullOrWhiteSpace(settings.SigningIdentity)
                    ? settings.SigningIdentity.Trim()
                    : FindDeveloperIdIdentityInKeychain();

                if (string.IsNullOrEmpty(identity))
                {
                    step("[Avalanche] Warning: No Apple Developer ID Application identity found in Keychain. Falling back to Ad-Hoc signature.");
                    identity = "-";
                }
                else
                {
                    useHardenedRuntime = true;
                }
            }
            else // Auto
            {
                string? discovered = !string.IsNullOrWhiteSpace(settings.SigningIdentity)
                    ? settings.SigningIdentity.Trim()
                    : FindDeveloperIdIdentityInKeychain();

                if (!string.IsNullOrEmpty(discovered))
                {
                    identity = discovered;
                    useHardenedRuntime = true;
                }
                else
                {
                    identity = "-";
                }
            }

            // Generate entitlements.plist with Hardened Runtime exemptions for .NET 8 JIT & native physics dylibs
            string entitlementsPath = Path.Combine(resourcesDir, "entitlements.plist");
            File.WriteAllText(entitlementsPath, GenerateEntitlementsXml(), Encoding.UTF8);

            // Inside-out code signing: sign all nested dylibs and binaries first
            foreach (string file in Directory.GetFiles(macosDir, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith("Bin", StringComparison.Ordinal))
                {
                    RunCodesign(file, identity, useHardenedRuntime, entitlementsPath);
                }
            }

            // The bundled .NET runtime runs as the game's process, so its code is signed the same way.
            foreach (string file in BundledRuntimeCodeFiles(resourcesDir))
                RunCodesign(file, identity, useHardenedRuntime, entitlementsPath);

            // Sign launcher script executable
            string launcherExe = Path.Combine(macosDir, SanitizeBundleExecutableName(settings.AppName));
            if (File.Exists(launcherExe))
            {
                RunCodesign(launcherExe, identity, useHardenedRuntime, entitlementsPath);
            }

            // Sign top-level .app bundle
            if (RunCodesign(appBundlePath, identity, useHardenedRuntime, entitlementsPath, deep: true))
            {
                if (useHardenedRuntime)
                {
                    step($"[Avalanche] Signed macOS .app bundle with Developer ID '{identity}' (Hardened Runtime + Entitlements)");
                }
                else
                {
                    step("[Avalanche] Signed macOS .app bundle with Ad-Hoc code signature");
                }
            }
            else if (identity == "-")
            {
                step("[Avalanche] ⚠️ Code signing failed. macOS may refuse to open this app. Run 'codesign --verify --deep --strict' on it to see why.");
            }
            else if (identity != "-")
            {
                // If Keychain access is restricted (e.g. sandboxed session) or certificate lacks private key access,
                // ensure the bundle is still sealed with an Ad-Hoc signature so Gatekeeper/LaunchServices accepts it.
                if (RunCodesign(appBundlePath, "-", hardenedRuntime: false, entitlementsPath, deep: true))
                {
                    step($"[Avalanche] Keychain identity '{identity}' unavailable in current session; sealed bundle with Ad-Hoc signature");
                }
            }
        }
        catch (Exception ex)
        {
            step($"[Avalanche] Code signing encountered non-fatal error: {ex.Message}");
        }
    }

    private static string? FindDeveloperIdIdentityInKeychain()
    {
        if (!OperatingSystem.IsMacOS() || !File.Exists("/usr/bin/security"))
            return null;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/usr/bin/security",
                Arguments = "find-identity -v -p codesigning",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return null;
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);

            // Only a "Developer ID Application" certificate lets Gatekeeper accept an app
            // outside the App Store. Apple Development and self-made certificates are
            // rejected, so without a Developer ID the caller signs ad-hoc instead.
            string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                int quoteStart = line.IndexOf('"');
                int quoteEnd = line.LastIndexOf('"');
                if (quoteStart >= 0 && quoteEnd > quoteStart)
                {
                    string idName = line.Substring(quoteStart + 1, quoteEnd - quoteStart - 1).Trim();
                    if (idName.StartsWith("Developer ID Application", StringComparison.OrdinalIgnoreCase))
                        return idName;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string GenerateEntitlementsXml()
    {
        return """
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
                <!-- Required for .NET 8 RyuJIT and tiered dynamic code generation -->
                <key>com.apple.security.cs.allow-jit</key>
                <true/>
                <key>com.apple.security.cs.allow-unsigned-executable-memory</key>
                <true/>
                <!-- Required for third-party native dylibs (libjoltc, libEOSSDK) signed by external vendors -->
                <key>com.apple.security.cs.disable-library-validation</key>
                <true/>
                <!-- Required for launcher dyld environment variable propagation -->
                <key>com.apple.security.cs.allow-dyld-environment-variables</key>
                <true/>
            </dict>
            </plist>
            """;
    }

    private static bool RunCodesign(string targetPath, string identity, bool hardenedRuntime, string? entitlementsPath, bool deep = false)
    {
        try
        {
            var args = new StringBuilder("--force ");
            if (deep)
            {
                args.Append("--deep ");
            }
            if (hardenedRuntime)
            {
                args.Append("--options runtime --timestamp ");
                if (!string.IsNullOrEmpty(entitlementsPath) && File.Exists(entitlementsPath))
                {
                    args.Append($"--entitlements \"{entitlementsPath}\" ");
                }
            }

            args.Append($"--sign \"{identity}\" \"{targetPath}\"");

            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "/usr/bin/codesign",
                Arguments = args.ToString(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            proc?.WaitForExit(5000);
            return proc is { ExitCode: 0 };
        }
        catch
        {
            return false;
        }
    }

    private static void CopyDirectoryRecursive(string sourceDir, string targetDir, bool skipBuildsFolder)
    {
        Directory.CreateDirectory(targetDir);
        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string name = Path.GetFileName(file);
            if (name == ".DS_Store") continue;
            File.Copy(file, Path.Combine(targetDir, name), overwrite: true);
        }

        foreach (string subDir in Directory.GetDirectories(sourceDir))
        {
            string dirName = Path.GetFileName(subDir);
            if (skipBuildsFolder && dirName.Equals("Builds", StringComparison.OrdinalIgnoreCase))
                continue;
            if (dirName.StartsWith('.'))
                continue;
            CopyDirectoryRecursive(subDir, Path.Combine(targetDir, dirName), skipBuildsFolder);
        }
    }

    private static long ComputeDirectorySize(string dir)
    {
        if (!Directory.Exists(dir)) return 0;
        long total = 0;
        foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
        {
            try { total += new FileInfo(f).Length; } catch { }
        }
        return total;
    }

    private static string ComputeFileHashSafe(string path)
    {
        try { return BlueAsset.ComputeFileHash(path); }
        catch { return ""; }
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes >= 1024 * 1024)
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        if (bytes >= 1024)
            return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }
}
