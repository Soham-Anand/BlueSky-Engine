using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Linq;

namespace BlueSky.Editor
{
    public class ProjectMetadata
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public DateTime LastOpened { get; set; }
    }

    public static class ProjectConfig
    {
        public static List<ProjectMetadata> RecentProjects { get; private set; } = new();

        private static string ConfigPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BlueSkyEngine",
            "recent_projects.json"
        );

        public static void Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    var projects = JsonSerializer.Deserialize<List<ProjectMetadata>>(json);
                    if (projects != null)
                    {
                        RecentProjects = projects.OrderByDescending(p => p.LastOpened).ToList();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load recent projects: {ex.Message}");
            }
        }

        public static void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(ConfigPath)!;
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = JsonSerializer.Serialize(RecentProjects, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save recent projects: {ex.Message}");
            }
        }

        public static void AddOrUpdateProject(string projectPath)
        {
            projectPath = Path.GetFullPath(projectPath);
            var existing = RecentProjects.FirstOrDefault(p => string.Equals(p.Path, projectPath, StringComparison.OrdinalIgnoreCase));
            
            if (existing != null)
            {
                existing.LastOpened = DateTime.Now;
            }
            else
            {
                RecentProjects.Add(new ProjectMetadata
                {
                    Name = Path.GetFileName(projectPath),
                    Path = projectPath,
                    LastOpened = DateTime.Now
                });
            }

            RecentProjects = RecentProjects.OrderByDescending(p => p.LastOpened).ToList();
            Save();
        }

        public static void RemoveProject(string projectPath)
        {
            RecentProjects.RemoveAll(p => string.Equals(p.Path, projectPath, StringComparison.OrdinalIgnoreCase));
            Save();
        }

        public static void ScanDesktopForProjects()
        {
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                // Scan depth 3 to avoid massive slowdowns
                ScanDirectory(desktop, 3);
                
                RecentProjects = RecentProjects.OrderByDescending(p => p.LastOpened).ToList();
                Save();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to scan desktop: {ex.Message}");
            }
        }

        private static void ScanDirectory(string root, int maxDepth, int currentDepth = 0)
        {
            if (currentDepth > maxDepth) return;

            try
            {
                // Is this a project?
                if (Directory.GetFiles(root, "*.BlueSkyProj").Length > 0)
                {
                    DiscoverProject(root);
                    return; // Don't scan inside a project
                }

                foreach (var dir in Directory.GetDirectories(root))
                {
                    ScanDirectory(dir, maxDepth, currentDepth + 1);
                }
            }
            catch (UnauthorizedAccessException) { /* ignore */ }
            catch (Exception) { /* ignore */ }
        }

        private static void DiscoverProject(string projectPath)
        {
            projectPath = Path.GetFullPath(projectPath);
            var existing = RecentProjects.FirstOrDefault(p => string.Equals(p.Path, projectPath, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                RecentProjects.Add(new ProjectMetadata
                {
                    Name = Path.GetFileName(projectPath),
                    Path = projectPath,
                    // Give it an old date so actively used ones stay on top
                    LastOpened = DateTime.MinValue 
                });
            }
        }
    }
}
