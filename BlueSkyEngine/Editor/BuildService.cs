using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Text;

namespace BlueSky.Editor
{
    public class BuildService
    {
        public static async Task BuildProjectAsync(string projectPath, string targetOs, string rid, string configuration)
        {
            await Task.Run(() =>
            {
                try
                {
                    string runtimeProjectPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../../BlueSky.Runtime/BlueSky.Runtime.csproj"));
                    
                    if (!File.Exists(runtimeProjectPath))
                    {
                        runtimeProjectPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../BlueSky.Runtime/BlueSky.Runtime.csproj"));
                    }

                    string projectName = new DirectoryInfo(projectPath).Name;
                    string buildRoot = Path.Combine(projectPath, "Builds");
                    string outputDir = Path.Combine(buildRoot, rid); // Group by RID for clarity
                    
                    if (Directory.Exists(outputDir))
                        Directory.Delete(outputDir, true);

                    Directory.CreateDirectory(outputDir);

                    // Use Single File Publish and Self Contained with Native Library Extraction
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "dotnet",
                        Arguments = $"publish \"{runtimeProjectPath}\" -c {configuration} -r {rid} /p:PublishSingleFile=true /p:SelfContained=true /p:IncludeNativeLibrariesForSelfExtract=true /p:IncludeAllContentForSelfExtract=true -o \"{outputDir}/publish_tmp\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using Process? process = Process.Start(psi);
                    if (process != null)
                    {
                        var stdout = process.StandardOutput.ReadToEndAsync();
                        var stderr = process.StandardError.ReadToEndAsync();
                        process.WaitForExit();

                        string buildOutput = stdout.Result;
                        string buildErrors = stderr.Result;
                        
                        if (process.ExitCode != 0)
                        {
                            Debug.WriteLine($"Build failed with exit code {process.ExitCode}");
                            Debug.WriteLine($"Build output: {buildOutput}");
                            Debug.WriteLine($"Build errors: {buildErrors}");
                            return;
                        }
                        
                        Debug.WriteLine($"Build succeeded for {rid}");

                        string publishTmp = Path.Combine(outputDir, "publish_tmp");
                        string binaryName = "BlueSky.Runtime";
                        if (rid.StartsWith("win")) binaryName += ".exe";

                        string sourceBinary = Path.Combine(publishTmp, binaryName);

                        if (rid.StartsWith("osx"))
                        {
                            // Create .app Bundle Structure
                            string appBundle = Path.Combine(outputDir, $"{projectName}.app");
                            string contentsDir = Path.Combine(appBundle, "Contents");
                            string macOsDir = Path.Combine(contentsDir, "MacOS");
                            string resourcesDir = Path.Combine(contentsDir, "Resources");

                            Directory.CreateDirectory(macOsDir);
                            Directory.CreateDirectory(resourcesDir);

                            // Move Binary
                            string targetBinary = Path.Combine(macOsDir, projectName);
                            if (File.Exists(sourceBinary))
                                File.Move(sourceBinary, targetBinary);

                            // Create Info.plist
                            string plistContent = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE plist PUBLIC ""Apple//DTD PLIST 1.0//EN"" ""http://www.apple.com/DTDs/PropertyList-1.0.dtd"">
<plist version=""1.0"">
<dict>
    <key>CFBundleExecutable</key>
    <string>{projectName}</string>
    <key>CFBundleIdentifier</key>
    <string>com.bluesky.{projectName.ToLower()}</string>
    <key>CFBundleName</key>
    <string>{projectName}</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0</string>
    <key>LSMinimumSystemVersion</key>
    <string>10.15</string>
</dict>
</plist>";
                            File.WriteAllText(Path.Combine(contentsDir, "Info.plist"), plistContent);

                            // Copy Content to Resources
                            string sourceContent = Path.Combine(projectPath, "Content");
                            if (Directory.Exists(sourceContent))
                                CopyDirectory(sourceContent, Path.Combine(resourcesDir, "Content"));

                            Directory.Delete(publishTmp, true);
                            OpenFolder(outputDir);
                        }
                        else
                        {
                            string finalBinary = Path.Combine(outputDir, projectName + (rid.StartsWith("win") ? ".exe" : ""));
                            if (File.Exists(sourceBinary))
                                File.Move(sourceBinary, finalBinary);
                            
                            string sourceContent = Path.Combine(projectPath, "Content");
                            if (Directory.Exists(sourceContent))
                                CopyDirectory(sourceContent, Path.Combine(outputDir, "Content"));

                            Directory.Delete(publishTmp, true);
                            OpenFolder(outputDir);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Build failed: {ex.Message}");
                }
            });
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);
            foreach (var file in Directory.GetFiles(sourceDir))
                File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), true);
            foreach (var directory in Directory.GetDirectories(sourceDir))
                CopyDirectory(directory, Path.Combine(destinationDir, Path.GetFileName(directory)));
        }

        private static void OpenFolder(string path)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start(new ProcessStartInfo("explorer", path) { UseShellExecute = true });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Process.Start(new ProcessStartInfo("open", path) { UseShellExecute = true });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                Process.Start(new ProcessStartInfo("xdg-open", path) { UseShellExecute = true });
        }
    }
}
