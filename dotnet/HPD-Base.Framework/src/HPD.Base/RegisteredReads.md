# Registered reads

Registered reads are generated, principal-bound relational queries with a closed
parameter shape, projection, disclosure policy, required grant, and execution
budget. They are application authority: changing their execution timeout changes
the logical-schema identity.

Declare the parameter and row contracts, then build the query topology in
`Configure`:

```csharp
[BaseRead("project-by-state", typeof(AppJsonContext),
    RequiredGrantId = "project.read")]
internal sealed partial record ProjectByState
{
    [BaseReadParameter("project.state")]
    public ProcessingState? State { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("project.row.id")]
        public required BaseRecordId<Project> Id { get; init; }
    }

    public static void Configure(
        BaseReadDefinitionBuilder<ProjectByState, Row> read)
    {
        read.From(Project.Collection, "project",
                out BaseReadSource<Project> project)
            .Project(Row.Fields.Id, project.RecordId)
            .Limits(
                maximumResultRows: 100,
                maximumResultBytes: 64 * 1024,
                maximumOperations: 1_000,
                maximumExecutionMilliseconds: 2_000);
    }
}
```

Every read plan contains positive limits for result rows, result bytes, query
operations, and execution time. Call `Limits(...)` to replace the bounded
defaults. A caller cannot widen the resulting limits. The runtime passes the
exact timeout to the relational provider and validates it against the host's
maximum execution duration.

## Generated scalar contracts

Generated parameters and rows support Base-owned scalars through canonical,
closed codecs:

| Type | Wire representation | Nullable behavior |
| --- | --- | --- |
| `BaseRecordId<TRecord>` | The target collection's canonical record-ID string | Nullable parameters encode `QueryValueKind.Null`; generated types preserve target-record identity. |
| `BaseBinary` | Canonical padded RFC 4648 Base64 | Nullable parameters encode `QueryValueKind.Null`. |
| `BaseModuleGeneration` | Canonical positive decimal text | Nullable parameters encode `QueryValueKind.Null`. |
| `RevisionToken` | Bounded canonical text | Invalid provider output is rejected before materialization. |
| `BaseCanonicalJson` | Ordinary application JSON; canonical padded Base64 only inside the provider protocol | Nullable parameters use `QueryValueKind.Null`; present JSON remains source-field bounded. |
| Closed enums | Exact declared wire literal | Nullable parameters encode `QueryValueKind.Null`. |

`RevisionToken` values must be nonempty NFC text, contain no whitespace,
control, or surrogate characters, and occupy at most 512 UTF-8 bytes.

`BaseCanonicalJson` parameters must be bound to one installed source field with
`BindCanonicalJsonParameter(...)`. They may be used only as the right operand of
`Equal` or `NotEqual` against that field. Canonical-JSON outputs must directly project
one source field. Base copies that field's complete shape, limits, scalar checksum,
and purpose-bound authority checksum into the plan; those values participate in
logical-schema identity and are validated before provider influence and again before
generated row materialization. Generic platform limits and string substitutes are
not accepted.

TypeScript preserves L44's complete number domain. Exactly round-trippable values use
ordinary JavaScript numbers; wide integers and high-precision decimals use immutable
`BaseCanonicalJsonNumber` values constructed with `baseCanonicalJsonNumber(...)`.
Canonical JSON fields retain L44's object/array shape authority. A root `null` is the
container-null representation and is not admitted as a separate canonical-content
value.

## Stored subject-reference projection

`ProjectStoredSubjectReference(...)` projects the exact `BaseSubjectReference<TSubject>`
already stored in an authoritative relation field. It does not reacquire the subject,
consult its current lifecycle state, or synthesize new authority. Required fields map
to required row properties. Optional or nullable fields map only to nullable row
properties, with both admitted missing and explicit-null storage states producing
`null`.

The operand is output-only. It is invalid in joins, predicates, grouping, aggregates,
having clauses, and sorting. Providers must return the complete canonical subject ID,
ID kind and bound, authority epoch, and incarnation; malformed or substituted values
fail as `base.relational.read.resultInvalid`. Restore-capable providers project the
atomically rewritten post-restore authority. Projection never exposes the private
backing collection or reacquires a retired subject.

## Exact closed enums

Use `JsonStringEnumMemberName` when the wire literal differs from the CLR member
name, and bind the matching Base converter on an authoritative collection field:

```csharp
public enum ProcessingState
{
    [JsonStringEnumMemberName("queued-v1")]
    Queued,
}

[BaseField("state", AllowedEnumLiterals = ["queued-v1"])]
[JsonConverter(typeof(BaseClosedEnumJsonConverter<ProcessingState>))]
public required ProcessingState State { get; init; }
```

Base source-generates the enum-to-wire authority table. Runtime conversion is
reflection-free and uses exact ordinal lookup, including under Native AOT.
Numeric input, wrong-case text, unknown values, flags enums, numeric aliases,
duplicate wire literals, invalid literals, and undeclared output values are
rejected.

## Missing and explicit null

A nullable registered-read parameter represents an explicit query null when its
value is absent. For authoritative collection JSON, missing and explicit null
remain different states.

`JsonIgnoreCondition.WhenWritingNull` is admitted only when every nullable
authoritative property reachable from the serializer context is a collection
field declared with `Presence = Optional` and `Nullability = NonNullable`. In
that shape, CLR `null` serializes as a missing property; explicit JSON `null`
remains invalid. Read, operation, subject, and other DTO roots do not implicitly
gain omission semantics.

## Provider boundary

Providers receive the closed plan and exact budgets. Returned rows are decoded
through the generated row codec before application materialization. Noncanonical
Base64, invalid generations or revision tokens, and unknown enum literals fail
as invalid provider results rather than reaching application code.

## Compound independent counts

Use `CountBranch(...)` when one registered read must prove several independent
bounded counts from one authoritative snapshot. Each branch owns exactly one source,
one optional predicate, and one opaque public discriminator. Base counts record IDs,
returns one required `{ discriminator, count }` row per installed branch—including
zero—and canonicalizes output in ordinal discriminator order.

Branches may use their own fields, declared parameters,
source-generated closed-enum literals, and the explicit `RecordIdParameter<TTarget>`
bridge from a canonical GUID parameter to a typed relation operand. They cannot refer
to another branch's source or use joins, arbitrary projections, dynamic aggregates,
request-time/provider callbacks, or raw collection/source identifiers as public
discriminators. `CompoundLimits(...)` fixes branch, operation, byte, and execution
budgets in the logical schema.

Runtime authorizes every branch before provider influence. A provider receives the
whole closed plan once and must return all branch counts with exact checksum,
generation, ordering, dependency, and accounting evidence from one captured
InMemory snapshot or one SQLite read transaction. `ToArrayAsync` and live reads are
supported; `FirstAsync` and `AnyAsync` fail with
`base.relational.read.terminalUnsupported` because a compound result is one complete
fixed set rather than a pageable sequence.
