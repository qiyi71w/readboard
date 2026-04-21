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
        private const string ExpectedTargetFrameworkVersion = "v4.8";
        private const string ExpectedRuntimeSku = ".NETFramework,Version=v4.8";

        [Fact]
        public void ProjectAndAppConfig_DeclareDotNetFramework48()
        {
            string repositoryRoot = VerificationFixtureLocator.RepositoryRoot();
            string projectPath = Path.Combine(repositoryRoot, "readboard", "readboard.csproj");
            string appConfigPath = Path.Combine(repositoryRoot, "readboard", "App.config");

            XNamespace projectNamespace = "http://schemas.microsoft.com/developer/msbuild/2003";
            XDocument projectDocument = XDocument.Load(projectPath);
            string[] targetFrameworkVersions = projectDocument
                .Descendants(projectNamespace + "TargetFrameworkVersion")
                .Select(element => element.Value.Trim())
                .ToArray();

            Assert.NotEmpty(targetFrameworkVersions);
            Assert.All(targetFrameworkVersions, value => Assert.Equal(ExpectedTargetFrameworkVersion, value));

            XDocument appConfigDocument = XDocument.Load(appConfigPath);
            XElement supportedRuntime = appConfigDocument.Descendants("supportedRuntime").Single();
            Assert.Equal(ExpectedRuntimeSku, supportedRuntime.Attribute("sku").Value);
        }

        [Fact]
        public void PackagingScriptAndWorkflow_UseSharedFramework48Contract()
        {
            string repositoryRoot = VerificationFixtureLocator.RepositoryRoot();
            string scriptPath = Path.Combine(repositoryRoot, "scripts", "package-readboard-release.local.ps1");
            string workflowPath = Path.Combine(repositoryRoot, ".github", "workflows", "package-release.yml");
            string scriptContent = File.ReadAllText(scriptPath);
            string workflowContent = File.ReadAllText(workflowPath);

            Assert.Contains("/p:TargetFrameworkVersion=v4.8", scriptContent);
            Assert.DoesNotContain("v4.5", scriptContent);
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
        public void PackagingScriptAndWorkflow_RestoreLegacyPackagesConfigBeforeBuild()
        {
            string repositoryRoot = VerificationFixtureLocator.RepositoryRoot();
            string scriptContent = File.ReadAllText(Path.Combine(repositoryRoot, "scripts", "package-readboard-release.local.ps1"));
            string workflowContent = File.ReadAllText(Path.Combine(repositoryRoot, ".github", "workflows", "package-release.yml"));

            Assert.Contains("RestorePackagesConfig=true", scriptContent);
            Assert.Contains("NuGet.Config", scriptContent);
            Assert.Contains("RestorePackagesConfig=true", workflowContent);
            Assert.Contains("NuGet.Config", workflowContent);
        }

        [Fact]
        public void PackagingScript_UsesBuildTargetForReadboardProject()
        {
            string repositoryRoot = VerificationFixtureLocator.RepositoryRoot();
            string scriptContent = File.ReadAllText(Path.Combine(repositoryRoot, "scripts", "package-readboard-release.local.ps1"));

            Assert.Contains("/t:Build", scriptContent);
            Assert.DoesNotContain("/t:Rebuild", scriptContent);
        }

        [Fact]
        public void PackagingScript_RequiresLightweightRuntimeFile()
        {
            string repositoryRoot = VerificationFixtureLocator.RepositoryRoot();
            string scriptContent = File.ReadAllText(Path.Combine(repositoryRoot, "scripts", "package-readboard-release.local.ps1"));

            Assert.Contains("$requiredStaticFiles", scriptContent);
            Assert.Contains("'lw.dll'", scriptContent);
            Assert.Contains("Copy-RelativeFiles -SourceDir $projectRoot -RelativePaths $requiredStaticFiles", scriptContent);
        }

        [Fact]
        public void ReadboardProject_UsesCheckedInInteropAssembly()
        {
            string repositoryRoot = VerificationFixtureLocator.RepositoryRoot();
            string projectContent = File.ReadAllText(Path.Combine(repositoryRoot, "readboard", "readboard.csproj"));
            string interopAssemblyPath = Path.Combine(repositoryRoot, "readboard", "Interop.lw.dll");

            Assert.True(File.Exists(interopAssemblyPath), "Expected checked-in Interop.lw.dll for clean builds.");
            Assert.Contains("<HintPath>Interop.lw.dll</HintPath>", projectContent);
            Assert.DoesNotContain("<Content Include=\"lw.dll\">", projectContent);
            Assert.DoesNotContain("GenerateLwInterop", projectContent);
            Assert.DoesNotContain("generate_lw_interop.ps1", projectContent);
        }

        [Fact]
        public void ProtocolConfigBenchmarks_Project_IncludesRequiredProductionSources()
        {
            string repositoryRoot = VerificationFixtureLocator.RepositoryRoot();
            string projectPath = Path.Combine(
                repositoryRoot,
                "benchmarks",
                "Readboard.ProtocolConfigBenchmarks",
                "Readboard.ProtocolConfigBenchmarks.csproj");
            XDocument projectDocument = XDocument.Load(projectPath);

            Assert.Contains(
                projectDocument.Descendants("Compile"),
                element => string.Equals(
                    (string)element.Attribute("Include"),
                    @"..\..\readboard\Core\Protocol\PlaceRequestExecutionResult.cs",
                    StringComparison.Ordinal));
            Assert.Contains(
                projectDocument.Descendants("Compile"),
                element => string.Equals(
                    (string)element.Attribute("Include"),
                    @"..\..\readboard\Core\Display\DisplayScaling.cs",
                    StringComparison.Ordinal));
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
