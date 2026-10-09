using System;
using System.IO;
using BlueSky.Networking;

namespace BlueSky.Runtime;

/// <summary>
/// BlueSky Runtime - Standalone game execution without editor.
/// This is the entry point for "Play Standalone" mode and built games.
/// </summary>
class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("=== BlueSky Runtime Starting ===");
        Console.WriteLine($"Arguments: {string.Join(" ", args)}");

        if (Array.Exists(args, a => a.Equals("--test-physics", StringComparison.OrdinalIgnoreCase)))
        {
            bool testResult = BlueSky.Tests.PhysicsVehicleTests.RunAllTests();
            Environment.Exit(testResult ? 0 : 1);
            return;
        }

        try
        {
            // Auto-detect project root and change working directory to it
            AutoDetectAndSetProjectDirectory(args);

            // Parse command line arguments
            var config = ParseArguments(args);
            
            // Initialize runtime
            var runtime = new GameRuntime(config);
            
            // Run the game (blocks until quit)
            runtime.Run();
            
            Console.WriteLine("=== BlueSky Runtime Stopped ===");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FATAL ERROR: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Environment.Exit(1);
        }
    }

    static void AutoDetectAndSetProjectDirectory(string[] args)
    {
        string scenePath = "";
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--scene" && i + 1 < args.Length)
            {
                scenePath = args[i + 1];
                break;
            }
        }

        string projectDir = "";

        bool IsProjectRoot(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return false;

            if (Directory.GetFiles(dir, "*.BlueSkyProj").Length > 0)
                return true;

            if (Directory.Exists(Path.Combine(dir, "Assets")) && 
                Directory.Exists(Path.Combine(dir, "Config")))
                return true;

            return false;
        }

        string FindRoot(string startDir)
        {
            if (string.IsNullOrEmpty(startDir))
                return "";

            try
            {
                var dir = Path.GetFullPath(startDir);
                while (dir != null)
                {
                    if (IsProjectRoot(dir))
                        return dir;
                    dir = Path.GetDirectoryName(dir);
                }
            }
            catch {}
            return "";
        }

        // 1. Try from scenePath parent
        if (!string.IsNullOrEmpty(scenePath))
        {
            projectDir = FindRoot(scenePath);
        }

        // 2. Try from current directory
        if (string.IsNullOrEmpty(projectDir))
        {
            projectDir = FindRoot(Directory.GetCurrentDirectory());
        }

        // 3. Try from app base directory
        if (string.IsNullOrEmpty(projectDir))
        {
            projectDir = FindRoot(AppDomain.CurrentDomain.BaseDirectory);
        }

        if (!string.IsNullOrEmpty(projectDir))
        {
            try
            {
                Directory.SetCurrentDirectory(projectDir);
                Console.WriteLine($"[Runtime] Changed working directory to project root: {projectDir}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Runtime] ⚠️ Failed to set working directory to {projectDir}: {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine("[Runtime] ⚠️ Could not auto-detect project root directory!");
        }
    }
    
    static RuntimeConfig ParseArguments(string[] args)
    {
        var config = new RuntimeConfig();
        
        // Find --scene first to locate the project config
        string scenePath = "";
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--scene" && i + 1 < args.Length)
            {
                scenePath = args[i + 1];
                break;
            }
        }

        string projDir = FindProjectDir(scenePath);
        config.Multiplayer.Eos = EosCredentials.LoadForProject(projDir);
        
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--scene":
                    if (i + 1 < args.Length)
                        config.ScenePath = args[++i];
                    break;
                    
                case "--mode":
                    if (i + 1 < args.Length)
                    {
                        config.Mode = args[++i];
                        // --mode also sets the multiplayer mode if the value matches
                        if (Enum.TryParse<MultiplayerMode>(config.Mode, true, out var modeParsed))
                            config.Multiplayer.Mode = modeParsed;
                    }
                    break;

                case "--multiplayer":
                    if (i + 1 < args.Length && Enum.TryParse<MultiplayerMode>(args[++i], true, out var parsedMode))
                        config.Multiplayer.Mode = parsedMode;
                    break;

                case "--session-name":
                    if (i + 1 < args.Length)
                        config.Multiplayer.SessionName = args[++i];
                    break;

                case "--join-session":
                    if (i + 1 < args.Length)
                        config.Multiplayer.JoinSessionId = args[++i];
                    break;

                case "--player":
                case "--player-name":
                    if (i + 1 < args.Length)
                        config.Multiplayer.LocalPlayerName = args[++i];
                    break;

                case "--eos-product-id":
                    if (i + 1 < args.Length)
                        config.Multiplayer.Eos.ProductId = args[++i];
                    break;

                case "--eos-sandbox-id":
                    if (i + 1 < args.Length)
                        config.Multiplayer.Eos.SandboxId = args[++i];
                    break;

                case "--eos-deployment-id":
                    if (i + 1 < args.Length)
                        config.Multiplayer.Eos.DeploymentId = args[++i];
                    break;

                case "--eos-client-id":
                    if (i + 1 < args.Length)
                        config.Multiplayer.Eos.ClientId = args[++i];
                    break;

                case "--eos-client-secret":
                    if (i + 1 < args.Length)
                        config.Multiplayer.Eos.ClientSecret = args[++i];
                    break;

                case "--eos-artifact-id":
                    if (i + 1 < args.Length)
                        config.Multiplayer.Eos.ArtifactId = args[++i];
                    break;

                case "--eos-product-name":
                    if (i + 1 < args.Length)
                        config.Multiplayer.Eos.ProductName = args[++i];
                    break;

                case "--eos-product-version":
                    if (i + 1 < args.Length)
                        config.Multiplayer.Eos.ProductVersion = args[++i];
                    break;
                    
                case "--width":
                    if (i + 1 < args.Length)
                        config.WindowWidth = int.Parse(args[++i]);
                    break;
                    
                case "--height":
                    if (i + 1 < args.Length)
                        config.WindowHeight = int.Parse(args[++i]);
                    break;
                    
                case "--fullscreen":
                    config.Fullscreen = true;
                    break;
                    
                case "--vsync":
                    config.VSync = true;
                    break;
                    
                case "--help":
                    PrintHelp();
                    Environment.Exit(0);
                    break;
            }
        }
        
        return config;
    }
    
    static void PrintHelp()
    {
        Console.WriteLine("BlueSky Runtime - Standalone Game Execution");
        Console.WriteLine();
        Console.WriteLine("Usage: BlueSkyRuntime [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --scene <path>        Path to scene file (.bsscene)");
        Console.WriteLine("  --scene <path>        Path to scene file (.bsscene)");
        Console.WriteLine("  --mode <mode>         Window title mode (standalone, build)");
        Console.WriteLine("  --multiplayer <mode>  offline, host, or join (default: host)");
        Console.WriteLine("  --width <pixels>      Window width (default: 1280)");
        Console.WriteLine("  --height <pixels>     Window height (default: 720)");
        Console.WriteLine("  --fullscreen          Start in fullscreen mode");
        Console.WriteLine("  --vsync               Enable VSync");
        Console.WriteLine("  --session-name <id>   EOS lobby/session name");
        Console.WriteLine("  --join-session <id>   Join an existing session id");
        Console.WriteLine("  --player <name>       Local player display name (alias for --player-name)");
        Console.WriteLine("  --player-name <name>  Local player display name");
        Console.WriteLine("  --eos-product-id <id> EOS product id");
        Console.WriteLine("  --eos-sandbox-id <id> EOS sandbox id");
        Console.WriteLine("  --eos-deployment-id <id> EOS deployment id");
        Console.WriteLine("  --eos-client-id <id> EOS client id");
        Console.WriteLine("  --eos-client-secret <secret> EOS client secret");
        Console.WriteLine("  --help                Show this help");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine("  BlueSkyRuntime --scene game.bsscene --player Host");
        Console.WriteLine("  BlueSkyRuntime --scene game.bsscene --multiplayer join --player Client");
    }

    static string FindProjectDir(string scenePath)
    {
        if (string.IsNullOrEmpty(scenePath))
            return "";

        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(scenePath));
            while (dir != null)
            {
                if (Directory.GetFiles(dir, "*.BlueSkyProj").Length > 0)
                    return dir;

                if (Directory.Exists(Path.Combine(dir, "Assets")) && 
                    Directory.Exists(Path.Combine(dir, "Config")))
                    return dir;

                dir = Path.GetDirectoryName(dir);
            }
        }
        catch (Exception)
        {
            // Ignore path resolution errors
        }
        return "";
    }
}

/// <summary>
/// Runtime configuration parsed from command line arguments
/// </summary>
public class RuntimeConfig
{
    public string ScenePath { get; set; } = "";
    public string Mode { get; set; } = "standalone";
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 720;
    public bool Fullscreen { get; set; } = false;
    public bool VSync { get; set; } = true;
    public MultiplayerConfig Multiplayer { get; set; } = new();
}
