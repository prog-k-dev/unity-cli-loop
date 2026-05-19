using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Identifies the shell family that controls the CLI PATH setup target.
    /// </summary>
    public enum CliPathSetupShellKind
    {
        Unsupported,
        Zsh,
        Bash,
        Fish
    }

    /// <summary>
    /// Reports the outcome of a shell PATH setup write attempt.
    /// </summary>
    public enum CliPathSetupApplyStatus
    {
        Applied,
        AlreadyConfigured,
        Unsupported
    }

    /// <summary>
    /// Carries the shell-specific file, line, and command needed for explicit CLI PATH setup.
    /// </summary>
    public readonly struct CliPathSetupPlan
    {
        public CliPathSetupPlan(
            CliPathSetupShellKind shellKind,
            string shellName,
            bool canApplyAutomatically,
            string installDirectory,
            string profileInstallDirectory,
            string configurationFilePath,
            string configurationLine,
            string manualCommand)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(installDirectory), "installDirectory must not be null or empty");
            Debug.Assert(!string.IsNullOrWhiteSpace(profileInstallDirectory), "profileInstallDirectory must not be null or empty");
            Debug.Assert(!string.IsNullOrWhiteSpace(manualCommand), "manualCommand must not be null or empty");
            if (canApplyAutomatically)
            {
                Debug.Assert(!string.IsNullOrWhiteSpace(configurationFilePath), "configurationFilePath must not be null or empty");
                Debug.Assert(!string.IsNullOrWhiteSpace(configurationLine), "configurationLine must not be null or empty");
            }

            ShellKind = shellKind;
            ShellName = shellName ?? string.Empty;
            CanApplyAutomatically = canApplyAutomatically;
            InstallDirectory = installDirectory;
            ProfileInstallDirectory = profileInstallDirectory;
            ConfigurationFilePath = configurationFilePath ?? string.Empty;
            ConfigurationLine = configurationLine ?? string.Empty;
            ManualCommand = manualCommand;
        }

        public CliPathSetupShellKind ShellKind { get; }
        public string ShellName { get; }
        public bool CanApplyAutomatically { get; }
        public string InstallDirectory { get; }
        public string ProfileInstallDirectory { get; }
        public string ConfigurationFilePath { get; }
        public string ConfigurationLine { get; }
        public string ManualCommand { get; }
    }

    /// <summary>
    /// Reports whether shell PATH setup wrote a file, found an existing setting, or needs manual setup.
    /// </summary>
    public readonly struct CliPathSetupApplyResult
    {
        public CliPathSetupApplyResult(
            bool success,
            CliPathSetupApplyStatus status,
            string errorOutput)
        {
            Success = success;
            Status = status;
            ErrorOutput = errorOutput ?? string.Empty;
        }

        public bool Success { get; }
        public CliPathSetupApplyStatus Status { get; }
        public string ErrorOutput { get; }
    }
}
