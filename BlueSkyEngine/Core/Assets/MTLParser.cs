using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;

namespace BlueSky.Core.Assets;

public class MTLMaterial
{
    public string Name { get; set; } = "default";
    public Vector3 Kd { get; set; } = new Vector3(1, 1, 1);
    public Vector3 Ks { get; set; } = new Vector3(0, 0, 0);
    public Vector3 Ke { get; set; } = new Vector3(0, 0, 0); // Emissive color
    public float Ns { get; set; } = 0; // Specular exponent (roughness approx)
    public float d { get; set; } = 1;  // Opacity
    public int illum { get; set; } = 2; // Illumination model
    public string? map_Kd { get; set; }
    public string? map_Ka { get; set; } // Ambient map (often used as fallback albedo)
    public string? map_Bump { get; set; }
    public string? map_Ks { get; set; }
    public string? map_Ns { get; set; }
    public string? map_d { get; set; }
    public string? map_Ke { get; set; } // Emissive map
}

public class MTLParser
{
    private static readonly CultureInfo InvariantCulture = CultureInfo.InvariantCulture;

    public static Dictionary<string, MTLMaterial> Parse(string filePath)
    {
        var materials = new Dictionary<string, MTLMaterial>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(filePath))
            return materials;

        try
        {
            string text = File.ReadAllText(filePath);
            ReadOnlySpan<char> span = text.AsSpan();
            MTLMaterial? currentMat = null;

            while (!span.IsEmpty)
            {
                int lineEnd = span.IndexOf('\n');
                ReadOnlySpan<char> line;
                if (lineEnd == -1)
                {
                    line = span;
                    span = default;
                }
                else
                {
                    line = span.Slice(0, lineEnd);
                    span = span.Slice(lineEnd + 1);
                }
                
                if (!line.IsEmpty && line[^1] == '\r')
                    line = line.Slice(0, line.Length - 1);
                // Handle \r\r\n (double CR) from Roblox Studio exports
                while (!line.IsEmpty && line[^1] == '\r')
                    line = line.Slice(0, line.Length - 1);
                
                line = line.Trim();
                if (line.IsEmpty || line[0] == '#') continue;

                int firstSpace = line.IndexOfAny(' ', '\t');
                if (firstSpace == -1) continue;

                ReadOnlySpan<char> type = line.Slice(0, firstSpace);
                ReadOnlySpan<char> rest = line.Slice(firstSpace + 1).TrimStart(" \t");
                
                // Skip "Material Color" line
                if (type.Equals("material", StringComparison.OrdinalIgnoreCase) && 
                    rest.StartsWith("color", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (type.Equals("newmtl", StringComparison.OrdinalIgnoreCase))
                {
                    currentMat = new MTLMaterial { Name = rest.ToString() };
                    materials[currentMat.Name] = currentMat;
                }
                else if (type.Equals("kd", StringComparison.OrdinalIgnoreCase))
                {
                    if (currentMat != null)
                        currentMat.Kd = ParseVector3(rest);
                }
                else if (type.Equals("ks", StringComparison.OrdinalIgnoreCase))
                {
                    if (currentMat != null)
                        currentMat.Ks = ParseVector3(rest);
                }
                else if (type.Equals("ns", StringComparison.OrdinalIgnoreCase))
                {
                    if (currentMat != null)
                        currentMat.Ns = float.Parse(rest.TrimEnd(" \t\r"), InvariantCulture);
                }
                else if (type.Equals("d", StringComparison.OrdinalIgnoreCase) || type.Equals("tr", StringComparison.OrdinalIgnoreCase))
                {
                    if (currentMat != null)
                    {
                        // Trim trailing whitespace from value (Roblox exports have trailing spaces)
                        var valSpan = rest.TrimEnd(" \t\r");
                        float val = float.Parse(valSpan, InvariantCulture);
                        if (type.Equals("tr", StringComparison.OrdinalIgnoreCase)) val = 1.0f - val;
                        currentMat.d = val;
                    }
                }
                else if (type.Equals("ke", StringComparison.OrdinalIgnoreCase))
                {
                    if (currentMat != null)
                        currentMat.Ke = ParseVector3(rest);
                }
                else if (type.Equals("illum", StringComparison.OrdinalIgnoreCase))
                {
                    if (currentMat != null)
                    {
                        var valSpan = rest.TrimEnd(" \t\r");
                        if (int.TryParse(valSpan, System.Globalization.NumberStyles.Integer, InvariantCulture, out int illumVal))
                            currentMat.illum = illumVal;
                    }
                }
                else if (type.Equals("map_kd", StringComparison.OrdinalIgnoreCase) ||
                         type.Equals("map_ka", StringComparison.OrdinalIgnoreCase) ||
                         type.Equals("map_bump", StringComparison.OrdinalIgnoreCase) ||
                         type.Equals("bump", StringComparison.OrdinalIgnoreCase) ||
                         type.Equals("map_ks", StringComparison.OrdinalIgnoreCase) ||
                         type.Equals("map_ns", StringComparison.OrdinalIgnoreCase) ||
                         type.Equals("map_d", StringComparison.OrdinalIgnoreCase) ||
                         type.Equals("map_ke", StringComparison.OrdinalIgnoreCase) ||
                         type.Equals("map_pr", StringComparison.OrdinalIgnoreCase))
                {
                    if (currentMat != null)
                    {
                        // Skip options like -s, -o, -bm, etc.
                        while (rest.Length > 0 && rest[0] == '-')
                        {
                            int space = rest.IndexOfAny(' ', '\t');
                            if (space == -1) { rest = default; break; }
                            
                            ReadOnlySpan<char> opt = rest.Slice(0, space);
                            rest = rest.Slice(space + 1).TrimStart(" \t");
                            
                            int skipArgs = 0;
                            if (opt.Equals("-s", StringComparison.OrdinalIgnoreCase) || opt.Equals("-o", StringComparison.OrdinalIgnoreCase)) skipArgs = 3;
                            else if (opt.Equals("-mm", StringComparison.OrdinalIgnoreCase)) skipArgs = 2;
                            else if (opt.Equals("-bm", StringComparison.OrdinalIgnoreCase) || opt.Equals("-imfchan", StringComparison.OrdinalIgnoreCase) || opt.Equals("-type", StringComparison.OrdinalIgnoreCase)) skipArgs = 1;
                            
                            for (int i = 0; i < skipArgs; i++)
                            {
                                int nextSpace = rest.IndexOfAny(' ', '\t');
                                if (nextSpace == -1) { rest = default; break; }
                                rest = rest.Slice(nextSpace + 1).TrimStart(" \t");
                            }
                        }
                        
                        if (!rest.IsEmpty)
                        {
                            string fullPath = rest.ToString().Replace("\\", "/").TrimEnd();
                            string fileName = Path.GetFileName(fullPath);
                            
                            if (type.Equals("map_kd", StringComparison.OrdinalIgnoreCase)) currentMat.map_Kd = fileName;
                            else if (type.Equals("map_ka", StringComparison.OrdinalIgnoreCase))
                            {
                                currentMat.map_Ka = fileName;
                                // Use map_Ka as fallback albedo if map_Kd is not set
                                if (string.IsNullOrEmpty(currentMat.map_Kd))
                                    currentMat.map_Kd = fileName;
                            }
                            else if (type.Equals("map_bump", StringComparison.OrdinalIgnoreCase) || type.Equals("bump", StringComparison.OrdinalIgnoreCase)) currentMat.map_Bump = fileName;
                            else if (type.Equals("map_ks", StringComparison.OrdinalIgnoreCase)) currentMat.map_Ks = fileName;
                            else if (type.Equals("map_ns", StringComparison.OrdinalIgnoreCase)) currentMat.map_Ns = fileName;
                            else if (type.Equals("map_d", StringComparison.OrdinalIgnoreCase)) currentMat.map_d = fileName;
                            else if (type.Equals("map_ke", StringComparison.OrdinalIgnoreCase)) currentMat.map_Ke = fileName;
                            else currentMat.map_Ns = fileName; // map_pr fallback
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MTLParser] Error parsing {filePath}: {ex.Message}");
            Console.WriteLine($"[MTLParser] Stack trace: {ex.StackTrace}");
        }

        Console.WriteLine($"[MTLParser] Successfully parsed {materials.Count} materials from {Path.GetFileName(filePath)}");
        foreach (var mat in materials)
        {
            Console.WriteLine($"[MTLParser]   - {mat.Key}: Kd=({mat.Value.Kd.X:F2},{mat.Value.Kd.Y:F2},{mat.Value.Kd.Z:F2}), Ns={mat.Value.Ns:F1}, d={mat.Value.d:F2}, illum={mat.Value.illum}, textures={(!string.IsNullOrEmpty(mat.Value.map_Kd) ? "albedo " : "")}{(!string.IsNullOrEmpty(mat.Value.map_Bump) ? "normal " : "")}{(!string.IsNullOrEmpty(mat.Value.map_Ns) ? "spec " : "")}{(!string.IsNullOrEmpty(mat.Value.map_d) ? "opacity " : "")}{(!string.IsNullOrEmpty(mat.Value.map_Ke) ? "emissive " : "")}");
        }

        return materials;
    }

    private static Vector3 ParseVector3(ReadOnlySpan<char> span)
    {
        Vector3 v = new Vector3();
        int space1 = span.IndexOfAny(' ', '\t');
        if (space1 != -1)
        {
            v.X = float.Parse(span.Slice(0, space1), InvariantCulture);
            span = span.Slice(space1 + 1).TrimStart(" \t");
            int space2 = span.IndexOfAny(' ', '\t');
            if (space2 != -1)
            {
                v.Y = float.Parse(span.Slice(0, space2), InvariantCulture);
                v.Z = float.Parse(span.Slice(space2 + 1).TrimEnd(" \t\r"), InvariantCulture);
            }
            else
            {
                v.Y = float.Parse(span.TrimEnd(" \t\r"), InvariantCulture);
            }
        }
        else
        {
            v.X = float.Parse(span.TrimEnd(" \t\r"), InvariantCulture);
        }
        return v;
    }
}
