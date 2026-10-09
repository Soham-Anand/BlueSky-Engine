using System;

namespace BlueSky.Networking;

/// <summary>
/// EOS bootstrap credentials used to initialize the online platform.
/// These are intentionally read from environment variables by default so
/// standalone builds can be launched without baking secrets into source.
/// </summary>
public sealed class EosCredentials
{
    public string ProductId { get; set; } = "";
    public string SandboxId { get; set; } = "";
    public string DeploymentId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string ArtifactId { get; set; } = "";
    public string ProductName { get; set; } = "BlueSky Engine";
    public string ProductVersion { get; set; } = "0.1.0";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ProductId) &&
        !string.IsNullOrWhiteSpace(SandboxId) &&
        !string.IsNullOrWhiteSpace(DeploymentId) &&
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(ClientSecret);

    public static EosCredentials FromEnvironment()
    {
        return new EosCredentials
        {
            ProductId = GetEnv("EOS_PRODUCT_ID"),
            SandboxId = GetEnv("EOS_SANDBOX_ID"),
            DeploymentId = GetEnv("EOS_DEPLOYMENT_ID"),
            ClientId = GetEnv("EOS_CLIENT_ID"),
            ClientSecret = GetEnv("EOS_CLIENT_SECRET"),
            ArtifactId = GetEnv("EOS_ARTIFACT_ID"),
            ProductName = GetEnv("EOS_PRODUCT_NAME", "BlueSky Engine"),
            ProductVersion = GetEnv("EOS_PRODUCT_VERSION", "0.1.0"),
        };
    }

    public static EosCredentials FromFile(string filePath)
    {
        var creds = FromEnvironment();
        if (System.IO.File.Exists(filePath))
        {
            try
            {
                var lines = System.IO.File.ReadAllLines(filePath);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith(";") || trimmed.StartsWith("#") || trimmed.StartsWith("["))
                        continue;

                    var parts = trimmed.Split('=', 2);
                    if (parts.Length == 2)
                    {
                        var key = parts[0].Trim().ToLowerInvariant();
                        var val = parts[1].Trim().Trim('"').Trim('\'');

                        switch (key)
                        {
                            case "productid": creds.ProductId = val; break;
                            case "sandboxid": creds.SandboxId = val; break;
                            case "deploymentid": creds.DeploymentId = val; break;
                            case "clientid": creds.ClientId = val; break;
                            case "clientsecret": creds.ClientSecret = val; break;
                            case "artifactid": creds.ArtifactId = val; break;
                            case "productname": creds.ProductName = val; break;
                            case "productversion": creds.ProductVersion = val; break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Net] Warning: Failed to read EOS.ini from {filePath}: {ex.Message}");
            }
        }
        return creds;
    }

    public static EosCredentials LoadForProject(string projectDir)
    {
        string configPath = "";
        if (!string.IsNullOrEmpty(projectDir))
        {
            configPath = System.IO.Path.Combine(projectDir, "Config", "EOS.ini");
        }
        else
        {
            configPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "EOS.ini");
            if (!System.IO.File.Exists(configPath))
            {
                configPath = System.IO.Path.Combine(Environment.CurrentDirectory, "Config", "EOS.ini");
            }
        }
        return FromFile(configPath);
    }

    private static string GetEnv(string name, string fallback = "")
        => Environment.GetEnvironmentVariable(name) ?? fallback;
}

