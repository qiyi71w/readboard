using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace Readboard.VerificationTests
{
    public sealed class FrameworkContractTests
    {
        private const string ExpectedTargetFramework = "net10.0-windows";

        [Fact]
        public void Project_DeclaresNet10WindowsTargetFramework()
        {
            string repositoryRoot = VerificationFixtureLocator.RepositoryRoot();
            string projectPath = Path.Combine(repositoryRoot, "readboard", "readboard.csproj");

            XDocument projectDocument = XDocument.Load(projectPath);
            string[] targetFrameworks = projectDocument
                .Descendants("TargetFramework")
                .Select(element => element.Value.Trim())
                .ToArray();

            Assert.NotEmpty(targetFrameworks);
            Assert.All(targetFrameworks, value => Assert.Equal(ExpectedTargetFramework, value));
        }

        [Fact]
        public void PackagingScriptAndWorkflow_UseNet10BuildContract()
        {
            string repositoryRoot = VerificationFixtureLocator.RepositoryRoot();
            string scriptPath = Path.Combine(repositoryRoot, "scripts", "package-readboard-release.local.ps1");
            string workflowPath = Path.Combine(repositoryRoot, ".github", "workflows", "package-release.yml");
            string scriptContent = File.ReadAllText(scriptPath);
            string workflowContent = File.ReadAllText(workflowPath);

            Assert.Contains("dotnet publish", scriptContent);
            Assert.Contains("--self-contained true", scriptContent);
            Assert.Contains("$publishRuntimeIdentifier = 'win-x64'", scriptContent);
            Assert.Contains("-r $publishRuntimeIdentifier", scriptContent);
            Assert.DoesNotContain("TargetFrameworkVersion=v4.8", scriptContent);
            Assert.Contains("./scripts/package-readboard-release.local.ps1", workflowContent);
            Assert.Contains("Readboard.VerificationTests.csproj", workflowContent);
        }

        [Fact]
        public void PackagingWorkflow_RunsForVersionTags()
        {
            string repositoryRoot = VerificationFixtureLocator.RepositoryRoot();
            string workflowContent = File.ReadAllText(Path.Combine(repositoryRoot, ".github", "workflows", "package-release.yml"));
            string[] tags = ReadWorkflowSequence(workflowContent, "on", "push", "tags");

            Assert.Contains("v*", tags);
        }

        [Fact]
        public void PackagingScript_DoesNotUseLegacyPackagesConfigRestore()
        {
            string repositoryRoot = VerificationFixtureLocator.RepositoryRoot();
            string scriptContent = File.ReadAllText(Path.Combine(repositoryRoot, "scripts", "package-readboard-release.local.ps1"));

            Assert.DoesNotContain("RestorePackagesConfig", scriptContent);
            Assert.DoesNotContain("packages.config", scriptContent);
        }

        [Fact]
        public void PackagingScript_UsesDotnetPublishForReadboardProject()
        {
            string repositoryRoot = VerificationFixtureLocator.RepositoryRoot();
            string scriptContent = File.ReadAllText(Path.Combine(repositoryRoot, "scripts", "package-readboard-release.local.ps1"));

            Assert.Contains("dotnet publish", scriptContent);
            Assert.DoesNotContain("/t:Rebuild", scriptContent);
        }

        [Fact]
        public void PackagingScript_DoesNotReferenceLightweightRuntime()
        {
            string repositoryRoot = VerificationFixtureLocator.RepositoryRoot();
            string scriptContent = File.ReadAllText(Path.Combine(repositoryRoot, "scripts", "package-readboard-release.local.ps1"));

            Assert.DoesNotContain("lw.dll", scriptContent);
            Assert.DoesNotContain("Interop.lw", scriptContent);
        }

        [Fact]
        public void ReadboardProject_DoesNotReferenceRemovedLightweightInterop()
        {
            string repositoryRoot = VerificationFixtureLocator.RepositoryRoot();
            string projectContent = File.ReadAllText(Path.Combine(repositoryRoot, "readboard", "readboard.csproj"));

            Assert.False(File.Exists(Path.Combine(repositoryRoot, "readboard", "Interop.lw.dll")));
            Assert.False(File.Exists(Path.Combine(repositoryRoot, "readboard", "lw.dll")));
            Assert.DoesNotContain("Interop.lw", projectContent);
            Assert.DoesNotContain("lw.dll", projectContent);
            Assert.DoesNotContain("GenerateLwInterop", projectContent);
            Assert.DoesNotContain("generate_lw_interop.ps1", projectContent);
        }

        [Fact]
        public void ReadboardProject_DoesNotKeepRemovedMainFormLayoutProfileOrStaleRuleSet()
        {
            string repositoryRoot = VerificationFixtureLocator.RepositoryRoot();
            string projectContent = File.ReadAllText(Path.Combine(repositoryRoot, "readboard", "readboard.csproj"));

            Assert.False(File.Exists(Path.Combine(repositoryRoot, "readboard", "MainForm.LayoutProfile.cs")));
            Assert.DoesNotContain("MainForm.LayoutProfile.cs", projectContent);
            Assert.DoesNotContain("MinimumRecommendedRules.ruleset", projectContent);
        }

        [Fact]
        public void UiThemeSource_UsesRepositoryConfiguredCrlfLineEndings()
        {
            string repositoryRoot = VerificationFixtureLocator.RepositoryRoot();
            string editorConfigContent = File.ReadAllText(Path.Combine(repositoryRoot, ".editorconfig"));
            byte[] sourceBytes = File.ReadAllBytes(Path.Combine(repositoryRoot, "readboard", "UiTheme.cs"));

            Assert.Contains("[*.{sln,cs,csproj,config,resx,settings,manifest,user,props,targets,ruleset,ps1,bat,cmd,txt,rtf}]", editorConfigContent);
            Assert.Contains("end_of_line = crlf", editorConfigContent);
            Assert.Equal(0, CountBareLf(sourceBytes));
        }

        [Fact]
        public void ProtocolConfigBenchmarks_Project_UsesProjectReferenceForProductionSources()
        {
            string repositoryRoot = VerificationFixtureLocator.RepositoryRoot();
            string projectPath = Path.Combine(
                repositoryRoot,
                "benchmarks",
                "Readboard.ProtocolConfigBenchmarks",
                "Readboard.ProtocolConfigBenchmarks.csproj");
            XDocument projectDocument = XDocument.Load(projectPath);

            Assert.Contains(
                projectDocument.Descendants("ProjectReference"),
                element => string.Equals(
                    (string)element.Attribute("Include"),
                    @"..\..\readboard\readboard.csproj",
                    StringComparison.Ordinal));
            Assert.Contains(
                projectDocument.Descendants("Compile"),
                element => string.Equals(
                    (string)element.Attribute("Include"),
                    @"..\..\tests\Shared\ProtocolFixtureCatalog.cs",
                    StringComparison.Ordinal));
            Assert.DoesNotContain(
                projectDocument.Descendants("Compile"),
                element => ((string)element.Attribute("Include") ?? string.Empty)
                    .StartsWith(@"..\..\readboard\", StringComparison.Ordinal));
        }

        [Fact]
        public void VerificationTests_Project_UsesProjectReferenceForProductionSources()
        {
            string repositoryRoot = VerificationFixtureLocator.RepositoryRoot();
            string projectPath = Path.Combine(
                repositoryRoot,
                "tests",
                "Readboard.VerificationTests",
                "Readboard.VerificationTests.csproj");
            XDocument projectDocument = XDocument.Load(projectPath);

            Assert.Contains(
                projectDocument.Descendants("ProjectReference"),
                element => string.Equals(
                    (string)element.Attribute("Include"),
                    @"..\..\readboard\readboard.csproj",
                    StringComparison.Ordinal));
            Assert.Contains(
                projectDocument.Descendants("Compile"),
                element => string.Equals(
                    (string)element.Attribute("Include"),
                    @"..\Shared\ProtocolFixtureCatalog.cs",
                    StringComparison.Ordinal));
            Assert.DoesNotContain(
                projectDocument.Descendants("Compile"),
                element => ((string)element.Attribute("Include") ?? string.Empty)
                    .StartsWith(@"..\..\readboard\", StringComparison.Ordinal));
        }

        [Fact]
        public void Program_WiresColorModeStartupThroughGetSystemColorMode()
        {
            string repositoryRoot = VerificationFixtureLocator.RepositoryRoot();
            string source = File.ReadAllText(Path.Combine(repositoryRoot, "readboard", "Program.cs"));

            Assert.Contains("Application.SetColorMode(GetSystemColorMode(Config.ColorMode))", source);
            Assert.Contains("case AppConfig.ColorModeDark: return SystemColorMode.Dark;", source);
            Assert.Contains("case AppConfig.ColorModeLight: return SystemColorMode.Classic;", source);
            Assert.Contains("default: return SystemColorMode.System;", source);
        }

        private static string[] ReadWorkflowSequence(string workflowContent, string rootKey, string parentKey, string sequenceKey)
        {
            string[] lines = workflowContent.Replace("\r\n", "\n").Split('\n');
            int rootIndex = FindTopLevelMapping(lines, rootKey);
            MappingNode parentNode = FindChildMapping(lines, rootIndex, 0, parentKey);
            MappingNode sequenceNode = FindChildMapping(lines, parentNode.Index, parentNode.Indent, sequenceKey);
            return ReadSequenceItems(lines, sequenceNode.Index, sequenceNode.Indent);
        }

        private static int FindTopLevelMapping(string[] lines, string key)
        {
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index];
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (GetIndent(line) == 0 && string.Equals(line.Trim(), key + ":", StringComparison.Ordinal))
                    return index;
            }

            throw new InvalidDataException("Missing top-level YAML key: " + key);
        }

        private static MappingNode FindChildMapping(string[] lines, int startIndex, int parentIndent, string key)
        {
            for (int index = startIndex + 1; index < lines.Length; index++)
            {
                string line = lines[index];
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                int indent = GetIndent(line);
                if (indent <= parentIndent)
                    break;

                if (string.Equals(line.Trim(), key + ":", StringComparison.Ordinal))
                    return new MappingNode(index, indent);
            }

            throw new InvalidDataException("Missing child YAML key: " + key);
        }

        private static string[] ReadSequenceItems(string[] lines, int startIndex, int parentIndent)
        {
            List<string> items = new List<string>();

            for (int index = startIndex + 1; index < lines.Length; index++)
            {
                string line = lines[index];
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                int indent = GetIndent(line);
                if (indent <= parentIndent)
                    break;

                string trimmed = line.Trim();
                if (trimmed.StartsWith("- ", StringComparison.Ordinal))
                    items.Add(trimmed.Substring(2).Trim().Trim('\'', '"'));
            }

            return items.ToArray();
        }

        private static int GetIndent(string line)
        {
            return line.Length - line.TrimStart().Length;
        }

        private static int CountBareLf(byte[] bytes)
        {
            int count = 0;
            for (int index = 0; index < bytes.Length; index++)
            {
                if (bytes[index] == (byte)'\n' && (index == 0 || bytes[index - 1] != (byte)'\r'))
                    count++;
            }

            return count;
        }

        private readonly struct MappingNode
        {
            public MappingNode(int index, int indent)
            {
                Index = index;
                Indent = indent;
            }

            public int Index { get; }
            public int Indent { get; }
        }
    }
}
