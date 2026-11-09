// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using HPD.Agent.Evaluations.Storage;
using HPD.Base;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HPD.Agent.Evaluations.HpdBase;

/// <summary>Registration helpers for HPD Base-backed evaluation storage.</summary>
public static class HpdBaseEvaluationStoreExtensions
{
    /// <summary>Adds evaluation score and run collections to the HPD Base graph.</summary>
    public static HPDBaseBuilder AddHpdEvaluationStores(this HPDBaseBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddCollection(HpdBaseEvaluationScore.Collection)
            .AddCollection(HpdBaseEvaluationRun.Collection)
            .AddCollection(HpdBaseDatasetVersion.Collection)
            .AddCollection(HpdBaseDatasetHead.Collection);
    }

    /// <summary>Registers the durable HPD Base evaluation store.</summary>
    public static IServiceCollection AddHpdBaseEvaluationStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IScoreStore, HpdBaseScoreStore>();
        return services;
    }

    /// <summary>Registers durable HPD Base evaluation dataset storage.</summary>
    public static IServiceCollection AddHpdBaseDatasetStore(
        this IServiceCollection services,
        Action<HpdBaseDatasetStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new HpdBaseDatasetStoreOptions();
        configure?.Invoke(options);
        services.TryAddSingleton(options);
        services.TryAddSingleton<IDatasetStore, HpdBaseDatasetStore>();
        return services;
    }
}
