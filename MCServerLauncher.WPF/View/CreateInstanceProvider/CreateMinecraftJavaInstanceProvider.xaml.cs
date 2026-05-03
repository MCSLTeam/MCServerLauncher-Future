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
using static MCServerLauncher.WPF.Modules.Constants;
using static MCServerLauncher.WPF.Modules.VisualTreeHelper;

namespace MCServerLauncher.WPF.View.CreateInstanceProvider
{
    /// <summary>
    ///    CreateMinecraftJavaInstanceProvider.xaml 的交互逻辑
    /// </summary>
    public partial class CreateMinecraftJavaInstanceProvider : ICreateInstanceProvider
    {
        private List<ICreateInstanceStep> StepList;
        public InstanceType InstanceType { get; } = InstanceType.MCJava;
        public TargetType TargetType { get; } = TargetType.Jar;
        
        public CreateMinecraftJavaInstanceProvider()
        {
            InitializeComponent();
            StepList = new() { Core, Jvm, JvmArgument, InstanceName };
            
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
                string[] arguments = jvmArgumentData.Data as string[] ?? Array.Empty<string>();
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

                var setting = new InstanceFactorySetting
                {
                    Name = instanceName,
                    Source = corePath,
                    SourceType = SourceType.Core,
                    Target = corePath,
                    TargetType = TargetType,
                    InstanceType = InstanceType,
                    JavaPath = javaPath,
                    Arguments = arguments,
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
                Notification.Push(
                    Lang.Tr["Error"],
                    $"{Lang.Tr["CreateInstanceError"] ?? "Failed to create instance"}: {ex.Message}",
                    true,
                    InfoBarSeverity.Error,
                    Constants.InfoBarPosition.Top,
                    5000,
                    false
                );
                FinishButton.IsEnabled = true;
            }
        }
    }
}