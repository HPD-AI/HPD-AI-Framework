# HPD.Base.Application

`HPD.Base.Application` is the typed, Native AOT-safe application entry point
for HPD.BASE on .NET 10 and later.

Declare a persisted type once:

```csharp
[BaseCollection("projects", typeof(AppJsonContext))]
[BaseIndex("organization", nameof(OrganizationId), Required = false)]
public sealed partial record Project(
    string OrganizationId,
    [property: BaseField(
        Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order)]
    string Name);

[JsonSerializable(typeof(Project))]
public sealed partial class AppJsonContext : JsonSerializerContext;
```

Install one provider and the generated collection:

```csharp
services.AddHPDBase(hpd =>
{
    hpd;
    hpd.AddCollection(Project.Collection);
});
```

Use a principal-bound session:

```csharp
BaseSession session = sessions.For(principal);

var created = (await session
    .Collection(Project.Collection)
    .CreateAsync(
        RecordId.Create("project_1"),
        new Project("org_1", "First project")))
    .RequireValue();

Project[] projects = (await session
    .Collection(Project.Collection)
    .Query()
    .Where(Project.Fields.OrganizationId, "org_1")
    .OrderBy(Project.Fields.Name)
    .ToArrayAsync(100))
    .RequireValue();
```

Atomic writes use typed item handles and expose records only after the
aggregate outcome is proven committed. Files, dependency references, live
queries, and realtime feeds use the same session identity.

The package includes its incremental generator. It uses source-generated
`System.Text.Json` metadata and has no runtime reflection registration path.

For generated query declarations, immutable execution budgets, Base-owned
scalar codecs, exact closed enums, null semantics, and provider validation, see
[Registered reads](../RegisteredReads.md).
