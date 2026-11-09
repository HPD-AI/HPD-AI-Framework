// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using HPD.Agent.Evaluations.Storage;

namespace HPD.Agent.Evaluations.HpdBase;

/// <summary>Configures durable serialization codecs for HPD Base dataset storage.</summary>
public sealed class HpdBaseDatasetStoreOptions
{
    private readonly Dictionary<Type, object> _codecs = [];

    /// <summary>Adds or replaces the codec registered for one input type.</summary>
    public HpdBaseDatasetStoreOptions AddCodec<TInput>(DatasetCodec<TInput> codec)
    {
        ArgumentNullException.ThrowIfNull(codec);
        _codecs[typeof(TInput)] = codec;
        return this;
    }

    internal DatasetCodec<TInput> RequireCodec<TInput>()
    {
        if (_codecs.TryGetValue(typeof(TInput), out object? value) &&
            value is DatasetCodec<TInput> codec)
        {
            return codec;
        }

        throw new InvalidOperationException(
            $"Durable dataset storage requires a DatasetCodec<{typeof(TInput).Name}>. " +
            "Register one with AddHpdBaseDatasetStore(options => options.AddCodec(codec)).");
    }
}
