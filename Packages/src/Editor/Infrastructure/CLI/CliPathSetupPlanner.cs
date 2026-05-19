using System;
using System.IO;
using System.Security;
using System.Text;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Builds and applies POSIX shell PATH setup plans for the package-owned native CLI.
    /// </summary>
    internal static class CliPathSetupPlanner
    {
        private const string ZSH_CONFIGURATION_FILE_NAME = ".zshrc";
        private const string BASH_PROFILE_FILE_NAME = ".bash_profile";
        private const string BASH_LOGIN_FILE_NAME = ".bash_login";
        private const string POSIX_PROFILE_FILE_NAME = ".profile";
        private const string FISH_CONFIGURATION_DIRECTORY = ".config/fish";
        private const string FISH_CONFIGURATION_FILE_NAME = "config.fish";
        private const string ZDOTDIR_ENVIRONMENT_VARIABLE = "ZDOTDIR";
        private const string HOME_REFERENCE = "$HOME";
        private const string PATH_ENVIRONMENT_VARIABLE_NAME = "PATH";
        private const string FISH_ADD_PATH_COMMAND = "fish_add_path";
        private const int ZSH_CONFIGURATION_ROOT_PROCESS_TIMEOUT_MS = 5000;
        private const string ZSH_CONFIGURATION_ROOT_START_MARKER = "__ULOOP_ZDOTDIR_START__";
        private const string ZSH_CONFIGURATION_ROOT_END_MARKER = "__ULOOP_ZDOTDIR_END__";

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

            string shellPath = NodeEnvironmentResolver.GetUserShell();
            string homeDirectory = Environment.GetEnvironmentVariable(CliConstants.POSIX_HOME_ENVIRONMENT_VARIABLE);
            string zDotDirectory = ResolveZshConfigurationRoot(
                shellPath,
                homeDirectory,
                Environment.GetEnvironmentVariable(ZDOTDIR_ENVIRONMENT_VARIABLE),
                ResolveZshConfigurationRootFromLoginShell);

            return BuildPosixPlan(
                shellPath,
                homeDirectory,
                zDotDirectory,
                installDirectory,
                File.Exists);
        }

        internal static CliPathSetupPlan BuildPosixPlan(
            string shellPath,
            string homeDirectory,
            string zDotDirectory,
            string installDirectory,
            Func<string, bool> fileExists = null)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(installDirectory), "installDirectory must not be null or empty");

            string resolvedHomeDirectory = string.IsNullOrWhiteSpace(homeDirectory)
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : homeDirectory;
            string shellName = GetShellName(shellPath);
            string profileInstallDirectory = FormatInstallDirectoryForProfile(installDirectory, resolvedHomeDirectory);
            Func<string, bool> resolvedFileExists = fileExists ?? File.Exists;

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
                string configurationPath = SelectBashConfigurationPath(resolvedHomeDirectory, resolvedFileExists);
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

        internal static string ResolveZshConfigurationRoot(
            string shellPath,
            string homeDirectory,
            string environmentZDotDirectory,
            Func<string, string, string> resolveFromLoginShell)
        {
            Debug.Assert(resolveFromLoginShell != null, "resolveFromLoginShell must not be null");

            if (!string.Equals(GetShellName(shellPath), "zsh", StringComparison.Ordinal))
            {
                return environmentZDotDirectory;
            }

            if (!string.IsNullOrWhiteSpace(environmentZDotDirectory))
            {
                return environmentZDotDirectory;
            }

            string resolvedRoot = resolveFromLoginShell(shellPath, homeDirectory);
            if (!string.IsNullOrWhiteSpace(resolvedRoot))
            {
                return resolvedRoot;
            }

            return homeDirectory;
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

            string existingContent = string.Empty;
            try
            {
                existingContent = fileExists(plan.ConfigurationFilePath)
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
            catch (IOException ex)
            {
                return BuildFileSystemFailure(ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                return BuildFileSystemFailure(ex);
            }
            catch (SecurityException ex)
            {
                return BuildFileSystemFailure(ex);
            }
        }

        internal static bool ContainsInstallDirectoryReference(string content, CliPathSetupPlan plan)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(plan.InstallDirectory), "plan.InstallDirectory must not be null or empty");
            Debug.Assert(!string.IsNullOrWhiteSpace(plan.ProfileInstallDirectory), "plan.ProfileInstallDirectory must not be null or empty");

            if (string.IsNullOrEmpty(content))
            {
                return false;
            }

            string[] pathReferences = BuildPathReferenceCandidates(plan);
            string[] lines = content.Replace("\r\n", "\n").Split('\n');
            foreach (string line in lines)
            {
                if (ContainsPathSetupLine(line, pathReferences, plan.ShellKind))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsPathSetupLine(
            string line,
            string[] pathReferences,
            CliPathSetupShellKind shellKind)
        {
            Debug.Assert(line != null, "line must not be null");
            Debug.Assert(pathReferences != null, "pathReferences must not be null");

            string trimmedLine = line.TrimStart();
            if (trimmedLine.StartsWith("#", StringComparison.Ordinal))
            {
                return false;
            }

            foreach (string pathReference in pathReferences)
            {
                if (!ContainsDelimitedPathReference(line, pathReference))
                {
                    continue;
                }

                return shellKind == CliPathSetupShellKind.Fish
                    ? StartsWithShellCommand(trimmedLine, FISH_ADD_PATH_COMMAND)
                    : ContainsPosixPathAssignment(trimmedLine);
            }

            return false;
        }

        private static string[] BuildPathReferenceCandidates(CliPathSetupPlan plan)
        {
            if (!plan.ProfileInstallDirectory.StartsWith(HOME_REFERENCE + "/", StringComparison.Ordinal))
            {
                return new[] { plan.InstallDirectory, plan.ProfileInstallDirectory };
            }

            string homeRelativeSuffix = plan.ProfileInstallDirectory.Substring(HOME_REFERENCE.Length);
            return new[]
            {
                plan.InstallDirectory,
                plan.ProfileInstallDirectory,
                "${HOME}" + homeRelativeSuffix,
                "~" + homeRelativeSuffix
            };
        }

        private static bool ContainsPosixPathAssignment(string line)
        {
            int searchStartIndex = 0;
            while (searchStartIndex < line.Length)
            {
                int pathIndex = line.IndexOf(PATH_ENVIRONMENT_VARIABLE_NAME, searchStartIndex, StringComparison.Ordinal);
                if (pathIndex < 0)
                {
                    return false;
                }

                int afterPathIndex = pathIndex + PATH_ENVIRONMENT_VARIABLE_NAME.Length;
                if (IsShellNameStartBoundary(line, pathIndex - 1)
                    && IsShellNameEndBoundary(line, afterPathIndex)
                    && IsAssignmentAfterToken(line, afterPathIndex))
                {
                    return true;
                }

                searchStartIndex = afterPathIndex;
            }

            return false;
        }

        private static bool IsAssignmentAfterToken(string line, int index)
        {
            int cursor = index;
            while (cursor < line.Length && char.IsWhiteSpace(line[cursor]))
            {
                cursor++;
            }

            return cursor < line.Length && line[cursor] == '=';
        }

        private static bool StartsWithShellCommand(string line, string command)
        {
            Debug.Assert(line != null, "line must not be null");
            Debug.Assert(!string.IsNullOrEmpty(command), "command must not be null or empty");

            return line.StartsWith(command, StringComparison.Ordinal)
                && IsShellNameEndBoundary(line, command.Length);
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

        private static bool IsShellNameStartBoundary(string line, int index)
        {
            if (index < 0)
            {
                return true;
            }

            char character = line[index];
            return !IsShellNameCharacter(character);
        }

        private static bool IsShellNameEndBoundary(string line, int index)
        {
            if (index >= line.Length)
            {
                return true;
            }

            char character = line[index];
            return !IsShellNameCharacter(character);
        }

        private static bool IsShellNameCharacter(char character)
        {
            return char.IsLetterOrDigit(character)
                || character == '_';
        }

        private static CliPathSetupApplyResult BuildFileSystemFailure(Exception ex)
        {
            return new CliPathSetupApplyResult(
                false,
                CliPathSetupApplyStatus.Failed,
                ex.Message);
        }

        private static string ResolveZshConfigurationRootFromLoginShell(string shellPath, string homeDirectory)
        {
            if (!string.Equals(GetShellName(shellPath), "zsh", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(shellPath))
            {
                return string.Empty;
            }

            System.Diagnostics.ProcessStartInfo startInfo = new()
            {
                FileName = shellPath,
                Arguments = "-l -c " + QuoteProcessArgument(BuildZshConfigurationRootProbeCommand()),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            if (!string.IsNullOrWhiteSpace(homeDirectory))
            {
                startInfo.EnvironmentVariables[CliConstants.POSIX_HOME_ENVIRONMENT_VARIABLE] = homeDirectory;
            }

            string output = ExecuteZshConfigurationRootProbe(startInfo);
            string block = NodeEnvironmentResolver.ExtractBetweenMarkers(
                output,
                ZSH_CONFIGURATION_ROOT_START_MARKER,
                ZSH_CONFIGURATION_ROOT_END_MARKER);
            return ExtractFirstNonEmptyLine(block);
        }

        private static string BuildZshConfigurationRootProbeCommand()
        {
            return "printf '%s\\n' " + QuotePosixShellValue(ZSH_CONFIGURATION_ROOT_START_MARKER) + "\n"
                + "printf '%s\\n' \"${ZDOTDIR:-$HOME}\"\n"
                + "printf '%s\\n' " + QuotePosixShellValue(ZSH_CONFIGURATION_ROOT_END_MARKER);
        }

        private static string ExecuteZshConfigurationRootProbe(System.Diagnostics.ProcessStartInfo startInfo)
        {
            Debug.Assert(startInfo != null, "startInfo must not be null");
            Debug.Assert(startInfo.RedirectStandardOutput, "RedirectStandardOutput must be true");
            Debug.Assert(startInfo.RedirectStandardError, "RedirectStandardError must be true");

            System.Diagnostics.Process process = ProcessStartHelper.TryStart(startInfo);
            if (process == null)
            {
                return string.Empty;
            }

            using (process)
            {
                StringBuilder outputBuilder = new StringBuilder();
                process.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        outputBuilder.AppendLine(e.Data);
                    }
                };
                process.ErrorDataReceived += (sender, e) => { };

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                bool exited = process.WaitForExit(ZSH_CONFIGURATION_ROOT_PROCESS_TIMEOUT_MS);
                if (!exited)
                {
                    CliInstallationDetector.KillProcessIfRunning(process);
                    return string.Empty;
                }

                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    return string.Empty;
                }

                return outputBuilder.ToString();
            }
        }

        private static string ExtractFirstNonEmptyLine(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return string.Empty;
            }

            string[] lines = content.Replace("\r\n", "\n").Split('\n');
            foreach (string line in lines)
            {
                string trimmedLine = line.Trim();
                if (!string.IsNullOrEmpty(trimmedLine))
                {
                    return trimmedLine;
                }
            }

            return string.Empty;
        }

        private static string SelectBashConfigurationPath(string homeDirectory, Func<string, bool> fileExists)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(homeDirectory), "homeDirectory must not be null or empty");
            Debug.Assert(fileExists != null, "fileExists must not be null");

            string[] candidateFileNames =
            {
                BASH_PROFILE_FILE_NAME,
                BASH_LOGIN_FILE_NAME,
                POSIX_PROFILE_FILE_NAME
            };
            foreach (string candidateFileName in candidateFileNames)
            {
                string candidatePath = Path.Combine(homeDirectory, candidateFileName);
                if (fileExists(candidatePath))
                {
                    return candidatePath;
                }
            }

            return Path.Combine(homeDirectory, BASH_PROFILE_FILE_NAME);
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

        private static string QuoteProcessArgument(string value)
        {
            Debug.Assert(value != null, "value must not be null");
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
