# Helium V1 Parity Gaps

HPD-Math now covers the main Helium spine: decidable equality/order, finite enumeration,
finite support, order/lattice witnesses, finite powersets, algebraic operation witnesses,
finite collections, polynomials, quotient rings, rational functions, finite-field and
bounded-rational number-field facades, dense linear algebra, small exact numeric contexts,
p-adic/Witt fragments, and AOT-safe autodiff.

This file tracks what is not fully ported from `Helium.Primitives` and `Helium.Algebra`.
Items here should be rebuilt in HPD-Math style: explicit witnesses, caller-owned buffers,
status-returning kernels, no hot-path GC allocation, and Native AOT-friendly APIs.

## Numeric Contexts

- `Complex<T>`
- `Quaternion<T>`
- BigInteger-backed `Integer`
- BigInteger-backed `Rational`
- Broader integer number-theory helpers
- Characteristic witnesses such as `ICharP`
- Domain markers and algorithms such as `IGcdDomain`, `IEuclideanDomain`, and
  `INoZeroDivisors`

## Collections And Series

- `Seq<T>`
- `Stream<T>`
- `FormalPowerSeries<T>`
- `FiniteSupportSeries`
- `NatAntidiagonal`

## Algebraic Constructions

- First-class `Ideal`
- Group/ring actions beyond the current module contracts
- `IStar`-style involution/star structures

## Linear And Multilinear Algebra

- `FixedVector`
- `LinearMap`
- `Submodule`
- `BilinearForm`
- `TensorProduct`

## Lie And Hopf Islands

- `ILieRing`
- `ILieAlgebra`
- `SquareMatrixLieElement`
- Coalgebra contracts
- Hopf algebra contracts
- Group ring construction

## Parsing And Formatting

- Matrix parsing
- Sparse polynomial parsing
- Multivariate polynomial parsing
- Rational function parsing
- General math lexer/parser helpers
- Rich formatting helpers

## Notes

- These are parity gaps, not necessarily v2 design requirements.
- Order/lattice parity is covered in v2 style by witness-first lattice contracts,
  `ICompleteFiniteLatticeOps<T>`, `IOrderHomOps<TSource,TTarget>`,
  finite monotonicity validation kernels, and generated `FinitePowerSetContext`
  first-class value powersets with inline storage sized to the finite universe.
- Do not port Helium APIs mechanically when the v1 shape conflicts with zero-allocation
  or Native AOT goals.
- If a v1 feature depended on immutable collections, LINQ, reflection, delegates,
  closures, parser-heavy constructors, or exception-driven validation, prefer a new v2
  kernel/facade design instead of compatibility.
