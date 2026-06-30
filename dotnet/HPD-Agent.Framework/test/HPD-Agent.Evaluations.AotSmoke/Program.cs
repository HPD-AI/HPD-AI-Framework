// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using HPD.Agent.Evaluations.Batch;
using System.Text.Json.Nodes;

const string yaml = """
    cases:
      - name: native-aot
        input: |
          First line
          Second line
        ground_truth: ok
        evaluators:
          - OutputContains: First line
    """;

var dataset = Dataset<string>.FromYaml(yaml, ParseStringInput);
if (dataset.Cases.Count != 1)
    return 1;

var evalCase = dataset.Cases[0];
if (evalCase.Name != "native-aot")
    return 2;

if (evalCase.Input.TrimEnd('\n') != "First line\nSecond line")
    return 3;

if (evalCase.Evaluators?.Count != 1)
    return 4;

var emittedYaml = dataset.ToYaml(SerializeStringInput);
var roundTripped = Dataset<string>.FromYaml(emittedYaml, ParseStringInput);
if (roundTripped.Cases.Count != 1 || roundTripped.Cases[0].Input != evalCase.Input)
    return 5;

const string objectYaml = """
    cases:
      - name: object-input
        input:
          query: cats
          max_results: 3
    """;

var objectDataset = Dataset<SearchInput>.FromYaml(objectYaml, ParseSearchInput);
if (objectDataset.Cases.Count != 1)
    return 6;

if (objectDataset.Cases[0].Input.Query != "cats" ||
    objectDataset.Cases[0].Input.MaxResults != 3)
    return 7;

return 0;

static string ParseStringInput(JsonNode? node)
    => node is JsonValue value && value.TryGetValue<string>(out var text)
        ? text
        : node?.ToJsonString() ?? string.Empty;

static JsonNode? SerializeStringInput(string input)
    => JsonValue.Create(input);

static SearchInput ParseSearchInput(JsonNode? node)
{
    var obj = node as JsonObject;
    if (obj is null)
        return new SearchInput(string.Empty, 0);

    return new SearchInput(
        GetString(obj["query"]),
        GetInt32(obj["max_results"]));
}

static string GetString(JsonNode? node)
    => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : string.Empty;

static int GetInt32(JsonNode? node)
{
    if (node is not JsonValue value)
        return 0;

    if (value.TryGetValue<int>(out var intValue))
        return intValue;

    if (value.TryGetValue<long>(out var longValue))
        return checked((int)longValue);

    return 0;
}

internal sealed record SearchInput(string Query, int MaxResults);
