using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.DaemonClient;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.View.Components.CreateInstance;
using MCServerLauncher.WPF.View.Pages;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using static MCServerLauncher.WPF.Modules.Constants;
using static MCServerLauncher.WPF.Modules.VisualTreeHelper;

namespace MCServerLauncher.WPF.View.CreateInstanceProvider
{
    /// <summary>
    ///    CreateMinecraftJavaInstanceProvider.xaml 的交互逻辑
    /// </summary>
    public partial class CreateMinecraftJavaInstanceProvider : ICreateInstanceProvider
    {
        private readonly List<ICreateInstanceStep> StepList;
        public InstanceType InstanceType { get; } = InstanceType.MCJava;
        public TargetType TargetType { get; } = TargetType.Jar;
        
        public CreateMinecraftJavaInstanceProvider()
        {
            InitializeComponent();
            StepList = [Core, Jvm, JvmArgument, InstanceName];
            
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

        private void OnStepFinishedChanged(object sender, EventArgs e)
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
            if (StepList == null || StepList.Count == 0)
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
            } else {
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
                var coreData = Core.ActualData;
                var jvmData = Jvm.ActualData;
                var jvmArgumentData = JvmArgument.ActualData;
                var instanceNameData = InstanceName.ActualData;

                string corePath = coreData.Data as string ?? string.Empty;
                string javaPath = jvmData.Data as string ?? string.Empty;
                string[] arguments = jvmArgumentData.Data as string[] ?? [];
                string instanceName = instanceNameData.Data as string ?? string.Empty;

                if (string.IsNullOrWhiteSpace(corePath) || string.IsNullOrWhiteSpace(javaPath) || string.IsNullOrWhiteSpace(instanceName))
                {
                    Notification.Push(
                        Lang.Tr["Error"],
                        Lang.Tr["CreateInstanceMissingDataError"] ?? "Missing required data",
                        true,
                        InfoBarSeverity.Error,
                        Constants.InfoBarPosition.Top,
                        5000,
                        false
                    );
                    FinishButton.IsEnabled = true;
                    return;
                }

                // Show confirmation dialog
                var confirmationMessage = $"{Lang.Tr["CreateInstanceConfirmationMessage"] ?? "Are you sure you want to create the following instance?"}\n\n" +
                                        $"{Lang.Tr["InstanceName"] ?? "Instance Name"}: {instanceName}\n" +
                                        $"{Lang.Tr["InstanceType"] ?? "Instance Type"}: {InstanceType}\n" +
                                        $"{Lang.Tr["CorePath"] ?? "Core Path"}: {corePath}\n" +
                                        $"{Lang.Tr["JavaPath"] ?? "Java Path"}: {javaPath}\n" +
                                        $"{Lang.Tr["JvmArguments"] ?? "JVM Arguments"}: {(arguments.Length > 0 ? string.Join(" ", arguments) : Lang.Tr["None"] ?? "None")}";

                var setting = new InstanceFactorySetting
                {
                    Name = instanceName,
                    Source = sourcePathForDaemon,
                    SourceType = SourceType.Core,
                    Target = System.IO.Path.GetFileName(corePath),
                    TargetType = TargetType,
                    InstanceType = InstanceType,
                    JavaPath = javaPath,
                    Arguments = arguments,
                    McVersion = "1.21.1", // TODO: Extract from core filename or add version selection step
                    Mirror = InstanceFactoryMirror.None,
                    UsePostProcess = false
                };

                ContentDialog confirmDialog = new()
                {
                    Title = Lang.Tr["CreateInstanceConfirmationTitle"] ?? "Confirm Instance Creation",
                    Content = new TextBlock
                    {
                        Text = confirmationMessage,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 500
                    },
                    PrimaryButtonText = Lang.Tr["Continue"],
                    SecondaryButtonText = Lang.Tr["Cancel"],
                    DefaultButton = ContentDialogButton.Secondary,
                    FullSizeDesired = false
                };

                var mainWindow = this.TryFindParent<MainWindow>();
                if (mainWindow?.DebugItem?.Visibility == Visibility.Visible)
                {
                    confirmDialog.CloseButtonText = "Debug: Copy Config";
                    confirmDialog.CloseButtonClick += (s, args) =>
                    {
                        var json = System.Text.Json.JsonSerializer.Serialize(setting,
                            MCServerLauncher.DaemonClient.Serialization.DaemonClientRpcJsonBoundary.CreateStjOptions(
                                MCServerLauncher.Common.ProtoType.Serialization.DaemonClientStjReflectionFallbackPolicy.TrimFriendlyDefault,
                                writeIndented: true));
                        Modules.Clipboard.SetText(json);
                        Notification.Push(
                            Lang.Tr["Success"],
                            "【InstanceConfig copied to clipboard】",
                            true,
                            InfoBarSeverity.Success,
                            Constants.InfoBarPosition.Top,
                            3000,
                            false
                        );
                    };
                }

                var confirmResult = await confirmDialog.ShowAsync();
                if (confirmResult != ContentDialogResult.Primary)
                {
                    FinishButton.IsEnabled = true;
                    return;
                }


                var daemonConfig = DaemonsListManager.MatchDaemonBySelection(Constants.SelectedDaemon);
                var daemon = await DaemonsWsManager.Get(daemonConfig);

                if (daemon == null)
                {
                    Notification.Push(
                        Lang.Tr["Error"],
                        Lang.Tr["DaemonConnectionError"] ?? "Failed to connect to daemon",
                        true,
                        InfoBarSeverity.Error,
                        Constants.InfoBarPosition.Top,
                        5000,
                        false
                    );
                    FinishButton.IsEnabled = true;
                    return;
                }

                // Upload core file to daemon if it's a local file
                string sourcePathForDaemon = corePath;
                if (System.IO.File.Exists(corePath))
                {
                    var fileName = System.IO.Path.GetFileName(corePath);
                    var daemonUploadPath = $"caches/uploads/{fileName}";

                    Notification.Push(
                        Lang.Tr["PleaseWait"],
                        $"【{Lang.Tr["UploadingFile"] ?? "Uploading file"}...】",
                        false,
                        InfoBarSeverity.Informational,
                        Constants.InfoBarPosition.Top,
                        -1,
                        false
                    );

                    var uploadContext = await daemon.UploadFileAsync(corePath, daemonUploadPath, 1024 * 1024); // 1MB chunks
                    await uploadContext.NetworkLoadTask;

                    if (!uploadContext.Done)
                    {
                        Notification.Push(
                            Lang.Tr["Error"],
                            $"【{Lang.Tr["FileUploadFailed"] ?? "Failed to upload file"}】",
                            true,
                            InfoBarSeverity.Error,
                            Constants.InfoBarPosition.Top,
                            5000,
                            false
                        );
                        FinishButton.IsEnabled = true;
                        return;
                    }

                    sourcePathForDaemon = daemonUploadPath;
                }

                var setting = new InstanceFactorySetting
                {
                    Name = instanceName,
                    Source = sourcePathForDaemon,
                    SourceType = SourceType.Core,
                    Target = System.IO.Path.GetFileName(corePath),
                    TargetType = TargetType,
                    InstanceType = InstanceType,
                    JavaPath = javaPath,
                    Arguments = arguments,
                    McVersion = "1.21.1", // TODO: Extract from core filename or add version selection step
                    Mirror = InstanceFactoryMirror.None,
                    UsePostProcess = false
                };

                Notification.Push(
                    Lang.Tr["PleaseWait"],
                    Lang.Tr["CreatingInstance"] ?? "Creating instance...",
                    false,
                    InfoBarSeverity.Informational,
                    Constants.InfoBarPosition.Top,
                    -1,
                    false
                );

                var config = await daemon.AddInstanceAsync(setting);

                Notification.Push(
                    Lang.Tr["Success"],
                    Lang.Tr["InstanceCreatedSuccess"] ?? $"Instance '{instanceName}' created successfully",
                    true,
                    InfoBarSeverity.Success,
                    Constants.InfoBarPosition.Top,
                    3000,
                    false
                );

                var parent = this.TryFindParent<CreateInstancePage>();
                parent?.CurrentCreateInstance.GoBack();
            }
            catch (Exception ex)
            {
                FinishButton.IsEnabled = true;
                throw;
            }
        }
    }
}