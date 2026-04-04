using System.Reflection;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.Remote;
using MCServerLauncher.Daemon.Remote.Action;
using RustyOptions;
using TouchSocket.Core;

namespace MCServerLauncher.ProtocolTests;

/// <summary>
/// T5 stop gate before generator work.
///
/// Runtime authority comes from daemon handler implementations annotated with <see cref="ActionHandlerAttribute"/>.
/// <c>proto_type.yml</c> is only cross-checked as an informational contract catalog and must not be treated as the
/// source of truth for effective runtime registration.
/// </summary>
[Collection("LegacyActionRegistryIsolation")]
public class ActionHandlerInventoryTests
{
    [Fact]
    [Trait("Category", "LegacyActionRegistry")]
    public void StopGate_RuntimeAnnotatedHandlers_MapEachActionToExactlyOneEffectiveHandler()
    {
        var handlerTypes = DiscoverRuntimeAnnotatedHandlerTypes();
        var expectedRegistrations = BuildExpectedEffectiveRegistrations(handlerTypes);

        Assert.NotEmpty(handlerTypes);
        Assert.Equal(handlerTypes.Length, expectedRegistrations.Count);
        Assert.All(handlerTypes, handlerType =>
            Assert.NotNull(handlerType.GetCustomAttribute<ActionHandlerAttribute>(inherit: false)));
    }

    [Fact]
    [Trait("Category", "LegacyActionRegistry")]
    public void StopGate_RuntimeAnnotatedHandlers_SyncAndAsyncClassificationMatchesLegacyRegistryBehavior()
    {
        var handlerTypes = DiscoverRuntimeAnnotatedHandlerTypes();
        var expectedRegistrations = BuildExpectedEffectiveRegistrations(handlerTypes);
        var snapshot = LegacyActionRegistryHarness.BuildSnapshot(handlerTypes);

        Assert.Equal(Order(expectedRegistrations.Keys), Order(snapshot.HandlerMetas.Keys));
        Assert.Equal(
            Order(expectedRegistrations.Values.Where(registration => registration.EffectiveHandlerType == EActionHandlerType.Sync)
                .Select(registration => registration.ActionType)),
            Order(snapshot.SyncHandlers.Keys));
        Assert.Equal(
            Order(expectedRegistrations.Values.Where(registration => registration.EffectiveHandlerType == EActionHandlerType.Async)
                .Select(registration => registration.ActionType)),
            Order(snapshot.AsyncHandlers.Keys));

        foreach (var (actionType, registration) in expectedRegistrations)
        {
            Assert.Equal(registration.EffectiveHandlerType, snapshot.HandlerMetas[actionType].Type);
            Assert.Equal(registration.EffectiveHandlerType == EActionHandlerType.Sync, snapshot.SyncHandlers.ContainsKey(actionType));
            Assert.Equal(registration.EffectiveHandlerType == EActionHandlerType.Async, snapshot.AsyncHandlers.ContainsKey(actionType));
        }
    }

    [Fact]
    [Trait("Category", "LegacyActionRegistry")]
    public void StopGate_SyncWins_WhenAHandlerImplementsBothSyncAndAsyncInterfaces()
    {
        var expectedRegistrations = BuildExpectedEffectiveRegistrations([typeof(SyncWinsProbeHandler)]);
        var registration = expectedRegistrations[ActionType.GetPermissions];
        var snapshot = LegacyActionRegistryHarness.BuildSnapshot(typeof(SyncWinsProbeHandler));

        Assert.True(registration.ImplementsSync);
        Assert.True(registration.ImplementsAsync);
        Assert.Equal(EActionHandlerType.Sync, registration.EffectiveHandlerType);
        Assert.True(snapshot.SyncHandlers.ContainsKey(ActionType.GetPermissions));
        Assert.False(snapshot.AsyncHandlers.ContainsKey(ActionType.GetPermissions));
        Assert.Equal(EActionHandlerType.Sync, snapshot.HandlerMetas[ActionType.GetPermissions].Type);
    }

    [Fact]
    [Trait("Category", "LegacyActionRegistry")]
    public void StopGate_DuplicateAnnotatedHandlers_DoNotPassInventoryCollisionGuard()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            BuildExpectedEffectiveRegistrations([
                typeof(DuplicateRemoveInstanceProbeHandlerA),
                typeof(DuplicateRemoveInstanceProbeHandlerB)
            ]));

        Assert.Contains(nameof(ActionType.RemoveInstance), exception.Message, StringComparison.Ordinal);
        Assert.Contains("duplicate effective handlers", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "LegacyActionRegistry")]
    public void StopGate_ProtoTypeYaml_IsAnInformationalSubsetOfRuntimeAnnotatedHandlers()
    {
        // This cross-check is intentionally asymmetrical: generator work must key off annotated handler types because
        // they define the runtime registry. proto_type.yml is useful documentation for the external contract surface,
        // but it is not allowed to override or define the effective registration inventory.
        var runtimeActions = Order(BuildExpectedEffectiveRegistrations(DiscoverRuntimeAnnotatedHandlerTypes()).Keys);
        var yamlActions = Order(LoadProtoTypeYamlActionTypes());
        var runtimeOnlyActions = Order(runtimeActions.Except(yamlActions));

        Assert.All(yamlActions, yamlAction => Assert.Contains(yamlAction, runtimeActions));
        Assert.Equal(
            Order([
                ActionType.GetInstanceLogHistory,
                ActionType.GetEventRules,
                ActionType.SaveEventRules
            ]),
            runtimeOnlyActions);
    }

    private static Type[] DiscoverRuntimeAnnotatedHandlerTypes()
    {
        return typeof(ActionHandlerAttribute).Assembly
            .GetTypes()
            .Where(type =>
                type.IsClass
                && !type.IsAbstract
                && type.GetCustomAttribute<ActionHandlerAttribute>(inherit: false) is not null)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyDictionary<ActionType, ExpectedHandlerRegistration> BuildExpectedEffectiveRegistrations(
        IEnumerable<Type> handlerTypes)
    {
        var registrations = handlerTypes
            .Select(CreateExpectedRegistration)
            .OrderBy(registration => registration.ActionType)
            .ThenBy(registration => registration.HandlerType.FullName, StringComparer.Ordinal)
            .ToArray();

        var duplicateGroups = registrations
            .GroupBy(registration => registration.ActionType)
            .Where(group => group.Count() > 1)
            .Select(group =>
                $"{group.Key}: {string.Join(", ", group.Select(registration => registration.HandlerType.FullName))}")
            .ToArray();

        if (duplicateGroups.Length > 0)
        {
            throw new InvalidOperationException(
                "Annotated runtime handlers must map each action to exactly one effective registration; found duplicate effective handlers for "
                + string.Join("; ", duplicateGroups));
        }

        return registrations.ToDictionary(registration => registration.ActionType);
    }

    private static ExpectedHandlerRegistration CreateExpectedRegistration(Type handlerType)
    {
        var attribute = handlerType.GetCustomAttribute<ActionHandlerAttribute>(inherit: false)
                        ?? throw new InvalidOperationException($"Missing {nameof(ActionHandlerAttribute)} on {handlerType.FullName}.");

        var implementsSync = ImplementsOpenGenericInterface(handlerType, typeof(IActionHandler<,>));
        var implementsAsync = ImplementsOpenGenericInterface(handlerType, typeof(IAsyncActionHandler<,>));

        if (!implementsSync && !implementsAsync)
        {
            throw new InvalidOperationException(
                $"Annotated handler '{handlerType.FullName}' must implement {nameof(IActionHandler<EmptyActionParameter, EmptyActionResult>)} or {nameof(IAsyncActionHandler<EmptyActionParameter, EmptyActionResult>)}.");
        }

        return new ExpectedHandlerRegistration(
            handlerType,
            attribute.ActionType,
            implementsSync ? EActionHandlerType.Sync : EActionHandlerType.Async,
            implementsSync,
            implementsAsync);
    }

    private static bool ImplementsOpenGenericInterface(Type handlerType, Type openGenericInterface)
    {
        return handlerType
            .GetInterfaces()
            .Any(@interface =>
                @interface.IsGenericType
                && @interface.GetGenericTypeDefinition() == openGenericInterface);
    }

    private static ActionType[] LoadProtoTypeYamlActionTypes()
    {
        var actionTypes = new List<ActionType>();
        var inActionsSection = false;

        foreach (var rawLine in File.ReadAllLines(GetProtoTypeYamlPath()))
        {
            var line = rawLine.Trim();

            if (line == "actions:")
            {
                inActionsSection = true;
                continue;
            }

            if (line == "events:")
            {
                break;
            }

            if (!inActionsSection || !line.StartsWith("- ", StringComparison.Ordinal) || !line.EndsWith(":", StringComparison.Ordinal))
            {
                continue;
            }

            actionTypes.Add(ParseYamlActionType(line[2..^1].Trim()));
        }

        return actionTypes.ToArray();
    }

    private static ActionType ParseYamlActionType(string yamlActionName)
    {
        var pascalCaseName = string.Concat(yamlActionName
            .Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

        return Enum.Parse<ActionType>(pascalCaseName, ignoreCase: false);
    }

    private static string GetProtoTypeYamlPath()
    {
        var directory = AppDomain.CurrentDomain.BaseDirectory;

        while (directory is not null && !File.Exists(Path.Combine(directory, "MCServerLauncher.sln")))
        {
            directory = Directory.GetParent(directory)?.FullName;
        }

        if (directory is null)
        {
            throw new DirectoryNotFoundException("Repository root not found for proto_type.yml lookup.");
        }

        return Path.Combine(directory, "MCServerLauncher.Common", ".Resources", "proto_type.yml");
    }

    private static ActionType[] Order(IEnumerable<ActionType> actions)
    {
        return actions.OrderBy(action => (int)action).ToArray();
    }

    private sealed record ExpectedHandlerRegistration(
        Type HandlerType,
        ActionType ActionType,
        EActionHandlerType EffectiveHandlerType,
        bool ImplementsSync,
        bool ImplementsAsync);

    [ActionHandler(ActionType.GetPermissions, "*")]
    public sealed class SyncWinsProbeHandler :
        IActionHandler<EmptyActionParameter, EmptyActionResult>,
        IAsyncActionHandler<EmptyActionParameter, EmptyActionResult>
    {
        public Result<EmptyActionResult, ActionError> Handle(
            EmptyActionParameter param,
            WsContext ctx,
            IResolver resolver,
            CancellationToken ct)
        {
            return this.Ok(ActionHandlerExtensions.EmptyActionResult);
        }

        public Task<Result<EmptyActionResult, ActionError>> HandleAsync(
            EmptyActionParameter param,
            WsContext ctx,
            IResolver resolver,
            CancellationToken ct)
        {
            return Task.FromResult(this.Ok(ActionHandlerExtensions.EmptyActionResult));
        }
    }

    [ActionHandler(ActionType.RemoveInstance, "*")]
    public sealed class DuplicateRemoveInstanceProbeHandlerA : IActionHandler<EmptyActionParameter, EmptyActionResult>
    {
        public Result<EmptyActionResult, ActionError> Handle(
            EmptyActionParameter param,
            WsContext ctx,
            IResolver resolver,
            CancellationToken ct)
        {
            return this.Ok(ActionHandlerExtensions.EmptyActionResult);
        }
    }

    [ActionHandler(ActionType.RemoveInstance, "*")]
    public sealed class DuplicateRemoveInstanceProbeHandlerB : IAsyncActionHandler<EmptyActionParameter, EmptyActionResult>
    {
        public Task<Result<EmptyActionResult, ActionError>> HandleAsync(
            EmptyActionParameter param,
            WsContext ctx,
            IResolver resolver,
            CancellationToken ct)
        {
            return Task.FromResult(this.Ok(ActionHandlerExtensions.EmptyActionResult));
        }
    }
}
