# HPD-Math Rules

These rules govern new HPD-Math work.

HPD-Math is not a generic numeric library with math-themed helpers. It is an executable,
decidable mathematics library built for zero-GC hot paths and Native AOT.

## Non-Negotiables

1. Normal user DX is generated inline values for bounded structures, generated scopes for
   authoring, scope-local handles, C# extension syntax, and generated `Result`.
2. Kernels use explicit caller-owned spans, builders, views, workspaces, and
   `AlgebraStatus`.
3. Normal scope callers must not provide output spans or handwritten `stackalloc` buffers
   just to receive a result.
4. No hot-path GC allocation.
5. No exceptions for expected algebraic failure, capacity failure, invalid division,
   overflow, missing inverses, or non-canonical input. Return status.
6. Native AOT is a design constraint, not a final smoke test.
7. Operators belong on scope-local handles where failure can be recorded in scope status.
8. Parsing, formatting, reflection, diagnostics display, and heap-backed convenience stay
   outside hot-path packages.
9. First-class bounded values with known capacity should be generated inline-storage values,
   not hand-written size ladders or span views pretending to be ordinary values.
10. Do not turn implementation limits into universe limits. If a mathematical universe is
    valid beyond the algorithms currently implemented, generate the valid universe and gate
    only unsupported operations with explicit capability checks and `AlgebraStatus`.
11. If adding a feature feels like a retrofit, break the surface. There is no backward
   compatibility promise yet.
12. Do not mechanically port Helium APIs. Port the mathematical structure into the HPD-Math
    execution model.

## Layer Rules

### Contracts

Contracts describe executable mathematical structure:

- decidable equality and order
- finite enumeration
- finite support
- algebraic laws and operation witnesses
- status-returning semiring, ring, field, module, lattice, and domain operations

Contracts must be tiny, explicit, static/AOT-friendly, and free of managed convenience.

### Kernels

Kernels are allowed to be mechanical.

They should:

- accept views/builders/spans/workspaces explicitly
- return `AlgebraStatus`
- avoid LINQ, delegates, closures, reflection, and hidden global state
- avoid allocating arrays, collections, strings, or boxed values
- be easy to test directly

Kernel code is where explicit storage belongs.

### Contexts

Generated contexts name mathematical universes.

They may expose advanced/internal APIs such as:

- capacity constants
- `CreateScope(...)`
- explicit `CopyReturned(...)`
- raw `Try*` facades over kernels

Those advanced APIs may require spans because they are the performance or implementation
surface, not the ordinary user path.

Contexts may also generate first-class inline-storage values when the mathematical object is
bounded by the context. For example, a finite powerset context should generate a nested `Set`
value and `Ops` witness with storage sized from the universe cardinality.

Do this:

```csharp
[FinitePowerSetContext(200)]
public readonly partial struct Vertices;

Vertices.TrySingletonIndex(199, out var last);
var ops = default(Vertices.Ops);
var status = ops.TrySup(first, last, out var union);
```

not a ladder of unrelated value types:

```csharp
FinitePowerSet32 small;
FinitePowerSet64 medium;
FinitePowerSet128 large;
```

and not a span-backed view forced into first-class value contracts.

Generated inline values are the right pattern when:

- capacity/cardinality is known at generation time
- the value should satisfy operation witnesses such as lattice, boolean algebra, order, or ring
  contracts
- normal users should get value semantics without supplying buffers
- kernels still need direct span access internally

### Scopes

Generated scopes are the normal authoring surface.

They should:

- own the generated execution frame
- create scope-local handles
- track status
- stop mutating after first failure
- return inert handles after failure
- provide math-shaped methods such as `Const`, `Variable`, `Add`, `Mul`, `Derivative`,
  `DivMod`, `Gcd`, `Backward`, and `Return`

Scope bodies should feel like math, not buffer management.

### Results

Generated scope results are the normal extraction surface.

They should:

- use inline storage
- expose domain-specific accessors
- keep writable spans internal to generated code
- avoid heap allocation

Examples:

```csharp
var result = default(Example.Result);
var status = new Example().Run(ref result);
```

not:

```csharp
new Example().Run(outputDegrees, outputCoefficients, out var termCount);
```

### Managed And Text Layers

Managed/text packages may provide:

- heap-backed immutable convenience values
- parsing
- formatting
- display/debug helpers
- REPL/notebook comfort APIs

They must not leak into hot-path kernels, core contracts, or generated scope execution.

## Feature Intake Checklist

Before adding a Helium parity item or new construction, answer:

1. What mathematical structure is being represented?
2. What is decidable or executable about it?
3. What are the finite capacities or truncation boundaries?
4. What is the kernel storage model?
5. What failures must return `AlgebraStatus`?
6. Is this a first-class bounded value that should be generated with inline storage?
7. Which limits are mathematical/context validity, and which are only current algorithm
   support?
8. What does the generated scope authoring experience look like?
9. What does the generated `Result` look like?
10. Does any part require managed/text convenience, and if so, is it isolated?

If these answers are unclear, design the structure before writing code.

## Anti-Patterns

Avoid these in hot-path packages:

```csharp
var result = terms.ToArray();
var normalized = items.Select(...).ToList();
throw new InvalidOperationException("not invertible");
Func<T, T> operation = x => ...;
public static Polynomial Parse(string text);
public Polynomial Add(Polynomial other) => new(...);
public readonly struct FinitePowerSet32 { ... }
public readonly struct FinitePowerSet64 { ... }
public static bool IsValidContext => Prime > 1 && Length > 0 && Length <= 2;
```

Avoid this as normal user DX:

```csharp
Span<int> degrees = stackalloc int[32];
Span<Rational32> coefficients = stackalloc Rational32[32];
Span<int> workspaceDegrees = stackalloc int[64];
Span<Rational32> workspaceCoefficients = stackalloc Rational32[64];

var builder = new SparsePolynomialBuilder<Rational32>(degrees, coefficients);
var status = SparsePolynomialKernels.TryMul(...);
```

That shape is valid for kernels, generated internals, context tests, and advanced
performance code. It is not the public authoring experience.

## The Rule Of Thumb

```text
Explicit kernels for machines.
Generated inline values for first-class bounded structures.
Generated scopes for humans.
Generated results for extraction.
Managed/text layers for comfort.
```
