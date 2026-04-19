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
        public void PackagingWorkflow_RunsForMainAndRefactorFixPushes()
        {
            string repositoryRoot = VerificationFixtureLocator.RepositoryRoot();
            string workflowContent = File.ReadAllText(Path.Combine(repositoryRoot, ".github", "workflows", "package-release.yml"));

            Assert.Contains("branches:", workflowContent);
            Assert.Contains("- main", workflowContent);
            Assert.Contains("- refactor-fix", workflowContent);
            Assert.Contains("tags:", workflowContent);
            Assert.Contains("- 'v*'", workflowContent);
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
        public void ProtocolConfigBenchmarks_Project_IncludesPlaceRequestExecutionResult()
        {
            string repositoryRoot = VerificationFixtureLocator.RepositoryRoot();
            string projectContent = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "benchmarks",
                "Readboard.ProtocolConfigBenchmarks",
                "Readboard.ProtocolConfigBenchmarks.csproj"));

            Assert.Contains(@"..\..\readboard\Core\Protocol\PlaceRequestExecutionResult.cs", projectContent);
        }
    }
}
