using System.Diagnostics;
using System.Text.Json.Nodes;
using NUnit.Framework;

namespace RestBroker.Ui.Tests;

[TestFixture]
public sealed class UiProvisioningScriptTests
{
    private static readonly string[] Services =
    [
        "rbp-booking",
        "rbp-room",
        "rbp-branding",
        "rbp-assets",
        "rbp-auth",
        "rbp-report",
        "rbp-message"
    ];

    private string _temporaryDirectory = null!;
    private string _provisionScript = null!;
    private string _cleanupScript = null!;

    [SetUp]
    public void SetUp()
    {
        var root = FindRepositoryRoot();
        _provisionScript = Path.Combine(root, ".github", "scripts", "provision-ui.sh");
        _cleanupScript = Path.Combine(root, ".github", "scripts", "cleanup-ui.sh");
        _temporaryDirectory = Path.Combine(Path.GetTempPath(), $"restbroker-provisioning-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_temporaryDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    [Test]
    public void ValidateComposeModel_WithFrontendLoopbackPortOnly_Succeeds()
    {
        var model = WriteComposeModel(CreateComposeModel());

        var result = RunScript(_provisionScript, "--validate-compose-model", model);

        Assert.That(result.ExitCode, Is.Zero, result.StandardError);
    }

    [Test]
    public void ValidateComposeModel_WhenAssetsBuildUsesLocalhostRoomUrl_Fails()
    {
        var model = CreateComposeModel();
        model["services"]!["rbp-assets"]!["build"] = new JsonObject
        {
            ["args"] = new JsonObject { ["ROOM_API"] = "http://localhost:3001" }
        };

        var result = RunScript(_provisionScript, "--validate-compose-model", WriteComposeModel(model));

        Assert.That(result.ExitCode, Is.Not.Zero);
        Assert.That(result.StandardError, Does.Contain("assets build must set ROOM_API to the rbp-room service"));
    }

    [Test]
    public void ConfigureAssetsRoomApiBuild_AddsRoomApiToBuilderStage()
    {
        var source = CreateSourceFixture();
        var dockerfile = Path.Combine(source, "assets", "Dockerfile");

        var result = RunScript(_provisionScript, "--configure-assets-room-api-build", dockerfile);
        var configuredDockerfile = File.ReadAllText(dockerfile);

        Assert.That(result.ExitCode, Is.Zero, result.StandardError);
        Assert.That(configuredDockerfile, Does.Contain(
            "FROM base AS builder\nARG ROOM_API\nENV ROOM_API=${ROOM_API}\n"));
    }

    [Test]
    public void ConfigureAssetsRoomApiBuild_WhenDockerfileDoesNotMatchPinnedStages_FailsWithoutRewrite()
    {
        var source = CreateSourceFixture();
        var dockerfile = Path.Combine(source, "assets", "Dockerfile");
        const string unexpectedDockerfile = "FROM node:25 AS base\nFROM base AS runner\nRUN npm run build\n";
        File.WriteAllText(dockerfile, unexpectedDockerfile);

        var result = RunScript(_provisionScript, "--configure-assets-room-api-build", dockerfile);

        Assert.That(result.ExitCode, Is.Not.Zero);
        Assert.That(result.StandardError, Does.Contain("does not match the pinned assets build stages"));
        Assert.That(File.ReadAllText(dockerfile), Is.EqualTo(unexpectedDockerfile));
    }

    [Test]
    public void SummarizeComposeStatus_EmitsOnlyAllowlistedServiceStates()
    {
        var status = new JsonArray
        {
            new JsonObject
            {
                ["Service"] = "rbp-booking",
                ["State"] = "running",
                ["Name"] = "container-name",
                ["Environment"] = "secret-value"
            },
            new JsonObject
            {
                ["Service"] = "unrecognized-service",
                ["State"] = "running",
                ["Name"] = "sensitive-container"
            }
        };
        var statusFile = Path.Combine(_temporaryDirectory, "compose-status.json");
        File.WriteAllText(statusFile, status.ToJsonString());

        var result = RunScript(_provisionScript, "--summarize-compose-status", statusFile);

        Assert.That(result.ExitCode, Is.Zero, result.StandardError);
        Assert.That(result.StandardOutput, Does.Contain("rbp-booking=running"));
        Assert.That(result.StandardOutput, Does.Contain("rbp-assets=unknown"));
        Assert.That(result.StandardOutput, Does.Not.Contain("secret-value"));
        Assert.That(result.StandardOutput, Does.Not.Contain("sensitive-container"));
        Assert.That(result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries), Has.Length.EqualTo(7));
    }

    [Test]
    public void ValidateComposeModel_WithPublishedBackendPort_Fails()
    {
        var model = CreateComposeModel();
        model["services"]!["rbp-booking"]!["ports"] = new JsonArray(
            new JsonObject
            {
                ["target"] = 3000,
                ["published"] = "3000",
                ["host_ip"] = "0.0.0.0",
                ["protocol"] = "tcp"
            });

        var result = RunScript(_provisionScript, "--validate-compose-model", WriteComposeModel(model));

        Assert.That(result.ExitCode, Is.Not.Zero);
        Assert.That(result.StandardError, Does.Contain("unexpected published ports for rbp-booking"));
    }

    [Test]
    public void ValidateComposeModel_WithHostNetworkMode_Fails()
    {
        var model = CreateComposeModel();
        model["services"]!["rbp-booking"]!["network_mode"] = "host";

        var result = RunScript(_provisionScript, "--validate-compose-model", WriteComposeModel(model));

        Assert.That(result.ExitCode, Is.Not.Zero);
        Assert.That(result.StandardError, Does.Contain("custom network mode is not allowed for rbp-booking"));
    }

    [TestCase("0.0.0.0", "0")]
    [TestCase("127.0.0.1", "30080")]
    public void ValidateComposeModel_WithNonLoopbackOrFixedFrontendPort_Fails(string hostIp, string publishedPort)
    {
        var model = CreateComposeModel();
        model["services"]!["rbp-assets"]!["ports"]![0]!["host_ip"] = hostIp;
        model["services"]!["rbp-assets"]!["ports"]![0]!["published"] = publishedPort;

        var result = RunScript(_provisionScript, "--validate-compose-model", WriteComposeModel(model));

        Assert.That(result.ExitCode, Is.Not.Zero);
        Assert.That(result.StandardError, Does.Contain("frontend must publish only target 80"));
    }

    [Test]
    public void PinDockerfiles_WithExpectedPinnedSourceReferences_RewritesAllBaseImages()
    {
        var source = CreateSourceFixture();
        var model = WriteComposeModel(CreateComposeModel(source));

        var result = RunScript(_provisionScript, "--pin-dockerfiles", source, model);

        Assert.That(result.ExitCode, Is.Zero, result.StandardError);
        Assert.That(result.StandardOutput, Does.Contain("Pinned 7 Dockerfiles from 7 Compose services."));
        Assert.That(File.ReadAllText(Path.Combine(source, "booking", "Dockerfile")),
            Does.Contain("eclipse-temurin:26-jre-alpine@sha256:"));
        Assert.That(File.ReadAllText(Path.Combine(source, "assets", "Dockerfile")),
            Does.Contain("node:24.14.1@sha256:"));
    }

    [Test]
    public void PinDockerfiles_WhenExpectedBaseReferenceDrifts_FailsWithoutPartialRewrite()
    {
        var source = CreateSourceFixture();
        var model = WriteComposeModel(CreateComposeModel(source));
        var driftedFile = Path.Combine(source, "assets", "Dockerfile");
        File.WriteAllText(driftedFile, "FROM node:25 AS base\nFROM base AS runner\n");

        var result = RunScript(_provisionScript, "--pin-dockerfiles", source, model);

        Assert.That(result.ExitCode, Is.Not.Zero);
        Assert.That(result.StandardError, Does.Contain("unexpected base-image reference in assets/Dockerfile"));
        Assert.That(File.ReadAllText(Path.Combine(source, "booking", "Dockerfile")),
            Is.EqualTo("FROM eclipse-temurin:26-jre-alpine\n"));
        Assert.That(File.ReadAllText(driftedFile), Is.EqualTo("FROM node:25 AS base\nFROM base AS runner\n"));
    }

    [Test]
    public void PinDockerfiles_IgnoresDockerfilesOutsideComposeBuildContexts()
    {
        var source = CreateSourceFixture();
        var utilityDockerfile = Path.Combine(source, ".utilities", "wirebridge", "Dockerfile");
        Directory.CreateDirectory(Path.GetDirectoryName(utilityDockerfile)!);
        File.WriteAllText(utilityDockerfile, "FROM maven:3.5.2-jdk-8-alpine\n");
        var model = WriteComposeModel(CreateComposeModel(source));

        var result = RunScript(_provisionScript, "--pin-dockerfiles", source, model);

        Assert.That(result.ExitCode, Is.Zero, result.StandardError);
        Assert.That(result.StandardOutput, Does.Contain("Pinned 7 Dockerfiles from 7 Compose services."));
        Assert.That(File.ReadAllText(utilityDockerfile), Is.EqualTo("FROM maven:3.5.2-jdk-8-alpine\n"));
        Assert.That(Directory.GetFiles(source, "Dockerfile", SearchOption.AllDirectories), Has.Length.EqualTo(8));
    }

    [Test]
    public void PinDockerfiles_WhenComposeBuildContextHasNoDockerfile_FailsWithoutPartialRewrite()
    {
        var source = CreateSourceFixture();
        File.Delete(Path.Combine(source, "room", "Dockerfile"));
        var model = WriteComposeModel(CreateComposeModel(source));

        var result = RunScript(_provisionScript, "--pin-dockerfiles", source, model);

        Assert.That(result.ExitCode, Is.Not.Zero);
        Assert.That(result.StandardError, Does.Contain("Dockerfile for rbp-room is missing"));
        Assert.That(File.ReadAllText(Path.Combine(source, "booking", "Dockerfile")),
            Is.EqualTo("FROM eclipse-temurin:26-jre-alpine\n"));
    }

    [Test]
    public void PinDockerfiles_WithUnapprovedDigestPinnedImage_FailsWithoutPartialRewrite()
    {
        var source = CreateSourceFixture();
        var model = WriteComposeModel(CreateComposeModel(source));
        var dockerfile = Path.Combine(source, "booking", "Dockerfile");
        File.AppendAllText(dockerfile, "FROM alpine:3.22@sha256:" + new string('a', 64) + "\n");

        var result = RunScript(_provisionScript, "--pin-dockerfiles", source, model);

        Assert.That(result.ExitCode, Is.Not.Zero);
        Assert.That(result.StandardError, Does.Contain("unexpected external base image in booking/Dockerfile"));
        Assert.That(File.ReadAllText(dockerfile),
            Does.Contain("FROM eclipse-temurin:26-jre-alpine\n"));
    }

    [Test]
    public void Cleanup_WithUnscopedProjectName_RefusesBeforeCallingDocker()
    {
        var composeFile = Path.Combine(_temporaryDirectory, "docker-compose.yml");
        var overrideFile = Path.Combine(_temporaryDirectory, "override.yml");
        File.WriteAllText(composeFile, "services: {}\n");
        File.WriteAllText(overrideFile, "services: {}\n");

        var result = RunScriptWithEnvironment(
            _cleanupScript,
            new Dictionary<string, string>
            {
                ["GITHUB_RUN_ID"] = "123",
                ["GITHUB_RUN_ATTEMPT"] = "1",
                ["RUNNER_TEMP"] = _temporaryDirectory
            },
            "unrelated-project",
            composeFile,
            overrideFile);

        Assert.That(result.ExitCode, Is.Not.Zero);
        Assert.That(result.StandardError, Does.Contain("does not match this workflow run"));
    }

    [Test]
    public void Cleanup_WithCurrentProjectButExternalComposeFiles_Refuses()
    {
        var composeFile = Path.Combine(_temporaryDirectory, "docker-compose.yml");
        var overrideFile = Path.Combine(_temporaryDirectory, "override.yml");
        File.WriteAllText(composeFile, "services: {}\n");
        File.WriteAllText(overrideFile, "services: {}\n");

        var result = RunScriptWithEnvironment(
            _cleanupScript,
            new Dictionary<string, string>
            {
                ["GITHUB_RUN_ID"] = "123",
                ["GITHUB_RUN_ATTEMPT"] = "2",
                ["RUNNER_TEMP"] = _temporaryDirectory
            },
            "restbroker-ui-123-2",
            composeFile,
            overrideFile);

        Assert.That(result.ExitCode, Is.Not.Zero);
        Assert.That(result.StandardError, Does.Contain("outside this workflow run directory"));
    }

    private string WriteComposeModel(JsonObject model)
    {
        var path = Path.Combine(_temporaryDirectory, "compose-model.json");
        File.WriteAllText(path, model.ToJsonString());
        return path;
    }

    private JsonObject CreateComposeModel(string? sourceDirectory = null)
    {
        var services = new JsonObject();
        foreach (var service in Services)
        {
            var definition = service == "rbp-assets"
                ? new JsonObject
                {
                    ["ports"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["target"] = 80,
                            ["published"] = "0",
                            ["host_ip"] = "127.0.0.1",
                            ["protocol"] = "tcp"
                        }
                    }
                }
                : new JsonObject();
            if (sourceDirectory is not null)
            {
                definition["build"] = new JsonObject
                {
                    ["context"] = Path.Combine(sourceDirectory, service["rbp-".Length..]),
                    ["dockerfile"] = "Dockerfile"
                };
            }
            if (service == "rbp-assets")
            {
                definition["build"] ??= new JsonObject();
                definition["build"]!["args"] = new JsonObject
                {
                    ["ROOM_API"] = "http://rbp-room:3001"
                };
            }
            services[service] = definition;
        }

        return new JsonObject { ["services"] = services };
    }

    private string CreateSourceFixture()
    {
        var source = Path.Combine(_temporaryDirectory, "source");
        foreach (var service in Services)
        {
            var serviceDirectory = Path.Combine(source, service["rbp-".Length..]);
            Directory.CreateDirectory(serviceDirectory);
            var dockerfile = service == "rbp-assets"
                ? "FROM node:24 AS base\nFROM base AS builder\nWORKDIR /app\nRUN npm run build\nFROM base AS runner\nENV ROOM_API=http://rbp-room:3001\n"
                : "FROM eclipse-temurin:26-jre-alpine\n";
            File.WriteAllText(Path.Combine(serviceDirectory, "Dockerfile"), dockerfile);
        }

        return source;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, ".github", "scripts", "provision-ui.sh")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root for UI provisioning script tests.");
    }

    private static ProcessResult RunScript(string script, params string[] arguments)
    {
        return RunScriptCore(script, null, arguments);
    }

    private static ProcessResult RunScriptWithEnvironment(
        string script,
        IReadOnlyDictionary<string, string> environment,
        params string[] arguments)
    {
        return RunScriptCore(script, environment, arguments);
    }

    private static ProcessResult RunScriptCore(
        string script,
        IReadOnlyDictionary<string, string>? environment,
        string[] arguments)
    {
        var shell = FindBash();
        var startInfo = new ProcessStartInfo(shell)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(script);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var nodeDirectory = FindPlaywrightNodeDirectory();
        if (nodeDirectory is not null)
        {
            var currentPath = startInfo.Environment.TryGetValue("PATH", out var value) ? value : string.Empty;
            startInfo.Environment["PATH"] = nodeDirectory + Path.PathSeparator + currentPath;
        }
        if (environment is not null)
        {
            foreach (var item in environment)
            {
                startInfo.Environment[item.Key] = item.Value;
            }
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start Bash for provisioning script tests.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(15_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Provisioning script contract test exceeded 15 seconds.");
        }

        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    private static string FindBash()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "/bin/bash";
        }

        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Git", "bin", "bash.exe")
        };
        var bash = candidates.FirstOrDefault(File.Exists);
        if (bash is null)
        {
            Assert.Ignore("Git Bash is required to run UI provisioning script contract tests on Windows.");
        }

        return bash!;
    }

    private static string? FindPlaywrightNodeDirectory()
    {
        var root = Path.Combine(AppContext.BaseDirectory, ".playwright", "node");
        if (!Directory.Exists(root))
        {
            return null;
        }

        var executableName = OperatingSystem.IsWindows() ? "node.exe" : "node";
        var executable = Directory.GetFiles(root, executableName, SearchOption.AllDirectories).FirstOrDefault();
        return executable is null ? null : Path.GetDirectoryName(executable);
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
