using System.Threading;
using System.Threading.Tasks;

using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Coordinates the explicit user prompt that can update shell PATH setup after native CLI installation.
    /// </summary>
    internal static class CliPathSetupPrompt
    {
        private const string DialogTitle = "Finish uLoop CLI PATH Setup";
        private const string AddButtonText = "Add to PATH";
        private const string LaterButtonText = "Later";
        private const string CopyButtonText = "Copy Command";

        public static async Task ShowAfterInstallIfNeededAsync(RuntimePlatform platform, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            bool isVisibleFromShell = await CliSetupApplicationFacade.IsCliVisibleFromShellAsync(platform, ct);
            if (!ShouldShowAfterInstall(platform, isVisibleFromShell))
            {
                return;
            }

            CliPathSetupPlan plan = CliSetupApplicationFacade.GetGlobalCliPathSetupPlan(platform);
            Show(plan);
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
                + $"Line to add: {plan.ConfigurationLine}\n\n"
                + "Choose Add to PATH to update this profile now. New terminal windows will pick it up automatically.";
        }

        private static void Show(CliPathSetupPlan plan)
        {
            if (!plan.CanApplyAutomatically)
            {
                ShowUnsupportedShellPrompt(plan);
                return;
            }

            int choice = EditorUtility.DisplayDialogComplex(
                DialogTitle,
                BuildMessage(plan),
                AddButtonText,
                LaterButtonText,
                CopyButtonText);
            if (choice == 0)
            {
                ApplyPathSetup(plan);
                return;
            }

            if (choice == 2)
            {
                CopyManualCommand(plan.ManualCommand);
            }
        }

        private static void ShowUnsupportedShellPrompt(CliPathSetupPlan plan)
        {
            bool copyCommand = EditorUtility.DisplayDialog(
                DialogTitle,
                BuildMessage(plan),
                CopyButtonText,
                LaterButtonText);
            if (!copyCommand)
            {
                return;
            }

            CopyManualCommand(plan.ManualCommand);
        }

        private static void ApplyPathSetup(CliPathSetupPlan plan)
        {
            CliInstallResult result = CliSetupApplicationFacade.ApplyGlobalCliPathSetup(plan);
            if (!result.Success)
            {
                EditorUtility.DisplayDialog(
                    "PATH Setup Failed",
                    $"Could not update your shell profile.\n\n{result.ErrorOutput}\n\nYou can run this manually:\n{plan.ManualCommand}",
                    "OK");
                return;
            }

            CliSetupApplicationFacade.InvalidateCliCache();
            EditorUtility.DisplayDialog(
                "PATH Setup Complete",
                "uLoop CLI PATH setup was updated. Open a new terminal window, or source your shell profile in an existing terminal.",
                "OK");
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
