// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using HPD.Agent.ErrorHandling;
using Microsoft.ML.OnnxRuntime;

namespace HPD.Agent.Providers.Audio.Silero;

internal sealed class SileroErrorHandlerV1 : IProviderErrorHandler
{
    internal static SileroErrorHandlerV1 Instance { get; } = new();
    private SileroErrorHandlerV1() { }

    public ProviderErrorDetails? ParseError(Exception exception) => exception is OnnxRuntimeException
        ? new ProviderErrorDetails
        {
            Category = ErrorCategory.ServerError,
            Message = exception.Message,
            ErrorType = "onnx-runtime"
        }
        : null;

    public TimeSpan? GetRetryDelay(ProviderErrorDetails details, int attempt, TimeSpan initialDelay,
        double multiplier, TimeSpan maxDelay) => null;

    public bool RequiresSpecialHandling(ProviderErrorDetails details) => true;
}
