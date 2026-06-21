namespace HPD.Agent.ErrorHandling;

/// <summary>
/// Utility for detecting model not found errors across different LLM providers.
/// Each provider returns slightly different error formats, so we check multiple patterns.
/// </summary>
public static class ModelNotFoundDetector
{
    /// <summary>
    /// Detects model not found errors across different providers.
    /// </summary>
    /// <param name="status">HTTP status code (typically 400 or 404)</param>
    /// <param name="message">Error message from the provider</param>
    /// <param name="errorCode">Provider-specific error code (e.g., "DeploymentNotFound", "NOT_FOUND")</param>
    /// <param name="errorType">Provider-specific error type (e.g., "not_found_error", "invalid_model")</param>
    /// <returns>True if this is a model not found error</returns>
    /// <remarks>
    /// Supported provider patterns:
    /// - OpenAI: 404 + "The model 'X' does not exist or you do not have access"
    /// - Anthropic: 404 + type="not_found_error" + "model: X"
    /// - OpenRouter: 400 + "Model 'X' not found"
    /// - Google Gemini: 404 + status="NOT_FOUND" + "is not found for API version"
    /// - Mistral: type="invalid_model" + "Invalid model: X"
    /// - Ollama: 404 + "model 'X' not found, try pulling it first"
    /// - AWS Bedrock: 400 + ValidationException + "model identifier is invalid"
    /// - Azure OpenAI: 404 + code="DeploymentNotFound" + "deployment does not exist"
    /// - HuggingFace: 400/422 + "model is not supported"
    /// </remarks>
    public static bool IsModelNotFoundError(int? status, string? message, string? errorCode, string? errorType)
    {
        // 1. Error type checks (Anthropic, Mistral)
        if (!string.IsNullOrEmpty(errorType))
        {
            if (errorType.Equals("not_found_error", StringComparison.OrdinalIgnoreCase) ||
                errorType.Equals("invalid_model", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // 2. Error code checks (Azure, Bedrock, Gemini)
        if (!string.IsNullOrEmpty(errorCode))
        {
            if (errorCode.Contains("DeploymentNotFound", StringComparison.OrdinalIgnoreCase) ||
                errorCode.Contains("ValidationException", StringComparison.OrdinalIgnoreCase) ||
                errorCode.Contains("model_not_found", StringComparison.OrdinalIgnoreCase) ||
                errorCode.Contains("invalid_model", StringComparison.OrdinalIgnoreCase) ||
                errorCode.Equals("NOT_FOUND", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // 3. Message pattern checks (all providers)
        if (string.IsNullOrEmpty(message))
        {
            return false;
        }

        var msgLower = message.ToLowerInvariant();

        // "model X not found" / "model not found" (OpenRouter, Ollama, general)
        if (msgLower.Contains("model") && msgLower.Contains("not found"))
            return true;

        // "does not exist" (OpenAI: "The model 'X' does not exist")
        if (msgLower.Contains("model") && msgLower.Contains("does not exist"))
            return true;

        // "invalid model" (Mistral: "Invalid model: X")
        if (msgLower.Contains("invalid model"))
            return true;

        // "model identifier is invalid" (Bedrock)
        if (msgLower.Contains("model identifier") && msgLower.Contains("invalid"))
            return true;

        // "deployment for this resource does not exist" (Azure OpenAI)
        if (msgLower.Contains("deployment") && msgLower.Contains("does not exist"))
            return true;

        // "is not found for API version" (Google Gemini)
        if (msgLower.Contains("is not found for api version"))
            return true;

        // "try pulling it first" (Ollama: model needs to be downloaded)
        if (msgLower.Contains("try pulling it first"))
            return true;

        // "is not a valid model ID" (OpenRouter)
        if (msgLower.Contains("is not a valid model"))
            return true;

        // "model is not supported" (HuggingFace)
        if (msgLower.Contains("model is not supported"))
            return true;

        // 4. Status code + model context (fallback)
        if (status == 404 && msgLower.Contains("model"))
            return true;

        return false;
    }

    /// <summary>
    /// Checks if an error indicates the model exists but the user doesn't have access.
    /// This is different from "model not found" - the model exists but permissions are lacking.
    /// </summary>
    /// <param name="message">Error message from the provider</param>
    /// <returns>True if this is an access denied error for a model</returns>
    public static bool IsModelAccessDeniedError(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return false;

        var msgLower = message.ToLowerInvariant();

        // OpenAI: "you do not have access to it"
        if (msgLower.Contains("do not have access"))
            return true;

        // General permission patterns
        if (msgLower.Contains("model") &&
            (msgLower.Contains("permission") || msgLower.Contains("unauthorized") || msgLower.Contains("forbidden")))
            return true;

        return false;
    }
}
