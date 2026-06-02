using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Management.Communicate;

namespace MCServerLauncher.ProtocolTests;

public class InstanceSettingsCoordinatorTests
{
    [Fact]
    public async Task GetInstanceSettings_StoppedInstance_ReturnsEditableSettings()
    {
        var manager = new InstanceManager();
        var config = CreateConfig(InstanceType.MCJava);
        var instance = new FakeInstance(config, InstanceStatus.Stopped);
        manager.Instances[config.Uuid] = instance;

        var result = await manager.GetInstanceSettings(config.Uuid);

        Assert.True(result.IsOk(out var settings));
        Assert.True(settings.CanEdit);
        Assert.Equal(config.Name, settings.Config.Name);
        Assert.Equal(config.Target, settings.Config.Target);
        Assert.Equal(config.InstanceType, settings.Config.InstanceType);
    }

    [Fact]
    public async Task UpdateInstanceSettings_RunningInstance_IsRejected()
    {
        var manager = new InstanceManager();
        var config = CreateConfig(InstanceType.MCJava);
        var instance = new FakeInstance(config, InstanceStatus.Running);
        manager.Instances[config.Uuid] = instance;
        manager.RunningInstances[config.Uuid] = instance;

        var result = await manager.UpdateInstanceSettings(new UpdateInstanceSettingsParameter
        {
            Id = config.Uuid,
            Name = "updated-name",
            InstanceType = InstanceType.MCJava,
            JavaPath = config.JavaPath,
            Arguments = config.Arguments,
            Version = config.Version,
            ForceRerunInstaller = false
        });

        Assert.True(result.IsErr(out var error));
        Assert.Contains("must be stopped", error!.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static InstanceConfig CreateConfig(InstanceType type)
    {
        return new InstanceConfig
        {
            Name = "test-instance",
            Target = "server.jar",
            InstanceType = type,
            TargetType = TargetType.Jar,
            Version = type.RequiresNumericMinecraftVersion() ? "1.20.1" : string.Empty,
            JavaPath = "java",
            Arguments = ["nogui"]
        };
    }

    private sealed class FakeInstance : IInstance
    {
        public FakeInstance(InstanceConfig config, InstanceStatus status)
        {
            Config = config;
            Status = status;
        }

        public InstanceConfig Config { get; }
        public InstanceProcess? Process => null;
        public InstanceStatus Status { get; }
        public int ServerProcessId => -1;
        public event Action<Guid, string>? OnLog;
        public event Action<Guid, InstanceStatus>? OnStatusChanged;

        public Task<InstanceReport> GetReportAsync()
        {
            return Task.FromResult(new InstanceReport(Status, Config, new Dictionary<string, string>(), [], default));
        }

        public Task<bool> StartAsync(int delayToCheck = 500)
        {
            return Task.FromResult(false);
        }

        public void Stop()
        {
        }

        public IReadOnlyList<string> GetLogHistory()
        {
            return Array.Empty<string>();
        }

        public void Dispose()
        {
        }
    }
}
