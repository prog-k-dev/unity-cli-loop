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
            // Verifies that the UI explains the exact file and line before the user clicks Add.
            CliPathSetupPlan plan = new(
                CliPathSetupShellKind.Zsh,
                "zsh",
                true,
                "/Users/ExampleUser/.local/bin",
                "/Users/ExampleUser/.zshrc",
                "export PATH=\"$HOME/.local/bin:$PATH\"",
                "echo 'export PATH=\"$HOME/.local/bin:$PATH\"' >> /Users/ExampleUser/.zshrc");

            string message = CliPathSetupPrompt.BuildMessage(plan);

            Assert.That(message, Does.Contain("/Users/ExampleUser/.zshrc"));
            Assert.That(message, Does.Contain("export PATH=\"$HOME/.local/bin:$PATH\""));
        }
    }
}
