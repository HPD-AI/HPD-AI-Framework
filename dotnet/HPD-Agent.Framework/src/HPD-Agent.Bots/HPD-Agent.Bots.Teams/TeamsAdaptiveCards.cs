using System.Text.Json.Serialization;

namespace HPD.Agent.Bots.Teams;

public sealed record TeamsAdaptiveCard(
    [property: JsonPropertyName("body")] IReadOnlyList<object> Body,
    [property: JsonPropertyName("actions")] IReadOnlyList<object>? Actions = null,
    [property: JsonPropertyName("type")] string Type = "AdaptiveCard",
    [property: JsonPropertyName("$schema")] string Schema = "http://adaptivecards.io/schemas/adaptive-card.json",
    [property: JsonPropertyName("version")] string Version = "1.4");

public sealed record TeamsTextBlock(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("weight")] string? Weight = null,
    [property: JsonPropertyName("size")] string? Size = null,
    [property: JsonPropertyName("isSubtle")] bool? IsSubtle = null,
    [property: JsonPropertyName("wrap")] bool Wrap = true,
    [property: JsonPropertyName("color")] string? Color = null,
    [property: JsonPropertyName("type")] string Type = "TextBlock");

public sealed record TeamsImage(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("altText")] string? AltText = null,
    [property: JsonPropertyName("type")] string Type = "Image");

public sealed record TeamsFactSet(
    [property: JsonPropertyName("facts")] IReadOnlyList<TeamsFact> Facts,
    [property: JsonPropertyName("type")] string Type = "FactSet");

public sealed record TeamsFact(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("value")] string Value);

public sealed record TeamsContainer(
    [property: JsonPropertyName("items")] IReadOnlyList<object> Items,
    [property: JsonPropertyName("separator")] bool? Separator = null,
    [property: JsonPropertyName("type")] string Type = "Container");

public sealed record TeamsColumnSet(
    [property: JsonPropertyName("columns")] IReadOnlyList<TeamsColumn> Columns,
    [property: JsonPropertyName("type")] string Type = "ColumnSet");

public sealed record TeamsColumn(
    [property: JsonPropertyName("items")] IReadOnlyList<object> Items,
    [property: JsonPropertyName("width")] string Width = "stretch",
    [property: JsonPropertyName("type")] string Type = "Column");

public sealed record TeamsInputText(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("placeholder")] string? Placeholder = null,
    [property: JsonPropertyName("value")] string? Value = null,
    [property: JsonPropertyName("isMultiline")] bool? IsMultiline = null,
    [property: JsonPropertyName("isRequired")] bool? IsRequired = null,
    [property: JsonPropertyName("maxLength")] int? MaxLength = null,
    [property: JsonPropertyName("type")] string Type = "Input.Text");

public sealed record TeamsChoiceSet(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("choices")] IReadOnlyList<TeamsChoice> Choices,
    [property: JsonPropertyName("placeholder")] string? Placeholder = null,
    [property: JsonPropertyName("value")] string? Value = null,
    [property: JsonPropertyName("style")] string? Style = null,
    [property: JsonPropertyName("isMultiSelect")] bool IsMultiSelect = false,
    [property: JsonPropertyName("label")] string? Label = null,
    [property: JsonPropertyName("isRequired")] bool? IsRequired = null,
    [property: JsonPropertyName("type")] string Type = "Input.ChoiceSet");

public sealed record TeamsChoice(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("value")] string Value);

public sealed record TeamsSubmitAction(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("data")] IReadOnlyDictionary<string, string> Data,
    [property: JsonPropertyName("style")] string? Style = null,
    [property: JsonPropertyName("type")] string Type = "Action.Submit");

public sealed record TeamsOpenUrlAction(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("type")] string Type = "Action.OpenUrl");

public sealed record TeamsTaskModuleResponse(
    [property: JsonPropertyName("task")] TeamsTaskModuleTask Task);

public sealed record TeamsTaskModuleTask(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("value")] object? Value = null);

public sealed record TeamsTaskModuleCardValue(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("card")] TeamsAdaptiveCard Card);
