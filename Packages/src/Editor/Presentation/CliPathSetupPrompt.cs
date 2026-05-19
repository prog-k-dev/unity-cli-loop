using System.Threading;
using System.Threading.Tasks;

using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Completes shell PATH setup after the user starts native CLI installation from Unity UI.
    /// </summary>
    internal static class CliPathSetupPrompt
    {
        private const string DialogTitle = "Finish uLoop CLI PATH Setup";
        private const string CopyButtonText = "Copy Command";
        private const string OkButtonText = "OK";

        public static async Task ShowAfterInstallIfNeededAsync(RuntimePlatform platform, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            bool isVisibleFromShell = await CliSetupApplicationFacade.IsCliVisibleFromShellAsync(platform, ct);
            if (!ShouldShowAfterInstall(platform, isVisibleFromShell))
            {
                return;
            }

            CliPathSetupPlan plan = CliSetupApplicationFacade.GetGlobalCliPathSetupPlan(platform);
            CompletePathSetup(plan);
        }

        internal static bool ShouldShowAfterInstall(RuntimePlatform platform, bool isVisibleFromShell)
        {
            return platform != RuntimePlatform.WindowsEditor && !isVisibleFromShell;
        }

        internal static string BuildMessage(CliPathSetupPlan plan)
        {
            string shellText = string.IsNullOrWhiteSpace(plan.ShellName)
                ? "your shell"
                : plan.ShellName;
            if (!plan.CanApplyAutomatically)
            {
                return "The uLoop CLI was installed, but your terminal cannot find the uloop command yet.\n\n"
                    + $"Detected shell: {shellText}\n"
                    + $"Install directory: {plan.InstallDirectory}\n\n"
                    + "Add the install directory to PATH in your shell profile.\n\n"
                    + plan.ManualCommand;
            }

            return "The uLoop CLI was installed, but your terminal cannot find the uloop command yet.\n\n"
                + $"Detected shell: {shellText}\n"
                + $"Profile: {plan.ConfigurationFilePath}\n"
                + $"Line to add: {plan.ConfigurationLine}";
        }

        internal static string BuildAppliedMessage(CliPathSetupPlan plan)
        {
            return "The uLoop CLI was installed and PATH setup was updated.\n\n"
                + $"Profile: {plan.ConfigurationFilePath}\n"
                + $"Added: {plan.ConfigurationLine}\n\n"
                + "Open a new terminal window to use uloop.";
        }

        internal static string BuildAlreadyConfiguredMessage(CliPathSetupPlan plan)
        {
            return "The uLoop CLI was installed and your shell profile already contains the PATH setup.\n\n"
                + $"Profile: {plan.ConfigurationFilePath}\n\n"
                + "Open a new terminal window, or source your shell profile in the existing terminal.";
        }

        private static void CompletePathSetup(CliPathSetupPlan plan)
        {
            if (!plan.CanApplyAutomatically)
            {
                ShowUnsupportedShellPrompt(plan);
                return;
            }

            ApplyPathSetup(plan);
        }

        private static void ShowUnsupportedShellPrompt(CliPathSetupPlan plan)
        {
            bool copyCommand = EditorUtility.DisplayDialog(
                DialogTitle,
                BuildMessage(plan),
                CopyButtonText,
                OkButtonText);
            if (!copyCommand)
            {
                return;
            }

            CopyManualCommand(plan.ManualCommand);
        }

        private static void ApplyPathSetup(CliPathSetupPlan plan)
        {
            CliPathSetupApplyResult result = CliSetupApplicationFacade.ApplyGlobalCliPathSetup(plan);
            if (!result.Success)
            {
                EditorUtility.DisplayDialog(
                    "PATH Setup Failed",
                    $"Could not update your shell profile.\n\n{result.ErrorOutput}\n\nYou can run this manually:\n{plan.ManualCommand}",
                    "OK");
                return;
            }

            CliSetupApplicationFacade.InvalidateCliCache();
            string message = result.Status == CliPathSetupApplyStatus.AlreadyConfigured
                ? BuildAlreadyConfiguredMessage(plan)
                : BuildAppliedMessage(plan);
            EditorUtility.DisplayDialog(
                "PATH Setup Complete",
                message,
                OkButtonText);
        }

        private static void CopyManualCommand(string manualCommand)
        {
            EditorGUIUtility.systemCopyBuffer = manualCommand;
            EditorUtility.DisplayDialog(
                "Command Copied",
                "The PATH setup command has been copied.",
                "OK");
        }
    }
}
