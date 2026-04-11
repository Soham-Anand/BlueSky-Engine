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
