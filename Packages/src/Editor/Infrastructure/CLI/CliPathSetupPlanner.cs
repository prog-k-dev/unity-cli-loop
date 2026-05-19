using System;
using System.IO;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Builds and applies POSIX shell PATH setup plans for the package-owned native CLI.
    /// </summary>
    internal static class CliPathSetupPlanner
    {
        private const string ZSH_CONFIGURATION_FILE_NAME = ".zshrc";
        private const string BASH_PROFILE_FILE_NAME = ".bash_profile";
        private const string BASH_RC_FILE_NAME = ".bashrc";
        private const string FISH_CONFIGURATION_DIRECTORY = ".config/fish";
        private const string FISH_CONFIGURATION_FILE_NAME = "config.fish";
        private const string ZDOTDIR_ENVIRONMENT_VARIABLE = "ZDOTDIR";
        private const string HOME_REFERENCE = "$HOME";

        public static CliPathSetupPlan BuildCurrentUserPlan(RuntimePlatform platform)
        {
            string installDirectory = NativeCliInstaller.GetCurrentUserGlobalCliInstallDirectory(platform);
            if (string.IsNullOrWhiteSpace(installDirectory))
            {
                return new CliPathSetupPlan(
                    CliPathSetupShellKind.Unsupported,
                    "unknown",
                    false,
                    CliConstants.EXECUTABLE_NAME,
                    CliConstants.EXECUTABLE_NAME,
                    "",
                    "",
                    $"Add the uLoop CLI install directory to {CliConstants.POSIX_PATH_ENVIRONMENT_VARIABLE}.");
            }

            if (platform == RuntimePlatform.WindowsEditor)
            {
                return new CliPathSetupPlan(
                    CliPathSetupShellKind.Unsupported,
                    "windows",
                    false,
                    installDirectory,
                    installDirectory,
                    "",
                    "",
                    "Windows User PATH is managed by the Windows installer.");
            }

            return BuildPosixPlan(
                NodeEnvironmentResolver.GetUserShell(),
                Environment.GetEnvironmentVariable(CliConstants.POSIX_HOME_ENVIRONMENT_VARIABLE),
                Environment.GetEnvironmentVariable(ZDOTDIR_ENVIRONMENT_VARIABLE),
                installDirectory,
                File.Exists);
        }

        internal static CliPathSetupPlan BuildPosixPlan(
            string shellPath,
            string homeDirectory,
            string zDotDirectory,
            string installDirectory,
            Func<string, bool> fileExists)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(installDirectory), "installDirectory must not be null or empty");
            Debug.Assert(fileExists != null, "fileExists must not be null");

            string resolvedHomeDirectory = string.IsNullOrWhiteSpace(homeDirectory)
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : homeDirectory;
            string shellName = GetShellName(shellPath);
            string profileInstallDirectory = FormatInstallDirectoryForProfile(installDirectory, resolvedHomeDirectory);

            if (string.Equals(shellName, "zsh", StringComparison.Ordinal))
            {
                string configurationRoot = string.IsNullOrWhiteSpace(zDotDirectory)
                    ? resolvedHomeDirectory
                    : zDotDirectory;
                string configurationPath = Path.Combine(configurationRoot, ZSH_CONFIGURATION_FILE_NAME);
                string configurationLine = BuildPosixExportLine(profileInstallDirectory);
                return BuildSupportedPlan(
                    CliPathSetupShellKind.Zsh,
                    shellName,
                    installDirectory,
                    configurationPath,
                    configurationLine);
            }

            if (string.Equals(shellName, "bash", StringComparison.Ordinal))
            {
                string bashRcPath = Path.Combine(resolvedHomeDirectory, BASH_RC_FILE_NAME);
                string configurationPath = fileExists(bashRcPath)
                    ? bashRcPath
                    : Path.Combine(resolvedHomeDirectory, BASH_PROFILE_FILE_NAME);
                string configurationLine = BuildPosixExportLine(profileInstallDirectory);
                return BuildSupportedPlan(
                    CliPathSetupShellKind.Bash,
                    shellName,
                    installDirectory,
                    configurationPath,
                    configurationLine);
            }

            if (string.Equals(shellName, "fish", StringComparison.Ordinal))
            {
                string configurationPath = Path.Combine(
                    resolvedHomeDirectory,
                    FISH_CONFIGURATION_DIRECTORY,
                    FISH_CONFIGURATION_FILE_NAME);
                string configurationLine = $"fish_add_path \"{profileInstallDirectory}\"";
                return BuildSupportedPlan(
                    CliPathSetupShellKind.Fish,
                    shellName,
                    installDirectory,
                    configurationPath,
                    configurationLine);
            }

            return new CliPathSetupPlan(
                CliPathSetupShellKind.Unsupported,
                string.IsNullOrWhiteSpace(shellName) ? "unknown" : shellName,
                false,
                installDirectory,
                installDirectory,
                "",
                "",
                $"Add {installDirectory} to {CliConstants.POSIX_PATH_ENVIRONMENT_VARIABLE} in your shell profile.");
        }

        public static CliPathSetupApplyResult ApplyPlanToFileSystem(CliPathSetupPlan plan)
        {
            return ApplyPlan(
                plan,
                File.Exists,
                File.ReadAllText,
                Directory.CreateDirectory,
                File.AppendAllText);
        }

        internal static CliPathSetupApplyResult ApplyPlan(
            CliPathSetupPlan plan,
            Func<string, bool> fileExists,
            Func<string, string> readAllText,
            Func<string, DirectoryInfo> createDirectory,
            Action<string, string> appendAllText)
        {
            Debug.Assert(fileExists != null, "fileExists must not be null");
            Debug.Assert(readAllText != null, "readAllText must not be null");
            Debug.Assert(createDirectory != null, "createDirectory must not be null");
            Debug.Assert(appendAllText != null, "appendAllText must not be null");

            if (!plan.CanApplyAutomatically)
            {
                return new CliPathSetupApplyResult(
                    false,
                    CliPathSetupApplyStatus.Unsupported,
                    "This shell is not supported for automatic PATH setup.");
            }

            string existingContent = fileExists(plan.ConfigurationFilePath)
                ? readAllText(plan.ConfigurationFilePath)
                : string.Empty;
            if (ContainsInstallDirectoryReference(existingContent, plan))
            {
                return new CliPathSetupApplyResult(
                    true,
                    CliPathSetupApplyStatus.AlreadyConfigured,
                    "");
            }

            string directory = Path.GetDirectoryName(plan.ConfigurationFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                createDirectory(directory);
            }

            string prefix = NeedsLeadingNewLine(existingContent) ? Environment.NewLine : string.Empty;
            appendAllText(
                plan.ConfigurationFilePath,
                prefix + plan.ConfigurationLine + Environment.NewLine);
            return new CliPathSetupApplyResult(
                true,
                CliPathSetupApplyStatus.Applied,
                "");
        }

        internal static bool ContainsInstallDirectoryReference(string content, CliPathSetupPlan plan)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(plan.InstallDirectory), "plan.InstallDirectory must not be null or empty");
            Debug.Assert(!string.IsNullOrWhiteSpace(plan.ProfileInstallDirectory), "plan.ProfileInstallDirectory must not be null or empty");

            if (string.IsNullOrEmpty(content))
            {
                return false;
            }

            if (ContainsPathReference(content, plan.InstallDirectory))
            {
                return true;
            }

            if (ContainsPathReference(content, plan.ProfileInstallDirectory))
            {
                return true;
            }

            if (!plan.ProfileInstallDirectory.StartsWith(HOME_REFERENCE + "/", StringComparison.Ordinal))
            {
                return false;
            }

            string homeRelativeSuffix = plan.ProfileInstallDirectory.Substring(HOME_REFERENCE.Length);
            string bracedHomeReference = "${HOME}" + homeRelativeSuffix;
            if (ContainsPathReference(content, bracedHomeReference))
            {
                return true;
            }

            string tildeReference = "~" + homeRelativeSuffix;
            return ContainsPathReference(content, tildeReference);
        }

        private static bool ContainsPathReference(string content, string pathReference)
        {
            Debug.Assert(pathReference != null, "pathReference must not be null");

            string[] lines = content.Replace("\r\n", "\n").Split('\n');
            foreach (string line in lines)
            {
                string trimmedLine = line.TrimStart();
                if (trimmedLine.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                if (ContainsDelimitedPathReference(line, pathReference))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsDelimitedPathReference(string line, string pathReference)
        {
            Debug.Assert(line != null, "line must not be null");
            Debug.Assert(pathReference != null, "pathReference must not be null");

            int searchStartIndex = 0;
            while (searchStartIndex < line.Length)
            {
                int pathIndex = line.IndexOf(pathReference, searchStartIndex, StringComparison.Ordinal);
                if (pathIndex < 0)
                {
                    return false;
                }

                int beforeIndex = pathIndex - 1;
                int afterIndex = pathIndex + pathReference.Length;
                if (IsPathReferenceStartBoundary(line, beforeIndex)
                    && IsPathReferenceEndBoundary(line, afterIndex))
                {
                    return true;
                }

                searchStartIndex = pathIndex + pathReference.Length;
            }

            return false;
        }

        private static bool IsPathReferenceStartBoundary(string line, int index)
        {
            if (index < 0)
            {
                return true;
            }

            char character = line[index];
            return char.IsWhiteSpace(character)
                || character == '"'
                || character == '\''
                || character == '='
                || character == ':'
                || character == '('
                || character == '['
                || character == '{';
        }

        private static bool IsPathReferenceEndBoundary(string line, int index)
        {
            if (index >= line.Length)
            {
                return true;
            }

            char character = line[index];
            return char.IsWhiteSpace(character)
                || character == '"'
                || character == '\''
                || character == ':'
                || character == ';'
                || character == ')'
                || character == ']'
                || character == '}'
                || character == '$';
        }

        private static CliPathSetupPlan BuildSupportedPlan(
            CliPathSetupShellKind shellKind,
            string shellName,
            string installDirectory,
            string configurationPath,
            string configurationLine)
        {
            return new CliPathSetupPlan(
                shellKind,
                shellName,
                true,
                installDirectory,
                ExtractProfileInstallDirectory(configurationLine, installDirectory),
                configurationPath,
                configurationLine,
                BuildManualCommand(configurationPath, configurationLine));
        }

        private static string ExtractProfileInstallDirectory(string configurationLine, string fallbackInstallDirectory)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(configurationLine), "configurationLine must not be null or empty");
            Debug.Assert(!string.IsNullOrWhiteSpace(fallbackInstallDirectory), "fallbackInstallDirectory must not be null or empty");

            int firstQuoteIndex = configurationLine.IndexOf('"');
            if (firstQuoteIndex < 0)
            {
                return fallbackInstallDirectory;
            }

            int secondQuoteIndex = configurationLine.IndexOf('"', firstQuoteIndex + 1);
            if (secondQuoteIndex <= firstQuoteIndex)
            {
                return fallbackInstallDirectory;
            }

            string quotedValue = configurationLine.Substring(firstQuoteIndex + 1, secondQuoteIndex - firstQuoteIndex - 1);
            int pathSeparatorIndex = quotedValue.IndexOf(':');
            if (pathSeparatorIndex < 0)
            {
                return quotedValue;
            }

            return quotedValue.Substring(0, pathSeparatorIndex);
        }

        private static string BuildManualCommand(string configurationPath, string configurationLine)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(configurationPath), "configurationPath must not be null or empty");
            Debug.Assert(!string.IsNullOrWhiteSpace(configurationLine), "configurationLine must not be null or empty");

            string configurationDirectory = Path.GetDirectoryName(configurationPath);
            if (string.IsNullOrEmpty(configurationDirectory))
            {
                return $"echo {QuotePosixShellValue(configurationLine)} >> {QuotePosixShellValue(configurationPath)}";
            }

            return $"mkdir -p {QuotePosixShellValue(configurationDirectory)} && "
                + $"echo {QuotePosixShellValue(configurationLine)} >> {QuotePosixShellValue(configurationPath)}";
        }

        private static string GetShellName(string shellPath)
        {
            if (string.IsNullOrWhiteSpace(shellPath))
            {
                return string.Empty;
            }

            return Path.GetFileName(shellPath.Trim()).ToLowerInvariant();
        }

        private static string FormatInstallDirectoryForProfile(string installDirectory, string homeDirectory)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(installDirectory), "installDirectory must not be null or empty");

            if (string.IsNullOrWhiteSpace(homeDirectory))
            {
                return installDirectory;
            }

            string normalizedHomeDirectory = homeDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(installDirectory, normalizedHomeDirectory, StringComparison.Ordinal))
            {
                return HOME_REFERENCE;
            }

            string homePrefix = normalizedHomeDirectory + Path.DirectorySeparatorChar;
            if (installDirectory.StartsWith(homePrefix, StringComparison.Ordinal))
            {
                return HOME_REFERENCE + "/" + installDirectory.Substring(homePrefix.Length);
            }

            return installDirectory;
        }

        private static string BuildPosixExportLine(string installDirectory)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(installDirectory), "installDirectory must not be null or empty");

            return $"export PATH=\"{installDirectory}:$PATH\"";
        }

        private static bool NeedsLeadingNewLine(string content)
        {
            return !string.IsNullOrEmpty(content)
                && !content.EndsWith("\n", StringComparison.Ordinal);
        }

        private static string QuotePosixShellValue(string value)
        {
            Debug.Assert(value != null, "value must not be null");
            return $"'{value.Replace("'", "'\"'\"'")}'";
        }
    }
}
