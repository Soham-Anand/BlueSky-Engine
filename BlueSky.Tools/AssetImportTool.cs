using BlueSky.Core.Assets;
using BlueSky.Core.Diagnostics;

namespace BlueSky.Tools;

/// <summary>
/// Command-line tool for importing assets into BlueSky projects.
/// Usage: AssetImportTool <project.blueproject> <source_file_or_directory>
/// </summary>
public class AssetImportTool
{
    public static void Main(string[] args)
    {
        Console.WriteLine("=== BlueSky Asset Import Tool ===\n");

        if (args.Length < 2)
        {
            PrintUsage();
            return;
        }

        var projectPath = args[0];
        var sourcePath = args[1];

        if (!File.Exists(projectPath))
        {
            Console.WriteLine($"✗ Project file not found: {projectPath}");
            return;
        }

        try
        {
            var importer = new AssetImporter(projectPath);

            if (Directory.Exists(sourcePath))
            {
                // Import entire directory
                ImportDirectory(importer, sourcePath);
            }
            else if (File.Exists(sourcePath))
            {
                // Import single file
                ImportFile(importer, sourcePath);
            }
            else
            {
                Console.WriteLine($"✗ Source not found: {sourcePath}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error: {ex.Message}");
        }
    }

    private static void ImportFile(AssetImporter importer, string filePath)
    {
        Console.WriteLine($"Importing: {Path.GetFileName(filePath)}");
        
        var asset = importer.Import(filePath);
        
        if (asset != null)
        {
            Console.WriteLine($"✓ Success! Asset ID: {asset.AssetId}");
            Console.WriteLine($"  Type: {asset.Type}");
            Console.WriteLine($"  Data: {asset.DataFile}");
        }
        else
        {
            Console.WriteLine("✗ Import failed");
        }
    }

    private static void ImportDirectory(AssetImporter importer, string directoryPath)
    {
        Console.WriteLine($"Scanning directory: {directoryPath}\n");

        var supportedExtensions = new[]
        {
            ".fbx", ".obj", ".gltf", ".glb", ".dae", ".blend",
            ".png", ".jpg", ".jpeg", ".tga", ".bmp", ".hdr",
            ".bluescript", ".cs"
        };

        var files = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories)
            .Where(f => supportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToList();

        Console.WriteLine($"Found {files.Count} importable files\n");

        var assets = importer.ImportBatch(files);

        Console.WriteLine($"\n=== Import Summary ===");
        Console.WriteLine($"Total: {files.Count}");
        Console.WriteLine($"Success: {assets.Count}");
        Console.WriteLine($"Failed: {files.Count - assets.Count}");

        // Group by type
        var byType = assets.GroupBy(a => a.Type);
        Console.WriteLine("\nBy Type:");
        foreach (var group in byType)
        {
            Console.WriteLine($"  {group.Key}: {group.Count()}");
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  AssetImportTool <project.blueproject> <source_file_or_directory>");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  AssetImportTool MyGame.blueproject Assets/Models/Character.fbx");
        Console.WriteLine("  AssetImportTool MyGame.blueproject Assets/");
        Console.WriteLine();
        Console.WriteLine("Supported formats:");
        Console.WriteLine("  Meshes: FBX, OBJ, glTF, GLB, DAE, Blend");
        Console.WriteLine("  Textures: PNG, JPG, TGA, BMP, HDR");
        Console.WriteLine("  Scripts: .bluescript, .cs");
    }
}

/// <summary>
/// Interactive asset browser for exploring imported assets.
/// </summary>
public class AssetBrowserTool
{
    public static void Browse(string projectPath)
    {
        Console.WriteLine("=== BlueSky Asset Browser ===\n");

        if (!File.Exists(projectPath))
        {
            Console.WriteLine($"✗ Project file not found: {projectPath}");
            return;
        }

        try
        {
            var database = new AssetDatabase(projectPath, watchForChanges: false);
            
            database.PrintStats();

            Console.WriteLine("\n=== Recent Assets ===");
            var recentAssets = database.AllAssets
                .OrderByDescending(a => a.Asset.ImportDate)
                .Take(10);

            foreach (var entry in recentAssets)
            {
                var asset = entry.Asset;
                Console.WriteLine($"\n{asset.AssetName}");
                Console.WriteLine($"  Type: {asset.Type}");
                Console.WriteLine($"  ID: {asset.AssetId}");
                Console.WriteLine($"  Source: {Path.GetFileName(asset.SourceFile)}");
                Console.WriteLine($"  Imported: {asset.ImportDate:yyyy-MM-dd HH:mm}");
                
                if (asset.Metadata.Count > 0)
                {
                    Console.WriteLine("  Metadata:");
                    foreach (var kvp in asset.Metadata.Take(3))
                    {
                        Console.WriteLine($"    {kvp.Key}: {kvp.Value}");
                    }
                }
            }

            database.Dispose();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error: {ex.Message}");
        }
    }
}
