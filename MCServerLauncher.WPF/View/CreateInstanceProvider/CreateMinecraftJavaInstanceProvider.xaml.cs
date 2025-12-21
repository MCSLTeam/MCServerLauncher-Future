using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.View.Components.CreateInstance;
using MCServerLauncher.WPF.View.Pages;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
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

        //private void FinishSetup(object sender, RoutedEventArgs e)
        //{
        //}
    }
}