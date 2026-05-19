using System;
using System.Diagnostics;
using System.IO;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies CLI installation detection behavior.
    /// </summary>
    public class CliInstallationDetectorTests
    {
        [Test]
        public void SelectPreferredDetection_WhenShellCommandShadowsPackageOwnedCliUsesShellPath()
        {
            // Verifies that the settings UI reports the same CLI command the user's terminal runs.
            CliInstallationDetection packageOwnedDetection = new(
                "3.0.0-beta.3",
                "/Users/ExampleUser/.local/bin/uloop");
            CliInstallationDetection shellDetection = new(
                "2.1.0",
                "/Users/ExampleUser/.npm-global/bin/uloop");

            CliInstallationDetection result = CliInstallationDetector.SelectPreferredDetection(
                packageOwnedDetection,
                shellDetection);

            Assert.That(result.Version, Is.EqualTo("2.1.0"));
            Assert.That(result.ExecutablePath, Is.EqualTo("/Users/ExampleUser/.npm-global/bin/uloop"));
        }

        [Test]
        public void SelectPreferredDetection_WhenShellCommandMissingUsesPackageOwnedCli()
        {
            // Verifies that package-owned installs still count when the shell cannot resolve uloop.
            CliInstallationDetection packageOwnedDetection = new(
                "3.0.0-beta.3",
                "/Users/ExampleUser/.local/bin/uloop");
            CliInstallationDetection shellDetection = new(
                null,
                null);

            CliInstallationDetection result = CliInstallationDetector.SelectPreferredDetection(
                packageOwnedDetection,
                shellDetection);

            Assert.That(result.Version, Is.EqualTo("3.0.0-beta.3"));
            Assert.That(result.ExecutablePath, Is.EqualTo("/Users/ExampleUser/.local/bin/uloop"));
        }

        [Test]
        public void SelectPreferredDetection_WhenShellCommandExistsButVersionFailsUsesShellPath()
        {
            // Verifies that a broken PATH command is surfaced instead of hidden by the package-owned binary.
            CliInstallationDetection packageOwnedDetection = new(
                "3.0.0-beta.3",
                "/Users/ExampleUser/.local/bin/uloop");
            CliInstallationDetection shellDetection = new(
                null,
                "/Users/ExampleUser/.npm-global/bin/uloop");

            CliInstallationDetection result = CliInstallationDetector.SelectPreferredDetection(
                packageOwnedDetection,
                shellDetection);

            Assert.That(result.Version, Is.Null);
            Assert.That(result.ExecutablePath, Is.EqualTo("/Users/ExampleUser/.npm-global/bin/uloop"));
        }

        [Test]
        public void SelectPreferredDetection_WhenPackageOwnedCliMissingUsesShellPath()
        {
            // Verifies that legacy CLI installs still surface as update candidates.
            CliInstallationDetection packageOwnedDetection = new(
                null,
                "/Users/ExampleUser/.local/bin/uloop");
            CliInstallationDetection shellDetection = new(
                "2.1.0",
                "/Users/ExampleUser/.npm-global/bin/uloop");

            CliInstallationDetection result = CliInstallationDetector.SelectPreferredDetection(
                packageOwnedDetection,
                shellDetection);

            Assert.That(result.Version, Is.EqualTo("2.1.0"));
            Assert.That(result.ExecutablePath, Is.EqualTo("/Users/ExampleUser/.npm-global/bin/uloop"));
        }

        [Test]
        public void SelectPreferredDetection_WhenShellVersionExistsWithoutPathUsesShellVersion()
        {
            // Verifies that installed state does not depend on command path availability.
            CliInstallationDetection packageOwnedDetection = new(
                "3.0.0-beta.3",
                "/Users/ExampleUser/.local/bin/uloop");
            CliInstallationDetection shellDetection = new(
                "2.1.0",
                null);

            CliInstallationDetection result = CliInstallationDetector.SelectPreferredDetection(
                packageOwnedDetection,
                shellDetection);

            Assert.That(result.Version, Is.EqualTo("2.1.0"));
            Assert.That(result.ExecutablePath, Is.Null);
        }

        [Test]
        public void BuildShellCliDetectionCommand_UsesShortVersionFlag()
        {
            // Verifies that shell detection asks the command itself for its terminal-visible version.
            CliPathSetupPlan plan = CreateZshPathSetupPlan();

            string command = CliInstallationDetector.BuildShellCliDetectionCommand("uloop", plan);

            Assert.That(command, Does.Contain("command -v uloop"));
            Assert.That(command, Does.Contain("uloop -v"));
            Assert.That(command, Does.Contain("uloop_version_status=$?"));
            Assert.That(command, Does.Contain("__ULOOP_VERSION_STATUS_START__"));
            Assert.That(command, Does.Not.Contain("uloop --version"));
        }

        [Test]
        public void BuildShellCliDetectionCommand_SanitizesInheritedInstallPathBeforeChecking()
        {
            // Verifies that shell detection does not trust PATH inherited from an already-open Unity process.
            CliPathSetupPlan plan = CreateZshPathSetupPlan();

            string command = CliInstallationDetector.BuildShellCliDetectionCommand("uloop", plan);

            Assert.That(command, Does.Contain("uloop_install_dir='/Users/ExampleUser/.local/bin'"));
            Assert.That(command, Does.Contain("awk -v remove=\"$uloop_install_dir\""));
            Assert.That(
                command.IndexOf("awk -v remove=\"$uloop_install_dir\"", System.StringComparison.Ordinal),
                Is.LessThan(command.IndexOf("command -v uloop", System.StringComparison.Ordinal)));
        }

        [Test]
        public void BuildShellCliDetectionCommand_SourcesCurrentProfileAfterSanitizingInheritedPath()
        {
            // Verifies that click-time checks read the current profile content after removing stale PATH.
            CliPathSetupPlan plan = CreateZshPathSetupPlan();

            string command = CliInstallationDetector.BuildShellCliDetectionCommand("uloop", plan);

            Assert.That(command, Does.Contain("uloop_profile='/Users/ExampleUser/.zshrc'"));
            Assert.That(command, Does.Contain(". \"$uloop_profile\""));
            Assert.That(
                command.IndexOf(". \"$uloop_profile\"", System.StringComparison.Ordinal),
                Is.LessThan(command.IndexOf("command -v uloop", System.StringComparison.Ordinal)));
        }

        [Test]
        public void BuildShellCliDetectionCommand_WhenInstallDirOnlyInherited_ReturnsMissingDetection()
        {
            // Verifies that a Unity process with stale PATH does not make shell detection report success.
            if (UnityEngine.Application.platform == UnityEngine.RuntimePlatform.WindowsEditor)
            {
                Assert.Ignore("POSIX shell detection is not used on Windows.");
            }

            string tempRoot = Path.Combine(Path.GetTempPath(), "uloop-cli-detection-" + Guid.NewGuid().ToString("N"));
            string installDirectory = Path.Combine(tempRoot, "bin");
            string profilePath = Path.Combine(tempRoot, ".zshrc");
            string executablePath = Path.Combine(installDirectory, "uloop");
            Directory.CreateDirectory(installDirectory);

            try
            {
                File.WriteAllText(
                    executablePath,
                    "#!/bin/sh\n"
                    + "if [ \"$1\" = \"-v\" ]; then\n"
                    + "  echo 3.0.0-test\n"
                    + "  exit 0\n"
                    + "fi\n"
                    + "exit 1\n");
                MakeExecutable(executablePath);
                File.WriteAllText(profilePath, "# PATH intentionally omitted\n");

                CliPathSetupPlan plan = new(
                    CliPathSetupShellKind.Zsh,
                    "zsh",
                    true,
                    installDirectory,
                    "$HOME/.local/bin",
                    profilePath,
                    "export PATH=\"$HOME/.local/bin:$PATH\"",
                    "manual command");
                string command = CliInstallationDetector.BuildShellCliDetectionCommand("uloop", plan);
                string output = ExecuteShellDetectionCommand(
                    command,
                    installDirectory + ":/usr/bin:/bin");

                CliInstallationDetection detection =
                    CliInstallationDetector.ParseShellCliInstallationOutput(output);

                Assert.That(detection.Version, Is.Null);
                Assert.That(detection.ExecutablePath, Is.Null);
            }
            finally
            {
                DeleteDirectoryIfExists(tempRoot);
            }
        }

        [Test]
        public void ParseShellCliInstallationOutput_WhenPathAndVersionExist_ReturnsDetection()
        {
            // Verifies that shell detection keeps terminal-visible path data as auxiliary UI context.
            string output = "banner\n"
                            + "__ULOOP_PATH_START__\n"
                            + "/Users/ExampleUser/.npm-global/bin/uloop\n"
                            + "__ULOOP_PATH_END__\n"
                            + "__ULOOP_VERSION_START__\n"
                            + "2.1.1\n"
                            + "__ULOOP_VERSION_END__\n"
                            + "__ULOOP_VERSION_STATUS_START__\n"
                            + "0\n"
                            + "__ULOOP_VERSION_STATUS_END__\n";

            CliInstallationDetection detection =
                CliInstallationDetector.ParseShellCliInstallationOutput(output);

            Assert.That(detection.Version, Is.EqualTo("2.1.1"));
            Assert.That(detection.ExecutablePath, Is.EqualTo("/Users/ExampleUser/.npm-global/bin/uloop"));
        }

        [Test]
        public void ParseShellCliInstallationOutput_WhenOnlyVersionExists_ReturnsInstalledDetection()
        {
            // Verifies that installation state depends on version output, not path availability.
            string output = "__ULOOP_PATH_START__\n"
                            + "__ULOOP_PATH_END__\n"
                            + "__ULOOP_VERSION_START__\n"
                            + "2.1.1\n"
                            + "__ULOOP_VERSION_END__\n"
                            + "__ULOOP_VERSION_STATUS_START__\n"
                            + "0\n"
                            + "__ULOOP_VERSION_STATUS_END__\n";

            CliInstallationDetection detection =
                CliInstallationDetector.ParseShellCliInstallationOutput(output);

            Assert.That(detection.Version, Is.EqualTo("2.1.1"));
            Assert.That(detection.ExecutablePath, Is.Null);
        }

        [Test]
        public void ParseShellCliInstallationOutput_WhenVersionCommandFails_ReturnsPathWithoutVersion()
        {
            // Verifies that failed shell probes do not treat stdout usage text as a CLI version.
            string output = "__ULOOP_PATH_START__\n"
                            + "/Users/ExampleUser/.npm-global/bin/uloop\n"
                            + "__ULOOP_PATH_END__\n"
                            + "__ULOOP_VERSION_START__\n"
                            + "usage: broken uloop\n"
                            + "__ULOOP_VERSION_END__\n"
                            + "__ULOOP_VERSION_STATUS_START__\n"
                            + "1\n"
                            + "__ULOOP_VERSION_STATUS_END__\n";

            CliInstallationDetection detection =
                CliInstallationDetector.ParseShellCliInstallationOutput(output);

            Assert.That(detection.Version, Is.Null);
            Assert.That(detection.ExecutablePath, Is.EqualTo("/Users/ExampleUser/.npm-global/bin/uloop"));
        }

        [Test]
        public void KillProcessIfRunning_WhenProcessAlreadyExited_DoesNotThrow()
        {
            // Verifies that process cleanup tolerates the race where the child exits before Kill.
            ProcessStartInfo startInfo = BuildImmediateExitProcessStartInfo();

            using Process process = Process.Start(startInfo);
            process.WaitForExit();

            Assert.DoesNotThrow(() => CliInstallationDetector.KillProcessIfRunning(process));
        }

        private static ProcessStartInfo BuildImmediateExitProcessStartInfo()
        {
            if (UnityEngine.Application.platform == UnityEngine.RuntimePlatform.WindowsEditor)
            {
                return new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c exit 0",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
            }

            return new ProcessStartInfo
            {
                FileName = "/bin/sh",
                Arguments = "-c \"exit 0\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
        }

        private static CliPathSetupPlan CreateZshPathSetupPlan()
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

        private static string ExecuteShellDetectionCommand(string command, string path)
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = "/bin/sh",
                Arguments = "-c " + QuoteProcessArgument(command),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.EnvironmentVariables["PATH"] = path;

            using Process process = Process.Start(startInfo);
            bool exited = process.WaitForExit(5000);
            if (!exited)
            {
                CliInstallationDetector.KillProcessIfRunning(process);
                Assert.Fail("Shell detection command timed out.");
            }

            return process.StandardOutput.ReadToEnd();
        }

        private static void MakeExecutable(string executablePath)
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = "/bin/chmod",
                Arguments = "+x " + QuoteProcessArgument(executablePath),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using Process process = Process.Start(startInfo);
            bool exited = process.WaitForExit(5000);

            Assert.That(exited, Is.True);
            Assert.That(process.ExitCode, Is.EqualTo(0));
        }

        private static string QuoteProcessArgument(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static void DeleteDirectoryIfExists(string directory)
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            Directory.Delete(directory, true);
        }
    }
}
