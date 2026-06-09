# HPD-Math V2 Pattern

HPD-Math v2 should be designed around one rule:

> Users write math inside generated scopes; kernels execute math with explicit memory.

This gives us Helium-like authoring without giving up zero-GC hot paths or Native AOT.

## The Stack

```text
1. Contracts
2. Kernels
3. Generated Contexts
4. Generated Scopes
5. Generated Handles
6. C# 14 Extension Members
7. Managed/Text Convenience
```

## 1. Contracts

Contracts describe executable mathematical structure.

Examples:

```csharp
IEqualityOps<T>
ITotalOrderOps<T>
IFiniteEnumerationOps<T>
IRingOps<T>
IStatusRingOps<T>
IFieldOps<T>
IStatusFieldOps<T>
IStaticDimension
IPrimeModulus
```

Contracts should be tiny, AOT-safe, and explicit.

## 2. Kernels

Kernels are the raw implementation layer.

Example:

```csharp
SparsePolynomialKernels.TryMul(
    left,
    right,
    ref destination,
    workspaceDegrees,
    workspaceCoefficients,
    coefficientOps);
```

Kernel rules:

- No GC allocation.
- No LINQ.
- No closures.
- No reflection.
- No hidden global state.
- Caller owns buffers and workspaces.
- Return `AlgebraStatus`.

Kernels are allowed to be ugly.

## 3. Generated Contexts

Contexts name mathematical universes.

Example:

```csharp
[PolynomialContext(typeof(Rational32), typeof(Rational32StatusFieldOps))]
public readonly partial struct Qx;

[FiniteFieldContext(typeof(ModInt<P7>), typeof(ModIntOps<P7>), typeof(Dim2))]
public readonly partial struct F49;
```

A context should generate:

- First-class inline values when the mathematical object is bounded by the context.
- Static witnesses.
- Ops witness creation.
- Raw `Try*` facade methods for advanced users.
- Capacity constants when specified.
- Nested low-level scope or copy helpers only when explicit storage is the point.

Example generated surface:

```csharp
var ops = Qx.CreateOps();    // witness over the named universe
Qx.TryFromTerms(...);        // construct a first-class bounded value
ops.TryMul(a, b, out c);     // ordinary value operation
Qx.TermCapacity              // generated capacity
Qx.WorkspaceCapacity         // generated capacity
```

## 3.1 Operation Witness Generation

Operation witnesses are part of the executable math identity, but v2 must not generate
standalone non-status arithmetic witnesses that can throw on hot paths.

Removed pattern:

```csharp
[RingOps(typeof(int), "0", "1")]
public readonly partial struct ZOps;
```

That style emits unchecked public math shape around checked arithmetic expressions. It is
too easy for overflow or invalid algebraic operations to escape as exceptions.

Allowed v2 patterns:

```csharp
public readonly struct Rational32StatusFieldOps : IStatusFieldOps<Rational32>
{
    public AlgebraStatus TryAdd(ref Rational32 destination, in Rational32 left, in Rational32 right);
}
```

or future context-owned generation:

```csharp
[ExactIntegerContext(typeof(int), Overflow = OverflowPolicy.Status)]
public readonly partial struct Z32;
```

where generated operations implement status-returning contracts such as
`IStatusRingOps<T>` or `IStatusFieldOps<T>`, and any convenient operators are exposed only
through a scope that records failure in `Scope.Status`.

Witness generation rules:

- Prefer explicit hand-written witnesses for primitive domains until a context needs
  repeated generated boilerplate.
- Generated witnesses must be status-first for partial or bounded arithmetic.
- Generated witnesses must never hide overflow, division failure, invalid inputs, or
  capacity failure behind exceptions.
- Non-status contracts are acceptable only for operations that are truly total for the
  represented type and cannot fail in the target execution model.
- Operators belong on scope-local handles, not on heap-owning values or standalone witness
  generators.

## 4. Generated Scopes

Scopes are the main v2 authoring DX layer.

They are generated authoring frames that hide their temporary storage from normal callers
and expose pleasant math methods through scope-local handles.

Example user code:

```csharp
[PolynomialScope(typeof(Rational32), typeof(Rational32StatusFieldOps), Terms = 32, Workspace = 64)]
public partial struct Example
{
    partial void Build(ref Example.Scope q)
    {
        var x = q.Variable();
        var p = x * x + q.Const(3) * x + q.Const(1);

        q.Return(p);
    }
}
```

Generated runner:

```csharp
public AlgebraStatus Run(ref Result result)
{
    result.Clear();

    Span<int> degrees = stackalloc int[32];
    Span<Rational32> coefficients = stackalloc Rational32[32];
    Span<int> workspaceDegrees = stackalloc int[64];
    Span<Rational32> workspaceCoefficients = stackalloc Rational32[64];

    var scope = new Scope(
        degrees,
        coefficients,
        workspaceDegrees,
        workspaceCoefficients);

    Build(ref scope);

    var status = scope.CopyReturned(
        result.DegreeStorage,
        result.CoefficientStorage,
        out var termCount);

    if (status == AlgebraStatus.Ok)
    {
        result.SetTermCount(termCount);
    }

    return scope.Status == AlgebraStatus.Ok ? status : scope.Status;
}
```

Scope rules:

- Stack-only.
- Holds or references all required builders/workspaces.
- Tracks `Status`.
- Stops mutating after first failure.
- Returns inert handles after failure.
- Exposes domain methods like `Const`, `Variable`, `Add`, `Mul`, `Square`, `DivMod`, `Evaluate`, `Backward`, `Gradient`.
- Hides extraction buffers from normal callers through generated `Result` structs.

## 4.1 Generated Results

Generated scope runners return values through per-scope result structs.

Caller code should look like this:

```csharp
var result = default(Example.Result);
var status = new Example().Run(ref result);

if (status == AlgebraStatus.Ok)
{
    var degree = result.DegreeAt(0);
    var coefficient = result.CoefficientAt(0);
}
```

Result rules:

- Use inline arrays, not heap arrays.
- Are generated per scope shape and capacity.
- Expose domain-specific accessors: `TermCount`, `DegreeAt`, `CoefficientAt` for
  polynomials; `RowCount`, `ColumnCount`, and indexers for matrices; `Primal` and
  `GradientAt` for reverse diff; numerator/denominator accessors for rational functions.
- Keep writable spans internal to generated code.
- Let normal users avoid handwritten output spans and `stackalloc`.
- Do not replace kernel APIs. Explicit span APIs can still exist in kernels, generated
  internals, and advanced performance hooks, but they must not define the normal context
  or scope DX.

## 5. Generated Handles

Handles are small values that represent results inside a scope.

Examples:

```csharp
Qx.Poly
F49.Element
R32Diff.Var
Mat3.Matrix
```

They should be lightweight descriptors, not heap-backed mathematical objects.

Example:

```csharp
public readonly ref struct Poly
{
    internal readonly int Handle;
    internal readonly Scope Scope;
}
```

Handle rules:

- No heap ownership.
- No array ownership.
- No global session.
- Cannot outlive the scope.
- Can expose cheap properties like `Degree`, `IsZero`, or `Index`.

## 6. C# 14 Extension Members

C# 14 extension members should target generated handles.

This is the key syntax layer.

Example:

```csharp
public readonly partial struct Qx
{
    public readonly ref struct Poly
    {
        public Poly Add(Poly other);
        public Poly Mul(Poly other);
    }
}

public static class QxExtensions
{
    extension(Qx.Poly receiver)
    {
        public static Qx.Poly operator +(Qx.Poly left, Qx.Poly right) => left.Add(right);
        public static Qx.Poly operator *(Qx.Poly left, Qx.Poly right) => left.Mul(right);
    }
}
```

C# extension blocks must live in top-level, non-generic static classes. Generated handles
therefore expose small public bridge methods such as `Add`, `Sub`, `Mul`, and `Neg`;
generated extension classes provide the symbolic operator layer.

The implemented v2 generator emits extension operators for generated polynomial, matrix,
reverse-diff, quotient/field-extension, rational-function, p-adic, and Witt-vector
handles.

```csharp
public static class QxExtensions
{
    extension(Qx.Poly receiver)
    {
        public Qx.Poly Square => receiver * receiver;

        public static Qx.Poly operator +(Qx.Poly left, Qx.Poly right)
            => left.Add(right);

        public static Qx.Poly operator *(Qx.Poly left, Qx.Poly right)
            => left.Mul(right);
    }
}
```

Then user code becomes:

```csharp
var p = x.Square + q.Const(3) * x;
```

Extension rules:

- Use operators only when failure can be recorded in the scope.
- Never allocate behind an operator.
- Never throw for expected algebraic failure.
- Prefer methods first; add operators once status semantics are proven.

## 7. Managed/Text Convenience

Managed and text layers are optional comfort layers.

Managed can offer:

- Heap-backed immutable objects.
- Easier REPL/notebook use.
- Debug-friendly APIs.
- Conversions to/from kernel views.

Text can offer:

- Parsing.
- Formatting.
- Math display.

These layers must not leak into hot-path kernels.

## Good V2 DX

Polynomial:

```csharp
partial void Build(ref Qx.Scope q)
{
    var x = q.Variable();
    var p = x * x + q.Const(3) * x + q.Const(1);
    var dp = q.Derivative(p);

    q.Return(dp);
}
```

Autodiff:

```csharp
partial void Build(ref R32Diff.Scope d)
{
    var x = d.Input(new Rational32(2, 1));
    var y = x * x + d.Const(3) * x;

    d.Backward(y);
    d.ReturnGradient(x);
}
```

Matrix:

```csharp
partial void Build(ref Mat3.Scope m)
{
    var a = m.Matrix(/* values */);
    var b = m.Identity();
    var c = a * b;

    m.Return(c);
}
```

## Bad V2 DX

Do not make users write this in normal authoring paths:

```csharp
Span<int> degrees = stackalloc int[32];
Span<Rational32> coefficients = stackalloc Rational32[32];
Span<int> workspaceDegrees = stackalloc int[64];
Span<Rational32> workspaceCoefficients = stackalloc Rational32[64];

var builder = new SparsePolynomialBuilder<Rational32>(degrees, coefficients);
var status = SparsePolynomialKernels.TryMul(...);
```

That code belongs in kernels, tests, generated code, and advanced performance work.

## First Pattern vs V2 Pattern

The first HPD-Math prototype is mathematically pointed in the right direction, but its
developer experience is still mostly a kernel facade. The v2 pattern moves the authoring
unit up one level: from "call this generated static method with spans" to "write inside a
generated mathematical scope."

### First Pattern

The first pattern looks like this:

```text
Contracts -> Views/Builders -> Kernels -> Generated static facades
```

User-facing code tends to look like:

```csharp
Span<int> degrees = stackalloc int[32];
Span<Rational32> coefficients = stackalloc Rational32[32];
Span<int> workspaceDegrees = stackalloc int[64];
Span<Rational32> workspaceCoefficients = stackalloc Rational32[64];

var left = new SparsePolynomialBuilder<Rational32>(degrees, coefficients);
var destination = new SparsePolynomialBuilder<Rational32>(degrees, coefficients);

var status = Qx.TryMul(
    left.View,
    right.View,
    ref destination,
    workspaceDegrees,
    workspaceCoefficients);
```

That pattern is strong for implementation quality:

- Zero-GC hot paths are explicit.
- Native AOT has no reflection or dynamic dispatch surprises.
- Algebraic failure is status-returning instead of exception-driven.
- Kernels are easy to test directly.
- Storage ownership is honest.

But it is weak for mathematical authoring:

- Users see storage layout before they see the math.
- Source generation mostly shortens type names.
- C# 14 extensions can only make raw views slightly nicer.
- Operators are awkward because there is nowhere natural to record failure.
- Stack allocation and workspace sizing leak into ordinary code.

This is the pattern we accidentally built first:

```text
Beautiful machine contract.
Honest performance model.
Still not a beautiful human math surface.
```

### V2 Pattern

The v2 pattern looks like this:

```text
Contracts -> Kernels -> Generated Contexts -> Generated Scopes -> Generated Handles -> Extensions
```

User-facing code should look like:

```csharp
[PolynomialScope(typeof(Rational32), typeof(Rational32StatusFieldOps), Terms = 32, Workspace = 64)]
public partial struct Example
{
    partial void Build(ref Example.Scope q)
    {
        var x = q.Variable();
        var p = x * x + q.Const(3) * x + q.Const(1);
        var dp = q.Derivative(p);

        q.Return(dp);
    }
}
```

Generated code owns the explicit machinery:

```csharp
var result = default(Example.Result);
var status = new Example().Run(ref result);
```

The generated runner owns the explicit machinery:

```csharp
Span<int> degrees = stackalloc int[32];
Span<Rational32> coefficients = stackalloc Rational32[32];
Span<int> workspaceDegrees = stackalloc int[64];
Span<Rational32> workspaceCoefficients = stackalloc Rational32[64];

var scope = new Example.Scope(
    degrees,
    coefficients,
    workspaceDegrees,
    workspaceCoefficients);

Build(ref scope);
scope.CopyReturned(result.DegreeStorage, result.CoefficientStorage, out var termCount);
```

That pattern preserves the good parts of the first design:

- Kernels stay explicit, allocation-free, and AOT-safe.
- Views/builders still exist for advanced control.
- Status is still first-class.
- Capacity is still visible at the declaration boundary.

But it fixes the authoring layer:

- Users write math inside a named mathematical world.
- Handles make C# 14 extension operators meaningful.
- Failure can be recorded in the active scope.
- Stack allocation is generated, not handwritten.
- Workspace and temporary lifetimes are owned by the generated frame.
- Returned values are read through generated result structs, not caller-supplied output
  spans.

This is the intended v2 shape:

```text
Beautiful human math surface.
Honest generated execution frame.
Same zero-GC kernels underneath.
```

### Comparison

| Concern | First Pattern | V2 Pattern |
| --- | --- | --- |
| Main user abstraction | Views, builders, static facades | Generated scopes and handles |
| Source generator role | Emits wrappers over kernels | Emits authoring worlds, scopes, handles, diagnostics |
| C# 14 extension target | Raw views/builders | Generated handles |
| Operator support | Awkward, status has nowhere to go | Natural, status lives in the scope |
| Stackalloc visibility | Often user-visible | Generated or advanced-only |
| Workspace visibility | Often user-visible | Scope-owned |
| Hot-path allocation | Avoided | Avoided |
| Native AOT posture | Good | Good |
| DX compared to Helium | More verbose and mechanical | Similar feel, with scoped capacity declarations |
| Best use | Kernels, tests, perf-sensitive internals | Normal mathematical authoring |

### The Decision

The first pattern should not be thrown away as an implementation strategy. It should be
demoted to the lower layer.

The v2 pattern should be the public architecture:

```text
First pattern becomes the engine.
V2 pattern becomes the driving experience.
```

## DX Compared To Helium

Helium's best DX comes from owning mathematical values. `SparsePolynomial<R>` owns its
finite support, `Matrix<R>` owns an array, and operations return new values.

That lets Helium code feel like ordinary math:

```csharp
using Q = Rational;
using P = SparsePolynomial<Q>;

var x = P.X;
var p = x * x + P.C(new Q(3)) * x + P.One;
var q = x + P.One;

var product = p * q;
var derivative = p.Derivative();
var (quotient, remainder) = product.DivMod(p);
var gcd = p.Gcd(q);
```

Helium's matrix DX follows the same shape:

```csharp
var a = Matrix<Rational>.FromArray(2, 2, [one, two, three, four]);
var b = Matrix<Rational>.Identity(2);

var c = a * b;
var t = c.Transpose();
```

And Helium reverse autodiff uses a global/thread-local authoring session:

```csharp
using var session = Tape<Rational>.Begin();

var x = new Var<Rational>(new Rational(2));
var y = x * x + new Var<Rational>(new Rational(3)) * x;

var gradients = session.Backward(y);
var dx = gradients[x.Index];
```

That is beautiful to write, but it gets the beauty by accepting v1 tradeoffs:

- Operators allocate new owning values.
- Polynomial multiplication uses dictionaries/finsupp construction.
- Matrix operations allocate result arrays.
- Autodiff uses thread-local state, lists, closures, and returned arrays.
- Parsing/formatting live close to core mathematical objects.
- Failure is often exceptions, default values, or assumed-valid operations.

The current HPD-Math prototype has the opposite shape. It protects hot paths first:

```csharp
Span<int> leftDegrees = stackalloc int[8];
Span<Rational32> leftCoefficients = stackalloc Rational32[8];
Span<int> rightDegrees = stackalloc int[8];
Span<Rational32> rightCoefficients = stackalloc Rational32[8];
Span<int> outputDegrees = stackalloc int[16];
Span<Rational32> outputCoefficients = stackalloc Rational32[16];
Span<int> workspaceDegrees = stackalloc int[32];
Span<Rational32> workspaceCoefficients = stackalloc Rational32[32];

var left = new SparsePolynomialBuilder<Rational32>(leftDegrees, leftCoefficients);
var right = new SparsePolynomialBuilder<Rational32>(rightDegrees, rightCoefficients);
var output = new SparsePolynomialBuilder<Rational32>(outputDegrees, outputCoefficients);

SparsePolynomialKernels.TryMonomial(1, Rational32.One, ref left, new Rational32StatusFieldOps());
SparsePolynomialKernels.TryMonomial(0, new Rational32(1, 1), ref right, new Rational32StatusFieldOps());

var status = left.AsView().TryMul(
    right.AsView(),
    ref output,
    workspaceDegrees,
    workspaceCoefficients,
    new Rational32StatusFieldOps());
```

That is mechanically excellent, but not pleasant authoring. The user is thinking about
buffers, capacities, workspaces, and status before thinking about the polynomial.

The v2 target is the middle path:

```csharp
[PolynomialScope(typeof(Rational32), typeof(Rational32StatusFieldOps), Terms = 32, Workspace = 64)]
public partial struct PolyExample
{
    partial void Build(ref PolyExample.Scope q)
    {
        var x = q.Variable();
        var p = x * x + q.Const(3) * x + q.Const(1);
        var qx = x + q.Const(1);

        var product = p * qx;
        var derivative = q.Derivative(p);
        var division = q.DivMod(product, p);
        var gcd = q.Gcd(p, qx);

        q.Return(gcd);
    }
}
```

Generated code owns the HPD-Math machinery:

```csharp
var result = default(PolyExample.Result);
var status = new PolyExample().Run(ref result);
```

The generated runner owns the HPD-Math machinery:

```csharp
Span<int> degrees = stackalloc int[32];
Span<Rational32> coefficients = stackalloc Rational32[32];
Span<int> workspaceDegrees = stackalloc int[64];
Span<Rational32> workspaceCoefficients = stackalloc Rational32[64];

var scope = new PolyExample.Scope(
    degrees,
    coefficients,
    workspaceDegrees,
    workspaceCoefficients);

Build(ref scope);
scope.CopyReturned(result.DegreeStorage, result.CoefficientStorage, out var termCount);
```

So the DX comparison is:

| Style | User Writes | Hidden Cost | Hot-Path Story |
| --- | --- | --- | --- |
| Helium v1 | `p * q`, `p.Gcd(q)`, `Matrix * Matrix` | Allocating values, arrays, dictionaries, closures | Nice, but not zero-GC/AOT strict |
| HPD-Math prototype | `TryMul(view, ref destination, workspace, ops)` | Almost none | Excellent, but too mechanical |
| HPD-Math v2 | `var p = x * x + q.Const(3) * x` inside scope | Generated stack frame, explicit capacity | Excellent if scope/handle rules are enforced |

The important distinction is not "Helium has operators and HPD-Math does not." The
distinction is where the result lives.

```text
Helium operator result lives in a new owning value.
Prototype HPD-Math result lives in caller-provided destination storage.
V2 HPD-Math operator result lives as a handle inside a generated scope.
```

That last version is the key. It is the only one that can feel close to Helium while still
being honest about zero allocation and Native AOT.

## Summary

The v2 pattern is:

```text
Explicit kernels for machines.
Generated scopes for humans.
C# 14 extensions for mathematical syntax.
Managed/text layers for convenience.
```

This is how HPD-Math can be zero-GC, Native AOT friendly, and still pleasant to write.
