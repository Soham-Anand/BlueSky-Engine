using System;
using System.IO;

namespace BlueSky.Editor
{
    public static class ProjectManager
    {
        public static string CurrentProjectDir { get; private set; } = "";
        public static string AssetsDir => string.IsNullOrEmpty(CurrentProjectDir) ? "" : Path.Combine(CurrentProjectDir, "Assets");

        public static bool TryCreateProject(string dirPath)
        {
            try
            {
                if (!Directory.Exists(dirPath))
                {
                    Directory.CreateDirectory(dirPath);
                }

                // Create the .BlueSkyProj file
                string projFile = Path.Combine(dirPath, Path.GetFileName(dirPath) + ".BlueSkyProj");
                File.WriteAllText(projFile, "{ \"version\": \"1.0\" }");

                // Create Assets folder
                string assets = Path.Combine(dirPath, "Assets");
                if (!Directory.Exists(assets))
                {
                    Directory.CreateDirectory(assets);
                }

                // Create Config folder and EOS.ini template
                string configDir = Path.Combine(dirPath, "Config");
                if (!Directory.Exists(configDir))
                {
                    Directory.CreateDirectory(configDir);
                }

                string eosIni = Path.Combine(configDir, "EOS.ini");
                if (!File.Exists(eosIni))
                {
                    File.WriteAllText(eosIni, 
                        "[EOS]\n" +
                        "ProductId=\n" +
                        "SandboxId=\n" +
                        "DeploymentId=\n" +
                        "ClientId=\n" +
                        "ClientSecret=\n" +
                        "ArtifactId=\n" +
                        "ProductName=BlueSky Engine\n" +
                        "ProductVersion=0.1.0\n");
                }

                string eosGuide = Path.Combine(configDir, "eos_setup_guide.md");
                if (!File.Exists(eosGuide))
                {
                    File.WriteAllText(eosGuide,
                        "# Epic Online Services (EOS) Authentication Setup Guide\n\n" +
                        "This guide explains how to set up Epic Online Services (EOS) for BlueSky Engine, configure your application settings in the Epic Games Developer Portal, and enable real browser-based OAuth login.\n\n" +
                        "1. Navigate to the Epic Games Developer Portal: https://dev.epicgames.com/portal/\n" +
                        "2. Under Product Settings -> Clients, create a client.\n" +
                        "3. Add the Redirect URI: http://localhost:8080/\n" +
                        "4. Copy your credentials into Config/EOS.ini or configure them as environment variables.\n");
                }

                CurrentProjectDir = dirPath;
                ProjectConfig.AddOrUpdateProject(dirPath);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating project: {ex.Message}");
                return false;
            }
        }

        public static bool TryOpenProject(string dirPath)
        {
            try
            {
                if (!Directory.Exists(dirPath)) return false;

                // Validate .BlueSkyProj exists
                bool hasProj = Directory.GetFiles(dirPath, "*.BlueSkyProj").Length > 0;
                if (!hasProj) return false;

                CurrentProjectDir = dirPath;
                ProjectConfig.AddOrUpdateProject(dirPath);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error opening project: {ex.Message}");
                return false;
            }
        }
    }
}
