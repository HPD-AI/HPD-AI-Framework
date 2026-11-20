using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using GenerativeAI;
using GenerativeAI.Core;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.GoogleAI;

/// <summary>Promotes the image parts that the SDK's chat adapter nests inside functionResponse.response.</summary>
internal sealed class GoogleImageToolResultPlatformAdapter(
    string apiKey, string apiVersion, bool validateAccessToken, string modelName)
    : GoogleAIPlatformAdapter(apiKey, apiVersion, validateAccessToken: validateAccessToken), IPlatformAdapter
{
    private const string ImagePartsProperty = "_hpdFunctionResponseImageParts";

    private readonly bool _supportsImages =
        modelName.StartsWith("gemini-3", StringComparison.Ordinal) ||
        modelName.StartsWith("models/gemini-3", StringComparison.Ordinal);

    internal JsonObject FormatImageResult(IReadOnlyList<AIContent> parts)
    {
        var images = parts.OfType<DataContent>().ToArray();
        if (!_supportsImages || images.Any(image => image.MediaType is not ("image/png" or "image/jpeg" or "image/webp")))
            return new JsonObject { ["content"] = ImageToolResultChatClient.DescribeImageParts(parts) };

        var media = new JsonArray();
        foreach (var image in images)
        {
            JsonNode mediaPart = new JsonObject
            {
                ["inlineData"] = new JsonObject
                {
                    ["mimeType"] = image.MediaType,
                    ["data"] = Convert.ToBase64String(image.Data.Span)
                }
            };
            media.Add(mediaPart);
        }

        return new JsonObject
        {
            ["content"] = string.Join("\n", parts.OfType<TextContent>().Select(part => part.Text)),
            [ImagePartsProperty] = media
        };
    }

    async Task IPlatformAdapter.AddAuthorizationAsync(
        HttpRequestMessage request, bool requireAccessToken, CancellationToken cancellationToken)
    {
        await AddAuthorizationAsync(request, requireAccessToken, cancellationToken).ConfigureAwait(false);

        if (!_supportsImages ||
            request.Content is not { } content ||
            request.RequestUri is not { } uri ||
            (!uri.AbsolutePath.EndsWith(":generateContent", StringComparison.Ordinal) &&
             !uri.AbsolutePath.EndsWith(":streamGenerateContent", StringComparison.Ordinal)))
            return;

        var body = await content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!body.Contains(ImagePartsProperty, StringComparison.Ordinal) ||
            JsonNode.Parse(body) is not JsonObject requestBody ||
            requestBody["contents"] is not JsonArray messages)
            return;

        var changed = false;
        foreach (var message in messages.OfType<JsonObject>())
        {
            if (message["parts"] is not JsonArray parts)
                continue;

            foreach (var part in parts.OfType<JsonObject>())
            {
                if (part["functionResponse"] is not JsonObject result ||
                    result["parts"] is not null ||
                    result["response"] is not JsonObject response ||
                    response[ImagePartsProperty] is not JsonArray images)
                    continue;

                response.Remove(ImagePartsProperty);
                result["parts"] = images;
                changed = true;
            }
        }

        if (changed)
        {
            request.Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json");
            content.Dispose();
        }
    }
}
