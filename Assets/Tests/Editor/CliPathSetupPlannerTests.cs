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
        public void BuildPosixPlan_WhenShellIsBashAndBashrcExistsUsesBashProfile()
        {
            // Verifies that bash targets the profile read by login shells, even when bashrc exists.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/bin/bash",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/.local/bin");

            Assert.That(plan.ShellKind, Is.EqualTo(CliPathSetupShellKind.Bash));
            Assert.That(plan.ConfigurationFilePath, Is.EqualTo("/Users/ExampleUser/.bash_profile"));
            Assert.That(plan.ConfigurationLine, Is.EqualTo("export PATH=\"$HOME/.local/bin:$PATH\""));
        }

        [Test]
        public void BuildPosixPlan_WhenShellIsBashWithoutBashrcUsesBashProfile()
        {
            // Verifies that bash has a deterministic profile target when bashrc is absent.
            CliPathSetupPlan plan = CliPathSetupPlanner.BuildPosixPlan(
                "/bin/bash",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/.local/bin");

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
        public void ApplyPlan_WhenHomePathUsesTildeSkipsAppend()
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
                path => "export PATH=\"~/.local/bin:$PATH\"\n",
                path => new DirectoryInfo(path),
                (path, content) => { appendCount++; });

            Assert.That(result.Success, Is.True);
            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.AlreadyConfigured));
            Assert.That(appendCount, Is.EqualTo(0));
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
