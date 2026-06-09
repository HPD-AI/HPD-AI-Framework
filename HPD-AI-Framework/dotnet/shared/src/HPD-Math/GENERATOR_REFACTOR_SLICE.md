# Generator Refactor Slice

This slice records the next architecture cleanup for `HPD.Math.Generators`.

The current generator works, but `MathGenerator.cs` has grown into a single large file that
does too many jobs. The right next move is an internal split by responsibility and domain,
not an immediate split into many generator packages.

## Current State

`MathGenerator.cs` currently handles:

- Roslyn entrypoint and incremental-generator wiring
- attribute discovery
- diagnostics
- attribute parsing
- model definitions
- context emitters
- scope emitters
- inline value emitters
- C# 14 extension member emission

The runtime packages are already domain-shaped:

- `HPD.Math.Core`
- `HPD.Math.Finite`
- `HPD.Math.Algebra`
- `HPD.Math.Numerics`
- `HPD.Math.LinearAlgebra`
- `HPD.Math.Autodiff`
- `HPD.Math.Managed`
- `HPD.Math.Text`

The generator should catch up to that shape internally.

## Rule

Do not split packages before splitting responsibilities.

The next refactor should keep one generator assembly but break the implementation into
small files with clear ownership.

## Proposed File Layout

```text
src/HPD.Math.Generators/
  MathGenerator.cs

  Discovery/
    GeneratedTypeDiscovery.cs
    AttributeReaders.cs
    Diagnostics.cs

  Models/
    GeneratedType.cs
    StaticWitnessModels.cs
    PolynomialModels.cs
    MatrixModels.cs
    AutodiffModels.cs
    QuotientFieldModels.cs
    RationalFunctionModels.cs
    NumberTheoryModels.cs
    FiniteModels.cs

  Emitters/
    StaticWitnessEmitter.cs
    PolynomialEmitter.cs
    MatrixEmitter.cs
    ReverseDiffEmitter.cs
    QuotientFieldEmitter.cs
    RationalFunctionEmitter.cs
    PadicEmitter.cs
    WittVectorEmitter.cs
    FinitePowerSetEmitter.cs
    ExtensionMemberEmitter.cs

  Shared/
    SourceBuilder.cs
    TypeNameHelpers.cs
    EmissionGuards.cs
```

## Refactor Order

1. Extract diagnostics and attribute-name constants.
2. Extract model types without changing behavior.
3. Extract attribute readers.
4. Extract one low-risk emitter first, preferably static witnesses.
5. Extract finite powerset and Witt emitters next.
6. Extract matrix and polynomial emitters.
7. Extract quotient, field extension, rational function, and p-adic emitters.
8. Extract C# 14 extension member emission last.

Every step should preserve generated output behavior unless the step is explicitly marked as
a breaking generator behavior change.

## Non-Goals

- Do not split into `HPD.Math.Generators.Core`, `.Algebra`, `.Autodiff`, etc. yet.
- Do not move attributes out of `HPD.Math.Core` yet.
- Do not redesign the generated public DX during this refactor.
- Do not mix this with new math feature work.

## Future Package Split Trigger

Split generator packages only when a new domain creates genuinely independent generated
worlds, such as:

- algorithm packages with domain-specific generated kernels
- hardware/codegen surfaces
- finance-specific generated structures
- separate text/managed convenience generators

Until then, one generator assembly with domain-shaped internal files is the best balance.

## Verification

After each extraction:

```bash
dotnet test test/HPD.Math.Tests/HPD.Math.Tests.csproj -warnaserror
dotnet build test/HPD.Math.AotSmoke/HPD.Math.AotSmoke.csproj -warnaserror
test/aot-gate.sh
```

Also compare generated behavior through existing context/scope tests rather than relying
only on compilation.
