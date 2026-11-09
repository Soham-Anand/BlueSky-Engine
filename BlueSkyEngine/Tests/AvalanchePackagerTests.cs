using System;
using BlueSky.Ramanujan;
using System.IO;
using BlueSky.Core.Assets;
using BlueSky.Meridian;
using BlueSky.Editor;
using BlueSky.SkyCore;
using BlueSky.SkyCore.Builtin;

namespace BlueSky.Tests;

/// <summary>
/// Verification suite for the Avalanche macOS (.app) Game Packager and AssetPathResolver.
/// </summary>
public static class AvalanchePackagerTests
{
    public static bool RunAllTests()
    {
        Console.WriteLine("\n--- Running Avalanche macOS Game Packager Tests ---");
        bool allPassed = true;

        allPassed &= TestBundleSanitizers();
        allPassed &= TestAssetPathResolverRelocation();
        allPassed &= TestBuildMacOSAppBundleEndToEnd();
        allPassed &= TestEntitlementsAndSigning();

        if (allPassed)
            Console.WriteLine("✓ All Avalanche macOS Packager tests passed!");
        else
            Console.WriteLine("✗ Some Avalanche macOS Packager tests failed!");

        return allPassed;
    }

    private static bool TestBundleSanitizers()
    {
        string exe = AvalanchePackager.SanitizeBundleExecutableName("My Cool Racer! 2026");
        if (exe != "My_Cool_Racer_2026")
        {
            Console.WriteLine($"  ✗ SanitizeBundleExecutableName unexpected output: {exe}");
            return false;
        }

        string bundleId = AvalanchePackager.SanitizeBundleIdentifier("", "BlueSky Drift");
        if (bundleId != "com.bluesky.bluesky-drift")
        {
            Console.WriteLine($"  ✗ SanitizeBundleIdentifier unexpected output: {bundleId}");
            return false;
        }

        byte[] iconPng = AvalanchePackager.SynthesizeAvalancheIconPng(64, 64);
        if (iconPng.Length < 128 || iconPng[0] != 137 || iconPng[1] != 80 || iconPng[2] != 78 || iconPng[3] != 71)
        {
            Console.WriteLine("  ✗ SynthesizeAvalancheIconPng did not produce a valid PNG header");
            return false;
        }

        Console.WriteLine("  ✓ Bundle sanitizers & procedural PNG icon synthesizer verified");
        return true;
    }

    private static bool TestAssetPathResolverRelocation()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"avalanche_resolver_{Guid.NewGuid():N}");
        string assetsDir = Path.Combine(tempRoot, "Assets", "Vehicles");
        Directory.CreateDirectory(assetsDir);

        try
        {
            string actualFile = Path.Combine(assetsDir, "Skyline.blueskyasset");
            File.WriteAllText(actualFile, "{}");

            AssetPathResolver.RegisterSearchRoot(tempRoot);

            // Simulate a path saved on a different developer machine
            string relocatedInput = "/Users/another_dev/Documents/OldProject/Assets/Vehicles/Skyline.blueskyasset";
            string resolved = AssetPathResolver.Resolve(relocatedInput);

            if (!File.Exists(resolved) || !string.Equals(Path.GetFullPath(resolved), Path.GetFullPath(actualFile), StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"  ✗ AssetPathResolver failed to resolve relocated path: got '{resolved}'");
                return false;
            }

            Console.WriteLine("  ✓ AssetPathResolver machine-relocated '/Assets/...' path resolution verified");
            return true;
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static bool TestBuildMacOSAppBundleEndToEnd()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"avalanche_build_{Guid.NewGuid():N}");
        string fakeProjDir = Path.Combine(tempRoot, "SampleProject");
        string fakeAssetsDir = Path.Combine(fakeProjDir, "Assets");
        string outDir = Path.Combine(tempRoot, "Builds", "MacOS");

        Directory.CreateDirectory(fakeAssetsDir);

        try
        {
            // Create a sample .tea script and mesh asset inside the fake project
            string scriptPath = Path.Combine(fakeAssetsDir, "drive_test.tea");
            File.WriteAllText(scriptPath, "fn start() { print(\"Avalanche Ready\"); }\nfn update() {}\n");

            string meshPath = Path.Combine(fakeAssetsDir, "TestCube.blueskyasset");
            var dummyAsset = new BlueAsset
            {
                AssetName = "TestCube",
                Type = AssetType.Mesh,
                PayloadData = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 }
            };
            dummyAsset.Metadata["strataSlot0"] = Path.Combine(fakeAssetsDir, "Paint.stratamat");
            File.WriteAllText(Path.Combine(fakeAssetsDir, "Paint.stratamat"), "STRATA");
            dummyAsset.Save(meshPath);

            // Create a World with an entity referencing absolute paths to verify relativization
            using var world = new World();
            var e = world.CreateEntity();
            world.AddComponent(e, new TransformComponent
            {
                Position = new BlueSky.Ramanujan.Vector3(1f, 2f, 3f),
                Rotation = BlueSky.Ramanujan.Quaternion.Identity,
                Scale = BlueSky.Ramanujan.Vector3.One
            });
            world.AddComponent(e, new StaticMeshComponent { MeshAssetId = meshPath });
            world.AddComponent(e, new TeaScriptComponent { ScriptAssetId = scriptPath, IsEnabled = true });

            string scenePath = Path.Combine(fakeAssetsDir, "MainTrack.bsscene");
            var sceneData = SceneConverter.WorldToSceneData(world, "MainTrack");
            SceneSerializer.SaveScene(sceneData, scenePath);

            var settings = new AvalancheBuildSettings
            {
                AppName = "Avalanche Test Racer",
                BundleIdentifier = "com.bluesky.testracer",
                Version = "1.2.0",
                ProjectDirectory = fakeProjDir,
                OutputDirectory = outDir,
                StartupScenePath = scenePath,
                WindowWidth = 1600,
                WindowHeight = 900,
                Fullscreen = false,
                VSync = true,
                ShowDiagnosticsHud = false,
                AdHocCodeSign = false
            };

            var report = AvalanchePackager.BuildMacOSApp(settings, world);
            if (!report.Success)
            {
                Console.WriteLine($"  ✗ AvalanchePackager.BuildMacOSApp failed: {report.ErrorMessage}");
                return false;
            }

            if (!Directory.Exists(report.AppBundlePath) || !report.AppBundlePath.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"  ✗ App bundle directory missing: {report.AppBundlePath}");
                return false;
            }

            string infoPlist = Path.Combine(report.AppBundlePath, "Contents", "Info.plist");
            if (!File.Exists(infoPlist) || !File.ReadAllText(infoPlist).Contains("com.bluesky.testracer"))
            {
                Console.WriteLine("  ✗ Contents/Info.plist missing or invalid");
                return false;
            }

            if (!File.Exists(report.ExecutablePath))
            {
                Console.WriteLine($"  ✗ Launcher executable missing: {report.ExecutablePath}");
                return false;
            }

            var loadedManifest = AvalancheManifest.Load(report.ManifestPath);
            if (loadedManifest == null ||
                loadedManifest.AppName != "Avalanche Test Racer" ||
                loadedManifest.WindowWidth != 1600 ||
                loadedManifest.WindowHeight != 900 ||
                loadedManifest.StartupScene != "Assets/MainTrack.bsscene")
            {
                Console.WriteLine("  ✗ Contents/Resources/avalanche.json manifest mismatch");
                return false;
            }

            // Verify that packaged scene paths were rewritten to portable relative 'Assets/...' paths
            string bundledSceneFile = Path.Combine(report.GameDataDirectory, "Assets", "MainTrack.bsscene");
            string bundledSceneJson = File.ReadAllText(bundledSceneFile);
            if (bundledSceneJson.Contains(fakeProjDir, StringComparison.OrdinalIgnoreCase) ||
                !bundledSceneJson.Contains("Assets/TestCube.blueskyasset") ||
                !bundledSceneJson.Contains("Assets/drive_test.tea"))
            {
                Console.WriteLine("  ✗ Packaged scene JSON still contains absolute developer paths or missing relative paths");
                return false;
            }

            // Verify that .blueskyasset strataSlot0 metadata was also relativized
            var bundledAssetHeader = BlueAsset.LoadHeader(Path.Combine(report.GameDataDirectory, "Assets", "TestCube.blueskyasset"));
            if (bundledAssetHeader == null ||
                !bundledAssetHeader.Metadata.TryGetValue("strataSlot0", out string? slotLink) ||
                slotLink != "Assets/Paint.stratamat")
            {
                Console.WriteLine($"  ✗ Packaged .blueskyasset strataSlot0 link was not relativized: '{ bundledAssetHeader?.Metadata.GetValueOrDefault("strataSlot0") }'");
                return false;
            }

            Console.WriteLine($"  ✓ End-to-end macOS .app bundle creation, manifest, shaders, and asset path relativization verified ({AvalanchePackager.FormatBytes(report.TotalBundleBytes)})");
            return true;
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static bool TestEntitlementsAndSigning()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"avalanche_sign_{Guid.NewGuid():N}");
        string fakeProjDir = Path.Combine(tempRoot, "SignProject");
        string fakeAssetsDir = Path.Combine(fakeProjDir, "Assets");
        string outDir = Path.Combine(tempRoot, "Builds", "MacOS");
        Directory.CreateDirectory(fakeAssetsDir);

        try
        {
            var settings = new AvalancheBuildSettings
            {
                AppName = "SignTestApp",
                BundleIdentifier = "com.bluesky.signtest",
                Version = "1.0.0",
                ProjectDirectory = fakeProjDir,
                OutputDirectory = outDir,
                SigningMode = AvalancheSigningMode.DeveloperId,
                SigningIdentity = "Developer ID Application: Fake Identity"
            };

            var report = AvalanchePackager.BuildMacOSApp(settings);
            if (!report.Success)
            {
                Console.WriteLine($"  ✗ BuildMacOSApp failed for signing test: {report.ErrorMessage}");
                return false;
            }

            string entitlementsPath = Path.Combine(report.AppBundlePath, "Contents", "Resources", "entitlements.plist");
            if (!File.Exists(entitlementsPath))
            {
                Console.WriteLine("  ✗ Contents/Resources/entitlements.plist was not generated");
                return false;
            }

            string xml = File.ReadAllText(entitlementsPath);
            if (!xml.Contains("com.apple.security.cs.allow-jit") ||
                !xml.Contains("com.apple.security.cs.disable-library-validation") ||
                !xml.Contains("com.apple.security.cs.allow-unsigned-executable-memory") ||
                !xml.Contains("com.apple.security.cs.allow-dyld-environment-variables"))
            {
                Console.WriteLine("  ✗ entitlements.plist missing required Hardened Runtime exemptions");
                return false;
            }

            Console.WriteLine("  ✓ Developer ID entitlements generation and Hardened Runtime fallback verified");
            return true;
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }
}
