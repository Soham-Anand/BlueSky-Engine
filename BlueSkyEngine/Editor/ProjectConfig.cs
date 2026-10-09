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
        private static Task<List<ProjectMetadata>>? _desktopScanTask;

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

        public static void StartDesktopProjectScan()
        {
            _desktopScanTask ??= Task.Run(ScanDesktopForProjects);
        }

        /// <summary>Applies background scan results on the caller thread once ready.</summary>
        public static void ApplyCompletedDesktopProjectScan()
        {
            var scan = _desktopScanTask;
            if (scan == null || !scan.IsCompleted)
                return;

            _desktopScanTask = null;
            try
            {
                if (!scan.IsCompletedSuccessfully)
                {
                    if (scan.Exception != null)
                        Console.WriteLine($"Failed to scan desktop: {scan.Exception.GetBaseException().Message}");
                    return;
                }

                bool added = false;
                foreach (var project in scan.Result)
                {
                    bool exists = RecentProjects.Any(p => string.Equals(p.Path, project.Path, StringComparison.OrdinalIgnoreCase));
                    if (!exists)
                    {
                        RecentProjects.Add(project);
                        added = true;
                    }
                }

                RecentProjects = RecentProjects.OrderByDescending(p => p.LastOpened).ToList();
                if (added)
                    Save();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to apply desktop project scan: {ex.Message}");
            }
        }

        private static List<ProjectMetadata> ScanDesktopForProjects()
        {
            var discovered = new List<ProjectMetadata>();
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                if (!string.IsNullOrWhiteSpace(desktop) && Directory.Exists(desktop))
                    ScanDirectory(desktop, 3, discovered);
            }

            catch (Exception ex)
            {
                Console.WriteLine($"Failed to scan desktop: {ex.Message}");
            }

            return discovered;
        }

        private static void ScanDirectory(string root, int maxDepth, List<ProjectMetadata> discovered, int currentDepth = 0)
        {
            if (currentDepth > maxDepth) return;

            try
            {
                // Stop descending once a project root has been found.
                if (Directory.EnumerateFiles(root, "*.BlueSkyProj").Any())
                {
                    string projectPath = Path.GetFullPath(root);
                    discovered.Add(new ProjectMetadata
                    {
                        Name = Path.GetFileName(projectPath),
                        Path = projectPath,
                        LastOpened = DateTime.MinValue
                    });
                    return;
                }

                foreach (var dir in Directory.EnumerateDirectories(root))
                    ScanDirectory(dir, maxDepth, discovered, currentDepth + 1);
            }
            catch (UnauthorizedAccessException) { /* ignore inaccessible branches */ }
            catch (DirectoryNotFoundException) { /* ignore folders removed mid-scan */ }
            catch (IOException) { /* ignore filesystem errors in one branch */ }
        }
    }
}
