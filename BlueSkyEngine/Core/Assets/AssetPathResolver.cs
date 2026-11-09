using System;
using System.Collections.Generic;
using System.IO;

namespace BlueSky.Core.Assets;

/// <summary>
/// Resolves asset, scene, prefab, terrain, physics, and script paths across both
/// the development Editor environment and packaged Avalanche (.app) bundles.
/// </summary>
public static class AssetPathResolver
{
    private static readonly List<string> _extraSearchRoots = new();

    /// <summary>
    /// Registers an additional root directory (such as a packaged GameData folder)
    /// to search when resolving relative or relocated asset paths.
    /// </summary>
    public static void RegisterSearchRoot(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
            return;

        try
        {
            string full = Path.GetFullPath(rootDirectory);
            if (Directory.Exists(full) && !_extraSearchRoots.Contains(full, StringComparer.OrdinalIgnoreCase))
            {
                _extraSearchRoots.Insert(0, full);
            }
        }
        catch
        {
            // Ignore malformed path roots
        }
    }

    /// <summary>
    /// Resolves an asset path to an existing file on disk if possible.
    /// Handles direct paths, cwd-relative paths, bundle GameData paths, and
    /// relocated developer paths containing an "/Assets/" segment.
    /// </summary>
    public static string Resolve(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return string.Empty;

        string trimmed = rawPath.Trim();

        // 1. Direct hit
        if (File.Exists(trimmed))
            return trimmed;

        // Normalize directory separators for cross-platform scenes
        string normalized = trimmed.Replace('\\', Path.DirectorySeparatorChar)
                                   .Replace('/', Path.DirectorySeparatorChar);
        if (File.Exists(normalized))
            return normalized;

        // Gather candidate roots: CurrentDirectory, registered roots, and macOS .app Resources/GameData
        Span<string> baseRoots = new string[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory,
            Path.Combine(AppContext.BaseDirectory, "..", "Resources", "GameData"),
            Path.Combine(AppContext.BaseDirectory, "..", "Resources")
        };

        // 2. If relative path, probe under candidate roots
        if (!Path.IsPathRooted(normalized))
        {
            foreach (string root in _extraSearchRoots)
            {
                string cand = Path.Combine(root, normalized);
                if (File.Exists(cand))
                    return Path.GetFullPath(cand);
            }

            foreach (string root in baseRoots)
            {
                if (string.IsNullOrEmpty(root)) continue;
                try
                {
                    string cand = Path.Combine(root, normalized);
                    if (File.Exists(cand))
                        return Path.GetFullPath(cand);
                }
                catch { }
            }
        }

        // 3. Extract relative suffix starting from "Assets/" or "Config/" if path was saved as an absolute path on another machine/folder
        string? assetsRel = ExtractSubPathFromMarker(normalized, "Assets");
        if (!string.IsNullOrEmpty(assetsRel))
        {
            foreach (string root in _extraSearchRoots)
            {
                string cand = Path.Combine(root, assetsRel);
                if (File.Exists(cand))
                    return Path.GetFullPath(cand);
            }

            foreach (string root in baseRoots)
            {
                if (string.IsNullOrEmpty(root)) continue;
                try
                {
                    string cand = Path.Combine(root, assetsRel);
                    if (File.Exists(cand))
                        return Path.GetFullPath(cand);
                }
                catch { }
            }
        }

        // 4. Check by filename inside <Root>/Assets and <Root>/Assets/Bundled
        string fileName = Path.GetFileName(normalized);
        if (!string.IsNullOrEmpty(fileName))
        {
            foreach (string root in _extraSearchRoots)
            {
                string c1 = Path.Combine(root, "Assets", fileName);
                if (File.Exists(c1)) return Path.GetFullPath(c1);

                string c2 = Path.Combine(root, "Assets", "Bundled", fileName);
                if (File.Exists(c2)) return Path.GetFullPath(c2);
            }

            foreach (string root in baseRoots)
            {
                if (string.IsNullOrEmpty(root)) continue;
                try
                {
                    string c1 = Path.Combine(root, "Assets", fileName);
                    if (File.Exists(c1)) return Path.GetFullPath(c1);

                    string c2 = Path.Combine(root, "Assets", "Bundled", fileName);
                    if (File.Exists(c2)) return Path.GetFullPath(c2);

                    string c3 = Path.Combine(root, fileName);
                    if (File.Exists(c3)) return Path.GetFullPath(c3);
                }
                catch { }
            }
        }

        return trimmed;
    }

    private static string? ExtractSubPathFromMarker(string path, string markerFolder)
    {
        char sep = Path.DirectorySeparatorChar;
        string token = $"{sep}{markerFolder}{sep}";
        int idx = path.LastIndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            return path[(idx + 1)..];
        }
        return null;
    }
}
