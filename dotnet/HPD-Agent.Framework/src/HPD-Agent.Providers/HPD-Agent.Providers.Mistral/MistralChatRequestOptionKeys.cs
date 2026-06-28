using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.Mistral;

internal static class MistralChatRequestOptionKeys
{
    public static void ApplyRawRequestOptions(ChatOptions options)
    {
        var requestOptions = MistralChatRequestOptions.From(options);
        if (requestOptions is null && options.Reasoning?.Effort is null)
            return;

        var existingFactory = options.RawRepresentationFactory;
        options.RawRepresentationFactory = provider =>
        {
            var request = existingFactory?.Invoke(provider) as global::Mistral.ChatCompletionRequest
                ?? new global::Mistral.ChatCompletionRequest
                {
                    Model = options.ModelId ?? string.Empty,
                    Messages = []
                };

            ApplyMistralRequestOptions(request, options, requestOptions);
            return request;
        };
    }

    private static void ApplyMistralRequestOptions(
        global::Mistral.ChatCompletionRequest request,
        ChatOptions options,
        MistralChatRequestOptions? requestOptions)
    {
        if (string.IsNullOrWhiteSpace(request.Model))
            request.Model = options.ModelId ?? string.Empty;

        if (requestOptions is not null)
        {
            request.SafePrompt ??= requestOptions.SafePrompt;
            request.PromptCacheKey ??= requestOptions.PromptCacheKey;
            request.N ??= requestOptions.CompletionCount;

            if (!string.IsNullOrEmpty(requestOptions.PredictionContent) && request.Prediction is null)
            {
                request.Prediction = new global::Mistral.Prediction
                {
                    Type = "content",
                    Content = requestOptions.PredictionContent
                };
            }
        }

        request.ReasoningEffort ??= ToMistralReasoningEffort(options.Reasoning?.Effort);
    }

    private static global::Mistral.ChatCompletionRequestReasoningEffort? ToMistralReasoningEffort(Microsoft.Extensions.AI.ReasoningEffort? effort)
        => effort switch
        {
            Microsoft.Extensions.AI.ReasoningEffort.None => global::Mistral.ChatCompletionRequestReasoningEffort.None,
            Microsoft.Extensions.AI.ReasoningEffort.High or Microsoft.Extensions.AI.ReasoningEffort.ExtraHigh => global::Mistral.ChatCompletionRequestReasoningEffort.High,
            _ => null
        };
}
