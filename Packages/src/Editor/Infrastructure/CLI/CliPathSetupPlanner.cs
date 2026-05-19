using System;
using System.Collections.Generic;
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
        private const string ZSH_LOGIN_CONFIGURATION_FILE_NAME = ".zlogin";
        private const string BASH_PROFILE_FILE_NAME = ".bash_profile";
        private const string BASH_LOGIN_FILE_NAME = ".bash_login";
        private const string POSIX_PROFILE_FILE_NAME = ".profile";
        private const string DEFAULT_XDG_CONFIGURATION_DIRECTORY = ".config";
        private const string FISH_CONFIGURATION_DIRECTORY_NAME = "fish";
        private const string FISH_CONFIGURATION_FILE_NAME = "config.fish";
        private const string ZDOTDIR_ENVIRONMENT_VARIABLE = "ZDOTDIR";
        private const string XDG_CONFIG_HOME_ENVIRONMENT_VARIABLE = "XDG_CONFIG_HOME";
        private const string HOME_REFERENCE = "$HOME";
        private const string PATH_ENVIRONMENT_VARIABLE_NAME = "PATH";
        private const string POSIX_EXPORT_COMMAND = "export";
        private const string FISH_ADD_PATH_COMMAND = "fish_add_path";
        private const string FISH_SET_COMMAND = "set";
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
                File.Exists,
                Environment.GetEnvironmentVariable(XDG_CONFIG_HOME_ENVIRONMENT_VARIABLE));
        }

        internal static CliPathSetupPlan BuildPosixPlan(
            string shellPath,
            string homeDirectory,
            string zDotDirectory,
            string installDirectory,
            Func<string, bool> fileExists = null,
            string xdgConfigDirectory = null)
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
                string configurationPath = SelectZshConfigurationPath(configurationRoot, resolvedFileExists);
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
                string fishConfigurationRoot = ResolveFishConfigurationRoot(resolvedHomeDirectory, xdgConfigDirectory);
                string configurationPath = Path.Combine(
                    fishConfigurationRoot,
                    FISH_CONFIGURATION_DIRECTORY_NAME,
                    FISH_CONFIGURATION_FILE_NAME);
                string configurationLine = $"fish_add_path \"{EscapeFishDoubleQuotedPathValue(profileInstallDirectory)}\"";
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
                BuildUnsupportedShellManualCommand(installDirectory));
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
            bool isConfigured = false;
            foreach (string line in lines)
            {
                if (TryEvaluatePathSetupLine(line, pathReferences, plan.ShellKind, isConfigured, out bool updatedConfigured))
                {
                    isConfigured = updatedConfigured;
                }
            }

            return isConfigured;
        }

        private static bool TryEvaluatePathSetupLine(
            string line,
            string[] pathReferences,
            CliPathSetupShellKind shellKind,
            bool currentConfigured,
            out bool updatedConfigured)
        {
            Debug.Assert(line != null, "line must not be null");
            Debug.Assert(pathReferences != null, "pathReferences must not be null");

            updatedConfigured = currentConfigured;
            string trimmedLine = line.TrimStart();
            if (trimmedLine.StartsWith("#", StringComparison.Ordinal))
            {
                return false;
            }

            if (shellKind == CliPathSetupShellKind.Fish)
            {
                if (StartsWithShellCommand(trimmedLine, FISH_ADD_PATH_COMMAND))
                {
                    if (!TryGetFishAddPathValueStart(
                            trimmedLine,
                            out int fishAddPathValueStartIndex,
                            out bool fishAddPathUsesAppendFlag))
                    {
                        updatedConfigured = currentConfigured;
                        return true;
                    }

                    updatedConfigured = fishAddPathUsesAppendFlag
                        ? currentConfigured
                        : IsPathValueConfigured(
                            trimmedLine,
                            fishAddPathValueStartIndex,
                            pathReferences,
                            currentConfigured);
                    return true;
                }

                if (TryGetFishPathSetValueStart(
                        trimmedLine,
                        out int fishValueStartIndex,
                        out bool fishPathSetUsesAppendFlag))
                {
                    updatedConfigured = fishPathSetUsesAppendFlag
                        ? currentConfigured
                        : IsPathValueConfigured(
                            trimmedLine,
                            fishValueStartIndex,
                            pathReferences,
                            currentConfigured);
                    return true;
                }

                return false;
            }

            if (!TryGetPosixPathAssignmentValueStart(trimmedLine, out int valueStartIndex))
            {
                return false;
            }

            updatedConfigured = IsPathValueConfigured(
                trimmedLine,
                valueStartIndex,
                pathReferences,
                currentConfigured);
            return true;
        }

        private static bool IsPathValueConfigured(
            string line,
            int valueStartIndex,
            string[] pathReferences,
            bool currentConfigured)
        {
            Debug.Assert(line != null, "line must not be null");
            Debug.Assert(valueStartIndex >= 0, "valueStartIndex must be zero or greater");
            Debug.Assert(pathReferences != null, "pathReferences must not be null");

            foreach (string pathReference in pathReferences)
            {
                if (StartsPathValueWithReference(line, valueStartIndex, pathReference))
                {
                    return true;
                }
            }

            return StartsPathValueWithInheritedPath(line, valueStartIndex) && currentConfigured;
        }

        private static string[] BuildPathReferenceCandidates(CliPathSetupPlan plan)
        {
            List<string> candidates = new List<string>();
            if (!plan.ProfileInstallDirectory.StartsWith(HOME_REFERENCE + "/", StringComparison.Ordinal))
            {
                AddPathReferenceCandidate(candidates, plan.InstallDirectory);
                AddPathReferenceCandidate(candidates, plan.ProfileInstallDirectory);
                return candidates.ToArray();
            }

            string homeRelativeSuffix = plan.ProfileInstallDirectory.Substring(HOME_REFERENCE.Length);
            AddPathReferenceCandidate(candidates, plan.InstallDirectory);
            AddPathReferenceCandidate(candidates, plan.ProfileInstallDirectory);
            AddPathReferenceCandidate(candidates, "${HOME}" + homeRelativeSuffix);
            AddPathReferenceCandidate(candidates, "~" + homeRelativeSuffix);
            return candidates.ToArray();
        }

        private static void AddPathReferenceCandidate(List<string> candidates, string value)
        {
            Debug.Assert(candidates != null, "candidates must not be null");

            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (!candidates.Contains(value))
            {
                candidates.Add(value);
            }

            string escapedValue = EscapePosixDoubleQuotedPathValue(value);
            if (!candidates.Contains(escapedValue))
            {
                candidates.Add(escapedValue);
            }
        }

        private static bool TryGetPosixPathAssignmentValueStart(string line, out int valueStartIndex)
        {
            Debug.Assert(line != null, "line must not be null");

            valueStartIndex = -1;
            if (StartsWithShellCommand(line, POSIX_EXPORT_COMMAND))
            {
                return TryGetExportedPathAssignmentValueStart(line, out valueStartIndex);
            }

            int afterPathIndex = PATH_ENVIRONMENT_VARIABLE_NAME.Length;
            return line.StartsWith(PATH_ENVIRONMENT_VARIABLE_NAME, StringComparison.Ordinal)
                && IsShellNameEndBoundary(line, afterPathIndex)
                && TryGetAssignmentValueStartIndex(line, afterPathIndex, out valueStartIndex)
                && IsPersistentPosixPathAssignment(line, valueStartIndex);
        }

        private static bool TryGetExportedPathAssignmentValueStart(string line, out int valueStartIndex)
        {
            Debug.Assert(line != null, "line must not be null");

            valueStartIndex = -1;
            int cursor = POSIX_EXPORT_COMMAND.Length;
            while (cursor < line.Length)
            {
                while (cursor < line.Length && char.IsWhiteSpace(line[cursor]))
                {
                    cursor++;
                }

                if (cursor >= line.Length)
                {
                    return false;
                }

                if (line[cursor] == '-')
                {
                    cursor = SkipShellToken(line, cursor);
                    continue;
                }

                int afterPathIndex = cursor + PATH_ENVIRONMENT_VARIABLE_NAME.Length;
                return line.Substring(cursor).StartsWith(PATH_ENVIRONMENT_VARIABLE_NAME, StringComparison.Ordinal)
                    && IsShellNameEndBoundary(line, afterPathIndex)
                    && TryGetAssignmentValueStartIndex(line, afterPathIndex, out valueStartIndex)
                    && IsPersistentPosixPathAssignment(line, valueStartIndex);
            }

            return false;
        }

        private static bool TryGetAssignmentValueStartIndex(string line, int index, out int valueStartIndex)
        {
            int cursor = index;
            while (cursor < line.Length && char.IsWhiteSpace(line[cursor]))
            {
                cursor++;
            }

            if (cursor >= line.Length || line[cursor] != '=')
            {
                valueStartIndex = -1;
                return false;
            }

            cursor++;
            while (cursor < line.Length && char.IsWhiteSpace(line[cursor]))
            {
                cursor++;
            }

            valueStartIndex = cursor;
            return true;
        }

        private static bool IsPersistentPosixPathAssignment(string line, int valueStartIndex)
        {
            Debug.Assert(line != null, "line must not be null");
            Debug.Assert(valueStartIndex >= 0, "valueStartIndex must be zero or greater");

            int cursor = SkipShellAssignmentValue(line, valueStartIndex);
            while (cursor < line.Length && char.IsWhiteSpace(line[cursor]))
            {
                cursor++;
            }

            return cursor >= line.Length || line[cursor] == '#' || line[cursor] == ';';
        }

        private static int SkipShellAssignmentValue(string line, int index)
        {
            Debug.Assert(line != null, "line must not be null");
            Debug.Assert(index >= 0, "index must be zero or greater");

            int cursor = index;
            if (cursor < line.Length && (line[cursor] == '"' || line[cursor] == '\''))
            {
                return SkipQuotedShellValue(line, cursor);
            }

            while (cursor < line.Length
                && !char.IsWhiteSpace(line[cursor])
                && line[cursor] != '#'
                && line[cursor] != ';')
            {
                if (line[cursor] == '\\' && cursor + 1 < line.Length)
                {
                    cursor += 2;
                    continue;
                }

                cursor++;
            }

            return cursor;
        }

        private static int SkipQuotedShellValue(string line, int quoteIndex)
        {
            Debug.Assert(line != null, "line must not be null");
            Debug.Assert(quoteIndex >= 0, "quoteIndex must be zero or greater");

            char quote = line[quoteIndex];
            int cursor = quoteIndex + 1;
            while (cursor < line.Length)
            {
                if (quote == '"' && line[cursor] == '\\' && cursor + 1 < line.Length)
                {
                    cursor += 2;
                    continue;
                }

                if (line[cursor] == quote)
                {
                    return cursor + 1;
                }

                cursor++;
            }

            return cursor;
        }

        private static bool TryGetFishPathSetValueStart(
            string line,
            out int valueStartIndex,
            out bool usesAppendFlag)
        {
            Debug.Assert(line != null, "line must not be null");

            valueStartIndex = -1;
            usesAppendFlag = false;
            if (!StartsWithShellCommand(line, FISH_SET_COMMAND))
            {
                return false;
            }

            int cursor = FISH_SET_COMMAND.Length;
            while (cursor < line.Length)
            {
                while (cursor < line.Length && char.IsWhiteSpace(line[cursor]))
                {
                    cursor++;
                }

                if (cursor >= line.Length)
                {
                    return false;
                }

                if (line[cursor] == '-')
                {
                    int optionStartIndex = cursor;
                    cursor = SkipShellToken(line, cursor);
                    string option = line.Substring(optionStartIndex, cursor - optionStartIndex);
                    if (string.Equals(option, "--append", StringComparison.Ordinal)
                        || IsFishShortAppendOption(option))
                    {
                        usesAppendFlag = true;
                    }

                    continue;
                }

                int afterPathIndex = cursor + PATH_ENVIRONMENT_VARIABLE_NAME.Length;
                if (!line.Substring(cursor).StartsWith(PATH_ENVIRONMENT_VARIABLE_NAME, StringComparison.Ordinal)
                    || !IsShellNameEndBoundary(line, afterPathIndex))
                {
                    return false;
                }

                cursor = afterPathIndex;
                while (cursor < line.Length && char.IsWhiteSpace(line[cursor]))
                {
                    cursor++;
                }

                if (cursor >= line.Length)
                {
                    return false;
                }

                valueStartIndex = cursor;
                return true;
            }

            return false;
        }

        private static bool TryGetFishAddPathValueStart(
            string line,
            out int valueStartIndex,
            out bool usesAppendFlag)
        {
            Debug.Assert(line != null, "line must not be null");

            valueStartIndex = -1;
            usesAppendFlag = false;
            int cursor = FISH_ADD_PATH_COMMAND.Length;
            while (cursor < line.Length)
            {
                while (cursor < line.Length && char.IsWhiteSpace(line[cursor]))
                {
                    cursor++;
                }

                if (cursor >= line.Length || line[cursor] != '-')
                {
                    valueStartIndex = cursor;
                    return true;
                }

                int optionStartIndex = cursor;
                cursor = SkipShellToken(line, cursor);
                string option = line.Substring(optionStartIndex, cursor - optionStartIndex);
                if (string.Equals(option, "--", StringComparison.Ordinal))
                {
                    while (cursor < line.Length && char.IsWhiteSpace(line[cursor]))
                    {
                        cursor++;
                    }

                    if (cursor >= line.Length)
                    {
                        return false;
                    }

                    valueStartIndex = cursor;
                    return true;
                }

                if (string.Equals(option, "--append", StringComparison.Ordinal)
                    || IsFishShortAppendOption(option))
                {
                    usesAppendFlag = true;
                }
            }

            return false;
        }

        private static bool IsFishShortAppendOption(string option)
        {
            Debug.Assert(option != null, "option must not be null");

            return option.Length > 1
                && option[0] == '-'
                && option[1] != '-'
                && option.IndexOf('a', 1) >= 0;
        }

        private static int SkipShellToken(string line, int index)
        {
            Debug.Assert(line != null, "line must not be null");
            Debug.Assert(index >= 0, "index must be zero or greater");

            int cursor = index;
            while (cursor < line.Length && !char.IsWhiteSpace(line[cursor]))
            {
                cursor++;
            }

            return cursor;
        }

        private static bool StartsPathValueWithReference(string line, int valueStartIndex, string pathReference)
        {
            Debug.Assert(line != null, "line must not be null");
            Debug.Assert(valueStartIndex >= 0, "valueStartIndex must be zero or greater");
            Debug.Assert(!string.IsNullOrWhiteSpace(pathReference), "pathReference must not be null or empty");

            int cursor = valueStartIndex;
            if (cursor < line.Length && (line[cursor] == '"' || line[cursor] == '\''))
            {
                char quote = line[cursor];
                cursor++;
                if (!CanPathReferenceExpandInsideQuote(pathReference, quote))
                {
                    return false;
                }
            }

            if (!line.Substring(cursor).StartsWith(pathReference, StringComparison.Ordinal))
            {
                return false;
            }

            return IsPathReferenceEndBoundary(line, cursor + pathReference.Length);
        }

        private static bool StartsPathValueWithInheritedPath(string line, int valueStartIndex)
        {
            Debug.Assert(line != null, "line must not be null");
            Debug.Assert(valueStartIndex >= 0, "valueStartIndex must be zero or greater");

            int cursor = valueStartIndex;
            if (cursor < line.Length && (line[cursor] == '"' || line[cursor] == '\''))
            {
                if (line[cursor] == '\'')
                {
                    return false;
                }

                cursor++;
            }

            if (line.Substring(cursor).StartsWith("$PATH", StringComparison.Ordinal)
                && IsShellNameEndBoundary(line, cursor + "$PATH".Length))
            {
                return true;
            }

            return line.Substring(cursor).StartsWith("${PATH}", StringComparison.Ordinal)
                && IsPathReferenceEndBoundary(line, cursor + "${PATH}".Length);
        }

        private static bool CanPathReferenceExpandInsideQuote(string pathReference, char quote)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(pathReference), "pathReference must not be null or empty");

            if (pathReference.StartsWith("~", StringComparison.Ordinal))
            {
                return false;
            }

            if (quote == '"')
            {
                return !ContainsUnescapedDoubleQuoteExpansionCharacter(pathReference);
            }

            return quote != '\''
                || (!pathReference.StartsWith(HOME_REFERENCE, StringComparison.Ordinal)
                    && !pathReference.StartsWith("${HOME}", StringComparison.Ordinal));
        }

        private static bool ContainsUnescapedDoubleQuoteExpansionCharacter(string pathReference)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(pathReference), "pathReference must not be null or empty");

            int cursor = GetExpansionScanStartIndex(pathReference);
            while (cursor < pathReference.Length)
            {
                if (pathReference[cursor] == '\\' && cursor + 1 < pathReference.Length)
                {
                    cursor += 2;
                    continue;
                }

                if (pathReference[cursor] == '$'
                    || pathReference[cursor] == '`'
                    || pathReference[cursor] == '"')
                {
                    return true;
                }

                cursor++;
            }

            return false;
        }

        private static int GetExpansionScanStartIndex(string pathReference)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(pathReference), "pathReference must not be null or empty");

            if (pathReference.StartsWith(HOME_REFERENCE, StringComparison.Ordinal))
            {
                return HOME_REFERENCE.Length;
            }

            const string bracedHomeReference = "${HOME}";
            if (pathReference.StartsWith(bracedHomeReference, StringComparison.Ordinal))
            {
                return bracedHomeReference.Length;
            }

            return 0;
        }

        private static bool ContainsAnyDelimitedPathReference(string line, string[] pathReferences)
        {
            Debug.Assert(line != null, "line must not be null");
            Debug.Assert(pathReferences != null, "pathReferences must not be null");

            foreach (string pathReference in pathReferences)
            {
                if (ContainsDelimitedPathReference(line, pathReference))
                {
                    return true;
                }
            }

            return false;
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

        private static string SelectZshConfigurationPath(string configurationRoot, Func<string, bool> fileExists)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(configurationRoot), "configurationRoot must not be null or empty");
            Debug.Assert(fileExists != null, "fileExists must not be null");

            string zloginPath = Path.Combine(configurationRoot, ZSH_LOGIN_CONFIGURATION_FILE_NAME);
            if (fileExists(zloginPath))
            {
                return zloginPath;
            }

            return Path.Combine(configurationRoot, ZSH_CONFIGURATION_FILE_NAME);
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

            int secondQuoteIndex = FindClosingDoubleQuoteIndex(configurationLine, firstQuoteIndex);
            if (secondQuoteIndex < 0)
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

        private static int FindClosingDoubleQuoteIndex(string value, int openingQuoteIndex)
        {
            Debug.Assert(value != null, "value must not be null");
            Debug.Assert(openingQuoteIndex >= 0, "openingQuoteIndex must be zero or greater");

            int cursor = openingQuoteIndex + 1;
            while (cursor < value.Length)
            {
                if (value[cursor] == '\\' && cursor + 1 < value.Length)
                {
                    cursor += 2;
                    continue;
                }

                if (value[cursor] == '"')
                {
                    return cursor;
                }

                cursor++;
            }

            return -1;
        }

        private static string BuildManualCommand(string configurationPath, string configurationLine)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(configurationPath), "configurationPath must not be null or empty");
            Debug.Assert(!string.IsNullOrWhiteSpace(configurationLine), "configurationLine must not be null or empty");

            string configurationDirectory = Path.GetDirectoryName(configurationPath);
            if (string.IsNullOrEmpty(configurationDirectory))
            {
                return "printf '\\n%s\\n' "
                    + $"{QuotePosixShellValue(configurationLine)} >> {QuotePosixShellValue(configurationPath)}";
            }

            return $"mkdir -p {QuotePosixShellValue(configurationDirectory)} && "
                + "printf '\\n%s\\n' "
                + $"{QuotePosixShellValue(configurationLine)} >> {QuotePosixShellValue(configurationPath)}";
        }

        private static string BuildUnsupportedShellManualCommand(string installDirectory)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(installDirectory), "installDirectory must not be null or empty");

            return "export " + PATH_ENVIRONMENT_VARIABLE_NAME + "="
                + QuotePosixShellValue(installDirectory)
                + ":\"$" + PATH_ENVIRONMENT_VARIABLE_NAME + "\"";
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

        private static string ResolveFishConfigurationRoot(string homeDirectory, string xdgConfigDirectory)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(homeDirectory), "homeDirectory must not be null or empty");

            return string.IsNullOrWhiteSpace(xdgConfigDirectory)
                ? Path.Combine(homeDirectory, DEFAULT_XDG_CONFIGURATION_DIRECTORY)
                : xdgConfigDirectory;
        }

        private static string BuildPosixExportLine(string installDirectory)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(installDirectory), "installDirectory must not be null or empty");

            return $"export PATH=\"{EscapePosixDoubleQuotedPathValue(installDirectory)}:$PATH\"";
        }

        private static string EscapePosixDoubleQuotedPathValue(string value)
        {
            Debug.Assert(value != null, "value must not be null");

            return EscapeDoubleQuotedPathValue(value, preserveLeadingHomeReference: true, escapeBacktick: true);
        }

        private static string EscapeFishDoubleQuotedPathValue(string value)
        {
            Debug.Assert(value != null, "value must not be null");

            return EscapeDoubleQuotedPathValue(value, preserveLeadingHomeReference: true, escapeBacktick: false);
        }

        private static string EscapeDoubleQuotedPathValue(
            string value,
            bool preserveLeadingHomeReference,
            bool escapeBacktick)
        {
            Debug.Assert(value != null, "value must not be null");

            StringBuilder builder = new StringBuilder();
            int cursor = 0;
            if (preserveLeadingHomeReference
                && (string.Equals(value, HOME_REFERENCE, StringComparison.Ordinal)
                    || value.StartsWith(HOME_REFERENCE + "/", StringComparison.Ordinal)))
            {
                builder.Append(HOME_REFERENCE);
                cursor = HOME_REFERENCE.Length;
            }

            while (cursor < value.Length)
            {
                char character = value[cursor];
                if (character == '\\'
                    || character == '"'
                    || character == '$'
                    || (escapeBacktick && character == '`'))
                {
                    builder.Append('\\');
                }

                builder.Append(character);
                cursor++;
            }

            return builder.ToString();
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
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
