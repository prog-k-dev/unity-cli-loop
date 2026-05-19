using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies CLI setup application service behavior.
    /// </summary>
    public class CliSetupApplicationServiceTests
    {
        [Test]
        public async Task InstallGlobalCliAsync_UsesMinimumRequiredCliReleaseTag()
        {
            // Verifies that manual installs target the independent CLI release stream.
            FakeNativeCliInstaller nativeCliInstaller = new();
            CliSetupApplicationService service = new(
                new FakeCliInstallationDetector(new string[] { null }),
                nativeCliInstaller);

            await service.InstallGlobalCliAsync(RuntimePlatform.OSXEditor, CancellationToken.None);

            Assert.That(
                nativeCliInstaller.InstalledVersion,
                Is.EqualTo(CliConstants.MINIMUM_REQUIRED_CLI_RELEASE_TAG));
        }

        [Test]
        public void GetMinimumRequiredCliVersion_RequiresDynamicCodeDomainReloadWaitCliRelease()
        {
            // Verifies this package release rejects CLIs without dynamic-code domain reload waiting.
            CliSetupApplicationService service = new(
                new FakeCliInstallationDetector(new string[] { null }),
                new FakeNativeCliInstaller());

            Assert.That(service.GetMinimumRequiredCliVersion(), Is.EqualTo("3.0.0-beta.9"));
        }

        [Test]
        public void GetMinimumRequiredCliReleaseTag_UsesCliGitHubReleaseTag()
        {
            // Verifies installers target the prefixed CLI GitHub Release tag.
            CliSetupApplicationService service = new(
                new FakeCliInstallationDetector(new string[] { null }),
                new FakeNativeCliInstaller());

            Assert.That(service.GetMinimumRequiredCliReleaseTag(), Is.EqualTo("cli-v3.0.0-beta.9"));
        }

        [Test]
        public void GetGlobalCliInstallCommand_UsesMinimumRequiredCliReleaseTag()
        {
            // Verifies that fallback manual commands point at the independent CLI release stream.
            FakeNativeCliInstaller nativeCliInstaller = new();
            CliSetupApplicationService service = new(
                new FakeCliInstallationDetector(new string[] { null }),
                nativeCliInstaller);

            NativeCliInstallCommand command = service.GetGlobalCliInstallCommand(
                RuntimePlatform.OSXEditor,
                false);

            Assert.That(
                command.ManualCommand,
                Is.EqualTo("install " + CliConstants.MINIMUM_REQUIRED_CLI_RELEASE_TAG));
        }

        [Test]
        public async Task IsCliVisibleFromShellAsync_DelegatesToDetector()
        {
            // Verifies that UI can ask specifically whether the terminal shell resolves uloop.
            CliSetupApplicationService service = new(
                new FakeCliInstallationDetector(new string[] { null }, isCliVisibleFromShell: false),
                new FakeNativeCliInstaller());

            bool result = await service.IsCliVisibleFromShellAsync(
                RuntimePlatform.OSXEditor,
                CancellationToken.None);

            Assert.That(result, Is.False);
        }

        [Test]
        public void GetGlobalCliPathSetupPlan_DelegatesToInstaller()
        {
            // Verifies that UI receives shell-specific PATH setup data through the application service.
            FakeNativeCliInstaller nativeCliInstaller = new();
            CliSetupApplicationService service = new(
                new FakeCliInstallationDetector(new string[] { null }),
                nativeCliInstaller);

            CliPathSetupPlan result = service.GetGlobalCliPathSetupPlan(RuntimePlatform.OSXEditor);

            Assert.That(result.ShellKind, Is.EqualTo(CliPathSetupShellKind.Zsh));
            Assert.That(result.ConfigurationFilePath, Is.EqualTo("/Users/ExampleUser/.zshrc"));
        }

        [Test]
        public void ApplyGlobalCliPathSetup_DelegatesToInstaller()
        {
            // Verifies that explicit UI approval flows through the application service.
            FakeNativeCliInstaller nativeCliInstaller = new();
            CliSetupApplicationService service = new(
                new FakeCliInstallationDetector(new string[] { null }),
                nativeCliInstaller);
            CliPathSetupPlan plan = nativeCliInstaller.GetGlobalCliPathSetupPlan(RuntimePlatform.OSXEditor);

            CliPathSetupApplyResult result = service.ApplyGlobalCliPathSetup(plan);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.Applied));
            Assert.That(nativeCliInstaller.AppliedPathSetup, Is.True);
        }

        private sealed class FakeCliInstallationDetector : ICliInstallationDetector
        {
            private readonly string[] _versions;
            private readonly bool _isCliVisibleFromShell;
            private int _versionIndex;

            public FakeCliInstallationDetector(string[] versions, bool isCliVisibleFromShell = true)
            {
                Debug.Assert(versions != null, "versions must not be null");
                Debug.Assert(versions.Length > 0, "versions must not be empty");

                _versions = versions;
                _isCliVisibleFromShell = isCliVisibleFromShell;
            }
            public bool IsCliInstalled() => GetCachedCliVersion() != null;
            public string GetCachedCliVersion() => _versions[_versionIndex];
            public string GetCachedCliExecutablePath() => "";
            public bool IsCheckCompleted() => true;
            public Task RefreshCliVersionAsync(CancellationToken ct) => Task.CompletedTask;
            public Task<bool> IsCliVisibleFromShellAsync(RuntimePlatform platform, CancellationToken ct)
                => Task.FromResult(_isCliVisibleFromShell);

            public Task ForceRefreshCliVersionAsync(CancellationToken ct)
            {
                if (_versionIndex < _versions.Length - 1)
                {
                    _versionIndex++;
                }

                return Task.CompletedTask;
            }

            public void InvalidateCache() { }
        }

        private sealed class FakeNativeCliInstaller : INativeCliInstaller
        {
            public string InstalledVersion { get; private set; }
            public bool AppliedPathSetup { get; private set; }

            public bool IsPackageOwnedCurrentUserInstallPath(string cliExecutablePath, RuntimePlatform platform)
            {
                return false;
            }

            public Task<CliInstallResult> InstallGlobalCliAsync(
                RuntimePlatform platform,
                string cliReleaseTag,
                CancellationToken ct)
            {
                InstalledVersion = cliReleaseTag;
                return Task.FromResult(new CliInstallResult(true, ""));
            }

            public Task<CliInstallResult> UninstallGlobalCliAsync(RuntimePlatform platform, CancellationToken ct)
            {
                return Task.FromResult(new CliInstallResult(true, ""));
            }

            public CliPathSetupPlan GetGlobalCliPathSetupPlan(RuntimePlatform platform)
            {
                return new CliPathSetupPlan(
                    CliPathSetupShellKind.Zsh,
                    "zsh",
                    true,
                    "/Users/ExampleUser/.local/bin",
                    "$HOME/.local/bin",
                    "/Users/ExampleUser/.zshrc",
                    "export PATH=\"$HOME/.local/bin:$PATH\"",
                    "echo 'export PATH=\"$HOME/.local/bin:$PATH\"' >> /Users/ExampleUser/.zshrc");
            }

            public CliPathSetupApplyResult ApplyGlobalCliPathSetup(CliPathSetupPlan pathSetupPlan)
            {
                AppliedPathSetup = true;
                return new CliPathSetupApplyResult(
                    true,
                    CliPathSetupApplyStatus.Applied,
                    "");
            }

            public NativeCliInstallCommand GetGlobalCliInstallCommand(
                RuntimePlatform platform,
                string cliReleaseTag,
                bool removeLegacyLaunchers)
            {
                return new NativeCliInstallCommand("sh", "-c true", $"install {cliReleaseTag}");
            }
        }
    }
}
