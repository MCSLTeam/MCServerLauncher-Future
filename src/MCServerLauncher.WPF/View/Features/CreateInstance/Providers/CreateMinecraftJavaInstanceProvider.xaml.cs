using MCServerLauncher.Common.Extensibility;
using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Contracts.Serialization;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.Services;
using MCServerLauncher.WPF.View.Components.CreateInstance;
using MCServerLauncher.WPF.View.Pages;
using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using static MCServerLauncher.WPF.Modules.Constants;
using static MCServerLauncher.WPF.Modules.VisualTreeHelper;
using TypedDaemonClient = MCServerLauncher.DaemonClient.DaemonClient;

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
                var coreData = Core.ActualData;
                var jvmData = Jvm.ActualData;
                var jvmArgumentData = JvmArgument.ActualData;
                var instanceNameData = InstanceName.ActualData;

                string corePath = CreateInstanceValidation.NormalizeString(coreData.Data);
                string javaPath = CreateInstanceValidation.NormalizeString(jvmData.Data);
                string[] arguments = jvmArgumentData.Data as string[] ?? [];
                string instanceName = CreateInstanceValidation.NormalizeString(instanceNameData.Data);

                if (!CreateInstanceValidation.TryValidateLocalJarPath(corePath, out var validationError)
                    || !CreateInstanceValidation.TryValidateJavaPath(javaPath, out validationError)
                    || !CreateInstanceValidation.TryValidateInstanceName(instanceName, out validationError))
                {
                    CreateInstanceValidation.PushError(validationError);
                    FinishButton.IsEnabled = true;
                    return;
                }

                // Show confirmation dialog
                var confirmationMessage = Lang.Tr["CreateInstanceConfirmationMessage"] +
                                        $"{Lang.Tr["InstanceName"]}: {instanceName}\n" +
                                        $"{Lang.Tr["InstanceType"]}: {InstanceType}\n" +
                                        $"{Lang.Tr["CorePath"]}: {corePath}\n" +
                                        $"{Lang.Tr["JavaPath"]}: {javaPath}\n" +
                                        $"{Lang.Tr["JvmArguments"]}: {(arguments.Length > 0 ? string.Join(" ", arguments) : Lang.Tr["None"])}";

                ContentDialog confirmDialog = new()
                {
                    Title = Lang.Tr["CreateInstanceConfirmationTitle"],
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
                    confirmDialog.CloseButtonText = Lang.Tr["DebugCopyConfig"];
                    confirmDialog.CloseButtonClick += (s, args) =>
                    {
                        var previewRequest = CreateRequest(
                            instanceName, corePath, Path.GetFileName(corePath), javaPath, arguments);
                        var serializerOptions = new JsonSerializerOptions(
                            ApplicationContractJsonContext.Default.Options)
                        {
                            WriteIndented = true
                        };
                        var serializerContext = new ApplicationContractJsonContext(serializerOptions);
                        var json = JsonSerializer.Serialize(
                            previewRequest,
                            serializerContext.CreateInstanceRequest);
                        Modules.Clipboard.SetText(json);
                        Notification.Push(
                            Lang.Tr["Success"],
                            Lang.Tr["InstanceConfigCopied"],
                            true,
                            InfoBarSeverity.Success,
                            InfoBarPosition.Top,
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


                var daemonConfig = DaemonsListManager.MatchDaemonBySelection(SelectedDaemon);
                var connectionResult = await DaemonsWsManager.Get(daemonConfig);

                if (connectionResult.IsErr(out _))
                {
                    Notification.Push(
                        Lang.Tr["Error"],
                        Lang.Tr["DaemonConnectionError"],
                        true,
                        InfoBarSeverity.Error,
                        InfoBarPosition.Top,
                        5000,
                        false
                    );
                    FinishButton.IsEnabled = true;
                    return;
                }
                var daemon = connectionResult.Unwrap();

                // Upload core file to daemon
                string sourcePathForDaemon = corePath;
                if (System.IO.File.Exists(corePath))
                {
                    var fileName = System.IO.Path.GetFileName(corePath);
                    var daemonUploadPath = $"caches/downloads/{fileName}";

                    Notification.Push(
                        Lang.Tr["PleaseWait"],
                        Lang.Tr["UploadingFile"],
                        false,
                        InfoBarSeverity.Informational,
                        InfoBarPosition.Top,
                        1500,
                        false
                    );

                    if (!await UploadFileAsync(daemon, corePath, daemonUploadPath, CancellationToken.None))
                    {
                        Notification.Push(
                            Lang.Tr["Error"],
                            Lang.Tr["FileUploadFailed"],
                            true,
                            InfoBarSeverity.Error,
                            InfoBarPosition.Top,
                            5000,
                            false
                        );
                        FinishButton.IsEnabled = true;
                        return;
                    }

                    sourcePathForDaemon = daemonUploadPath;
                }

                var request = CreateRequest(
                    instanceName, sourcePathForDaemon, Path.GetFileName(corePath), javaPath, arguments);

                Notification.Push(
                    Lang.Tr["PleaseWait"],
                    Lang.Tr["CreatingInstance"],
                    false,
                    InfoBarSeverity.Informational,
                    InfoBarPosition.Top,
                    5000,
                    false
                );

                var createResult = await daemon.Instances.CreateInstanceAsync(request, CancellationToken.None);
                if (createResult.IsErr(out var createError))
                    throw DaemonErrorLocalization.ToException(createError!);

                Notification.Push(
                    Lang.Tr["Success"],
                    Lang.Tr["InstanceCreatedSuccess"],
                    true,
                    InfoBarSeverity.Success,
                    InfoBarPosition.Top,
                    3000,
                    false
                );

                var parent = this.TryFindParent<CreateInstancePage>();
                parent?.CurrentCreateInstance.GoBack();
            }
            catch
            {
                FinishButton.IsEnabled = true;
                throw;
            }
        }

        private CreateInstanceRequest CreateRequest(
            string name,
            string source,
            string target,
            string javaPath,
            string[] arguments)
        {
            using var eventRules = JsonDocument.Parse("[]");
            var configuration = new InstanceConfiguration(
                Guid.NewGuid(), name, target, InstanceType, TargetType,
                "1.21.1", "utf-8", "utf-8", javaPath, arguments.ToImmutableArray(),
                ImmutableDictionary<string, string>.Empty,
                eventRules.RootElement);
            return new CreateInstanceRequest(new InstanceFactoryConfiguration(
                configuration, source, SourceType.Core, InstanceFactoryMirror.None, false));
        }

        private static async Task<bool> UploadFileAsync(
            TypedDaemonClient daemon,
            string sourcePath,
            string destinationPath,
            CancellationToken cancellationToken)
        {
            Guid? sessionId = null;
            var closed = false;
            try
            {
                await using var hashStream = new FileStream(
                    sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                var sha256 = Convert.ToHexString(
                    await SHA256.HashDataAsync(hashStream, cancellationToken));

                var openResult = await daemon.Files.OpenUploadAsync(
                    new UploadOpenRequest(destinationPath, hashStream.Length, sha256),
                    cancellationToken);
                if (openResult.IsErr(out _))
                    return false;

                var session = openResult.Unwrap();
                sessionId = session.SessionId;
                var buffer = new byte[session.MaxChunkSize];
                long offset = 0;

                await using var uploadStream = new FileStream(
                    sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                while (true)
                {
                    var read = await uploadStream.ReadAsync(buffer.AsMemory(), cancellationToken);
                    if (read == 0)
                        break;

                    var writeResult = await daemon.Files.WriteUploadChunkAsync(
                        new UploadChunkRequest(
                            session.SessionId,
                            offset,
                            ImmutableArray.Create(buffer, 0, read)),
                        cancellationToken);
                    if (writeResult.IsErr(out _))
                        return false;
                    offset += read;
                }

                var closeResult = await daemon.Files.CloseUploadAsync(
                    session.SessionId,
                    cancellationToken);
                closed = closeResult.IsOk(out _);
                return closed;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (sessionId is Guid openedSessionId && !closed)
                {
                    try
                    {
                        await daemon.Files.CancelUploadAsync(openedSessionId, CancellationToken.None);
                    }
                    catch
                    {
                        // Best-effort cleanup must not replace the original upload failure.
                    }
                }
            }
        }
    }
}
