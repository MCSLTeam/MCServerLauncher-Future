using MCServerLauncher.Common.Extensibility;
using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.Services;
using MCServerLauncher.WPF.View.Components.CreateInstance;
using MCServerLauncher.WPF.View.Pages;
using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using static MCServerLauncher.WPF.Modules.Constants;
using static MCServerLauncher.WPF.Modules.VisualTreeHelper;

namespace MCServerLauncher.WPF.View.CreateInstanceProvider
{
    /// <summary>
    ///    CreateMinecraftNeoForgeInstanceProvider.xaml 的交互逻辑
    /// </summary>
    public partial class CreateMinecraftNeoForgeInstanceProvider : ICreateInstanceProvider
    {
        private List<ICreateInstanceStep> StepList;
        public InstanceType InstanceType { get; } = InstanceType.MCNeoForge;
        public TargetType TargetType { get; } = TargetType.Jar;
        public CreateMinecraftNeoForgeInstanceProvider()
        {
            InitializeComponent();
            StepList = new() { LoaderSet, Jvm, JvmArgument, InstanceName };

            foreach (var step in StepList)
            {
                if (step is DependencyObject dependencyObject)
                {
                    var isFinishedProperty = DependencyPropertyDescriptor.FromName("IsFinished", step.GetType(), step.GetType());
                    isFinishedProperty?.AddValueChanged(step, OnStepFinishedChanged);
                }
            }
            UpdateFinishButtonState();
        }

        private void OnStepFinishedChanged(object? sender, EventArgs e)
        {
            UpdateFinishButtonState();
        }

        /// <summary>
        /// Check whether the next button should be enabled.
        /// </summary>
        private void UpdateFinishButtonState()
        {
            bool allStepsFinished = true;
            foreach (var step in StepList)
            {
                if (!step.IsFinished)
                {
                    allStepsFinished = false;
                    break;
                }
            }
            FinishButton.IsEnabled = allStepsFinished;
        }

        /// <summary>
        /// Check if any step is finished.
        /// </summary>
        private bool CheckIfAnyStepFinished()
        {
            if (StepList == null || !StepList.Any())
            {
                return false;
            }
            return StepList.Any(step => step.IsFinished);
        }

        /// <summary>
        ///    Go back.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void GoPreCreateInstance(object sender, RoutedEventArgs e)
        {
            if (!CheckIfAnyStepFinished())
            {
                var parent = this.TryFindParent<CreateInstancePage>();
                parent?.CurrentCreateInstance.GoBack();
            }
            else
            {

                ContentDialog dialog = new()
                {
                    Title = Lang.Tr["AreYouSure"],
                    PrimaryButtonText = Lang.Tr["Back"],
                    SecondaryButtonText = Lang.Tr["Cancel"],
                    DefaultButton = ContentDialogButton.Primary,
                    FullSizeDesired = false,
                    Content = Lang.Tr["GoBackLostTip"]
                };
                dialog.PrimaryButtonClick += (s, args) =>
                {
                    var parent = this.TryFindParent<CreateInstancePage>();
                    parent?.CurrentCreateInstance.GoBack();
                };
                await dialog.ShowAsync();
            }
        }

        private async void FinishSetup(object sender, RoutedEventArgs e)
        {
            FinishButton.IsEnabled = false;

            try
            {
                var loaderData = LoaderSet.ActualData;
                var jvmData = Jvm.ActualData;
                var jvmArgumentData = JvmArgument.ActualData;
                var instanceNameData = InstanceName.ActualData;

                if (!CreateInstanceValidation.TryGetLoaderVersion(loaderData, out MinecraftLoaderVersion loaderVersion, out string validationError))
                {
                    CreateInstanceValidation.PushError(validationError);
                    FinishButton.IsEnabled = true;
                    return;
                }

                string mcVersion = loaderVersion.MCVersion;
                string neoForgeVersion = loaderVersion.LoaderVersion;
                string javaPath = CreateInstanceValidation.NormalizeString(jvmData.Data);
                string[] arguments = jvmArgumentData.Data as string[] ?? [];
                string instanceName = CreateInstanceValidation.NormalizeString(instanceNameData.Data);

                if (!CreateInstanceValidation.TryValidateJavaPath(javaPath, out validationError)
                    || !CreateInstanceValidation.TryValidateInstanceName(instanceName, out validationError))
                {
                    CreateInstanceValidation.PushError(validationError);
                    FinishButton.IsEnabled = true;
                    return;
                }

                bool useMirror = SettingsManager.Get?.InstanceCreation?.UseMirrorForMinecraftNeoForgeInstall ?? false;

                // NeoForge legacy (1.20.1) uses net.neoforged:forge, newer uses net.neoforged:neoforge
                bool isLegacy = mcVersion == "1.20.1";
                string installerUrl;
                string installerFileName;
                if (isLegacy)
                {
                    installerFileName = $"forge-1.20.1-{neoForgeVersion}-installer.jar";
                    installerUrl = useMirror
                        ? $"https://bmclapi2.bangbang93.com/maven/net/neoforged/forge/1.20.1-{neoForgeVersion}/{installerFileName}"
                        : $"https://maven.neoforged.net/releases/net/neoforged/forge/1.20.1-{neoForgeVersion}/{installerFileName}";
                }
                else
                {
                    installerFileName = $"neoforge-{neoForgeVersion}-installer.jar";
                    installerUrl = useMirror
                        ? $"https://bmclapi2.bangbang93.com/maven/net/neoforged/neoforge/{neoForgeVersion}/{installerFileName}"
                        : $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{neoForgeVersion}/{installerFileName}";
                }

                var confirmationMessage = Lang.Tr["CreateInstanceConfirmationMessage"] +
                                          $"{Lang.Tr["InstanceName"]}: {instanceName}\n" +
                                          $"{Lang.Tr["InstanceType"]}: {Lang.Tr["InstanceType_MCNeoForge"]}\n" +
                                          $"{Lang.Tr["MinecraftVersionLabel"]}: {mcVersion}\n" +
                                          $"{Lang.Tr["NeoForgeVersionLabel"]}: {neoForgeVersion}\n" +
                                          $"{Lang.Tr["JavaPath"]}: {javaPath}\n" +
                                          $"{Lang.Tr["JvmArguments"]}: {(arguments.Length > 0 ? string.Join(" ", arguments) : Lang.Tr["None"])}";

                ContentDialog confirmDialog = new()
                {
                    Title = Lang.Tr["CreateInstanceConfirmationTitle"],
                    Content = new TextBlock { Text = confirmationMessage, TextWrapping = TextWrapping.Wrap, MaxWidth = 500 },
                    PrimaryButtonText = Lang.Tr["Continue"],
                    SecondaryButtonText = Lang.Tr["Cancel"],
                    DefaultButton = ContentDialogButton.Secondary,
                    FullSizeDesired = false
                };

                var confirmResult = await confirmDialog.ShowAsync();
                if (confirmResult != ContentDialogResult.Primary)
                {
                    FinishButton.IsEnabled = true;
                    return;
                }

                var daemonConfig = DaemonsListManager.MatchDaemonBySelection(SelectedDaemon);
                var connectionResult = await DaemonsWsManager.Get(daemonConfig);

                if (connectionResult.IsErr(out _))
                {
                    Notification.Push(Lang.Tr["Error"], Lang.Tr["DaemonConnectionError"],
                        true, InfoBarSeverity.Error, InfoBarPosition.Top, 5000, false);
                    FinishButton.IsEnabled = true;
                    return;
                }

                var daemon = connectionResult.Unwrap();
                var request = CreateRequest(
                    instanceName, installerUrl, installerFileName, javaPath, arguments, mcVersion,
                    useMirror ? InstanceFactoryMirror.BmclApi : InstanceFactoryMirror.None);

                Notification.Push(Lang.Tr["PleaseWait"], Lang.Tr["CreatingInstance"],
                    false, InfoBarSeverity.Informational, InfoBarPosition.Top, 5000, false);

                var createResult = await daemon.Instances.CreateInstanceAsync(request, CancellationToken.None);
                if (createResult.IsErr(out var createError))
                    throw DaemonErrorLocalization.ToException(createError!);

                Notification.Push(Lang.Tr["Success"], Lang.Tr["InstanceCreatedSuccess"],
                    true, InfoBarSeverity.Success, InfoBarPosition.Top, 3000, false);

                var parent = this.TryFindParent<CreateInstancePage>();
                parent?.CurrentCreateInstance.GoBack();
            }
            catch (Exception ex)
            {
                Notification.Push(Lang.Tr["Error"], ex.Message,
                    true, InfoBarSeverity.Error, InfoBarPosition.Top, 5000, false);
                FinishButton.IsEnabled = true;
            }
        }

        private CreateInstanceRequest CreateRequest(
            string name,
            string source,
            string target,
            string javaPath,
            string[] arguments,
            string version,
            InstanceFactoryMirror mirror)
        {
            using var eventRules = JsonDocument.Parse("[]");
            var configuration = new InstanceConfiguration(
                Guid.NewGuid(), name, target, InstanceType, TargetType, version,
                "utf-8", "utf-8", javaPath, arguments.ToImmutableArray(),
                ImmutableDictionary<string, string>.Empty,
                eventRules.RootElement);
            return new CreateInstanceRequest(new InstanceFactoryConfiguration(
                configuration, source, SourceType.Core, mirror, false));
        }
    }
}
