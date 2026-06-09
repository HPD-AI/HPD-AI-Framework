# HPD-Math Official V2 Proposal

HPD-Math should treat the current prototype as a donor implementation, not as the final
public architecture.

The kernel work is worth keeping. The public shape should be restarted around generated
mathematical universes that emit inline values for bounded objects and scopes for
expression authoring.

## Decision

Build HPD-Math v2 around this stack:

```text
Contracts
  -> Allocation-free kernels
    -> Generated mathematical contexts
      -> Generated inline values
      -> Generated scopes
        -> Scope-local handles
        -> C# 14 extension operators/methods
      -> Optional managed/text convenience
```

The current code already proves the hard lower-level claim: Helium's decidable-math and
algebraic spine can be represented with explicit storage, status-returning operations,
Native AOT friendliness, and no GC allocation on hot paths.

The old generator/public DX was the wrong top layer because it exposed builders,
workspaces, and destination buffers too early.

## Keep

Keep these as v2 engine material:

- Core contracts and witnesses.
- `AlgebraStatus`.
- Finite views/builders/kernels.
- Sparse/dense polynomial kernels.
- Rational function and quotient ring kernels.
- Modular/rational/p-adic/Witt numeric kernels.
- Linear algebra kernels.
- Explicit reverse tape autodiff.
- Kernel tests and AOT smoke logic.

These are not wasted. They are the engine.

## Break And Redesign

Restart these parts:

- Source generator architecture.
- Public generated APIs.
- Generated attributes.
- Extension-member strategy.
- DX tests.
- AOT smoke examples.
- Any non-status bridge that throws in hot-path math.

The current generator emits facade methods like:

```csharp
Zx.TryMul(left, right, ref destination, workspaceDegrees, workspaceCoefficients);
```

V2 should generate authoring scopes like:

```csharp
partial void Build(ref Qx.Scope q)
{
    var x = q.Variable();
    var p = x * x + q.Const(3) * x + q.Const(1);

    q.Return(p);
}
```

That is a different architecture, not a patch.

It should also generate first-class bounded values from contexts:

```csharp
[MatrixContext(typeof(int), typeof(CheckedInt32RingOps), Rows = 2, Columns = 2)]
public readonly partial struct Mat2;

var status = Mat2.TryFromValues([1, 2, 3, 4], out var a);
var ops = default(Mat2.Ops);
status = ops.TryIdentity(out var i);
status = ops.TryMul(a, i, out var result);
```

## Why This Is Better

Helium got beautiful syntax by letting values own memory and return new objects. The
prototype HPD-Math gets zero allocation by making users manually pass builders and
workspaces.

V2 gets the third path:

```text
Contexts emit inline values for bounded math objects.
Operators return scope-local handles.
The scope owns buffers and status.
Kernels still do explicit zero-GC execution.
```

That gives HPD-Math Helium-like authoring without lying about allocation.

## Bootstrapping Order

1. Freeze the current prototype as the donor engine.
2. Create clean v2 generator attributes: bounded `*Context` attributes for inline values,
   `*Scope` attributes for authoring, and explicit kernels underneath both.
3. Implement one vertical slice first: univariate polynomial scope over
   `Rational32StatusFieldOps`.
4. Generate context-level `Poly` values plus `Scope`, `Poly` handles, stack-backed runner, generated inline-storage
   `Result`, `Run(ref Result)`, status tracking, `Const`, `Variable`, `Add`, `Mul`,
   `Derivative`, `Return`, and C# 14 extension/operator syntax.
5. Port matrix scope next.
6. Port autodiff scope next.
7. Bring back quotient rings, finite fields, rational functions, number fields, p-adics,
   and Witt vectors as generated contexts/scopes.
8. Keep managed/text packages separate and explicitly allocating.

## Call

Do not continue layering DX onto facade-only generator output. It becomes a retrofit.
Normal callers should not have to provide output spans or handwritten `stackalloc`
buffers. Explicit spans belong in kernels, generated internals, kernel tests, and advanced
performance hooks. Generated contexts should emit first-class inline values when capacity is
part of the universe. Generated scopes should own their execution frame and copy returned
values into generated result structs.

Do not delete the mathematical engine.

Start v2 clean at the public/generator layer, using the current implementation as the
tested kernel substrate.
