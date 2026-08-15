// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Collections.Frozen;

namespace HPD.Agent.Providers.Audio.Silero;

/// <summary>Configures the pinned Silero VAD model artifact and CPU execution.</summary>
public sealed class SileroVadOptions : global::HPD.Agent.IProviderConfig
{
    /// <summary>Gets or sets the explicit local ONNX model path.</summary>
    public string? ModelPath { get; set; }

    /// <summary>Gets or sets the required lowercase SHA-256 digest.</summary>
    public string ModelSha256 { get; set; } = SileroModelArtifactV1.OfficialSha256;

    /// <summary>Gets or sets the bounded ONNX Runtime intra-operation thread count.</summary>
    public int IntraOpThreads { get; set; } = 1;
}

/// <summary>Describes the exact upstream artifact accepted by default.</summary>
public static class SileroModelArtifactV1
{
    public const string Version = "6.2";
    public const string UpstreamCommit = "be95df9152c0d7618fa1edfeb296fc3dae32376f";
    public const string OfficialSha256 = "1a153a22f4509e292a94e67d6f9b85e8deb25b4988682b7e174c65279d8788e3";
    public const long OfficialLength = 2_327_524;
    public const string License = "MIT";
    public const string FileName = "silero_vad.onnx";

    /// <summary>Gets the ONNX Runtime package version qualified with this provider.</summary>
    public const string OnnxRuntimeVersion = "1.23.0";

    /// <summary>Gets the desktop RIDs for which the pinned runtime package supplies a native CPU asset.</summary>
    public static IReadOnlyDictionary<string, string> NativeRuntimeAssets { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["linux-arm64"] = "libonnxruntime.so",
            ["linux-x64"] = "libonnxruntime.so",
            ["osx-arm64"] = "libonnxruntime.dylib",
            ["osx-x64"] = "libonnxruntime.dylib",
            ["win-arm64"] = "onnxruntime.dll",
            ["win-x64"] = "onnxruntime.dll",
        }.ToFrozenDictionary(StringComparer.Ordinal);
}
