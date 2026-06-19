using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.View.Components.CreateInstance;
using System;
using System.IO;
using System.Linq;
using static MCServerLauncher.WPF.Modules.Constants;

namespace MCServerLauncher.WPF.View.CreateInstanceProvider
{
    internal static class CreateInstanceValidation
    {
        public static string NormalizeString(object? value)
        {
            return (value as string)?.Trim() ?? string.Empty;
        }

        public static bool TryGetLoaderVersion(
            CreateInstanceData data,
            out MinecraftLoaderVersion loaderVersion,
            out string errorMessage)
        {
            if (data.Data is not MinecraftLoaderVersion version
                || string.IsNullOrWhiteSpace(version.MCVersion)
                || string.IsNullOrWhiteSpace(version.LoaderVersion))
            {
                loaderVersion = default;
                errorMessage = Lang.Tr["CreateInstanceMissingDataError"];
                return false;
            }

            loaderVersion = version;
            errorMessage = string.Empty;
            return true;
        }

        public static bool TryValidateInstanceName(string instanceName, out string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(instanceName))
            {
                errorMessage = Lang.Tr["CreateInstanceMissingDataError"];
                return false;
            }

            if (instanceName is "." or ".."
                || instanceName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || instanceName.Any(char.IsControl))
            {
                errorMessage = $"{Lang.Tr["InstanceName"]}: {Lang.Tr["CreateInstanceMissingDataError"]}";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        public static bool TryValidateJavaPath(string javaPath, out string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(javaPath))
            {
                errorMessage = Lang.Tr["CreateInstanceMissingDataError"];
                return false;
            }

            if (javaPath.Any(char.IsControl) || LooksLikeJavaDisplayText(javaPath))
            {
                errorMessage = $"{Lang.Tr["JavaPath"]}: {Lang.Tr["CreateInstanceMissingDataError"]}";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        public static bool TryValidateLocalJarPath(string jarPath, out string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(jarPath))
            {
                errorMessage = Lang.Tr["CreateInstanceMissingDataError"];
                return false;
            }

            if (jarPath.Any(char.IsControl)
                || !Path.GetExtension(jarPath).Equals(".jar", StringComparison.OrdinalIgnoreCase)
                || !File.Exists(jarPath))
            {
                errorMessage = $"{Lang.Tr["CorePath"]}: {Lang.Tr["CreateInstanceMissingDataError"]}";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        public static void PushError(string message)
        {
            Notification.Push(
                Lang.Tr["Error"],
                message,
                true,
                InfoBarSeverity.Error,
                InfoBarPosition.Top,
                5000,
                false
            );
        }

        private static bool LooksLikeJavaDisplayText(string javaPath)
        {
            return javaPath.StartsWith("(", StringComparison.Ordinal)
                   && javaPath.Contains(") ", StringComparison.Ordinal);
        }
    }
}
