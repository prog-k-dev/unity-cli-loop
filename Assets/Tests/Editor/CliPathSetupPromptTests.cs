using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Presentation;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies CLI PATH setup prompt decisions.
    /// </summary>
    public class CliPathSetupPromptTests
    {
        [Test]
        public void ShouldShowAfterInstall_OnMacWhenShellCannotResolveCliReturnsTrue()
        {
            // Verifies that successful POSIX installs still prompt when terminal PATH is incomplete.
            bool result = CliPathSetupPrompt.ShouldShowAfterInstall(
                RuntimePlatform.OSXEditor,
                false);

            Assert.That(result, Is.True);
        }

        [Test]
        public void ShouldShowAfterInstall_OnWindowsReturnsFalse()
        {
            // Verifies that Windows keeps using the installer-managed User PATH flow.
            bool result = CliPathSetupPrompt.ShouldShowAfterInstall(
                RuntimePlatform.WindowsEditor,
                false);

            Assert.That(result, Is.False);
        }

        [Test]
        public void BuildMessage_WhenPlanCanApplyAutomaticallyMentionsProfile()
        {
            // Verifies that supported plan messages can still show the exact file and line.
            CliPathSetupPlan plan = new CliPathSetupPlan(
                CliPathSetupShellKind.Zsh,
                "zsh",
                true,
                "/Users/ExampleUser/.local/bin",
                "$HOME/.local/bin",
                "/Users/ExampleUser/.zshrc",
                "export PATH=\"$HOME/.local/bin:$PATH\"",
                "printf '\\n%s\\n' 'export PATH=\"$HOME/.local/bin:$PATH\"' >> /Users/ExampleUser/.zshrc");

            string message = CliPathSetupPrompt.BuildMessage(plan);

            Assert.That(message, Does.Contain("/Users/ExampleUser/.zshrc"));
            Assert.That(message, Does.Contain("export PATH=\"$HOME/.local/bin:$PATH\""));
        }

        [Test]
        public void BuildAppliedMessage_MentionsNewTerminal()
        {
            // Verifies that automatic UI setup tells users how to use the updated PATH.
            CliPathSetupPlan plan = new CliPathSetupPlan(
                CliPathSetupShellKind.Zsh,
                "zsh",
                true,
                "/Users/ExampleUser/.local/bin",
                "$HOME/.local/bin",
                "/Users/ExampleUser/.zshrc",
                "export PATH=\"$HOME/.local/bin:$PATH\"",
                "printf '\\n%s\\n' 'export PATH=\"$HOME/.local/bin:$PATH\"' >> /Users/ExampleUser/.zshrc");

            string message = CliPathSetupPrompt.BuildAppliedMessage(plan);

            Assert.That(message, Does.Contain("PATH setup was updated"));
            Assert.That(message, Does.Contain("Open a new terminal"));
        }

        [Test]
        public void BuildAlreadyConfiguredMessage_MentionsExistingProfile()
        {
            // Verifies that existing profile settings do not look like a failed install.
            CliPathSetupPlan plan = new CliPathSetupPlan(
                CliPathSetupShellKind.Zsh,
                "zsh",
                true,
                "/Users/ExampleUser/.local/bin",
                "$HOME/.local/bin",
                "/Users/ExampleUser/.zshrc",
                "export PATH=\"$HOME/.local/bin:$PATH\"",
                "printf '\\n%s\\n' 'export PATH=\"$HOME/.local/bin:$PATH\"' >> /Users/ExampleUser/.zshrc");

            string message = CliPathSetupPrompt.BuildAlreadyConfiguredMessage(plan);

            Assert.That(message, Does.Contain("already contains"));
            Assert.That(message, Does.Contain("source your shell profile"));
        }

        [Test]
        public void ShouldReportPathSetupComplete_WhenShellStillCannotResolveReturnsFalse()
        {
            // Verifies that UI success is gated on a fresh shell resolving uloop after repair.
            bool result = CliPathSetupPrompt.ShouldReportPathSetupComplete(false);

            Assert.That(result, Is.False);
        }

        [Test]
        public void BuildStillNotVisibleMessage_MentionsLaterStartupFiles()
        {
            // Verifies that ineffective existing PATH lines guide users toward later shell resets.
            CliPathSetupPlan plan = new CliPathSetupPlan(
                CliPathSetupShellKind.Zsh,
                "zsh",
                true,
                "/Users/ExampleUser/.local/bin",
                "$HOME/.local/bin",
                "/Users/ExampleUser/.zshrc",
                "export PATH=\"$HOME/.local/bin:$PATH\"",
                "printf '\\n%s\\n' 'export PATH=\"$HOME/.local/bin:$PATH\"' >> /Users/ExampleUser/.zshrc");

            string message = CliPathSetupPrompt.BuildStillNotVisibleMessage(plan);

            Assert.That(message, Does.Contain("still cannot find"));
            Assert.That(message, Does.Contain("later shell startup files"));
            Assert.That(message, Does.Contain(plan.ManualCommand));
        }
    }
}
