# Witt Vector Future Slice

This slice records what we learned from comparing HPD-Math, Helium v1, and broader CAS
support.

## Current State

Helium v1 supports truncated p-typical Witt vector arithmetic only for lengths 1 and 2.
It rejects longer lengths at the type/universe level and uses heap-backed arrays.

HPD-Math should not preserve that ceiling as a universe rule.

Current HPD-Math behavior:

- `WittVectorContext` accepts any positive generated length.
- Generated `Vector` values use inline storage.
- Construction, zero, one, equality, indexing, and generated ghost components work for
  positive lengths.
- Add, neg, sub, and mul currently support lengths 1 and 2.
- Unsupported arithmetic for longer lengths returns status instead of pretending the
  universe is invalid.

## Rule

Do not turn implementation limits into universe limits.

For Witt vectors:

```csharp
IsValidContext          // Prime > 1 && Length > 0
HasSupportedArithmetic // Current arithmetic algorithm support
```

Length 3 is a valid Witt vector universe even if length-3 multiplication is not implemented
yet.

## Proposed Next Steps

1. Add a distinct status for unsupported-but-valid operations.

   Prefer:

   ```csharp
   AlgebraStatus.UnsupportedOperation
   ```

   over returning `InvalidInput` when the value/context is mathematically valid but the
   operation is not implemented.

2. Generalize low-level ghost component kernels.

   Implement:

   ```text
   w_n(x) = x_0^(p^n) + p*x_1^(p^(n-1)) + ... + p^n*x_n
   ```

   This is finite, allocation-free, and fits the current kernel model.

3. Add explicit coefficient capability boundaries.

   General Witt arithmetic via ghost inversion needs operations beyond a plain ring:

   - add/sub/mul
   - integer embedding
   - small exponentiation
   - exact division by powers of `p`, when valid for the coefficient domain

   Do not hide this behind a generic `ICommRing<T>` claim.

4. Implement general finite-length addition and negation first.

   For each coordinate `n`, solve recursively so:

   ```text
   ghost(c)_n = ghost(a)_n + ghost(b)_n
   ```

   Earlier coordinates are already known, so the nth coordinate can be solved with explicit
   scratch storage and status-returning exact division.

5. Implement subtraction from negation plus addition.

6. Implement multiplication after addition is proven.

   Solve recursively so:

   ```text
   ghost(c)_n = ghost(a)_n * ghost(b)_n
   ```

7. Keep generated context DX stable.

   The public shape should not change as arithmetic support deepens:

   ```csharp
   [WittVectorContext(typeof(T), typeof(TOps), typeof(P), 8)]
   public readonly partial struct W8;

   var status = W8.TryFromComponents(..., out var a);
   var ops = default(W8.Ops);
   status = ops.TryMul(a, b, out var product);
   ```

   Future work should change `TryMul` from `UnsupportedOperation` to `Ok`, not redesign the
   user surface.

## Test Plan

Cover three levels:

- Length 3 storage/construction/ghost components continue to work.
- Length 3 arithmetic reports `UnsupportedOperation` until implemented.
- Length 3+ arithmetic eventually passes against known examples.

Cover at least these coefficient domains:

- `int` with checked status behavior for small exact examples.
- `Rational32StatusFieldOps`, where exact division is natural but bounded overflow remains
  status-returning.
- `ModInt<P>` carefully, because characteristic `p` changes what can be recovered through
  ghost coordinates.

## Non-Goals

- Do not port Sage/Magma-style heap-backed generality directly.
- Do not introduce LINQ, arrays, closures, or exception-driven validation into hot paths.
- Do not claim generic ring support for arithmetic that needs stronger coefficient
  capabilities.
