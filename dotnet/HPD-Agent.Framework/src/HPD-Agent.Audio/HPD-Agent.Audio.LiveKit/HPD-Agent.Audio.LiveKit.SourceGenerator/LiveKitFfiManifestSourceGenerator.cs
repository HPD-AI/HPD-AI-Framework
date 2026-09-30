using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace HPD.Agent.Audio.LiveKit.SourceGenerator;

/// <summary>
/// Validates the reviewed LiveKit FFI protocol, native and admitted-surface
/// manifests and emits the complete mechanically admitted B3 binding.
/// </summary>
[Generator]
public sealed class LiveKitFfiManifestSourceGenerator : IIncrementalGenerator
{
    private const string BindingFile = "livekit-ffi-binding-v0.12.60.txt";
    private const string ProtocolFile = "livekit-ffi-protocol-v0.12.60.txt";
    private const string NativeFile = "livekit-ffi-native-v0.12.60.txt";

    private static readonly DiagnosticDescriptor DescriptorMismatch = Error(
        "HPDLK001", "Pinned descriptor mismatch", "LiveKit FFI descriptor mismatch: {0}");
    private static readonly DiagnosticDescriptor MissingExport = Error(
        "HPDLK002", "Required native export missing", "LiveKit FFI required native export is missing: {0}");
    private static readonly DiagnosticDescriptor MissingCompletion = Error(
        "HPDLK003", "Request has no exact completion mapping", "LiveKit FFI operation mapping is invalid: {0}");
    private static readonly DiagnosticDescriptor DuplicateCorrelation = Error(
        "HPDLK004", "Duplicate correlation owner", "LiveKit FFI correlation mapping is duplicated: {0}");
    private static readonly DiagnosticDescriptor MissingRelease = Error(
        "HPDLK005", "Native handle has no qualified release mapping", "LiveKit FFI native handle release is missing: {0}");
    private static readonly DiagnosticDescriptor UnknownEvent = Error(
        "HPDLK006", "Unknown pinned-protocol event", "LiveKit FFI event classification is invalid: {0}");
    private static readonly DiagnosticDescriptor IncompleteLock = Error(
        "HPDLK007", "Artifact lock incomplete", "LiveKit FFI reviewed inputs are incomplete or malformed: {0}");
    private static readonly DiagnosticDescriptor UnboundedCallback = Error(
        "HPDLK009", "Callback path lacks bounded admission", "LiveKit FFI operation lacks bounded admission: {0}");
    private static readonly DiagnosticDescriptor AbandonedIssuedOperation = Error(
        "HPDLK010", "Cancellation can abandon an issued operation", "LiveKit FFI async cancellation law is invalid: {0}");
    private static readonly DiagnosticDescriptor InvalidSafeHandle = Error(
        "HPDLK011", "Asynchronous release incorrectly mapped to SafeHandle", "LiveKit FFI handle cannot use SafeHandle: {0}");
    private static readonly DiagnosticDescriptor OverclaimedProof = Error(
        "HPDLK013", "Output proof boundary overclaimed", "LiveKit FFI proof boundary is invalid: {0}");
    private static readonly DiagnosticDescriptor UnsupportedRid = Error(
        "HPDLK014", "Unsupported RID advertised", "LiveKit FFI RID is not execution-qualified: {0}");
    private static readonly DiagnosticDescriptor GeneratedSurfaceMismatch = Error(
        "HPDLK015", "Generated native import surface mismatch", "LiveKit FFI generated native import surface is invalid: {0}");

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var inputs = context.AdditionalTextsProvider
            .Where(static file => IsInput(Path.GetFileName(file.Path)))
            .Select(static (file, cancellationToken) => new Input(
                Path.GetFileName(file.Path),
                file.GetText(cancellationToken)?.ToString() ?? string.Empty))
            .Collect();
        var required = context.AnalyzerConfigOptionsProvider.Select(static (provider, _) =>
            provider.GlobalOptions.TryGetValue("build_property.HpdLiveKitFfiManifestRequired", out var value) &&
            bool.TryParse(value, out var parsed) && parsed);
        context.RegisterSourceOutput(inputs.Combine(required).Combine(context.CompilationProvider), static (productionContext, pair) =>
            Validate(productionContext, pair.Left.Left, pair.Left.Right, pair.Right));
    }

    private static bool IsInput(string name) =>
        string.Equals(name, BindingFile, StringComparison.Ordinal) ||
        string.Equals(name, ProtocolFile, StringComparison.Ordinal) ||
        string.Equals(name, NativeFile, StringComparison.Ordinal);

    private static void Validate(SourceProductionContext context, ImmutableArray<Input> inputs, bool required, Compilation compilation)
    {
        if (inputs.Length == 0)
        {
            if (required)
                Report(context, IncompleteLock, "the LiveKit product shell requires exactly one binding, protocol and native inventory");
            return;
        }
        if (!TrySingle(inputs, BindingFile, out var bindingText) ||
            !TrySingle(inputs, ProtocolFile, out var protocolText) ||
            !TrySingle(inputs, NativeFile, out var nativeText))
        {
            Report(context, IncompleteLock, "expected exactly one binding, protocol and native inventory");
            return;
        }

        if (!TryParseProtocol(protocolText, out var protocol, out var protocolError))
        {
            Report(context, IncompleteLock, protocolError ?? "unknown protocol parse failure");
            return;
        }
        if (!TryParseNative(nativeText, out var native, out var nativeError))
        {
            Report(context, IncompleteLock, nativeError ?? "unknown native parse failure");
            return;
        }
        if (!TryParseBinding(bindingText, out var binding, out var bindingError))
        {
            Report(context, IncompleteLock, bindingError ?? "unknown binding parse failure");
            return;
        }

        if (!string.Equals(binding.Tag, protocol.Tag, StringComparison.Ordinal) ||
            !string.Equals(binding.Commit, protocol.Commit, StringComparison.Ordinal) ||
            !string.Equals(binding.DescriptorSha256, protocol.DescriptorSha256, StringComparison.Ordinal))
        {
            Report(context, DescriptorMismatch, "protocol tag, commit or descriptor digest differs from reviewed truth");
            return;
        }
        if (!string.Equals(binding.Tag, native.Tag, StringComparison.Ordinal) ||
            !string.Equals(binding.Commit, native.Commit, StringComparison.Ordinal) ||
            !string.Equals(binding.HeaderSha256, native.HeaderSha256, StringComparison.Ordinal) ||
            !string.Equals(binding.ResponseBufferLaw, native.ResponseBufferLaw, StringComparison.Ordinal) ||
            !string.Equals(binding.CallbackBufferLaw, native.CallbackBufferLaw, StringComparison.Ordinal))
        {
            Report(context, IncompleteLock, "native revision, header or memory law differs from reviewed truth");
            return;
        }
        if (!binding.Abi.SequenceEqual(native.Abi, StringComparer.Ordinal))
        {
            Report(context, IncompleteLock, "ABI width inventory differs from reviewed native inventory");
            return;
        }
        foreach (var requiredExport in binding.RequiredExports)
        {
            if (!native.Exports.Contains(requiredExport))
            {
                Report(context, MissingExport, requiredExport);
                return;
            }
        }
        if (!SameArtifacts(binding.Artifacts, native.Artifacts))
        {
            Report(context, IncompleteLock, "artifact hashes or library names differ from native inventory");
            return;
        }
        foreach (var artifact in binding.Artifacts)
        {
            var qualified = string.Equals(native.Artifacts[artifact.Key].Disposition, "qualified", StringComparison.Ordinal);
            if (artifact.Value.Advertised && !qualified)
            {
                Report(context, UnsupportedRid, artifact.Key);
                return;
            }
        }
        if (binding.Artifacts.Count(static item => item.Value.Advertised) != 1 ||
            !binding.Artifacts.TryGetValue("osx-arm64", out var osxArm64) || !osxArm64.Advertised)
        {
            Report(context, UnsupportedRid, "the B1 support set must be exactly osx-arm64");
            return;
        }

        var correlations = new HashSet<string>(StringComparer.Ordinal);
        foreach (var operation in binding.Operations)
        {
            if (!protocol.Requests.Contains(operation.RequestCase) ||
                !protocol.Responses.Contains(operation.ResponseCase))
            {
                Report(context, MissingCompletion, $"{operation.Name} is not present in pinned request/response truth");
                return;
            }
            if (operation.IsAsync)
            {
                if (operation.CompletionCase == "none" || !protocol.Events.Contains(operation.CompletionCase))
                {
                    Report(context, MissingCompletion, operation.Name);
                    return;
                }
                var correlationOwner = operation.CompletionCase + "|" + operation.CorrelationField;
                if (operation.CorrelationField == "none" || !correlations.Add(correlationOwner))
                {
                    Report(context, DuplicateCorrelation, correlationOwner);
                    return;
                }
                if (!string.Equals(operation.Cancellation, "detach-after-issue", StringComparison.Ordinal))
                {
                    Report(context, AbandonedIssuedOperation, operation.Name);
                    return;
                }
                if (!string.Equals(operation.Name, operation.RequestCase, StringComparison.Ordinal) ||
                    !string.Equals(operation.Name, operation.ResponseCase, StringComparison.Ordinal) ||
                    !string.Equals(operation.Name, operation.CompletionCase, StringComparison.Ordinal) ||
                    !string.Equals(operation.CorrelationField, "AsyncId", StringComparison.Ordinal))
                {
                    Report(context, MissingCompletion, operation.Name);
                    return;
                }
            }
            else if (operation.CompletionCase != "none" || operation.CorrelationField != "none")
            {
                Report(context, MissingCompletion, $"synchronous {operation.Name} declares asynchronous completion state");
                return;
            }
            else if (!string.Equals(operation.Name, operation.RequestCase, StringComparison.Ordinal) ||
                     !string.Equals(operation.Name, operation.ResponseCase, StringComparison.Ordinal))
            {
                Report(context, MissingCompletion, operation.Name);
                return;
            }
            if (!string.Equals(operation.Admission, "bounded", StringComparison.Ordinal))
            {
                Report(context, UnboundedCallback, operation.Name);
                return;
            }
            foreach (var handle in operation.Handles)
            {
                if (!native.Releases.TryGetValue(handle, out var release))
                {
                    Report(context, MissingRelease, $"{operation.Name}:{handle}");
                    return;
                }
                if (string.Equals(operation.Release, "safehandle", StringComparison.Ordinal))
                {
                    Report(context, InvalidSafeHandle, $"{operation.Name}:{handle}");
                    return;
                }
                if (!string.Equals(operation.Release, "explicit-only", StringComparison.Ordinal) ||
                    !string.Equals(release, "explicit-only", StringComparison.Ordinal))
                {
                    Report(context, MissingRelease, $"{operation.Name}:{handle}");
                    return;
                }
            }
            if (string.Equals(operation.Name, "ClearAudioBuffer", StringComparison.Ordinal) &&
                !string.Equals(operation.Release, "local-source-queue-only", StringComparison.Ordinal))
            {
                Report(context, OverclaimedProof, operation.Release);
                return;
            }
        }

        if (!string.Equals(binding.UnknownEventDisposition, "quarantine", StringComparison.Ordinal))
        {
            Report(context, UnknownEvent, "unknown events must quarantine");
            return;
        }
        foreach (var observedEvent in binding.Events)
        {
            if (!protocol.Events.Contains(observedEvent.Name) ||
                (observedEvent.Name == "Panic" && observedEvent.Disposition != "quarantine"))
            {
                Report(context, UnknownEvent, observedEvent.Name);
                return;
            }
            foreach (var handle in observedEvent.Handles)
            {
                if (!native.Releases.TryGetValue(handle, out var release) ||
                    observedEvent.Release != "explicit-only" || release != "explicit-only")
                {
                    Report(context, MissingRelease, $"{observedEvent.Name}:{handle}");
                    return;
                }
            }
        }
        foreach (var operation in binding.Operations.Where(static item => item.IsAsync))
        {
            if (!binding.Events.Any(item => item.Name == operation.CompletionCase && item.Disposition == "completion"))
            {
                Report(context, UnknownEvent, operation.CompletionCase);
                return;
            }
        }
        if (!binding.Events.Any(static item => item.Name == "RoomEvent" && item.Disposition == "admitted") ||
            !binding.Events.Any(static item => item.Name == "AudioStreamEvent" && item.Disposition == "admitted") ||
            !binding.Events.Any(static item => item.Name == "Panic" && item.Disposition == "quarantine"))
        {
            Report(context, UnknownEvent, "required observation disposition");
            return;
        }
        if (!ExactReviewedSurface(bindingText, protocolText, nativeText))
        {
            Report(context, IncompleteLock, "the admitted operation, event, ABI, export or release surface differs from the B1-reviewed inventory");
            return;
        }
        if (required && !ExactNativeImportSurface(compilation, out var nativeSurfaceError))
        {
            Report(context, GeneratedSurfaceMismatch, nativeSurfaceError);
            return;
        }

        context.AddSource("LiveKitFfiBinding.g.cs", SourceText.From(GeneratedBinding, System.Text.Encoding.UTF8));
        EmitAudioSessionBinding(context, compilation);
    }

    private static void EmitAudioSessionBinding(SourceProductionContext context, Compilation compilation)
    {
        var binding = compilation.GetTypeByMetadataName("HPD.Agent.Audio.LiveKit.LiveKitAudioSessionBinding");
        if (binding is null) return;
        var attribute = binding.GetAttributes().SingleOrDefault(static value =>
            value.AttributeClass?.ToDisplayString() == "HPD.Agent.Audio.AudioSessionBindingAttribute");
        if (attribute is null ||
            !attribute.NamedArguments.Any(static value => value.Key == "Component" && (string?)value.Value.Value == "livekit") ||
            !attribute.NamedArguments.Any(static value => value.Key == "Schema" && (string?)value.Value.Value == "hpd.provider.livekit.audiotransport.sessionbinding") ||
            !attribute.NamedArguments.Any(static value => value.Key == "Version" && (uint?)value.Value.Value == 1u))
        {
            Report(context, IncompleteLock, "LiveKit Audio session binding attribute differs from the reviewed component/schema/version identity");
            return;
        }
        // L52A retains the reviewed binding identity, while application-level
        // binding serialization is source-owned by the LiveKit package. The
        // former v9 provider-registration graph is intentionally not emitted.
    }

    private static bool ExactNativeImportSurface(Compilation compilation, out string error)
    {
        var type = compilation.GetTypeByMetadataName("HPD.Agent.Audio.LiveKit.Generated.LiveKitFfiNative");
        if (type is null || !type.IsStatic || type.DeclaredAccessibility != Accessibility.Internal)
            return NativeFail("missing internal static LiveKitFfiNative", out error);
        var methodArray = type.GetMembers().OfType<IMethodSymbol>()
            .Where(static method => method.MethodKind == MethodKind.Ordinary).ToArray();
        if (methodArray.Length != 4 || methodArray.Select(static method => method.Name).Distinct(StringComparer.Ordinal).Count() != 4)
            return NativeFail("expected exactly Initialize, Request, DropHandle and Dispose", out error);
        var methods = methodArray.ToDictionary(static method => method.Name, StringComparer.Ordinal);
        if (!methods.ContainsKey("Initialize") || !methods.ContainsKey("Request") ||
            !methods.ContainsKey("DropHandle") || !methods.ContainsKey("Dispose"))
            return NativeFail("expected exactly Initialize, Request, DropHandle and Dispose", out error);
        if (!NativeMethod(methods["Initialize"], "livekit_ffi_initialize", SpecialType.System_Void,
                TypeKind.FunctionPointer, SpecialType.System_Boolean, SpecialType.System_IntPtr, SpecialType.System_IntPtr) ||
            !NativeMethod(methods["Request"], "livekit_ffi_request", SpecialType.System_UInt64,
                TypeKind.Pointer, SpecialType.System_UIntPtr, SpecialType.System_IntPtr, SpecialType.System_UIntPtr) ||
            methods["Request"].Parameters[2].RefKind != RefKind.Out || methods["Request"].Parameters[3].RefKind != RefKind.Out ||
            !NativeMethod(methods["DropHandle"], "livekit_ffi_drop_handle", SpecialType.System_Boolean, SpecialType.System_UInt64) ||
            !NativeMethod(methods["Dispose"], "livekit_ffi_dispose", SpecialType.System_Void))
            return NativeFail("method signature, entry point or calling convention differs", out error);
        if (methods["Initialize"].Parameters.Any(static parameter => parameter.RefKind != RefKind.None) ||
            methods["Request"].Parameters[0].RefKind != RefKind.None || methods["Request"].Parameters[1].RefKind != RefKind.None ||
            methods["DropHandle"].Parameters[0].RefKind != RefKind.None)
            return NativeFail("native parameter ref-kind differs", out error);
        var callback = (IFunctionPointerTypeSymbol)methods["Initialize"].Parameters[0].Type;
        if (callback.Signature.CallingConvention != SignatureCallingConvention.CDecl ||
            callback.Signature.ReturnType.SpecialType != SpecialType.System_Void ||
            callback.Signature.Parameters.Length != 2 ||
            callback.Signature.Parameters[0].Type.SpecialType != SpecialType.System_IntPtr ||
            callback.Signature.Parameters[1].Type.SpecialType != SpecialType.System_UIntPtr)
            return NativeFail("callback ABI is not exact unmanaged Cdecl (nint,nuint)->void", out error);
        if (!IsI1(methods["DropHandle"].GetReturnTypeAttributes()) ||
            !IsI1(methods["Initialize"].Parameters[1].GetAttributes()))
            return NativeFail("capture-logs and drop-handle bool ABI are not exact I1", out error);
        var library = type.GetMembers("LibraryName").OfType<IFieldSymbol>().SingleOrDefault();
        if (library is null || !library.IsConst || !Equals(library.ConstantValue, "livekit_ffi"))
            return NativeFail("library name is not the exact constant livekit_ffi", out error);
        error = string.Empty;
        return true;
    }

    private static bool NativeMethod(IMethodSymbol method, string entryPoint, SpecialType returnType, params object[] parameterTypes)
    {
        if (!method.IsStatic || !method.IsPartialDefinition || method.Arity != 0 ||
            method.DeclaredAccessibility != Accessibility.Internal || method.ReturnType.SpecialType != returnType ||
            method.Parameters.Length != parameterTypes.Length)
            return false;
        for (var index = 0; index < parameterTypes.Length; index++)
        {
            var expected = parameterTypes[index];
            if (expected is SpecialType special && method.Parameters[index].Type.SpecialType != special) return false;
            if (expected is TypeKind kind && method.Parameters[index].Type.TypeKind != kind) return false;
        }
        var import = method.GetAttributes().SingleOrDefault(static attribute =>
            attribute.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.LibraryImportAttribute");
        var callConv = method.GetAttributes().SingleOrDefault(static attribute =>
            attribute.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.UnmanagedCallConvAttribute");
        var cdecl = callConv?.NamedArguments.SingleOrDefault(static item => item.Key == "CallConvs").Value.Values;
        return import is not null && import.ConstructorArguments.Length == 1 &&
            Equals(import.ConstructorArguments[0].Value, "livekit_ffi") &&
            import.NamedArguments.Length == 1 &&
            import.NamedArguments.Any(item => item.Key == "EntryPoint" && Equals(item.Value.Value, entryPoint)) &&
            callConv is not null && callConv.NamedArguments.Length == 1 && cdecl is { Length: 1 } &&
            cdecl.Value[0].Value is INamedTypeSymbol convention &&
            convention.ToDisplayString() == "System.Runtime.CompilerServices.CallConvCdecl";
    }

    private static bool NativeFail(string message, out string error) { error = message; return false; }
    private static bool IsI1(ImmutableArray<AttributeData> attributes)
    {
        var marshal = attributes.SingleOrDefault(static attribute =>
            attribute.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.MarshalAsAttribute");
        return marshal is not null && marshal.ConstructorArguments.Length == 1 &&
            Equals(marshal.ConstructorArguments[0].Value, 3);
    }

    private const string GeneratedBinding = """
// <auto-generated />
#nullable enable
namespace HPD.Agent.Audio.LiveKit.Generated;

using System;
using System.Collections.Immutable;
internal enum LiveKitFfiOperation : byte
{
    Connect = 1,
    ReadyForRoomEvent = 2,
    NewAudioStream = 3,
    NewAudioSource = 4,
    CreateAudioTrack = 5,
    PublishTrack = 6,
    CaptureAudioFrame = 7,
    ClearAudioBuffer = 8,
    UnpublishTrack = 9,
    Disconnect = 10
}

internal enum LiveKitFfiRequestCase
{
    None = 0,
    Connect = 3,
    Disconnect = 4,
    PublishTrack = 5,
    UnpublishTrack = 6,
    CreateAudioTrack = 16,
    NewAudioStream = 25,
    NewAudioSource = 26,
    CaptureAudioFrame = 27,
    ClearAudioBuffer = 28,
    ReadyForRoomEvent = 83
}

internal enum LiveKitFfiResponseCase
{
    None = 0,
    Connect = 3,
    Disconnect = 4,
    PublishTrack = 5,
    UnpublishTrack = 6,
    CreateAudioTrack = 16,
    NewAudioStream = 25,
    NewAudioSource = 26,
    CaptureAudioFrame = 27,
    ClearAudioBuffer = 28,
    ReadyForRoomEvent = 82
}

internal enum LiveKitFfiEventCase
{
    None = 0,
    RoomEvent = 1,
    AudioStreamEvent = 4,
    Connect = 5,
    Disconnect = 7,
    PublishTrack = 9,
    UnpublishTrack = 10,
    CaptureAudioFrame = 13,
    Panic = 20
}

internal enum LiveKitFfiRouteDisposition : byte
{
    Observation = 1,
    Completion = 2,
    Quarantined = 3,
    InvalidCorrelation = 4
}

internal enum LiveKitFfiHandleKind : byte
{
    Room = 1,
    Participant = 2,
    TrackPublication = 3,
    Track = 4,
    AudioStream = 5,
    AudioSource = 6,
    OwnedAudioFrame = 7
}

internal readonly record struct LiveKitFfiCompletionKey(LiveKitFfiOperation Operation, ulong AsyncId);
internal readonly record struct LiveKitFfiArtifactExpectation(
    string Rid, string ArchiveSha256, string LibraryName, string LibrarySha256, bool Qualified, bool Advertised);
internal readonly record struct LiveKitFfiHandleReleaseExpectation(
    LiveKitFfiHandleKind Kind, string EntryPoint);
internal readonly record struct LiveKitFfiRoomHandle(ulong Value) { internal bool IsNull => Value == 0; }
internal readonly record struct LiveKitFfiParticipantHandle(ulong Value) { internal bool IsNull => Value == 0; }
internal readonly record struct LiveKitFfiTrackPublicationHandle(ulong Value) { internal bool IsNull => Value == 0; }
internal readonly record struct LiveKitFfiTrackHandle(ulong Value) { internal bool IsNull => Value == 0; }
internal readonly record struct LiveKitFfiAudioStreamHandle(ulong Value) { internal bool IsNull => Value == 0; }
internal readonly record struct LiveKitFfiAudioSourceHandle(ulong Value) { internal bool IsNull => Value == 0; }
internal readonly record struct LiveKitFfiOwnedAudioFrameHandle(ulong Value) { internal bool IsNull => Value == 0; }

internal interface ILiveKitFfiIssuedOperationSink
{
    void ObserveCompletion(LiveKitFfiCompletionKey key, LiveKitFfiEventCase eventCase);
    void ObserveRoomEvent();
    void ObserveAudioStreamEvent();
    void Quarantine(LiveKitFfiEventCase eventCase, string safeCode);
}

internal static class LiveKitFfiGeneratedProtocol
{
    internal const string ProtocolTag = "livekit-ffi/v0.12.60";
    internal const string ProtocolCommit = "a4f41cdcae5214986ab4ac5a8cb8507e5cc7ee6e";
    internal const string DescriptorSha256 = "c19e07a6ade57bd49b4e0f2340d79bb555a30b0a5c840e00c522371f5fccf4d8";
    internal const string HeaderSha256 = "fb33a01ca733b32738620f4b167ab1156a1dd291aacc53b108579a432008dd8f";
    internal const string QualifiedRid = "osx-arm64";
    internal const string QualifiedLibrarySha256 = "cf034115fb3b94b5682151d2d36cb5ea351e97b881cd5ac0b97d0873b2a2b1da";

    internal static ReadOnlySpan<LiveKitFfiOperation> Operations =>
    [
        LiveKitFfiOperation.Connect, LiveKitFfiOperation.ReadyForRoomEvent,
        LiveKitFfiOperation.NewAudioStream, LiveKitFfiOperation.NewAudioSource,
        LiveKitFfiOperation.CreateAudioTrack, LiveKitFfiOperation.PublishTrack,
        LiveKitFfiOperation.CaptureAudioFrame, LiveKitFfiOperation.ClearAudioBuffer,
        LiveKitFfiOperation.UnpublishTrack, LiveKitFfiOperation.Disconnect
    ];

    internal static ReadOnlySpan<LiveKitFfiRequestCase> RequestCases =>
    [
        LiveKitFfiRequestCase.Connect, LiveKitFfiRequestCase.ReadyForRoomEvent,
        LiveKitFfiRequestCase.NewAudioStream, LiveKitFfiRequestCase.NewAudioSource,
        LiveKitFfiRequestCase.CreateAudioTrack, LiveKitFfiRequestCase.PublishTrack,
        LiveKitFfiRequestCase.CaptureAudioFrame, LiveKitFfiRequestCase.ClearAudioBuffer,
        LiveKitFfiRequestCase.UnpublishTrack, LiveKitFfiRequestCase.Disconnect
    ];

    internal static ReadOnlySpan<LiveKitFfiResponseCase> ResponseCases =>
    [
        LiveKitFfiResponseCase.Connect, LiveKitFfiResponseCase.ReadyForRoomEvent,
        LiveKitFfiResponseCase.NewAudioStream, LiveKitFfiResponseCase.NewAudioSource,
        LiveKitFfiResponseCase.CreateAudioTrack, LiveKitFfiResponseCase.PublishTrack,
        LiveKitFfiResponseCase.CaptureAudioFrame, LiveKitFfiResponseCase.ClearAudioBuffer,
        LiveKitFfiResponseCase.UnpublishTrack, LiveKitFfiResponseCase.Disconnect
    ];

    internal static ReadOnlySpan<LiveKitFfiEventCase> Events =>
    [
        LiveKitFfiEventCase.RoomEvent, LiveKitFfiEventCase.AudioStreamEvent,
        LiveKitFfiEventCase.Connect, LiveKitFfiEventCase.PublishTrack,
        LiveKitFfiEventCase.CaptureAudioFrame, LiveKitFfiEventCase.UnpublishTrack,
        LiveKitFfiEventCase.Disconnect, LiveKitFfiEventCase.Panic
    ];

    internal static ReadOnlySpan<LiveKitFfiHandleKind> Handles =>
    [
        LiveKitFfiHandleKind.Room, LiveKitFfiHandleKind.Participant,
        LiveKitFfiHandleKind.TrackPublication, LiveKitFfiHandleKind.Track,
        LiveKitFfiHandleKind.AudioStream, LiveKitFfiHandleKind.AudioSource,
        LiveKitFfiHandleKind.OwnedAudioFrame
    ];

    internal static ImmutableArray<string> Abi { get; } = ["request-length|nuint", "response-length|nuint"];
    internal static ImmutableArray<string> Exports { get; } =
        ["livekit_ffi_initialize", "livekit_ffi_request", "livekit_ffi_drop_handle", "livekit_ffi_dispose"];
    internal static ImmutableArray<LiveKitFfiHandleReleaseExpectation> Releases { get; } =
    [
        new(LiveKitFfiHandleKind.Room, "livekit_ffi_drop_handle"),
        new(LiveKitFfiHandleKind.Participant, "livekit_ffi_drop_handle"),
        new(LiveKitFfiHandleKind.TrackPublication, "livekit_ffi_drop_handle"),
        new(LiveKitFfiHandleKind.Track, "livekit_ffi_drop_handle"),
        new(LiveKitFfiHandleKind.AudioStream, "livekit_ffi_drop_handle"),
        new(LiveKitFfiHandleKind.AudioSource, "livekit_ffi_drop_handle"),
        new(LiveKitFfiHandleKind.OwnedAudioFrame, "livekit_ffi_drop_handle")
    ];
    internal static ImmutableArray<LiveKitFfiArtifactExpectation> Artifacts { get; } =
    [
        new("osx-arm64", "f9021cf5da0f1aae11b63c3d6cec2b049d74c9ba54a8f26527a7dcd717ea0ffe", "liblivekit_ffi.dylib", "cf034115fb3b94b5682151d2d36cb5ea351e97b881cd5ac0b97d0873b2a2b1da", true, true),
        new("osx-x64", "74a3612d2f7f1b32d309c5653fa82e5f3dde4d9a8e14e8ba679b77a2e877070a", "liblivekit_ffi.dylib", "d4c16cedfe4eb45a8d17533313a9a67036450acf35c418ae252c8699a9cf77f9", false, false),
        new("linux-arm64", "dde0eaada4224a16ea72d1b292650e57b436a83af361f8d7ddec9fe753429771", "liblivekit_ffi.so", "363ceaf8018cc6f182e06f3511bf641312acab9fa94623d3430a36c47c834765", false, false),
        new("linux-x64", "413b6a6be0d61ba551b899e3520cde6bb35a39eb49edf12484ce74bbe84eafb7", "liblivekit_ffi.so", "86e0cd105071083a475d11f3fe268b88f91077a1597ead805bc2585c633a9490", false, false),
        new("win-arm64", "f4d6e59724076a26dd087ff31b3841fb19a50d25a6f14b04b9bbc618aac31c58", "livekit_ffi.dll", "b9872f3413591f49ef849b221a539ec09ee643d498fc3be4435ebc03fcfc8654", false, false),
        new("win-x64", "dd67eb2fd021442566a3003c775884ff151a22215beaadf4b321b80670430817", "livekit_ffi.dll", "4eb322a0df83674b4b5424dcdf0d27daba998b09d7e25efde7fa9e4b78aba3f2", false, false)
    ];

    internal static bool TryGetOperation(LiveKitFfiRequestCase requestCase, out LiveKitFfiOperation operation)
    {
        operation = requestCase switch
        {
            LiveKitFfiRequestCase.Connect => LiveKitFfiOperation.Connect,
            LiveKitFfiRequestCase.ReadyForRoomEvent => LiveKitFfiOperation.ReadyForRoomEvent,
            LiveKitFfiRequestCase.NewAudioStream => LiveKitFfiOperation.NewAudioStream,
            LiveKitFfiRequestCase.NewAudioSource => LiveKitFfiOperation.NewAudioSource,
            LiveKitFfiRequestCase.CreateAudioTrack => LiveKitFfiOperation.CreateAudioTrack,
            LiveKitFfiRequestCase.PublishTrack => LiveKitFfiOperation.PublishTrack,
            LiveKitFfiRequestCase.CaptureAudioFrame => LiveKitFfiOperation.CaptureAudioFrame,
            LiveKitFfiRequestCase.ClearAudioBuffer => LiveKitFfiOperation.ClearAudioBuffer,
            LiveKitFfiRequestCase.UnpublishTrack => LiveKitFfiOperation.UnpublishTrack,
            LiveKitFfiRequestCase.Disconnect => LiveKitFfiOperation.Disconnect,
            _ => default
        };
        return requestCase is LiveKitFfiRequestCase.Connect or LiveKitFfiRequestCase.ReadyForRoomEvent or
            LiveKitFfiRequestCase.NewAudioStream or LiveKitFfiRequestCase.NewAudioSource or
            LiveKitFfiRequestCase.CreateAudioTrack or LiveKitFfiRequestCase.PublishTrack or
            LiveKitFfiRequestCase.CaptureAudioFrame or LiveKitFfiRequestCase.ClearAudioBuffer or
            LiveKitFfiRequestCase.UnpublishTrack or LiveKitFfiRequestCase.Disconnect;
    }

    internal static bool TryGetOperation(LiveKitFfiResponseCase responseCase, out LiveKitFfiOperation operation)
    {
        operation = responseCase switch
        {
            LiveKitFfiResponseCase.Connect => LiveKitFfiOperation.Connect,
            LiveKitFfiResponseCase.ReadyForRoomEvent => LiveKitFfiOperation.ReadyForRoomEvent,
            LiveKitFfiResponseCase.NewAudioStream => LiveKitFfiOperation.NewAudioStream,
            LiveKitFfiResponseCase.NewAudioSource => LiveKitFfiOperation.NewAudioSource,
            LiveKitFfiResponseCase.CreateAudioTrack => LiveKitFfiOperation.CreateAudioTrack,
            LiveKitFfiResponseCase.PublishTrack => LiveKitFfiOperation.PublishTrack,
            LiveKitFfiResponseCase.CaptureAudioFrame => LiveKitFfiOperation.CaptureAudioFrame,
            LiveKitFfiResponseCase.ClearAudioBuffer => LiveKitFfiOperation.ClearAudioBuffer,
            LiveKitFfiResponseCase.UnpublishTrack => LiveKitFfiOperation.UnpublishTrack,
            LiveKitFfiResponseCase.Disconnect => LiveKitFfiOperation.Disconnect,
            _ => default
        };
        return responseCase is LiveKitFfiResponseCase.Connect or LiveKitFfiResponseCase.ReadyForRoomEvent or
            LiveKitFfiResponseCase.NewAudioStream or LiveKitFfiResponseCase.NewAudioSource or
            LiveKitFfiResponseCase.CreateAudioTrack or LiveKitFfiResponseCase.PublishTrack or
            LiveKitFfiResponseCase.CaptureAudioFrame or LiveKitFfiResponseCase.ClearAudioBuffer or
            LiveKitFfiResponseCase.UnpublishTrack or LiveKitFfiResponseCase.Disconnect;
    }

    internal static bool TryGetIssuedCompletion(
        LiveKitFfiResponseCase responseCase,
        ulong asyncId,
        out LiveKitFfiCompletionKey key)
    {
        var operation = responseCase switch
        {
            LiveKitFfiResponseCase.Connect => LiveKitFfiOperation.Connect,
            LiveKitFfiResponseCase.PublishTrack => LiveKitFfiOperation.PublishTrack,
            LiveKitFfiResponseCase.CaptureAudioFrame => LiveKitFfiOperation.CaptureAudioFrame,
            LiveKitFfiResponseCase.UnpublishTrack => LiveKitFfiOperation.UnpublishTrack,
            LiveKitFfiResponseCase.Disconnect => LiveKitFfiOperation.Disconnect,
            _ => default
        };
        if (operation == default || asyncId == 0)
        {
            key = default;
            return false;
        }
        key = new LiveKitFfiCompletionKey(operation, asyncId);
        return true;
    }

    internal static bool TryGetCompletionEvent(LiveKitFfiOperation operation, out LiveKitFfiEventCase eventCase)
    {
        eventCase = operation switch
        {
            LiveKitFfiOperation.Connect => LiveKitFfiEventCase.Connect,
            LiveKitFfiOperation.PublishTrack => LiveKitFfiEventCase.PublishTrack,
            LiveKitFfiOperation.CaptureAudioFrame => LiveKitFfiEventCase.CaptureAudioFrame,
            LiveKitFfiOperation.UnpublishTrack => LiveKitFfiEventCase.UnpublishTrack,
            LiveKitFfiOperation.Disconnect => LiveKitFfiEventCase.Disconnect,
            _ => default
        };
        return eventCase != default;
    }

    internal static LiveKitFfiRouteDisposition RouteEvent(
        LiveKitFfiEventCase eventCase,
        ulong asyncId,
        ILiveKitFfiIssuedOperationSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        switch (eventCase)
        {
            case LiveKitFfiEventCase.RoomEvent:
                sink.ObserveRoomEvent();
                return LiveKitFfiRouteDisposition.Observation;
            case LiveKitFfiEventCase.AudioStreamEvent:
                sink.ObserveAudioStreamEvent();
                return LiveKitFfiRouteDisposition.Observation;
            case LiveKitFfiEventCase.Panic:
                sink.Quarantine(eventCase, "native-panic");
                return LiveKitFfiRouteDisposition.Quarantined;
            case LiveKitFfiEventCase.Connect:
                return Complete(LiveKitFfiOperation.Connect, eventCase, asyncId, sink);
            case LiveKitFfiEventCase.PublishTrack:
                return Complete(LiveKitFfiOperation.PublishTrack, eventCase, asyncId, sink);
            case LiveKitFfiEventCase.CaptureAudioFrame:
                return Complete(LiveKitFfiOperation.CaptureAudioFrame, eventCase, asyncId, sink);
            case LiveKitFfiEventCase.UnpublishTrack:
                return Complete(LiveKitFfiOperation.UnpublishTrack, eventCase, asyncId, sink);
            case LiveKitFfiEventCase.Disconnect:
                return Complete(LiveKitFfiOperation.Disconnect, eventCase, asyncId, sink);
            default:
                sink.Quarantine(eventCase, "unknown-ffi-event");
                return LiveKitFfiRouteDisposition.Quarantined;
        }
    }

    private static LiveKitFfiRouteDisposition Complete(
        LiveKitFfiOperation operation,
        LiveKitFfiEventCase eventCase,
        ulong asyncId,
        ILiveKitFfiIssuedOperationSink sink)
    {
        if (asyncId == 0)
        {
            sink.Quarantine(eventCase, "invalid-ffi-correlation");
            return LiveKitFfiRouteDisposition.InvalidCorrelation;
        }
        sink.ObserveCompletion(new LiveKitFfiCompletionKey(operation, asyncId), eventCase);
        return LiveKitFfiRouteDisposition.Completion;
    }
}
""";

    private const string GeneratedAudioSessionBinding = """
// <auto-generated />
#nullable enable
namespace HPD.Agent.Audio.LiveKit;

public sealed partial class LiveKitAudioTransportProvider
{
    public static global::HPD.Agent.Audio.AudioSessionBindingRegistration<global::HPD.Agent.Audio.LiveKit.LiveKitAudioSessionBinding> SessionBindingRegistration { get; } =
        new(
            Key,
            "hpd.provider.livekit.audiotransport.sessionbinding",
            1,
            global::HPD.Agent.Audio.LiveKit.LiveKitTransportJsonContext.Default.LiveKitAudioSessionBinding,
            static binding => binding with { },
            static binding => global::System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                binding,
                global::HPD.Agent.Audio.LiveKit.LiveKitTransportJsonContext.Default.LiveKitAudioSessionBinding),
            static binding =>
            {
                global::System.ArgumentException.ThrowIfNullOrWhiteSpace(binding.RoomName);
                global::System.ArgumentException.ThrowIfNullOrWhiteSpace(binding.ParticipantIdentity);
            });

    public static global::HPD.Agent.Audio.AudioSessionBindingCatalog CreateSessionBindingCatalog() =>
        new(new global::HPD.Agent.Audio.AudioSessionBindingRegistration[] { SessionBindingRegistration });
}

public static class LiveKitAudioTransport
{
    public static global::HPD.Agent.AudioSessionStartBindings Bindings(
        global::HPD.Agent.Audio.LiveKit.LiveKitAudioSessionBinding binding)
    {
        global::System.ArgumentNullException.ThrowIfNull(binding);
        global::System.ArgumentException.ThrowIfNullOrWhiteSpace(binding.RoomName);
        global::System.ArgumentException.ThrowIfNullOrWhiteSpace(binding.ParticipantIdentity);
        var owned = binding with { };
        return new global::HPD.Agent.AudioSessionStartBindings
        {
            Bindings = new global::HPD.Agent.AudioSessionStartBinding[]
            {
                new()
                {
                    ComponentInstance = global::HPD.Agent.Audio.LiveKit.LiveKitAudioTransportProvider.Key,
                    Schema = "hpd.provider.livekit.audiotransport.sessionbinding",
                    Version = 1,
                    Value = global::System.Text.Json.JsonSerializer.SerializeToElement(
                        owned,
                        global::HPD.Agent.Audio.LiveKit.LiveKitTransportJsonContext.Default.LiveKitAudioSessionBinding)
                }
            }
        };
    }
}
""";

    private static bool ExactReviewedSurface(string binding, string protocol, string native) =>
        ExactRows(protocol, "request", ReviewedRequests) &&
        ExactRows(protocol, "response", ReviewedResponses) &&
        ExactRows(protocol, "event", ReviewedEvents) &&
        ExactRows(native, "abi", ReviewedAbi) &&
        ExactRows(native, "export", ReviewedExports) &&
        ExactRows(native, "release", ReviewedReleases) &&
        ExactRows(binding, "operation", ReviewedOperations) &&
        ExactRows(binding, "observe-event", ReviewedObservations);

    private static bool ExactRows(string text, string kind, string[] expected)
    {
        var actual = Rows(text).Where(row => row[0] == kind).Select(static row => string.Join("|", row));
        return actual.OrderBy(static row => row, StringComparer.Ordinal)
            .SequenceEqual(expected.OrderBy(static row => row, StringComparer.Ordinal), StringComparer.Ordinal);
    }

    private static readonly string[] ReviewedRequests =
    ["request|Connect", "request|ReadyForRoomEvent", "request|NewAudioStream", "request|NewAudioSource", "request|CreateAudioTrack", "request|PublishTrack", "request|CaptureAudioFrame", "request|ClearAudioBuffer", "request|UnpublishTrack", "request|Disconnect"];
    private static readonly string[] ReviewedResponses =
    ["response|Connect", "response|ReadyForRoomEvent", "response|NewAudioStream", "response|NewAudioSource", "response|CreateAudioTrack", "response|PublishTrack", "response|CaptureAudioFrame", "response|ClearAudioBuffer", "response|UnpublishTrack", "response|Disconnect"];
    private static readonly string[] ReviewedEvents =
    ["event|RoomEvent", "event|AudioStreamEvent", "event|Connect", "event|PublishTrack", "event|CaptureAudioFrame", "event|UnpublishTrack", "event|Disconnect", "event|Panic"];
    private static readonly string[] ReviewedAbi = ["abi|request-length|nuint", "abi|response-length|nuint"];
    private static readonly string[] ReviewedExports = ["export|livekit_ffi_initialize", "export|livekit_ffi_request", "export|livekit_ffi_drop_handle", "export|livekit_ffi_dispose"];
    private static readonly string[] ReviewedReleases =
    ["release|Room|livekit_ffi_drop_handle|explicit-only", "release|Participant|livekit_ffi_drop_handle|explicit-only", "release|TrackPublication|livekit_ffi_drop_handle|explicit-only", "release|Track|livekit_ffi_drop_handle|explicit-only", "release|AudioStream|livekit_ffi_drop_handle|explicit-only", "release|AudioSource|livekit_ffi_drop_handle|explicit-only", "release|OwnedAudioFrame|livekit_ffi_drop_handle|explicit-only"];
    private static readonly string[] ReviewedOperations =
    [
        "operation|Connect|Connect|Connect|Connect|AsyncId|Room,Participant,TrackPublication|async|bounded|detach-after-issue|explicit-only",
        "operation|ReadyForRoomEvent|ReadyForRoomEvent|ReadyForRoomEvent|none|none|none|sync|bounded|pre-issue-only|none",
        "operation|NewAudioStream|NewAudioStream|NewAudioStream|none|none|AudioStream|sync|bounded|pre-issue-only|explicit-only",
        "operation|NewAudioSource|NewAudioSource|NewAudioSource|none|none|AudioSource|sync|bounded|pre-issue-only|explicit-only",
        "operation|CreateAudioTrack|CreateAudioTrack|CreateAudioTrack|none|none|Track|sync|bounded|pre-issue-only|explicit-only",
        "operation|PublishTrack|PublishTrack|PublishTrack|PublishTrack|AsyncId|TrackPublication|async|bounded|detach-after-issue|explicit-only",
        "operation|CaptureAudioFrame|CaptureAudioFrame|CaptureAudioFrame|CaptureAudioFrame|AsyncId|none|async|bounded|detach-after-issue|none",
        "operation|ClearAudioBuffer|ClearAudioBuffer|ClearAudioBuffer|none|none|none|sync|bounded|pre-issue-only|local-source-queue-only",
        "operation|UnpublishTrack|UnpublishTrack|UnpublishTrack|UnpublishTrack|AsyncId|none|async|bounded|detach-after-issue|none",
        "operation|Disconnect|Disconnect|Disconnect|Disconnect|AsyncId|none|async|bounded|detach-after-issue|none"
    ];
    private static readonly string[] ReviewedObservations =
    [
        "observe-event|RoomEvent|admitted|Track,Participant|explicit-only",
        "observe-event|AudioStreamEvent|admitted|OwnedAudioFrame|explicit-only",
        "observe-event|Connect|completion|none|none",
        "observe-event|PublishTrack|completion|none|none",
        "observe-event|CaptureAudioFrame|completion|none|none",
        "observe-event|UnpublishTrack|completion|none|none",
        "observe-event|Disconnect|completion|none|none",
        "observe-event|Panic|quarantine|none|none"
    ];

    private static bool TryParseProtocol(string text, out ProtocolInventory value, out string? error)
    {
        value = new ProtocolInventory();
        error = null;
        var formatSeen = false;
        foreach (var row in Rows(text))
        {
            switch (row[0])
            {
                case "format" when row.Length == 3 && row[1] == "hpd.livekit.ffi.protocol-inventory" && row[2] == "1":
                    if (formatSeen) return Fail("duplicate protocol format", out error);
                    formatSeen = true;
                    break;
                case "upstream" when row.Length == 3 && row[1] == "livekit-ffi/v0.12.60" && GitSha(row[2]) && value.Tag.Length == 0:
                    value.Tag = row[1];
                    value.Commit = row[2];
                    break;
                case "descriptor" when row.Length == 2 && Sha(row[1]) && value.DescriptorSha256.Length == 0:
                    value.DescriptorSha256 = row[1];
                    break;
                case "file" when row.Length == 3 && Token(row[1]) && Sha(row[2]) && AddUnique(value.Files, row[1], row[2]):
                    break;
                case "request" when row.Length == 2 && Case(row[1]) && value.Requests.Add(row[1]):
                    break;
                case "response" when row.Length == 2 && Case(row[1]) && value.Responses.Add(row[1]):
                    break;
                case "event" when row.Length == 2 && Case(row[1]) && value.Events.Add(row[1]):
                    break;
                default:
                    return Fail("invalid protocol row: " + string.Join("|", row), out error);
            }
        }
        var expectedFiles = new[] { "audio_frame.proto", "data_stream.proto", "data_track.proto", "e2ee.proto", "ffi.proto", "handle.proto", "participant.proto", "room.proto", "rpc.proto", "stats.proto", "track.proto", "track_publication.proto", "video_frame.proto" };
        if (!formatSeen || value.Tag.Length == 0 || value.Commit.Length == 0 || value.DescriptorSha256.Length == 0 ||
            !value.Files.Keys.OrderBy(static item => item, StringComparer.Ordinal).SequenceEqual(expectedFiles, StringComparer.Ordinal) ||
            value.Requests.Count == 0 || value.Responses.Count == 0 || value.Events.Count == 0)
            return Fail("protocol inventory is not the exact reviewed 13-file projection", out error);
        return true;
    }

    private static bool TryParseNative(string text, out NativeInventory value, out string? error)
    {
        value = new NativeInventory();
        error = null;
        var formatSeen = false;
        foreach (var row in Rows(text))
        {
            switch (row[0])
            {
                case "format" when row.Length == 3 && row[1] == "hpd.livekit.ffi.native-inventory" && row[2] == "1":
                    if (formatSeen) return Fail("duplicate native format", out error);
                    formatSeen = true;
                    break;
                case "upstream" when row.Length == 3 && row[1] == "livekit-ffi/v0.12.60" && GitSha(row[2]) && value.Tag.Length == 0:
                    value.Tag = row[1];
                    value.Commit = row[2];
                    break;
                case "header" when row.Length == 2 && Sha(row[1]) && value.HeaderSha256.Length == 0:
                    value.HeaderSha256 = row[1];
                    break;
                case "abi" when row.Length == 3 && Token(row[1]) && row[2] == "nuint" && value.Abi.Add(row[1] + "|" + row[2]):
                    break;
                case "response-buffer" when row.Length == 3 && row[1] == "livekit_ffi_drop_handle" && row[2] == "immediate-copy-then-drop" && value.ResponseBufferLaw.Length == 0:
                    value.ResponseBufferLaw = row[1] + "|" + row[2];
                    break;
                case "callback-buffer" when row.Length == 4 && row[1] == "borrowed-until-return" && row[2] == "copy-before-return" && row[3] == "bounded" && value.CallbackBufferLaw.Length == 0:
                    value.CallbackBufferLaw = row[1] + "|" + row[2] + "|" + row[3];
                    break;
                case "export" when row.Length == 2 && NativeName(row[1]) && value.Exports.Add(row[1]):
                    break;
                case "release" when row.Length == 4 && Case(row[1]) && NativeName(row[2]) && row[3] == "explicit-only" && AddUnique(value.Releases, row[1], row[3]):
                    break;
                case "artifact" when TryArtifact(row, qualifiedVocabulary: true, out var artifact) && AddUnique(value.Artifacts, artifact.Rid, artifact):
                    break;
                default:
                    return Fail("invalid native row: " + string.Join("|", row), out error);
            }
        }
        if (!formatSeen || value.Tag.Length == 0 || value.Commit.Length == 0 || value.HeaderSha256.Length == 0 ||
            value.ResponseBufferLaw.Length == 0 || value.CallbackBufferLaw.Length == 0 ||
            value.Abi.Count != 2 || value.Exports.Count == 0 || value.Releases.Count == 0 || !ExactRids(value.Artifacts.Keys) ||
            value.Artifacts.Count(static item => item.Value.Disposition == "qualified") != 1 ||
            !value.Artifacts.TryGetValue("osx-arm64", out var osxArm64) || osxArm64.Disposition != "qualified")
            return Fail("native inventory is incomplete", out error);
        return true;
    }

    private static bool TryParseBinding(string text, out BindingManifest value, out string? error)
    {
        value = new BindingManifest();
        error = null;
        var formatSeen = false;
        foreach (var row in Rows(text))
        {
            switch (row[0])
            {
                case "format" when row.Length == 3 && row[1] == "hpd.livekit.ffi.binding-manifest" && row[2] == "1":
                    if (formatSeen) return Fail("duplicate binding format", out error);
                    formatSeen = true;
                    break;
                case "protocol" when row.Length == 4 && row[1] == "livekit-ffi/v0.12.60" && GitSha(row[2]) && Sha(row[3]) && value.DescriptorSha256.Length == 0:
                    value.Tag = row[1];
                    value.Commit = row[2];
                    value.DescriptorSha256 = row[3];
                    break;
                case "native-header" when row.Length == 2 && Sha(row[1]) && value.HeaderSha256.Length == 0:
                    value.HeaderSha256 = row[1];
                    break;
                case "abi" when row.Length == 3 && Token(row[1]) && row[2] == "nuint" && value.Abi.Add(row[1] + "|" + row[2]):
                    break;
                case "response-buffer" when row.Length == 3 && row[1] == "livekit_ffi_drop_handle" && row[2] == "immediate-copy-then-drop" && value.ResponseBufferLaw.Length == 0:
                    value.ResponseBufferLaw = row[1] + "|" + row[2];
                    break;
                case "callback-buffer" when row.Length == 4 && row[1] == "borrowed-until-return" && row[2] == "copy-before-return" && row[3] == "bounded" && value.CallbackBufferLaw.Length == 0:
                    value.CallbackBufferLaw = row[1] + "|" + row[2] + "|" + row[3];
                    break;
                case "require-export" when row.Length == 2 && NativeName(row[1]) && value.RequiredExports.Add(row[1]):
                    break;
                case "artifact" when TryArtifact(row, qualifiedVocabulary: false, out var artifact) && AddUnique(value.Artifacts, artifact.Rid, artifact):
                    break;
                case "unknown-event" when row.Length == 2 && value.UnknownEventDisposition.Length == 0:
                    value.UnknownEventDisposition = row[1];
                    break;
                case "operation" when TryOperation(row, out var operation) && value.OperationNames.Add(operation.Name):
                    value.Operations.Add(operation);
                    break;
                case "observe-event" when TryObservedEvent(row, out var observedEvent) && value.EventNames.Add(observedEvent.Name):
                    value.Events.Add(observedEvent);
                    break;
                default:
                    return Fail("invalid binding row: " + string.Join("|", row), out error);
            }
        }
        if (!formatSeen || value.Tag.Length == 0 || value.Commit.Length == 0 || value.DescriptorSha256.Length == 0 ||
            value.HeaderSha256.Length == 0 || value.ResponseBufferLaw.Length == 0 || value.CallbackBufferLaw.Length == 0 ||
            value.Abi.Count != 2 || value.RequiredExports.Count != 4 ||
            !ExactRids(value.Artifacts.Keys) || value.Operations.Count != 10 || value.Events.Count != 8)
            return Fail("binding manifest is incomplete", out error);
        return true;
    }

    private static bool TryOperation(string[] row, out Operation operation)
    {
        operation = null!;
        if (row.Length != 11 || !Case(row[1]) || !Case(row[2]) || !Case(row[3]) ||
            !(row[4] == "none" || Case(row[4])) || !(row[5] == "none" || Case(row[5])) ||
            row[7] is not ("async" or "sync") || !Token(row[8]) || !Token(row[9]) || !Token(row[10]))
            return false;
        var handles = row[6] == "none" ? Array.Empty<string>() : row[6].Split(',');
        if (handles.Any(static item => !Case(item)) || handles.Distinct(StringComparer.Ordinal).Count() != handles.Length)
            return false;
        operation = new Operation(row[1], row[2], row[3], row[4], row[5], handles, row[7] == "async", row[8], row[9], row[10]);
        return true;
    }

    private static bool TryObservedEvent(string[] row, out ObservedEvent observedEvent)
    {
        observedEvent = null!;
        if (row.Length != 5 || !Case(row[1]) || !Token(row[2]) || !Token(row[4])) return false;
        var handles = row[3] == "none" ? Array.Empty<string>() : row[3].Split(',');
        if (handles.Any(static item => !Case(item)) || handles.Distinct(StringComparer.Ordinal).Count() != handles.Length) return false;
        observedEvent = new ObservedEvent(row[1], row[2], handles, row[4]);
        return true;
    }

    private static bool TryArtifact(string[] row, bool qualifiedVocabulary, out Artifact artifact)
    {
        artifact = null!;
        if (row.Length != 6 || !Rid(row[1]) || !Sha(row[2]) || !LibraryName(row[3]) || !Sha(row[4]))
            return false;
        var valid = qualifiedVocabulary
            ? row[5] is "qualified" or "locked-only"
            : row[5] is "advertised" or "unadvertised";
        if (!valid) return false;
        artifact = new Artifact(row[1], row[2], row[3], row[4], row[5], row[5] == "advertised");
        return true;
    }

    private static IEnumerable<string[]> Rows(string text) => text
        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(static raw => raw.Trim())
        .Where(static line => line.Length > 0 && line[0] != '#')
        .Select(static line => line.Split('|'));

    private static bool SameArtifacts(Dictionary<string, Artifact> binding, Dictionary<string, Artifact> native) =>
        binding.Count == native.Count && binding.All(item => native.TryGetValue(item.Key, out var other) &&
            item.Value.ArchiveSha256 == other.ArchiveSha256 && item.Value.LibraryName == other.LibraryName && item.Value.LibrarySha256 == other.LibrarySha256);

    private static bool ExactRids(IEnumerable<string> values) => values.OrderBy(static item => item, StringComparer.Ordinal).SequenceEqual(
        new[] { "linux-arm64", "linux-x64", "osx-arm64", "osx-x64", "win-arm64", "win-x64" }, StringComparer.Ordinal);
    private static bool TrySingle(ImmutableArray<Input> inputs, string name, out string text)
    {
        var matches = inputs.Where(item => string.Equals(item.Name, name, StringComparison.Ordinal)).ToArray();
        text = matches.Length == 1 ? matches[0].Text : string.Empty;
        return matches.Length == 1;
    }
    private static bool AddUnique<T>(Dictionary<string, T> values, string key, T value)
    {
        if (values.ContainsKey(key)) return false;
        values.Add(key, value);
        return true;
    }
    private static bool Fail(string message, out string? error) { error = message; return false; }
    private static void Report(SourceProductionContext context, DiagnosticDescriptor descriptor, string value) =>
        context.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None, value));
    private static DiagnosticDescriptor Error(string id, string title, string message) =>
        new(id, title, message, "HPD.Audio.LiveKit", DiagnosticSeverity.Error, true);
    private static bool Sha(string value) => value.Length == 64 && value.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool GitSha(string value) => value.Length == 40 && value.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool Token(string value) => value.Length > 0 && value.All(static c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' or '.');
    private static bool Case(string value) => value.Length > 0 && value[0] is >= 'A' and <= 'Z' && value.All(static c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9');
    private static bool NativeName(string value) => value.StartsWith("livekit_ffi_", StringComparison.Ordinal) && Token(value);
    private static bool Rid(string value) => value is "osx-arm64" or "osx-x64" or "linux-arm64" or "linux-x64" or "win-arm64" or "win-x64";
    private static bool LibraryName(string value) => value is "liblivekit_ffi.dylib" or "liblivekit_ffi.so" or "livekit_ffi.dll";

    private sealed record Input(string Name, string Text);
    private sealed class ProtocolInventory
    {
        public string Tag { get; set; } = string.Empty;
        public string Commit { get; set; } = string.Empty;
        public string DescriptorSha256 { get; set; } = string.Empty;
        public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Requests { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Responses { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Events { get; } = new(StringComparer.Ordinal);
    }
    private sealed class NativeInventory
    {
        public string Tag { get; set; } = string.Empty;
        public string Commit { get; set; } = string.Empty;
        public string HeaderSha256 { get; set; } = string.Empty;
        public string ResponseBufferLaw { get; set; } = string.Empty;
        public string CallbackBufferLaw { get; set; } = string.Empty;
        public HashSet<string> Abi { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Exports { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Releases { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, Artifact> Artifacts { get; } = new(StringComparer.Ordinal);
    }
    private sealed class BindingManifest
    {
        public string Tag { get; set; } = string.Empty;
        public string Commit { get; set; } = string.Empty;
        public string DescriptorSha256 { get; set; } = string.Empty;
        public string HeaderSha256 { get; set; } = string.Empty;
        public string ResponseBufferLaw { get; set; } = string.Empty;
        public string CallbackBufferLaw { get; set; } = string.Empty;
        public HashSet<string> Abi { get; } = new(StringComparer.Ordinal);
        public HashSet<string> RequiredExports { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, Artifact> Artifacts { get; } = new(StringComparer.Ordinal);
        public string UnknownEventDisposition { get; set; } = string.Empty;
        public List<Operation> Operations { get; } = [];
        public HashSet<string> OperationNames { get; } = new(StringComparer.Ordinal);
        public List<ObservedEvent> Events { get; } = [];
        public HashSet<string> EventNames { get; } = new(StringComparer.Ordinal);
    }
    private sealed record Artifact(string Rid, string ArchiveSha256, string LibraryName, string LibrarySha256, string Disposition, bool Advertised);
    private sealed record Operation(string Name, string RequestCase, string ResponseCase, string CompletionCase, string CorrelationField, string[] Handles, bool IsAsync, string Admission, string Cancellation, string Release);
    private sealed record ObservedEvent(string Name, string Disposition, string[] Handles, string Release);
}
