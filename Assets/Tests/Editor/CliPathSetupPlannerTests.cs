using System;
using System.Collections.Generic;
using System.IO;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies shell-specific CLI PATH setup planning.
    /// </summary>
    public class CliPathSetupPlannerTests
    {
        [Test]
        public void BuildPosixPlan_WhenShellIsZshUsesZshrc()
        {
            // Verifies that zsh users get an explicit zsh profile update plan.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/bin/zsh",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/.local/bin");

            Assert.That(plan.ShellKind, Is.EqualTo(CliPathSetupShellKind.Zsh));
            Assert.That(plan.CanApplyAutomatically, Is.True);
            Assert.That(plan.ConfigurationFilePath, Is.EqualTo("/Users/ExampleUser/.zshrc"));
            Assert.That(plan.ConfigurationLine, Is.EqualTo("export PATH=\"$HOME/.local/bin:$PATH\""));
            Assert.That(plan.ManualCommand, Does.Contain(".zshrc"));
        }

        [Test]
        public void BuildPosixPlan_WhenZloginExistsUsesZlogin()
        {
            // Verifies that zsh PATH repair survives later login startup resets.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/bin/zsh",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/.local/bin",
                path => string.Equals(path, "/Users/ExampleUser/.zlogin", StringComparison.Ordinal));

            Assert.That(plan.ShellKind, Is.EqualTo(CliPathSetupShellKind.Zsh));
            Assert.That(plan.ConfigurationFilePath, Is.EqualTo("/Users/ExampleUser/.zlogin"));
            Assert.That(plan.ManualCommand, Does.Contain(".zlogin"));
        }

        [Test]
        public void ResolveZshConfigurationRoot_WhenEnvironmentMissingUsesLoginShellRoot()
        {
            // Verifies that zsh profile writes follow the shell's effective ZDOTDIR.
            string result = CliPathSetupPlanner.ResolveZshConfigurationRoot(
                "/bin/zsh",
                "/Users/ExampleUser",
                null,
                (shellPath, homeDirectory) => "/Users/ExampleUser/.config/zsh");

            Assert.That(result, Is.EqualTo("/Users/ExampleUser/.config/zsh"));
        }

        [Test]
        public void ResolveZshConfigurationRoot_WhenEnvironmentExistsUsesEnvironmentValue()
        {
            // Verifies that exported ZDOTDIR takes precedence over probing the shell again.
            int resolveCount = 0;

            string result = CliPathSetupPlanner.ResolveZshConfigurationRoot(
                "/bin/zsh",
                "/Users/ExampleUser",
                "/Users/ExampleUser/.zsh",
                (shellPath, homeDirectory) =>
                {
                    resolveCount++;
                    return "/Users/ExampleUser/.config/zsh";
                });

            Assert.That(result, Is.EqualTo("/Users/ExampleUser/.zsh"));
            Assert.That(resolveCount, Is.EqualTo(0));
        }

        [Test]
        public void BuildCurrentUserPlan_WhenZshenvSetsZdotdirUsesResolvedZshrc()
        {
            // Verifies that the zsh probe preserves newlines and extracts the effective ZDOTDIR path.
            if (!File.Exists("/bin/zsh"))
            {
                Assert.Ignore("zsh is not available on this platform.");
            }

            string originalHome = Environment.GetEnvironmentVariable("HOME");
            string originalShell = Environment.GetEnvironmentVariable("SHELL");
            string originalZDotDirectory = Environment.GetEnvironmentVariable("ZDOTDIR");
            string originalInstallDirectory = Environment.GetEnvironmentVariable("ULOOP_INSTALL_DIR");
            string tempRoot = Path.Combine(Path.GetTempPath(), "uloop-zsh-plan-" + Guid.NewGuid().ToString("N"));
            string homeDirectory = Path.Combine(tempRoot, "home");
            string zshConfigurationRoot = Path.Combine(homeDirectory, ".config", "zsh");
            string installDirectory = Path.Combine(homeDirectory, ".local", "bin");
            Directory.CreateDirectory(zshConfigurationRoot);

            try
            {
                File.WriteAllText(
                    Path.Combine(homeDirectory, ".zshenv"),
                    "export ZDOTDIR=\"$HOME/.config/zsh\"\n");
                Environment.SetEnvironmentVariable("HOME", homeDirectory);
                Environment.SetEnvironmentVariable("SHELL", "/bin/zsh");
                Environment.SetEnvironmentVariable("ZDOTDIR", null);
                Environment.SetEnvironmentVariable("ULOOP_INSTALL_DIR", installDirectory);

                CliPathSetupPlan plan = CliPathSetupPlanner.BuildCurrentUserPlan(UnityEngine.RuntimePlatform.OSXEditor);

                Assert.That(plan.ConfigurationFilePath, Is.EqualTo(Path.Combine(zshConfigurationRoot, ".zshrc")));
            }
            finally
            {
                Environment.SetEnvironmentVariable("HOME", originalHome);
                Environment.SetEnvironmentVariable("SHELL", originalShell);
                Environment.SetEnvironmentVariable("ZDOTDIR", originalZDotDirectory);
                Environment.SetEnvironmentVariable("ULOOP_INSTALL_DIR", originalInstallDirectory);
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }

        [Test]
        public void BuildPosixPlan_WhenShellIsBashAndBashProfileExistsUsesBashProfile()
        {
            // Verifies that bash preserves the highest-precedence existing login profile.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/bin/bash",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/.local/bin",
                path => string.Equals(path, "/Users/ExampleUser/.bash_profile", StringComparison.Ordinal));

            Assert.That(plan.ShellKind, Is.EqualTo(CliPathSetupShellKind.Bash));
            Assert.That(plan.ConfigurationFilePath, Is.EqualTo("/Users/ExampleUser/.bash_profile"));
            Assert.That(plan.ConfigurationLine, Is.EqualTo("export PATH=\"$HOME/.local/bin:$PATH\""));
        }

        [Test]
        public void BuildPosixPlan_ManualCommandUsesPrintfWithLeadingNewline()
        {
            // Verifies that copied setup commands do not concatenate onto files missing final newlines.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/bin/zsh",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/.local/bin");

            Assert.That(
                plan.ManualCommand,
                Does.Contain("printf '\\n%s\\n' 'export PATH=\"$HOME/.local/bin:$PATH\"'"));
        }

        [Test]
        public void BuildPosixPlan_WhenShellIsBashAndProfileExistsUsesProfile()
        {
            // Verifies that PATH repair does not create .bash_profile over an existing .profile.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/bin/bash",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/.local/bin",
                path => string.Equals(path, "/Users/ExampleUser/.profile", StringComparison.Ordinal));

            Assert.That(plan.ConfigurationFilePath, Is.EqualTo("/Users/ExampleUser/.profile"));
        }

        [Test]
        public void BuildPosixPlan_WhenShellIsBashAndBashLoginExistsUsesBashLogin()
        {
            // Verifies that bash profile selection follows login-shell precedence.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/bin/bash",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/.local/bin",
                path => string.Equals(path, "/Users/ExampleUser/.bash_login", StringComparison.Ordinal)
                    || string.Equals(path, "/Users/ExampleUser/.profile", StringComparison.Ordinal));

            Assert.That(plan.ConfigurationFilePath, Is.EqualTo("/Users/ExampleUser/.bash_login"));
        }

        [Test]
        public void BuildPosixPlan_WhenShellIsBashWithoutLoginProfilesUsesBashProfile()
        {
            // Verifies that bash creates the highest-precedence login profile when none exists.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/bin/bash",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/.local/bin",
                path => false);

            Assert.That(plan.ConfigurationFilePath, Is.EqualTo("/Users/ExampleUser/.bash_profile"));
        }

        [Test]
        public void BuildPosixPlan_WhenShellIsFishUsesFishConfig()
        {
            // Verifies that fish gets fish syntax instead of POSIX export syntax.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/opt/homebrew/bin/fish",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/.local/bin");

            Assert.That(plan.ShellKind, Is.EqualTo(CliPathSetupShellKind.Fish));
            Assert.That(plan.ConfigurationFilePath, Is.EqualTo("/Users/ExampleUser/.config/fish/config.fish"));
            Assert.That(plan.ConfigurationLine, Is.EqualTo("fish_add_path \"$HOME/.local/bin\""));
        }

        [Test]
        public void BuildPosixPlan_WhenFishHasXdgConfigHomeUsesXdgFishConfig()
        {
            // Verifies that fish PATH repair writes to the effective XDG config file.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/opt/homebrew/bin/fish",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/.local/bin",
                fileExists: null,
                xdgConfigDirectory: "/Users/ExampleUser/.xdg");

            Assert.That(plan.ConfigurationFilePath, Is.EqualTo("/Users/ExampleUser/.xdg/fish/config.fish"));
        }

        [Test]
        public void BuildPosixPlan_WhenInstallDirectoryContainsShellMetacharactersEscapesProfileLine()
        {
            // Verifies that custom install paths are written as literal PATH entries.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/bin/zsh",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/bin$cash\"quote");

            Assert.That(plan.ConfigurationLine, Is.EqualTo("export PATH=\"$HOME/bin\\$cash\\\"quote:$PATH\""));
        }

        [Test]
        public void BuildPosixPlan_WhenInstallDirectoryContainsQuoteKeepsEscapedProfileInstallDirectory()
        {
            // Verifies escaped quotes do not truncate the profile path used by idempotency checks.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/bin/zsh",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/bin$cash\"quote");

            Assert.That(plan.ProfileInstallDirectory, Is.EqualTo("$HOME/bin\\$cash\\\"quote"));
        }

        [Test]
        public void BuildPosixPlan_WhenShellIsUnsupportedDisablesAutomaticApply()
        {
            // Verifies that unknown shells never get guessed file writes.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/bin/tcsh",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/.local/bin");

            Assert.That(plan.ShellKind, Is.EqualTo(CliPathSetupShellKind.Unsupported));
            Assert.That(plan.CanApplyAutomatically, Is.False);
            Assert.That(plan.ConfigurationFilePath, Is.Empty);
            Assert.That(plan.ManualCommand, Is.EqualTo("export PATH='/Users/ExampleUser/.local/bin':\"$PATH\""));
        }

        [Test]
        public void ApplyPlan_WhenConfigurationLineExistsSkipsAppend()
        {
            // Verifies that explicit UI apply is idempotent for existing PATH setup.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/bin/zsh",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/.local/bin");
            int appendCount = 0;

            CliPathSetupApplyResult result = CliPathSetupPlanner.ApplyPlan(
                plan,
                path => true,
                path => "export PATH=\"$HOME/.local/bin:$PATH\"\n",
                path => new DirectoryInfo(path),
                (path, content) => { appendCount++; });

            Assert.That(result.Success, Is.True);
            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.AlreadyConfigured));
            Assert.That(appendCount, Is.EqualTo(0));
        }

        [Test]
        public void ApplyPlan_WhenHomePathUsesBracesSkipsAppend()
        {
            // Verifies that existing HOME-based PATH setup avoids duplicate UI writes.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/bin/zsh",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/.local/bin");
            int appendCount = 0;

            CliPathSetupApplyResult result = CliPathSetupPlanner.ApplyPlan(
                plan,
                path => true,
                path => "export PATH=\"${HOME}/.local/bin:$PATH\"\n",
                path => new DirectoryInfo(path),
                (path, content) => { appendCount++; });

            Assert.That(result.Success, Is.True);
            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.AlreadyConfigured));
            Assert.That(appendCount, Is.EqualTo(0));
        }

        [Test]
        public void ApplyPlan_WhenHomePathUsesUnquotedTildeSkipsAppend()
        {
            // Verifies that existing tilde PATH setup avoids duplicate UI writes.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/bin/zsh",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/.local/bin");
            int appendCount = 0;

            CliPathSetupApplyResult result = CliPathSetupPlanner.ApplyPlan(
                plan,
                path => true,
                path => "export PATH=~/.local/bin:$PATH\n",
                path => new DirectoryInfo(path),
                (path, content) => { appendCount++; });

            Assert.That(result.Success, Is.True);
            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.AlreadyConfigured));
            Assert.That(appendCount, Is.EqualTo(0));
        }

        [Test]
        public void ApplyPlan_WhenHomePathUsesQuotedTildeAppendsLine()
        {
            // Verifies that quoted zsh tilde paths do not block PATH repair.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/bin/zsh",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/.local/bin");
            int appendCount = 0;

            CliPathSetupApplyResult result = CliPathSetupPlanner.ApplyPlan(
                plan,
                path => true,
                path => "export PATH=\"~/.local/bin:$PATH\"\n",
                path => new DirectoryInfo(path),
                (path, content) => { appendCount++; });

            Assert.That(result.Success, Is.True);
            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.Applied));
            Assert.That(appendCount, Is.EqualTo(1));
        }

        [Test]
        public void ApplyPlan_WhenAbsolutePathExistsSkipsAppend()
        {
            // Verifies that existing absolute PATH setup avoids duplicate UI writes.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/bin/zsh",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/.local/bin");
            int appendCount = 0;

            CliPathSetupApplyResult result = CliPathSetupPlanner.ApplyPlan(
                plan,
                path => true,
                path => "export PATH=\"/Users/ExampleUser/.local/bin:$PATH\"\n",
                path => new DirectoryInfo(path),
                (path, content) => { appendCount++; });

            Assert.That(result.Success, Is.True);
            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.AlreadyConfigured));
            Assert.That(appendCount, Is.EqualTo(0));
        }

        [Test]
        public void ApplyPlan_WhenInstallDirectoryIsOnlyReferencedAppendsLine()
        {
            // Verifies that unrelated variables do not block PATH repair.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/bin/zsh",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/.local/bin");
            int appendCount = 0;

            CliPathSetupApplyResult result = CliPathSetupPlanner.ApplyPlan(
                plan,
                path => true,
                path => "ULOOP_BIN=\"$HOME/.local/bin\"\n",
                path => new DirectoryInfo(path),
                (path, content) => { appendCount++; });

            Assert.That(result.Success, Is.True);
            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.Applied));
            Assert.That(appendCount, Is.EqualTo(1));
        }

        [Test]
        public void ApplyPlan_WhenPathAssignmentIsOnlyEchoedAppendsLine()
        {
            // Verifies that quoted documentation text does not block PATH repair.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/bin/zsh",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/.local/bin");
            int appendCount = 0;

            CliPathSetupApplyResult result = CliPathSetupPlanner.ApplyPlan(
                plan,
                path => true,
                path => "echo 'export PATH=\"$HOME/.local/bin:$PATH\"'\n",
                path => new DirectoryInfo(path),
                (path, content) => { appendCount++; });

            Assert.That(result.Success, Is.True);
            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.Applied));
            Assert.That(appendCount, Is.EqualTo(1));
        }

        [Test]
        public void ApplyPlan_WhenPathAssignmentOnlyPrefixesCommandAppendsLine()
        {
            // Verifies that one-shot command environments do not count as profile PATH setup.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/bin/zsh",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/.local/bin");
            int appendCount = 0;

            CliPathSetupApplyResult result = CliPathSetupPlanner.ApplyPlan(
                plan,
                path => true,
                path => "PATH=\"$HOME/.local/bin:$PATH\" some-command\n",
                path => new DirectoryInfo(path),
                (path, content) => { appendCount++; });

            Assert.That(result.Success, Is.True);
            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.Applied));
            Assert.That(appendCount, Is.EqualTo(1));
        }

        [Test]
        public void ApplyPlan_WhenLeadingPathAssignmentExistsSkipsAppend()
        {
            // Verifies that real shell PATH assignments remain idempotent without export.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/bin/zsh",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/.local/bin");
            int appendCount = 0;

            CliPathSetupApplyResult result = CliPathSetupPlanner.ApplyPlan(
                plan,
                path => true,
                path => "PATH=\"$HOME/.local/bin:$PATH\"; export PATH\n",
                path => new DirectoryInfo(path),
                (path, content) => { appendCount++; });

            Assert.That(result.Success, Is.True);
            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.AlreadyConfigured));
            Assert.That(appendCount, Is.EqualTo(0));
        }

        [Test]
        public void ApplyPlan_WhenInstallDirectoryIsShadowedInPathAssignmentAppendsLine()
        {
            // Verifies that shadowed PATH entries do not block PATH repair.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/bin/zsh",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/.local/bin");
            int appendCount = 0;

            CliPathSetupApplyResult result = CliPathSetupPlanner.ApplyPlan(
                plan,
                path => true,
                path => "export PATH=\"$HOME/.npm-global/bin:$HOME/.local/bin:$PATH\"\n",
                path => new DirectoryInfo(path),
                (path, content) => { appendCount++; });

            Assert.That(result.Success, Is.True);
            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.Applied));
            Assert.That(appendCount, Is.EqualTo(1));
        }

        [Test]
        public void ApplyPlan_WhenLaterPathAssignmentShadowsInstallDirectoryAppendsLine()
        {
            // Verifies that later PATH assignments decide whether the profile still needs repair.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/bin/zsh",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/.local/bin");
            int appendCount = 0;

            CliPathSetupApplyResult result = CliPathSetupPlanner.ApplyPlan(
                plan,
                path => true,
                path => "export PATH=\"$HOME/.local/bin:$PATH\"\n"
                    + "export PATH=\"$HOME/.npm-global/bin:$PATH\"\n",
                path => new DirectoryInfo(path),
                (path, content) => { appendCount++; });

            Assert.That(result.Success, Is.True);
            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.Applied));
            Assert.That(appendCount, Is.EqualTo(1));
        }

        [Test]
        public void ApplyPlan_WhenLaterPathAssignmentPreservesInheritedOrderSkipsAppend()
        {
            // Verifies that PATH extensions preserving existing order remain idempotent.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/bin/zsh",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/.local/bin");
            int appendCount = 0;

            CliPathSetupApplyResult result = CliPathSetupPlanner.ApplyPlan(
                plan,
                path => true,
                path => "export PATH=\"$HOME/.local/bin:$PATH\"\n"
                    + "export PATH=\"$PATH:/opt/extra/bin\"\n",
                path => new DirectoryInfo(path),
                (path, content) => { appendCount++; });

            Assert.That(result.Success, Is.True);
            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.AlreadyConfigured));
            Assert.That(appendCount, Is.EqualTo(0));
        }

        [Test]
        public void ApplyPlan_WhenEscapedConfigurationLineExistsSkipsAppend()
        {
            // Verifies that escaped profile lines remain idempotent for custom install paths.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/bin/zsh",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/bin$cash\"quote");
            int appendCount = 0;

            CliPathSetupApplyResult result = CliPathSetupPlanner.ApplyPlan(
                plan,
                path => true,
                path => plan.ConfigurationLine + "\n",
                path => new DirectoryInfo(path),
                (path, content) => { appendCount++; });

            Assert.That(result.Success, Is.True);
            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.AlreadyConfigured));
            Assert.That(appendCount, Is.EqualTo(0));
        }

        [Test]
        public void ApplyPlan_WhenAbsolutePathUsesUnescapedDollarInDoubleQuotesAppendsLine()
        {
            // Verifies shell-expanded custom paths do not block PATH repair.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/bin/zsh",
                "/Users/ExampleUser",
                null,
                "/tmp/bin$cash");
            int appendCount = 0;

            CliPathSetupApplyResult result = CliPathSetupPlanner.ApplyPlan(
                plan,
                path => true,
                path => "export PATH=\"/tmp/bin$cash:$PATH\"\n",
                path => new DirectoryInfo(path),
                (path, content) => { appendCount++; });

            Assert.That(result.Success, Is.True);
            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.Applied));
            Assert.That(appendCount, Is.EqualTo(1));
        }

        [Test]
        public void ApplyPlan_WhenQuotedInstallDirectoryOnlySharesEscapedPrefixAppendsLine()
        {
            // Verifies escaped quote paths are not confused with shorter PATH entries.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/bin/zsh",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/bin$cash\"quote");
            int appendCount = 0;

            CliPathSetupApplyResult result = CliPathSetupPlanner.ApplyPlan(
                plan,
                path => true,
                path => "export PATH=\"$HOME/bin\\$cash\\:$PATH\"\n",
                path => new DirectoryInfo(path),
                (path, content) => { appendCount++; });

            Assert.That(result.Success, Is.True);
            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.Applied));
            Assert.That(appendCount, Is.EqualTo(1));
        }

        [Test]
        public void ApplyPlan_WhenFishAddPathExistsSkipsAppend()
        {
            // Verifies that fish_add_path is treated as an active PATH setup.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/opt/homebrew/bin/fish",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/.local/bin");
            int appendCount = 0;

            CliPathSetupApplyResult result = CliPathSetupPlanner.ApplyPlan(
                plan,
                path => true,
                path => "fish_add_path \"$HOME/.local/bin\"\n",
                path => new DirectoryInfo(path),
                (path, content) => { appendCount++; });

            Assert.That(result.Success, Is.True);
            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.AlreadyConfigured));
            Assert.That(appendCount, Is.EqualTo(0));
        }

        [Test]
        public void ApplyPlan_WhenFishAddPathAppendsInstallDirectoryAppendsLine()
        {
            // Verifies that appending fish paths does not block PATH repair.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/opt/homebrew/bin/fish",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/.local/bin");
            int appendCount = 0;

            CliPathSetupApplyResult result = CliPathSetupPlanner.ApplyPlan(
                plan,
                path => true,
                path => "fish_add_path --append \"$HOME/.local/bin\"\n",
                path => new DirectoryInfo(path),
                (path, content) => { appendCount++; });

            Assert.That(result.Success, Is.True);
            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.Applied));
            Assert.That(appendCount, Is.EqualTo(1));
        }

        [Test]
        public void ApplyPlan_WhenFishAddPathShortAppendFlagAppendsLine()
        {
            // Verifies that short fish append flags do not block PATH repair.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/opt/homebrew/bin/fish",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/.local/bin");
            int appendCount = 0;

            CliPathSetupApplyResult result = CliPathSetupPlanner.ApplyPlan(
                plan,
                path => true,
                path => "fish_add_path -a \"$HOME/.local/bin\"\n",
                path => new DirectoryInfo(path),
                (path, content) => { appendCount++; });

            Assert.That(result.Success, Is.True);
            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.Applied));
            Assert.That(appendCount, Is.EqualTo(1));
        }

        [Test]
        public void ApplyPlan_WhenFishAddPathPrependsOldPathBeforeInstallDirectoryAppendsLine()
        {
            // Verifies that shadowing fish_add_path order does not block PATH repair.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/opt/homebrew/bin/fish",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/.local/bin");
            int appendCount = 0;

            CliPathSetupApplyResult result = CliPathSetupPlanner.ApplyPlan(
                plan,
                path => true,
                path => "fish_add_path /opt/old \"$HOME/.local/bin\"\n",
                path => new DirectoryInfo(path),
                (path, content) => { appendCount++; });

            Assert.That(result.Success, Is.True);
            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.Applied));
            Assert.That(appendCount, Is.EqualTo(1));
        }

        [Test]
        public void ApplyPlan_WhenFishPathResetShadowsFishAddPathAppendsLine()
        {
            // Verifies that later fish PATH resets can still require PATH repair.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/opt/homebrew/bin/fish",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/.local/bin");
            int appendCount = 0;

            CliPathSetupApplyResult result = CliPathSetupPlanner.ApplyPlan(
                plan,
                path => true,
                path => "fish_add_path \"$HOME/.local/bin\"\n"
                    + "set -gx PATH /old/bin $PATH\n",
                path => new DirectoryInfo(path),
                (path, content) => { appendCount++; });

            Assert.That(result.Success, Is.True);
            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.Applied));
            Assert.That(appendCount, Is.EqualTo(1));
        }

        [Test]
        public void ApplyPlan_WhenFishSetAppendsInstallDirectoryAppendsLine()
        {
            // Verifies that appending fish PATH assignments do not block PATH repair.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/opt/homebrew/bin/fish",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/.local/bin");
            int appendCount = 0;

            CliPathSetupApplyResult result = CliPathSetupPlanner.ApplyPlan(
                plan,
                path => true,
                path => "set --append PATH \"$HOME/.local/bin\"\n",
                path => new DirectoryInfo(path),
                (path, content) => { appendCount++; });

            Assert.That(result.Success, Is.True);
            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.Applied));
            Assert.That(appendCount, Is.EqualTo(1));
        }

        [Test]
        public void ApplyPlan_WhenFishSetShortAppendFlagAppendsLine()
        {
            // Verifies that short fish set append flags do not block PATH repair.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/opt/homebrew/bin/fish",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/.local/bin");
            int appendCount = 0;

            CliPathSetupApplyResult result = CliPathSetupPlanner.ApplyPlan(
                plan,
                path => true,
                path => "set -a PATH \"$HOME/.local/bin\"\n",
                path => new DirectoryInfo(path),
                (path, content) => { appendCount++; });

            Assert.That(result.Success, Is.True);
            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.Applied));
            Assert.That(appendCount, Is.EqualTo(1));
        }

        [Test]
        public void ApplyPlan_WhenPathLineIsCommentedAppendsLine()
        {
            // Verifies that disabled profile lines are not treated as active PATH setup.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/bin/zsh",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/.local/bin");
            int appendCount = 0;

            CliPathSetupApplyResult result = CliPathSetupPlanner.ApplyPlan(
                plan,
                path => true,
                path => "# export PATH=\"$HOME/.local/bin:$PATH\"\n",
                path => new DirectoryInfo(path),
                (path, content) => { appendCount++; });

            Assert.That(result.Success, Is.True);
            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.Applied));
            Assert.That(appendCount, Is.EqualTo(1));
        }

        [Test]
        public void ApplyPlan_WhenSimilarDirectoryExistsAppendsLine()
        {
            // Verifies that sibling directory names do not suppress the required PATH setup.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/bin/zsh",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/.local/bin");
            int appendCount = 0;

            CliPathSetupApplyResult result = CliPathSetupPlanner.ApplyPlan(
                plan,
                path => true,
                path => "export PATH=\"$HOME/.local/bin-old:$PATH\"\n",
                path => new DirectoryInfo(path),
                (path, content) => { appendCount++; });

            Assert.That(result.Success, Is.True);
            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.Applied));
            Assert.That(appendCount, Is.EqualTo(1));
        }

        [Test]
        public void ApplyPlan_WhenConfigurationLineIsMissingAppendsLine()
        {
            // Verifies that explicit UI apply appends the shell-specific PATH line.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/bin/zsh",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/.local/bin");
            List<string> appendedContent = new List<string>();

            CliPathSetupApplyResult result = CliPathSetupPlanner.ApplyPlan(
                plan,
                path => true,
                path => "# existing",
                path => new DirectoryInfo(path),
                (path, content) => appendedContent.Add(content));

            Assert.That(result.Success, Is.True);
            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.Applied));
            Assert.That(appendedContent, Has.Count.EqualTo(1));
            Assert.That(appendedContent[0], Is.EqualTo("\nexport PATH=\"$HOME/.local/bin:$PATH\"\n"));
        }

        [Test]
        public void ApplyPlan_WhenReadFailsReturnsFailure()
        {
            // Verifies that profile read failures return a user-recoverable result.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/bin/zsh",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/.local/bin");

            CliPathSetupApplyResult result = CliPathSetupPlanner.ApplyPlan(
                plan,
                path => true,
                path => throw new IOException("read denied"),
                path => new DirectoryInfo(path),
                (path, content) => { });

            Assert.That(result.Success, Is.False);
            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.Failed));
            Assert.That(result.ErrorOutput, Does.Contain("read denied"));
        }

        [Test]
        public void ApplyPlan_WhenAppendFailsReturnsFailure()
        {
            // Verifies that profile append failures return a user-recoverable result.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/bin/zsh",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/.local/bin");

            CliPathSetupApplyResult result = CliPathSetupPlanner.ApplyPlan(
                plan,
                path => true,
                path => "",
                path => new DirectoryInfo(path),
                (path, content) => throw new UnauthorizedAccessException("append denied"));

            Assert.That(result.Success, Is.False);
            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.Failed));
            Assert.That(result.ErrorOutput, Does.Contain("append denied"));
        }
    }
}
