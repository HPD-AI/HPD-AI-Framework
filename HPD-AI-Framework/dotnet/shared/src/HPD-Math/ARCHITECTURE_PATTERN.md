# HPD-Math Architecture Pattern

This is the intended scratch architecture for HPD-Math.

The goal is to combine three things that normally fight each other:

- Helium's mathematical feel.
- Rhodium's generated-context ergonomics.
- HPD-Math's zero-GC and Native AOT discipline.

The mistake to avoid is treating source generation as a thin facade over kernels only.
That improves type plumbing, but it does not hide destination buffers, workspaces, or
status threading. The better pattern is to make source generation produce complete
authoring scopes and small domain handles.

## Design Principle

Separate the library into layers with different jobs:

```text
Contracts  ->  Kernels  ->  Generated Contexts  ->  Inline Values
                                            \->  Scopes  ->  Handles/Extensions
                       \->  Managed/Text convenience layers
```

The low layers are allowed to be explicit and mechanical. The high layers are responsible
for developer experience.

## 1. Contracts

Contracts define the executable mathematical structure.

Examples:

- Decidable equality.
- Decidable order.
- Finite enumeration.
- Ring, field, module, lattice, group, and status-returning variants.
- Static witnesses such as dimension, precision, prime modulus, and capacity.

Rules:

- No allocation assumptions.
- No parsing or formatting.
- No domain-specific storage.
- Must be Native AOT friendly.

Contracts answer: **what operations and laws does this structure expose?**

## 2. Kernels

Kernels are the lowest executable implementation layer.

Examples:

- Polynomial add/mul/div/gcd.
- Matrix multiplication.
- Finsupp map/remap/fold.
- Reverse autodiff backward pass.
- Rational normalization.

Rules:

- Caller owns all memory.
- Use `Span<T>`, `ReadOnlySpan<T>`, builders, and workspaces.
- Return `AlgebraStatus`.
- No hidden allocation.
- No LINQ on hot paths.
- No closures or delegates on hot paths.
- No exception-driven validation.
- No source generator dependency.

Kernel usage can be verbose:

```csharp
var status = SparsePolynomialKernels.TryMul(
    left,
    right,
    ref destination,
    workspaceDegrees,
    workspaceCoefficients,
    coefficientOps);
```

That is acceptable. Kernels optimize control, not authoring comfort.

Kernels answer: **how does this operation execute with explicit memory and failure?**

## 3. Generated Contexts

Generated contexts name mathematical worlds. When the mathematical object has a bounded
capacity/cardinality in that world, the context should generate a first-class inline-storage
value for it.

Examples:

```csharp
[FieldExtensionContext(typeof(ModInt<P7>), typeof(ModIntOps<P7>), Terms = 3, Handles = 8, Workspace = 4)]
public readonly partial struct F49;

[PolynomialContext(typeof(Rational32), typeof(Rational32StatusFieldOps), Terms = 8, Workspace = 16)]
public readonly partial struct Qx;

[FinitePowerSetContext(200)]
public readonly partial struct Vertices;

[ReverseDiffContext(typeof(Rational32), typeof(Rational32StatusFieldOps))]
public readonly partial struct R32Diff;
```

Generated contexts should expose:

- The coefficient/value type.
- The operation witness type.
- Static dimensions and capacities.
- First-class inline values such as `Matrix`, `Poly`, `Element`, `Value`, `Vector`, or `Set`
  when capacity is part of the mathematical universe.
- Operation witnesses over those inline values.
- Direct `Try*` methods for ordinary value construction and advanced raw control.

Raw views/builders may still exist for kernels and advanced hooks, but they must not define
the normal DX.

Contexts answer: **which mathematical universe are we working in?**

## 3.5 Generated Inline Values

Generated inline values are ordinary value-layer math objects with storage sized from the
context.

Examples:

- `Qx.Poly`
- `F49.Element`
- `Mat2.Matrix`
- `RationalFx.Value`
- `Witt2.Vector`
- `Vertices.Set`

They should:

- use inline arrays or scalar fields, not heap arrays
- expose domain-specific accessors
- operate through status-returning generated witnesses
- hide spans from normal callers
- project to kernel views internally when kernels need explicit storage

## 4. Generated Scopes

Generated scopes are the authoring DX layer.

A scope is a generated authoring execution frame. Its internals may be `ref struct` storage
and stack frames, but normal callers should use generated runners and `Result` structs
instead of supplying buffers manually. It tracks status and exposes domain-specific
operations through scope-local handles.

Example target shape:

```csharp
[PolynomialScope(typeof(Rational32), typeof(Rational32StatusFieldOps), Terms = 32, Workspace = 64)]
public partial struct BuildPolynomial
{
    partial void Build(ref BuildPolynomial.Scope q)
    {
        var x = q.Variable();
        var p = x * x + q.Const(3) * x + q.Const(1);
        q.Return(p);
    }
}
```

Generated code owns the explicit frame:

```csharp
Span<int> degrees = stackalloc int[32];
Span<Rational32> coefficients = stackalloc Rational32[32];
Span<int> workspaceDegrees = stackalloc int[64];
Span<Rational32> workspaceCoefficients = stackalloc Rational32[64];

var q = new BuildPolynomial.Scope(
    degrees,
    coefficients,
    workspaceDegrees,
    workspaceCoefficients);

Build(ref q);
return q.Status;
```

Scope rules:

- A scope is stack-only.
- It exposes friendly methods such as `Const`, `Variable`, `Add`, `Mul`, `Square`,
  `Evaluate`, `Backward`, `Gradient`, or `Return`.
- It stores or references the builders/workspaces needed by kernels.
- It accumulates the first non-OK `AlgebraStatus`.
- After failure, later operations should return inert handles and preserve the failure.
- It never hides heap allocation in hot paths.

Scopes answer: **how can users write math without manually wiring buffers?**

## 5. Generated Handles

Handles are tiny values returned by scopes.

Examples:

- `Qx.Poly`
- `F49.Element`
- `Mat3.Matrix`
- `R32Diff.Var`

They are not heap-owning public values. The ordinary public value layer is the generated
inline value layer above; handles are for expression authoring inside a scope.

Handles should not be owning heap objects. They should be small references into the active
scope, usually an index, slice descriptor, or compact view descriptor.

Example:

```csharp
public readonly ref struct Poly
{
    private readonly Scope _scope;
    private readonly int _handle;
}
```

Handle rules:

- No heap ownership.
- No global session.
- No thread-static tape.
- No hidden arrays.
- Must not outlive the scope.
- Can expose properties such as `Degree`, `IsZero`, or `Value` when these are cheap.

Handles answer: **what is the user holding while authoring inside a scope?**

## 6. C# 14 Extension Members

C# 14 extension members should target generated handles and scopes, not raw kernel views.

Low-level view extensions only improve this:

```csharp
SparsePolynomialKernels.TryAdd(left, right, ref destination, ops);
```

into this:

```csharp
left.TryAdd(right, ref destination, ops);
```

That is not enough.

The stronger pattern is:

```csharp
extension(Qx.Poly self)
{
    public Qx.Poly Square => self * self;

    public static Qx.Poly operator +(Qx.Poly left, Qx.Poly right)
        => left.Scope.Add(left, right);

    public static Qx.Poly operator *(Qx.Poly left, Qx.Poly right)
        => left.Scope.Mul(left, right);
}
```

Then authoring can become:

```csharp
var p = x.Square + q.Const(3) * x;
```

Extension-member rules:

- Use extension properties for cheap derived facts.
- Use extension operators only when the handle can route failure into its scope.
- Do not put allocation behind operators.
- Do not use operators where status would be lost silently.
- Prefer method syntax first, then add operators after the status story is proven.

Extension members answer: **how do we recover mathematical syntax without owning heap objects?**

## 7. Managed Convenience Layer

The managed layer is allowed to be friendlier and allocate.

Examples:

- Heap-backed polynomials.
- Heap-backed matrices.
- Easy parser/formatter integration.
- Debugging and notebooks.
- Conversion to and from kernel views.

Rules:

- Never confuse managed convenience with hot-path kernels.
- Managed types may offer Helium-like syntax.
- Managed APIs should make allocation explicit in package naming and docs.

Managed convenience answers: **how do humans explore and debug this math easily?**

## 8. Text Layer

Parsing and formatting belong outside core kernels.

Examples:

- Polynomial parsing.
- Matrix parsing.
- Rational-function parsing.
- MathML or display formatting.

Rules:

- No parser dependency in core structures.
- No formatting requirement for hot-path types.
- Text APIs can target managed types, scopes, or explicit builders.

Text answers: **how do we read and display mathematical objects?**

## 9. Source Generator Responsibilities

The generator should eventually produce more than wrappers.

It should generate:

- Static witnesses.
- Operation witnesses when requested.
- Named mathematical contexts.
- Scope types.
- Handle types.
- C# 14 extension members for handles.
- Capacity constants.
- Optional generated runners that allocate stack buffers internally.
- Diagnostics when capacity/status/operator usage is unsafe.

The generator should not generate:

- Hidden heap allocations for hot paths.
- Reflection-based dispatch.
- Runtime type discovery.
- Global sessions.
- Closure-based tapes.

## 10. Example Final Shape

### Polynomial

```csharp
[PolynomialScope(typeof(Rational32), typeof(Rational32StatusFieldOps), Terms = 32, Workspace = 64)]
public partial struct PolyExample
{
    partial void Build(ref PolyExample.Scope q)
    {
        var x = q.Variable();
        var p = x * x + q.Const(3) * x + q.Const(1);
        q.Return(p);
    }
}
```

### Autodiff

```csharp
[ReverseDiffScope(typeof(Rational32), typeof(Rational32StatusFieldOps), Nodes = 32)]
public partial struct DiffExample
{
    partial void Build(ref DiffExample.Scope d)
    {
        var x = d.Input(new Rational32(2, 1));
        var y = x * x + d.Const(3) * x;
        d.Backward(y);
        d.ReturnGradient(x);
    }
}
```

### Matrix

```csharp
[MatrixScope(typeof(Rational32), typeof(Rational32StatusFieldOps), Rows = 3, Columns = 3)]
public partial struct MatrixExample
{
    partial void Build(ref MatrixExample.Scope m)
    {
        var a = m.Matrix(/* values */);
        var b = m.Identity();
        var c = a * b;
        m.Return(c);
    }
}
```

## 11. What We Would Do Differently From The Current Prototype

If starting from scratch, build in this order:

1. Contracts.
2. Kernels.
3. Scope model and handle conventions.
4. Generator diagnostics.
5. Generated contexts.
6. Generated scopes.
7. C# 14 handle extensions/operators.
8. Managed convenience.
9. Text/parsing.

The current prototype built kernels first, then facades, then discovered scopes. The kernel
work is still valuable, but the public architecture should be scope-first.

## Summary

The desired HPD-Math pattern is:

```text
Ugly explicit kernels underneath.
Generated mathematical worlds in the middle.
Beautiful scope-local syntax on top.
```

That is how HPD-Math can preserve zero-GC and Native AOT while recovering the mathematical
authoring feel that made Helium pleasant.
