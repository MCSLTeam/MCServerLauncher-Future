using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace MCServerLauncher.Daemon.Generators;

[Generator]
public sealed class DaemonActionRegistryGenerator : IIncrementalGenerator
{
    private const string ActionHandlerAttributeMetadataName = "MCServerLauncher.Daemon.Remote.Action.ActionHandlerAttribute";
    private const string SyncActionHandlerInterfaceMetadataName = "MCServerLauncher.Daemon.Remote.Action.IActionHandler`2";
    private const string AsyncActionHandlerInterfaceMetadataName = "MCServerLauncher.Daemon.Remote.Action.IAsyncActionHandler`2";
    private const string ActionTypeEnumMetadataName = "MCServerLauncher.Common.ProtoType.Action.ActionType";
    private const string ActionParameterInterfaceMetadataName = "MCServerLauncher.Common.ProtoType.Action.IActionParameter";
    private const string ActionResultInterfaceMetadataName = "MCServerLauncher.Common.ProtoType.Action.IActionResult";

    private const string DiagnosticCategory = "DaemonActionRegistryGenerator";
    private const string GeneratedRegistryArtifactsSourceHintName = "DaemonActionRegistryArtifacts.g.cs";

    private static readonly DiagnosticDescriptor DuplicateActionRegistrationDiagnostic = new(
        id: "MCSLDAG001",
        title: "Duplicate ActionHandler registration",
        messageFormat:
        "Action '{0}' is registered by multiple supported [ActionHandler] classes: {1}. Keep exactly one effective handler per action.",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Every ActionType must map to exactly one supported handler class.");

    private static readonly DiagnosticDescriptor MissingAttributeArgumentsDiagnostic = new(
        id: "MCSLDAG002",
        title: "ActionHandler attribute is missing required arguments",
        messageFormat: "[ActionHandler] on '{0}' must provide both ActionType and permission string constructor arguments",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "ActionHandlerAttribute requires ActionType and permission arguments.");

    private static readonly DiagnosticDescriptor MissingSupportedInterfaceDiagnostic = new(
        id: "MCSLDAG003",
        title: "Annotated handler does not implement a supported handler interface",
        messageFormat:
        "[ActionHandler] class '{0}' must implement IActionHandler<TParam, TResult> or IAsyncActionHandler<TParam, TResult>",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Annotated handlers must implement one of the supported action-handler interfaces.");

    private static readonly DiagnosticDescriptor MalformedDualInterfaceDiagnostic = new(
        id: "MCSLDAG004",
        title: "Malformed dual-interface ActionHandler",
        messageFormat:
        "[ActionHandler] class '{0}' implements both sync and async handler interfaces, but the generic parameter/result types do not match ({1} vs {2})",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
        "A handler may implement both sync/async interfaces only when both interfaces use the same TParam and TResult pair.");

    private static readonly DiagnosticDescriptor UnsupportedHandlerShapeDiagnostic = new(
        id: "MCSLDAG005",
        title: "Unsupported ActionHandler shape",
        messageFormat: "[ActionHandler] class '{0}' cannot be mapped to runtime registration rules: {1}",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The handler declaration shape is unsupported by current runtime registration rules.");

    private static readonly SymbolDisplayFormat TypeDisplayFormat =
        new(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    private static readonly SymbolDisplayFormat FullyQualifiedTypeDisplayFormat =
        new(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static postInitContext =>
        {
            postInitContext.AddSource(
                "DaemonActionRegistryGenerator.g.cs",
                SourceText.From(
                    """
                    // <auto-generated />
                    namespace MCServerLauncher.Daemon.Generated;

                    internal static class DaemonActionRegistryGeneratorMarker
                    {
                        internal const string Generator = "DaemonActionRegistryGenerator";
                    }
                    """,
                    System.Text.Encoding.UTF8));
        });

        var handlerCandidates = context.SyntaxProvider.ForAttributeWithMetadataName(
                ActionHandlerAttributeMetadataName,
                static (_, _) => true,
                static (attributeContext, cancellationToken) => CreateCandidate(attributeContext, cancellationToken))
            .Where(static candidate => candidate is not null)
            .Select(static (candidate, _) => candidate!);

        var discoveryModel = context.CompilationProvider.Combine(handlerCandidates.Collect())
            .Select(static (payload, cancellationToken) =>
                BuildDiscoveryModel(payload.Left, payload.Right, cancellationToken));

        context.RegisterSourceOutput(discoveryModel, static (sourceProductionContext, analysisResult) =>
        {
            foreach (var diagnostic in analysisResult.Diagnostics)
            {
                sourceProductionContext.ReportDiagnostic(diagnostic);
            }

            EmitRegistryArtifacts(sourceProductionContext, analysisResult);
        });
    }

    private static ActionHandlerCandidate? CreateCandidate(
        GeneratorAttributeSyntaxContext attributeContext,
        CancellationToken cancellationToken)
    {
        if (attributeContext.TargetSymbol is not INamedTypeSymbol handlerType)
        {
            return null;
        }

        var matchingAttribute = attributeContext.Attributes
            .FirstOrDefault(attribute =>
                attribute.AttributeClass is not null
                && attribute.AttributeClass.ToDisplayString() == ActionHandlerAttributeMetadataName);

        if (matchingAttribute is null)
        {
            return null;
        }

        var location = matchingAttribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation()
                       ?? handlerType.Locations.FirstOrDefault();

        return new ActionHandlerCandidate(handlerType, matchingAttribute, location);
    }

    private static DiscoveryModelAnalysisResult BuildDiscoveryModel(
        Compilation compilation,
        ImmutableArray<ActionHandlerCandidate> candidates,
        CancellationToken cancellationToken)
    {
        if (candidates.IsDefaultOrEmpty)
        {
            return DiscoveryModelAnalysisResult.Empty;
        }

        var syncActionHandlerInterface = compilation.GetTypeByMetadataName(SyncActionHandlerInterfaceMetadataName);
        var asyncActionHandlerInterface = compilation.GetTypeByMetadataName(AsyncActionHandlerInterfaceMetadataName);
        var actionTypeEnum = compilation.GetTypeByMetadataName(ActionTypeEnumMetadataName);
        var actionParameterInterface = compilation.GetTypeByMetadataName(ActionParameterInterfaceMetadataName);
        var actionResultInterface = compilation.GetTypeByMetadataName(ActionResultInterfaceMetadataName);

        if (syncActionHandlerInterface is null
            || asyncActionHandlerInterface is null
            || actionTypeEnum is null
            || actionParameterInterface is null
            || actionResultInterface is null)
        {
            return DiscoveryModelAnalysisResult.Empty;
        }

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var discoveredHandlers = ImmutableArray.CreateBuilder<DiscoveredActionHandler>();

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidateAnalysis = AnalyzeCandidate(
                candidate,
                syncActionHandlerInterface,
                asyncActionHandlerInterface,
                actionTypeEnum,
                actionParameterInterface,
                actionResultInterface);

            diagnostics.AddRange(candidateAnalysis.Diagnostics);

            if (candidateAnalysis.DiscoveredHandler is not null)
            {
                discoveredHandlers.Add(candidateAnalysis.DiscoveredHandler);
            }
        }

        AppendDuplicateActionDiagnostics(discoveredHandlers, diagnostics);

        return new DiscoveryModelAnalysisResult(
            discoveredHandlers.ToImmutable(),
            diagnostics.ToImmutable());
    }

    private static CandidateAnalysisResult AnalyzeCandidate(
        ActionHandlerCandidate candidate,
        INamedTypeSymbol syncActionHandlerInterface,
        INamedTypeSymbol asyncActionHandlerInterface,
        INamedTypeSymbol actionTypeEnum,
        INamedTypeSymbol actionParameterInterface,
        INamedTypeSymbol actionResultInterface)
    {
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var handlerType = candidate.HandlerType;
        var location = candidate.Location ?? handlerType.Locations.FirstOrDefault();

        if (location is null)
        {
            return CandidateAnalysisResult.Empty;
        }

        if (candidate.AttributeData.ConstructorArguments.Length < 2)
        {
            diagnostics.Add(Diagnostic.Create(
                MissingAttributeArgumentsDiagnostic,
                location,
                handlerType.ToDisplayString(TypeDisplayFormat)));
            return new CandidateAnalysisResult(diagnostics.ToImmutable(), null);
        }

        var actionArgument = candidate.AttributeData.ConstructorArguments[0];
        var permissionArgument = candidate.AttributeData.ConstructorArguments[1];

        if (!TryReadActionType(actionTypeEnum, actionArgument, out var actionTypeValue, out var actionTypeDisplay)
            || !TryReadPermission(permissionArgument, out var permission))
        {
            diagnostics.Add(Diagnostic.Create(
                MissingAttributeArgumentsDiagnostic,
                location,
                handlerType.ToDisplayString(TypeDisplayFormat)));
            return new CandidateAnalysisResult(diagnostics.ToImmutable(), null);
        }

        if (handlerType.IsAbstract)
        {
            diagnostics.Add(Diagnostic.Create(
                UnsupportedHandlerShapeDiagnostic,
                location,
                handlerType.ToDisplayString(TypeDisplayFormat),
                "abstract handler classes cannot be instantiated by runtime registration"));
            return new CandidateAnalysisResult(diagnostics.ToImmutable(), null);
        }

        if (handlerType.IsGenericType)
        {
            diagnostics.Add(Diagnostic.Create(
                UnsupportedHandlerShapeDiagnostic,
                location,
                handlerType.ToDisplayString(TypeDisplayFormat),
                "generic handler classes are unsupported because runtime registration constructs concrete instances"));
            return new CandidateAnalysisResult(diagnostics.ToImmutable(), null);
        }

        var syncImplementations = handlerType.AllInterfaces
            .Where(@interface => SymbolEqualityComparer.Default.Equals(@interface.OriginalDefinition, syncActionHandlerInterface))
            .ToImmutableArray();

        var asyncImplementations = handlerType.AllInterfaces
            .Where(@interface => SymbolEqualityComparer.Default.Equals(@interface.OriginalDefinition, asyncActionHandlerInterface))
            .ToImmutableArray();

        var hasSyncImplementation = !syncImplementations.IsDefaultOrEmpty;
        var hasAsyncImplementation = !asyncImplementations.IsDefaultOrEmpty;

        if (!hasSyncImplementation && !hasAsyncImplementation)
        {
            diagnostics.Add(Diagnostic.Create(
                MissingSupportedInterfaceDiagnostic,
                location,
                handlerType.ToDisplayString(TypeDisplayFormat)));
            return new CandidateAnalysisResult(diagnostics.ToImmutable(), null);
        }

        if (syncImplementations.Length > 1)
        {
            diagnostics.Add(Diagnostic.Create(
                UnsupportedHandlerShapeDiagnostic,
                location,
                handlerType.ToDisplayString(TypeDisplayFormat),
                "multiple IActionHandler<TParam, TResult> implementations were found"));
            return new CandidateAnalysisResult(diagnostics.ToImmutable(), null);
        }

        if (asyncImplementations.Length > 1)
        {
            diagnostics.Add(Diagnostic.Create(
                UnsupportedHandlerShapeDiagnostic,
                location,
                handlerType.ToDisplayString(TypeDisplayFormat),
                "multiple IAsyncActionHandler<TParam, TResult> implementations were found"));
            return new CandidateAnalysisResult(diagnostics.ToImmutable(), null);
        }

        if (hasSyncImplementation
            && hasAsyncImplementation
            && !HaveEquivalentGenericArguments(syncImplementations[0], asyncImplementations[0]))
        {
            diagnostics.Add(Diagnostic.Create(
                MalformedDualInterfaceDiagnostic,
                location,
                handlerType.ToDisplayString(TypeDisplayFormat),
                FormatInterfacePair(syncImplementations[0]),
                FormatInterfacePair(asyncImplementations[0])));
            return new CandidateAnalysisResult(diagnostics.ToImmutable(), null);
        }

        var effectiveImplementation = hasSyncImplementation ? syncImplementations[0] : asyncImplementations[0];
        var handlerKind = hasSyncImplementation
            ? EffectiveHandlerKind.Sync
            : EffectiveHandlerKind.Async;

        if (effectiveImplementation.TypeArguments.Length != 2)
        {
            diagnostics.Add(Diagnostic.Create(
                UnsupportedHandlerShapeDiagnostic,
                location,
                handlerType.ToDisplayString(TypeDisplayFormat),
                "handler interface generic arity was expected to be 2"));
            return new CandidateAnalysisResult(diagnostics.ToImmutable(), null);
        }

        var parameterType = effectiveImplementation.TypeArguments[0];
        var resultType = effectiveImplementation.TypeArguments[1];

        if (!TryValidateMappingTypes(parameterType, resultType, actionParameterInterface, actionResultInterface,
                out var unsupportedReason))
        {
            diagnostics.Add(Diagnostic.Create(
                UnsupportedHandlerShapeDiagnostic,
                location,
                handlerType.ToDisplayString(TypeDisplayFormat),
                unsupportedReason));
            return new CandidateAnalysisResult(diagnostics.ToImmutable(), null);
        }

        var discoveredHandler = new DiscoveredActionHandler(
            handlerType,
            actionTypeValue,
            actionTypeDisplay,
            permission,
            handlerKind,
            hasSyncImplementation,
            hasAsyncImplementation,
            parameterType,
            resultType,
            location);

        return new CandidateAnalysisResult(diagnostics.ToImmutable(), discoveredHandler);
    }

    private static void AppendDuplicateActionDiagnostics(
        ImmutableArray<DiscoveredActionHandler>.Builder discoveredHandlers,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        foreach (var duplicateGroup in discoveredHandlers
                     .GroupBy(handler => handler.ActionTypeValue)
                     .Where(group => group.Count() > 1)
                     .OrderBy(group => group.Key))
        {
            var orderedGroup = duplicateGroup
                .OrderBy(handler => handler.HandlerType.ToDisplayString(TypeDisplayFormat), StringComparer.Ordinal)
                .ToArray();

            var actionTypeDisplay = orderedGroup[0].ActionTypeDisplay;
            var handlerList = string.Join(
                ", ",
                orderedGroup.Select(handler => handler.HandlerType.ToDisplayString(TypeDisplayFormat)));

            foreach (var handler in orderedGroup)
            {
                diagnostics.Add(Diagnostic.Create(
                    DuplicateActionRegistrationDiagnostic,
                    handler.Location,
                    actionTypeDisplay,
                    handlerList));
            }
        }
    }

    private static bool TryReadActionType(
        INamedTypeSymbol actionTypeEnum,
        TypedConstant actionArgument,
        out long actionTypeValue,
        out string actionTypeDisplay)
    {
        actionTypeValue = default;
        actionTypeDisplay = string.Empty;

        if (actionArgument.Kind == TypedConstantKind.Error || actionArgument.Value is null)
        {
            return false;
        }

        try
        {
            actionTypeValue = Convert.ToInt64(actionArgument.Value, CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return false;
        }

        var actionValue = actionTypeValue;
        var actionTypeField = actionTypeEnum.GetMembers()
            .OfType<IFieldSymbol>()
            .FirstOrDefault(field => field.HasConstantValue && EqualsConstantValue(field.ConstantValue, actionValue));

        actionTypeDisplay = actionTypeField is null
            ? actionTypeValue.ToString(CultureInfo.InvariantCulture)
            : $"{actionTypeEnum.Name}.{actionTypeField.Name}";

        return true;
    }

    private static bool TryReadPermission(TypedConstant permissionArgument, out string permission)
    {
        permission = string.Empty;

        if (permissionArgument.Kind == TypedConstantKind.Error || permissionArgument.Value is not string permissionValue)
        {
            return false;
        }

        permission = permissionValue;
        return true;
    }

    private static bool EqualsConstantValue(object? constantValue, long expected)
    {
        if (constantValue is null)
        {
            return false;
        }

        try
        {
            return Convert.ToInt64(constantValue, CultureInfo.InvariantCulture) == expected;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool HaveEquivalentGenericArguments(
        INamedTypeSymbol syncImplementation,
        INamedTypeSymbol asyncImplementation)
    {
        if (syncImplementation.TypeArguments.Length != 2 || asyncImplementation.TypeArguments.Length != 2)
        {
            return false;
        }

        return SymbolEqualityComparer.Default.Equals(syncImplementation.TypeArguments[0], asyncImplementation.TypeArguments[0])
               && SymbolEqualityComparer.Default.Equals(syncImplementation.TypeArguments[1],
                   asyncImplementation.TypeArguments[1]);
    }

    private static string FormatInterfacePair(INamedTypeSymbol implementation)
    {
        if (implementation.TypeArguments.Length != 2)
        {
            return implementation.ToDisplayString(TypeDisplayFormat);
        }

        return $"{implementation.TypeArguments[0].ToDisplayString(TypeDisplayFormat)}, {implementation.TypeArguments[1].ToDisplayString(TypeDisplayFormat)}";
    }

    private static bool TryValidateMappingTypes(
        ITypeSymbol parameterType,
        ITypeSymbol resultType,
        INamedTypeSymbol actionParameterInterface,
        INamedTypeSymbol actionResultInterface,
        out string reason)
    {
        reason = string.Empty;

        if (parameterType.TypeKind == TypeKind.Error)
        {
            reason = "parameter type could not be resolved";
            return false;
        }

        if (resultType.TypeKind == TypeKind.Error)
        {
            reason = "result type could not be resolved";
            return false;
        }

        if (!parameterType.IsReferenceType)
        {
            reason = $"parameter type '{parameterType.ToDisplayString(TypeDisplayFormat)}' must be a reference type";
            return false;
        }

        if (!resultType.IsReferenceType)
        {
            reason = $"result type '{resultType.ToDisplayString(TypeDisplayFormat)}' must be a reference type";
            return false;
        }

        if (!ImplementsInterface(parameterType, actionParameterInterface))
        {
            reason =
                $"parameter type '{parameterType.ToDisplayString(TypeDisplayFormat)}' must implement {actionParameterInterface.ToDisplayString(TypeDisplayFormat)}";
            return false;
        }

        if (!ImplementsInterface(resultType, actionResultInterface))
        {
            reason =
                $"result type '{resultType.ToDisplayString(TypeDisplayFormat)}' must implement {actionResultInterface.ToDisplayString(TypeDisplayFormat)}";
            return false;
        }

        return true;
    }

    private static bool ImplementsInterface(ITypeSymbol candidateType, INamedTypeSymbol requiredInterface)
    {
        return candidateType switch
        {
            INamedTypeSymbol namedType =>
                SymbolEqualityComparer.Default.Equals(namedType, requiredInterface)
                || namedType.AllInterfaces.Any(@interface =>
                    SymbolEqualityComparer.Default.Equals(@interface, requiredInterface)),
            _ => false
        };
    }

    private static void EmitRegistryArtifacts(
        SourceProductionContext sourceProductionContext,
        DiscoveryModelAnalysisResult analysisResult)
    {
        var effectiveHandlers = analysisResult.Handlers
            .OrderBy(handler => handler.ActionTypeValue)
            .ThenBy(handler => handler.HandlerType.ToDisplayString(TypeDisplayFormat), StringComparer.Ordinal)
            .GroupBy(handler => handler.ActionTypeValue)
            .Select(group => group.First())
            .ToImmutableArray();

        sourceProductionContext.AddSource(
            GeneratedRegistryArtifactsSourceHintName,
            SourceText.From(BuildRegistryArtifactsSource(effectiveHandlers), Encoding.UTF8));
    }

    private static string BuildRegistryArtifactsSource(ImmutableArray<DiscoveredActionHandler> handlers)
    {
        var sourceBuilder = new StringBuilder();

        _ = sourceBuilder
            .AppendLine("// <auto-generated />")
            .AppendLine("using System.Collections.Generic;")
            .AppendLine("using System.Threading;")
            .AppendLine("using System.Threading.Tasks;")
            .AppendLine("using MCServerLauncher.Common.ProtoType.Action;")
            .AppendLine("using MCServerLauncher.Daemon.Remote.Authentication;")
            .AppendLine("using TouchSocket.Core;")
            .AppendLine("using JsonElement = System.Text.Json.JsonElement;")
            .AppendLine()
            .AppendLine("namespace MCServerLauncher.Daemon.Remote.Action;")
            .AppendLine()
            .AppendLine("internal static class GeneratedActionHandlerRegistryArtifacts")
            .AppendLine("{");

        foreach (var handler in handlers)
        {
            var memberBaseName = BuildHandlerMemberBaseName(handler);
            var parameterType = handler.ParameterType.ToDisplayString(FullyQualifiedTypeDisplayFormat);
            var resultType = handler.ResultType.ToDisplayString(FullyQualifiedTypeDisplayFormat);
            var handlerType = handler.HandlerType.ToDisplayString(FullyQualifiedTypeDisplayFormat);

            _ = sourceBuilder
                .Append("    private static readonly ")
                .Append(handlerType)
                .Append(' ')
                .Append(memberBaseName)
                .AppendLine("Handler = new();");

            if (handler.Kind == EffectiveHandlerKind.Sync)
            {
                _ = sourceBuilder
                    .Append("    private static readonly Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, ActionResponse> ")
                    .Append(memberBaseName)
                    .AppendLine("Dispatch =")
                    .Append("        (param, id, ctx, resolver, ct) => ActionHandlerExtensions.Process<")
                    .Append(parameterType)
                    .Append(", ")
                    .Append(resultType)
                    .Append('>')
                    .Append('(')
                    .Append(memberBaseName)
                    .AppendLine("Handler, param, id, ctx, resolver, ct);")
                    .AppendLine();
            }
            else
            {
                _ = sourceBuilder
                    .Append("    private static readonly Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>> ")
                    .Append(memberBaseName)
                    .AppendLine("Dispatch =")
                    .Append("        (param, id, ctx, resolver, ct) => ActionHandlerExtensions.ProcessAsync<")
                    .Append(parameterType)
                    .Append(", ")
                    .Append(resultType)
                    .Append('>')
                    .Append('(')
                    .Append(memberBaseName)
                    .AppendLine("Handler, param, id, ctx, resolver, ct);")
                    .AppendLine();
            }
        }

        _ = sourceBuilder
            .AppendLine("    private static readonly Dictionary<ActionType, ActionHandlerMeta> GeneratedHandlerMetaMap = CreateGeneratedHandlerMetaMap();")
            .AppendLine("    private static readonly Dictionary<ActionType, Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, ActionResponse>> GeneratedSyncHandlerMap = CreateGeneratedSyncHandlerMap();")
            .AppendLine("    private static readonly Dictionary<ActionType, Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>>> GeneratedAsyncHandlerMap = CreateGeneratedAsyncHandlerMap();")
            .AppendLine()
            .AppendLine("    internal static IReadOnlyDictionary<ActionType, ActionHandlerMeta> HandlerMetaMap => GeneratedHandlerMetaMap;")
            .AppendLine("    internal static IReadOnlyDictionary<ActionType, Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, ActionResponse>> SyncHandlerMap => GeneratedSyncHandlerMap;")
            .AppendLine("    internal static IReadOnlyDictionary<ActionType, Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>>> AsyncHandlerMap => GeneratedAsyncHandlerMap;")
            .AppendLine()
            .AppendLine("    internal static Dictionary<ActionType, ActionHandlerMeta> CreateHandlerMetaMap()")
            .AppendLine("    {")
            .AppendLine("        return new Dictionary<ActionType, ActionHandlerMeta>(GeneratedHandlerMetaMap);")
            .AppendLine("    }")
            .AppendLine()
            .AppendLine("    internal static Dictionary<ActionType, Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, ActionResponse>> CreateSyncHandlerMap()")
            .AppendLine("    {")
            .AppendLine("        return new Dictionary<ActionType, Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, ActionResponse>>(GeneratedSyncHandlerMap);")
            .AppendLine("    }")
            .AppendLine()
            .AppendLine("    internal static Dictionary<ActionType, Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>>> CreateAsyncHandlerMap()")
            .AppendLine("    {")
            .AppendLine("        return new Dictionary<ActionType, Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>>>(GeneratedAsyncHandlerMap);")
            .AppendLine("    }")
            .AppendLine()
            .AppendLine("    internal static void RegisterHandlers(")
            .AppendLine("        IDictionary<ActionType, ActionHandlerMeta> handlerMeta,")
            .AppendLine("        IDictionary<ActionType, Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, ActionResponse>> syncHandlers,")
            .AppendLine("        IDictionary<ActionType, Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>>> asyncHandlers)")
            .AppendLine("    {")
            .AppendLine("        foreach (var entry in GeneratedHandlerMetaMap)")
            .AppendLine("        {")
            .AppendLine("            handlerMeta[entry.Key] = entry.Value;")
            .AppendLine("        }")
            .AppendLine()
            .AppendLine("        foreach (var entry in GeneratedSyncHandlerMap)")
            .AppendLine("        {")
            .AppendLine("            syncHandlers[entry.Key] = entry.Value;")
            .AppendLine("        }")
            .AppendLine()
            .AppendLine("        foreach (var entry in GeneratedAsyncHandlerMap)")
            .AppendLine("        {")
            .AppendLine("            asyncHandlers[entry.Key] = entry.Value;")
            .AppendLine("        }")
            .AppendLine("    }");

        _ = sourceBuilder
            .AppendLine("    private static Dictionary<ActionType, ActionHandlerMeta> CreateGeneratedHandlerMetaMap()")
            .AppendLine("    {")
            .AppendLine("        return new Dictionary<ActionType, ActionHandlerMeta>")
            .AppendLine("        {");

        foreach (var handler in handlers)
        {
            var actionLiteral = BuildActionTypeLiteral(handler.ActionTypeValue);
            var permissionLiteral = SymbolDisplay.FormatLiteral(handler.Permission, quote: true);
            var handlerTypeLiteral = handler.Kind == EffectiveHandlerKind.Sync
                ? "EActionHandlerType.Sync"
                : "EActionHandlerType.Async";

            _ = sourceBuilder
                .Append("            { ")
                .Append(actionLiteral)
                .Append(", new ActionHandlerMeta(Permission.Of(")
                .Append(permissionLiteral)
                .Append("), ")
                .Append(handlerTypeLiteral)
                .AppendLine(") },");
        }

        _ = sourceBuilder
            .AppendLine("        };")
            .AppendLine("    }")
            .AppendLine()
            .AppendLine("    private static Dictionary<ActionType, Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, ActionResponse>> CreateGeneratedSyncHandlerMap()")
            .AppendLine("    {")
            .AppendLine("        return new Dictionary<ActionType, Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, ActionResponse>>")
            .AppendLine("        {");

        foreach (var handler in handlers.Where(handler => handler.Kind == EffectiveHandlerKind.Sync))
        {
            var actionLiteral = BuildActionTypeLiteral(handler.ActionTypeValue);
            var memberBaseName = BuildHandlerMemberBaseName(handler);

            _ = sourceBuilder
                .Append("            { ")
                .Append(actionLiteral)
                .Append(", ")
                .Append(memberBaseName)
                .AppendLine("Dispatch },");
        }

        _ = sourceBuilder
            .AppendLine("        };")
            .AppendLine("    }")
            .AppendLine()
            .AppendLine("    private static Dictionary<ActionType, Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>>> CreateGeneratedAsyncHandlerMap()")
            .AppendLine("    {")
            .AppendLine("        return new Dictionary<ActionType, Func<JsonElement?, Guid, WsContext, IResolver, CancellationToken, Task<ActionResponse>>>")
            .AppendLine("        {");

        foreach (var handler in handlers.Where(handler => handler.Kind == EffectiveHandlerKind.Async))
        {
            var actionLiteral = BuildActionTypeLiteral(handler.ActionTypeValue);
            var memberBaseName = BuildHandlerMemberBaseName(handler);

            _ = sourceBuilder
                .Append("            { ")
                .Append(actionLiteral)
                .Append(", ")
                .Append(memberBaseName)
                .AppendLine("Dispatch },");
        }

        _ = sourceBuilder
            .AppendLine("        };")
            .AppendLine("    }")
            .AppendLine("}");

        return sourceBuilder.ToString();
    }

    private static string BuildActionTypeLiteral(long actionTypeValue)
    {
        return $"unchecked((global::MCServerLauncher.Common.ProtoType.Action.ActionType){actionTypeValue.ToString(CultureInfo.InvariantCulture)})";
    }

    private static string BuildHandlerMemberBaseName(DiscoveredActionHandler handler)
    {
        var rawName = $"{handler.ActionTypeDisplay}_{handler.Kind}_{handler.ActionTypeValue.ToString(CultureInfo.InvariantCulture)}";
        var builder = new StringBuilder(rawName.Length + 1);

        if (rawName.Length == 0 || !IsIdentifierStart(rawName[0]))
        {
            builder.Append('_');
        }

        foreach (var ch in rawName)
        {
            builder.Append(IsIdentifierPart(ch) ? ch : '_');
        }

        return builder.ToString();
    }

    private static bool IsIdentifierStart(char ch)
    {
        return ch == '_' || char.IsLetter(ch);
    }

    private static bool IsIdentifierPart(char ch)
    {
        return ch == '_' || char.IsLetterOrDigit(ch);
    }

    private sealed class ActionHandlerCandidate
    {
        public ActionHandlerCandidate(INamedTypeSymbol handlerType, AttributeData attributeData, Location? location)
        {
            HandlerType = handlerType;
            AttributeData = attributeData;
            Location = location;
        }

        public INamedTypeSymbol HandlerType { get; }
        public AttributeData AttributeData { get; }
        public Location? Location { get; }
    }

    private sealed class CandidateAnalysisResult
    {
        public CandidateAnalysisResult(ImmutableArray<Diagnostic> diagnostics, DiscoveredActionHandler? discoveredHandler)
        {
            Diagnostics = diagnostics;
            DiscoveredHandler = discoveredHandler;
        }

        public ImmutableArray<Diagnostic> Diagnostics { get; }
        public DiscoveredActionHandler? DiscoveredHandler { get; }

        public static CandidateAnalysisResult Empty => new(ImmutableArray<Diagnostic>.Empty, null);
    }

    private sealed class DiscoveryModelAnalysisResult
    {
        public DiscoveryModelAnalysisResult(ImmutableArray<DiscoveredActionHandler> handlers, ImmutableArray<Diagnostic> diagnostics)
        {
            Handlers = handlers;
            Diagnostics = diagnostics;
        }

        public ImmutableArray<DiscoveredActionHandler> Handlers { get; }
        public ImmutableArray<Diagnostic> Diagnostics { get; }

        public static DiscoveryModelAnalysisResult Empty =>
            new(ImmutableArray<DiscoveredActionHandler>.Empty, ImmutableArray<Diagnostic>.Empty);
    }

    private sealed class DiscoveredActionHandler
    {
        public DiscoveredActionHandler(
            INamedTypeSymbol handlerType,
            long actionTypeValue,
            string actionTypeDisplay,
            string permission,
            EffectiveHandlerKind kind,
            bool implementsSync,
            bool implementsAsync,
            ITypeSymbol parameterType,
            ITypeSymbol resultType,
            Location location)
        {
            HandlerType = handlerType;
            ActionTypeValue = actionTypeValue;
            ActionTypeDisplay = actionTypeDisplay;
            Permission = permission;
            Kind = kind;
            ImplementsSync = implementsSync;
            ImplementsAsync = implementsAsync;
            ParameterType = parameterType;
            ResultType = resultType;
            Location = location;
        }

        public INamedTypeSymbol HandlerType { get; }
        public long ActionTypeValue { get; }
        public string ActionTypeDisplay { get; }
        public string Permission { get; }
        public EffectiveHandlerKind Kind { get; }
        public bool ImplementsSync { get; }
        public bool ImplementsAsync { get; }
        public ITypeSymbol ParameterType { get; }
        public ITypeSymbol ResultType { get; }
        public Location Location { get; }
    }

    private enum EffectiveHandlerKind
    {
        Sync,
        Async
    }
}
