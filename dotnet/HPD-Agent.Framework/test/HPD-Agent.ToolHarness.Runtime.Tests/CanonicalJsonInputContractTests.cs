using System.Text.Json;

namespace HPD.Agent.Tests;

public sealed class CanonicalJsonInputContractTests
{
    private const string ActionSchema = """
        {
          "type":"object",
          "properties":{
            "request":{
              "oneOf":[
                {
                  "type":"object",
                  "properties":{
                    "action":{"type":"string","const":"launch"},
                    "target":{"type":"string"}
                  },
                  "required":["action","target"],
                  "additionalProperties":false
                },
                {
                  "type":"object",
                  "properties":{
                    "action":{"type":"string","const":"listSessions"}
                  },
                  "required":["action"],
                  "additionalProperties":false
                }
              ]
            }
          },
          "required":["request"],
          "additionalProperties":false
        }
        """;

    [Fact]
    public void KnownActionPreservesSelectedBranchError()
    {
        var error = BindError(ActionSchema, """{"request":{"action":"launch"}}""");

        Assert.Equal("request.target", error.Property);
        Assert.Equal("missing_required_property", error.ErrorCode);
        Assert.Null(error.AttemptedValue);
        Assert.Null(error.AllowedValues);
    }

    [Fact]
    public void UnknownActionReportsAttemptedAndAllowedValues()
    {
        var error = BindError(ActionSchema, """{"request":{"action":"status"}}""");

        Assert.Equal("request.action", error.Property);
        Assert.Equal("unknown_union_discriminator", error.ErrorCode);
        Assert.Equal("status", error.AttemptedValue?.GetString());
        Assert.Equal(["launch", "listSessions"], error.AllowedValues!);
    }

    [Fact]
    public void UnknownPropertyUsesSelectedBranchPath()
    {
        var error = BindError(
            ActionSchema,
            """{"request":{"action":"launch","target":"app.dll","extra":true}}""");

        Assert.Equal("request.extra", error.Property);
        Assert.Equal("unknown_property", error.ErrorCode);
        Assert.True(error.AttemptedValue?.GetBoolean());
    }

    [Fact]
    public void NestedUnionReportsNestedDiscriminatorPath()
    {
        const string schema = """
            {
              "type":"object",
              "properties":{
                "request":{
                  "oneOf":[
                    {
                      "type":"object",
                      "properties":{
                        "action":{"type":"string","const":"launch"},
                        "target":{
                          "oneOf":[
                            {
                              "type":"object",
                              "properties":{
                                "targetKind":{"type":"string","const":"process"},
                                "processId":{"type":"integer"}
                              },
                              "required":["targetKind","processId"],
                              "additionalProperties":false
                            },
                            {
                              "type":"object",
                              "properties":{
                                "targetKind":{"type":"string","const":"endpoint"},
                                "endpointId":{"type":"string"}
                              },
                              "required":["targetKind","endpointId"],
                              "additionalProperties":false
                            }
                          ]
                        }
                      },
                      "required":["action","target"],
                      "additionalProperties":false
                    }
                  ]
                }
              },
              "required":["request"],
              "additionalProperties":false
            }
            """;

        var error = BindError(schema, """{"request":{"action":"launch","target":{}}}""");

        Assert.Equal("request.target.targetKind", error.Property);
        Assert.Equal("missing_required_property", error.ErrorCode);
    }

    [Fact]
    public void MalformedDiscriminatorsHaveDistinctErrors()
    {
        var missing = BindError(ActionSchema, """{"request":{}}""");
        var nullValue = BindError(ActionSchema, """{"request":{"action":null}}""");
        var wrongKind = BindError(ActionSchema, """{"request":{"action":1}}""");
        var empty = BindError(ActionSchema, """{"request":{"action":""}}""");
        var duplicate = BindError(
            ActionSchema,
            """{"request":{"action":"launch","action":"listSessions","target":"app.dll"}}""");

        Assert.Equal(("request.action", "missing_required_property"), (missing.Property, missing.ErrorCode));
        Assert.Equal(("request.action", "null_not_allowed"), (nullValue.Property, nullValue.ErrorCode));
        Assert.Equal(("request.action", "invalid_json_kind"), (wrongKind.Property, wrongKind.ErrorCode));
        Assert.Equal(("request.action", "invalid_string_value"), (empty.Property, empty.ErrorCode));
        Assert.Equal(("request.action", "duplicate_property"), (duplicate.Property, duplicate.ErrorCode));
    }

    [Fact]
    public void UnionEnvelopeRequiresObject()
    {
        var nullValue = BindError(ActionSchema, """{"request":null}""");
        var wrongKind = BindError(ActionSchema, """{"request":[]}""");

        Assert.Equal(("request", "null_not_allowed"), (nullValue.Property, nullValue.ErrorCode));
        Assert.Equal(("request", "invalid_json_kind"), (wrongKind.Property, wrongKind.ErrorCode));
    }

    [Fact]
    public void DuplicateOrEmptyDiscriminatorConstantsFailCompilation()
    {
        const string duplicate = """
            {
              "type":"object",
              "properties":{
                "request":{
                  "oneOf":[
                    {"type":"object","properties":{"action":{"type":"string","const":"launch"}},"required":["action"],"additionalProperties":false},
                    {"type":"object","properties":{"action":{"type":"string","const":"launch"}},"required":["action"],"additionalProperties":false}
                  ]
                }
              },
              "required":["request"],
              "additionalProperties":false
            }
            """;
        const string empty = """
            {
              "type":"object",
              "properties":{
                "request":{
                  "oneOf":[
                    {"type":"object","properties":{"action":{"type":"string","const":""}},"required":["action"],"additionalProperties":false}
                  ]
                }
              },
              "required":["request"],
              "additionalProperties":false
            }
            """;

        Assert.Throws<InvalidDataException>(() => Create(duplicate));
        Assert.Throws<InvalidDataException>(() => Create(empty));
    }

    [Fact]
    public void SingleBranchUnionUsesDiscriminatorSemantics()
    {
        const string schema = """
            {
              "type":"object",
              "properties":{
                "request":{
                  "oneOf":[
                    {"type":"object","properties":{"action":{"type":"string","const":"launch"}},"required":["action"],"additionalProperties":false}
                  ]
                }
              },
              "required":["request"],
              "additionalProperties":false
            }
            """;

        var error = BindError(schema, """{"request":{"action":"status"}}""");

        Assert.Equal("request.action", error.Property);
        Assert.Equal("unknown_union_discriminator", error.ErrorCode);
        Assert.Equal(["launch"], error.AllowedValues!);
    }

    [Fact]
    public void AmbiguousConstantShapeFallsBackToGenericUnion()
    {
        const string schema = """
            {
              "type":"object",
              "properties":{
                "request":{
                  "oneOf":[
                    {
                      "type":"object",
                      "properties":{
                        "action":{"type":"string","const":"launch"},
                        "mode":{"type":"string","const":"run"}
                      },
                      "required":["action","mode"],
                      "additionalProperties":false
                    },
                    {
                      "type":"object",
                      "properties":{
                        "action":{"type":"string","const":"attach"},
                        "mode":{"type":"string","const":"run"}
                      },
                      "required":["action","mode"],
                      "additionalProperties":false
                    }
                  ]
                }
              },
              "required":["request"],
              "additionalProperties":false
            }
            """;

        var error = BindError(schema, """{"request":{"action":"status","mode":"run"}}""");

        Assert.Equal("request", error.Property);
        Assert.Equal("no_union_branch_matches", error.ErrorCode);
        Assert.Null(error.AllowedValues);
    }

    private static ValidationError BindError(string schema, string arguments)
    {
        using var argumentsDocument = JsonDocument.Parse(arguments);
        var result = Create(schema).Bind(argumentsDocument.RootElement);
        return Assert.Single(result.Errors);
    }

    private static CanonicalJsonInputContract Create(string schema)
    {
        using var document = JsonDocument.Parse(schema);
        return CanonicalJsonInputContract.Create(document.RootElement);
    }
}
