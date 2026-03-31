// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using System.Text.Json;

namespace Microsoft.TemplateEngine.MCP.Analysis;

/// <summary>
/// Generates a reusable dotnet template from a <see cref="ProjectAnalysis"/>.
/// Produces template.json and a templatized .csproj that preserves the original's
/// conventions (SDK, CPM, analyzer metadata, shared compiles, etc.).
/// </summary>
internal static class TemplateGenerator
{
    /// <summary>
    /// Generate a complete template directory from a project analysis.
    /// Returns the path to the generated template root.
    /// </summary>
    public static string Generate(ProjectAnalysis analysis, string outputDir, string templateName, string? templateShortName = null)
    {
        var shortName = templateShortName ?? ToShortName(templateName);
        var templateRoot = Path.Combine(outputDir, shortName);
        var templateConfigDir = Path.Combine(templateRoot, ".template.config");

        Directory.CreateDirectory(templateConfigDir);

        // Generate template.json
        var templateJson = BuildTemplateJson(analysis, templateName, shortName);
        File.WriteAllText(Path.Combine(templateConfigDir, "template.json"), templateJson);

        // Generate templatized .csproj
        var csproj = BuildCsproj(analysis);
        File.WriteAllText(Path.Combine(templateRoot, "Template.csproj"), csproj);

        // Generate a placeholder test class
        var testClass = BuildPlaceholderClass(analysis);
        File.WriteAllText(Path.Combine(templateRoot, "Tests.cs"), testClass);

        return templateRoot;
    }

    internal static string BuildTemplateJson(ProjectAnalysis analysis, string templateName, string shortName)
    {
        var classifications = InferClassifications(analysis);
        var identity = $"Custom.{shortName}";

        var templateObj = new Dictionary<string, object>
        {
            ["$schema"] = "http://json.schemastore.org/template",
            ["author"] = "Generated from existing project",
            ["classifications"] = classifications,
            ["identity"] = identity,
            ["name"] = templateName,
            ["shortName"] = shortName,
            ["tags"] = new Dictionary<string, string>
            {
                ["language"] = "C#",
                ["type"] = "project",
            },
            ["sourceName"] = "Template",
            ["preferNameDirectory"] = true,
            ["sources"] = new[]
            {
                new Dictionary<string, object>
                {
                    ["modifiers"] = new[]
                    {
                        new Dictionary<string, object>
                        {
                            ["exclude"] = new[] { ".template.config/**" },
                        },
                    },
                },
            },
        };

        // Add symbols for parameterizable properties
        var symbols = new Dictionary<string, object>();
        foreach (var prop in analysis.Properties)
        {
            if (IsParameterizableProperty(prop.Name))
            {
                symbols[prop.Name] = new Dictionary<string, object>
                {
                    ["type"] = "parameter",
                    ["datatype"] = InferDataType(prop),
                    ["defaultValue"] = prop.Value,
                    ["description"] = $"{prop.Name} (from source project)",
                    ["replaces"] = prop.Value,
                };
            }
        }

        if (symbols.Count > 0)
        {
            templateObj["symbols"] = symbols;
        }

        return JsonSerializer.Serialize(templateObj, new JsonSerializerOptions { WriteIndented = true });
    }

    internal static string BuildCsproj(ProjectAnalysis analysis)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"<Project Sdk=\"{analysis.Sdk}\">");
        sb.AppendLine();

        // Properties
        if (analysis.Properties.Count > 0)
        {
            sb.AppendLine("  <PropertyGroup>");
            foreach (var prop in analysis.Properties)
            {
                if (prop.Condition != null)
                {
                    sb.AppendLine($"    <{prop.Name} Condition=\"{EscapeXml(prop.Condition)}\">{EscapeXml(prop.Value)}</{prop.Name}>");
                }
                else
                {
                    sb.AppendLine($"    <{prop.Name}>{EscapeXml(prop.Value)}</{prop.Name}>");
                }
            }

            sb.AppendLine("  </PropertyGroup>");
            sb.AppendLine();
        }

        // Package references
        if (analysis.PackageReferences.Count > 0)
        {
            sb.AppendLine("  <ItemGroup>");
            foreach (var pkg in analysis.PackageReferences)
            {
                bool hasChildMetadata = pkg.PrivateAssets != null || pkg.IncludeAssets != null || pkg.ExcludeAssets != null;

                if (hasChildMetadata)
                {
                    sb.Append($"    <PackageReference Include=\"{pkg.Include}\"");
                    if (pkg.Version != null)
                    {
                        sb.Append($" Version=\"{pkg.Version}\"");
                    }

                    sb.AppendLine(">");

                    if (pkg.IncludeAssets != null)
                    {
                        sb.AppendLine($"      <IncludeAssets>{pkg.IncludeAssets}</IncludeAssets>");
                    }

                    if (pkg.PrivateAssets != null)
                    {
                        sb.AppendLine($"      <PrivateAssets>{pkg.PrivateAssets}</PrivateAssets>");
                    }

                    if (pkg.ExcludeAssets != null)
                    {
                        sb.AppendLine($"      <ExcludeAssets>{pkg.ExcludeAssets}</ExcludeAssets>");
                    }

                    sb.AppendLine("    </PackageReference>");
                }
                else
                {
                    sb.Append($"    <PackageReference Include=\"{pkg.Include}\"");
                    if (pkg.Version != null)
                    {
                        sb.Append($" Version=\"{pkg.Version}\"");
                    }

                    sb.AppendLine(" />");
                }
            }

            sb.AppendLine("  </ItemGroup>");
            sb.AppendLine();
        }

        // Project references (kept as-is — user adjusts paths for their repo)
        if (analysis.ProjectReferences.Count > 0)
        {
            sb.AppendLine("  <ItemGroup>");
            foreach (var pr in analysis.ProjectReferences)
            {
                sb.AppendLine($"    <ProjectReference Include=\"{EscapeXml(pr)}\" />");
            }

            sb.AppendLine("  </ItemGroup>");
            sb.AppendLine();
        }

        // Shared compiles
        if (analysis.SharedCompiles.Count > 0)
        {
            sb.AppendLine("  <ItemGroup>");
            foreach (var sc in analysis.SharedCompiles)
            {
                sb.Append($"    <Compile Include=\"{EscapeXml(sc.Include)}\"");
                if (sc.Link != null)
                {
                    sb.Append($" Link=\"{EscapeXml(sc.Link)}\"");
                }

                sb.AppendLine(" />");
            }

            sb.AppendLine("  </ItemGroup>");
            sb.AppendLine();
        }

        // Content items
        if (analysis.ContentItems.Count > 0)
        {
            sb.AppendLine("  <ItemGroup>");
            foreach (var item in analysis.ContentItems)
            {
                if (item.Remove != null)
                {
                    sb.AppendLine($"    <{item.ItemType} Remove=\"{EscapeXml(item.Remove)}\" />");
                }
                else if (item.CopyToOutputDirectory != null)
                {
                    sb.AppendLine($"    <{item.ItemType} Include=\"{EscapeXml(item.Include!)}\" CopyToOutputDirectory=\"{item.CopyToOutputDirectory}\" />");
                }
                else
                {
                    sb.AppendLine($"    <{item.ItemType} Include=\"{EscapeXml(item.Include!)}\" />");
                }
            }

            sb.AppendLine("  </ItemGroup>");
            sb.AppendLine();
        }

        sb.AppendLine("</Project>");
        return sb.ToString();
    }

    internal static string BuildPlaceholderClass(ProjectAnalysis analysis)
    {
        // Detect test framework from packages
        var packages = analysis.PackageReferences.Select(p => p.Include.ToLowerInvariant()).ToList();
        bool isXunit = packages.Any(p => p.Contains("xunit"));
        bool isNunit = packages.Any(p => p.Contains("nunit"));
        bool isMsTest = packages.Any(p => p.Contains("mstest")) || analysis.Sdk.Contains("MSTest", StringComparison.OrdinalIgnoreCase);

        var sb = new StringBuilder();

        if (isXunit)
        {
            sb.AppendLine("namespace Template;");
            sb.AppendLine();
            sb.AppendLine("public class UnitTest1");
            sb.AppendLine("{");
            sb.AppendLine("    [Fact]");
            sb.AppendLine("    public void Test1()");
            sb.AppendLine("    {");
            sb.AppendLine("        // Arrange");
            sb.AppendLine();
            sb.AppendLine("        // Act");
            sb.AppendLine();
            sb.AppendLine("        // Assert");
            sb.AppendLine("        Assert.True(true);");
            sb.AppendLine("    }");
            sb.AppendLine("}");
        }
        else if (isNunit)
        {
            sb.AppendLine("namespace Template;");
            sb.AppendLine();
            sb.AppendLine("[TestFixture]");
            sb.AppendLine("public class UnitTest1");
            sb.AppendLine("{");
            sb.AppendLine("    [Test]");
            sb.AppendLine("    public void Test1()");
            sb.AppendLine("    {");
            sb.AppendLine("        // Arrange");
            sb.AppendLine();
            sb.AppendLine("        // Act");
            sb.AppendLine();
            sb.AppendLine("        // Assert");
            sb.AppendLine("        Assert.Pass();");
            sb.AppendLine("    }");
            sb.AppendLine("}");
        }
        else if (isMsTest)
        {
            sb.AppendLine("namespace Template;");
            sb.AppendLine();
            sb.AppendLine("[TestClass]");
            sb.AppendLine("public class UnitTest1");
            sb.AppendLine("{");
            sb.AppendLine("    [TestMethod]");
            sb.AppendLine("    public void TestMethod1()");
            sb.AppendLine("    {");
            sb.AppendLine("        // Arrange");
            sb.AppendLine();
            sb.AppendLine("        // Act");
            sb.AppendLine();
            sb.AppendLine("        // Assert");
            sb.AppendLine("        Assert.IsTrue(true);");
            sb.AppendLine("    }");
            sb.AppendLine("}");
        }
        else
        {
            sb.AppendLine("namespace Template;");
            sb.AppendLine();
            sb.AppendLine("public class Class1");
            sb.AppendLine("{");
            sb.AppendLine("}");
        }

        return sb.ToString();
    }

    private static string[] InferClassifications(ProjectAnalysis analysis)
    {
        var classifications = new List<string>();
        var packages = analysis.PackageReferences.Select(p => p.Include.ToLowerInvariant()).ToList();

        bool isTest = packages.Any(p => p.Contains("xunit") || p.Contains("nunit") || p.Contains("mstest") || p.Contains("test.sdk"))
            || analysis.Sdk.Contains("MSTest", StringComparison.OrdinalIgnoreCase)
            || analysis.Properties.Any(p => p.Name == "IsTestProject" && p.Value.Equals("true", StringComparison.OrdinalIgnoreCase));

        if (isTest)
        {
            classifications.Add("Test");
        }

        bool isWeb = analysis.Sdk.Contains("Web", StringComparison.OrdinalIgnoreCase)
            || packages.Any(p => p.Contains("aspnetcore"));

        if (isWeb)
        {
            classifications.Add("Web");
        }

        bool isLib = analysis.Properties.Any(p => p.Name == "OutputType" && p.Value.Equals("Library", StringComparison.OrdinalIgnoreCase))
            || (!isTest && !isWeb && !analysis.Properties.Any(p => p.Name == "OutputType"));

        if (isLib && !isTest)
        {
            classifications.Add("Library");
        }

        if (analysis.Properties.Any(p => p.Name == "OutputType" && p.Value.Equals("Exe", StringComparison.OrdinalIgnoreCase)))
        {
            classifications.Add("Console");
        }

        return classifications.Count > 0 ? classifications.ToArray() : new[] { "Custom" };
    }

    private static bool IsParameterizableProperty(string propertyName)
    {
        // Properties that make sense as template parameters
        var parameterizable = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "TargetFramework", "TargetFrameworks", "OutputType",
            "RootNamespace", "AssemblyName", "Nullable", "ImplicitUsings",
            "LangVersion",
        };
        return parameterizable.Contains(propertyName);
    }

    private static string InferDataType(ProjectProperty prop)
    {
        if (prop.Name.Equals("Nullable", StringComparison.OrdinalIgnoreCase) ||
            prop.Name.Equals("ImplicitUsings", StringComparison.OrdinalIgnoreCase))
        {
            return "choice";
        }

        return "string";
    }

    private static string EscapeXml(string value)
    {
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }

    private static string ToShortName(string templateName)
    {
        var shortName = templateName
            .ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("_", "-");

        // Remove characters that are invalid for dotnet new short names / filesystem
        shortName = System.Text.RegularExpressions.Regex.Replace(shortName, @"[/\\:*?""<>|#%&{}!@+`=\[\]]", "");

        // Collapse repeated hyphens and trim leading/trailing hyphens
        shortName = System.Text.RegularExpressions.Regex.Replace(shortName, @"-{2,}", "-").Trim('-');

        return string.IsNullOrEmpty(shortName) ? "template" : shortName;
    }
}
