# HPD-Math V2 Completion Audit

This project is not carrying backward compatibility forward.

The current repository contains two things at once:

- A useful allocation-free mathematical engine.
- A prototype public/generator layer that must be treated as donor code only.

V2 is complete only when the public surface is scope-first and the old facade-first
generator surface is removed or quarantined outside the v2 API.

## V2 Surface Already Proven

These generated universe and scope surfaces exist and are covered by tests:

- `FinitePowerSetContext`: first-class generated `Set` values for finite universes,
  including multi-word inline storage for larger bounded universes.
- `PolynomialContext`: first-class generated dense/sparse polynomial values and operation
  witnesses over bounded term/coefficient storage.
- `PolynomialScope`: scope-local polynomial handles, operators, derivative, explicit return.
- `MatrixContext`: first-class generated fixed dense matrix values and operation witnesses.
- `MatrixScope`: fixed dense matrix handles, add, multiply, transpose, identity.
- `ReverseDiffContext`: hidden-frame reverse-mode autodiff runner with generated `Result`.
  The tape remains explicit op-code storage internally, but normal callers do not provide
  tape buffers.
- `ReverseDiffScope`: explicit reverse tape handles, input/const/add/mul, backward gradients.
- `PolynomialQuotientContext`: first-class quotient-ring elements carrying bounded reduced
  polynomial storage and generated operation witnesses.
- `PolynomialQuotientScope`: quotient elements with explicit modulus setup and reduction.
- `RationalFunctionContext`: first-class rational-function values as generated numerator /
  denominator polynomial storage with normalization operations.
- `RationalFunctionScope`: numerator/denominator handles with explicit normalization.
- `FieldExtensionContext`: first-class field-extension elements backed by bounded polynomial
  quotient storage.
- `FieldExtensionScope`: polynomial quotient fields with defining-polynomial setup.
- `PadicContext`: first-class truncated p-adic values and generated operation witnesses.
- `PadicScope`: truncated p-adic handles with arithmetic and inversion.
- `WittVectorContext`: first-class truncated p-typical Witt vector values and generated
  operation witnesses.
- `WittVectorScope`: truncated p-typical Witt vector handles with arithmetic.

The current smoke program also exercises these scope-first APIs under the AOT smoke
project, including generated polynomial, matrix, reverse-diff, quotient, rational-function,
finite-field extension, bounded-rational number-field, p-adic, and Witt vector context
paths.

Generated scope and reverse-diff context examples now return through inline-storage
`Result` structs and `Run(ref Result)`. Normal callers do not provide output spans or
handwritten `stackalloc` buffers. Explicit spans remain part of kernels, generated
internals, nested low-level scopes, and advanced performance tests.

## Engine Material To Keep

Keep these as v2 implementation substrate:

- Core contracts, static witnesses, status contracts, and `AlgebraStatus`.
- Finite views/builders/kernels.
- Dense and sparse polynomial views/builders/kernels.
- Rational function kernels.
- Polynomial quotient and irreducibility kernels.
- Exact numeric kernels: modular integers, small rationals, p-adics, Witt vectors.
- Dense linear algebra kernels.
- Explicit reverse-mode autodiff tape kernels.
- Kernel-focused tests and AOT smoke logic.

These pieces match the v2 rule: callers own storage, kernels return status, and hot paths
avoid GC allocation.

`HPD.Math.Managed` and `HPD.Math.Text` are separate optional packages. They are allowed to
host heap-backed convenience wrappers, parsing, and formatting without leaking those
concerns into hot-path kernels.

## Compatibility Surface To Break

The following old attributes were facade-first and are no longer part of the v2 public API:

- `PolynomialRingAttribute`
- `MultivariatePolynomialRingAttribute`
- `RationalFunctionFieldAttribute`
- `PolynomialQuotientRingAttribute`
- `FiniteFieldExtensionAttribute`
- `NumberFieldAttribute`
- `PadicRingAttribute`
- `WittVectorRingAttribute`
- `VectorSpaceAttribute`
- `SquareMatrixSpaceAttribute`

The generator no longer discovers those attribute names, and the smoke project no longer
declares facade-generated types from them.

## Tests Replaced

`GeneratedMathTests.cs` no longer contains facade-oriented tests such as:

- `GeneratedPolynomialRingFacade_UsesKernelWithoutRawOps`
- `GeneratedMultivariatePolynomialRingFacade_UsesKernelWithoutRawOps`
- `GeneratedRationalFunctionFieldFacade_NormalizesWithExplicitWorkspace`
- `GeneratedPolynomialQuotientRingFacade_ReducesAndInvertsWithExplicitModulus`
- `GeneratedFiniteFieldExtensionFacade_UsesDefiningPolynomialDegree`
- `GeneratedNumberFieldFacade_ReducesOverRational32`
- `GeneratedPadicRingFacade_UsesStaticPrimeAndPrecision`
- `GeneratedWittVectorFacade_UsesStaticPrimeAndLength`
- `GeneratedVectorSpaceFacade_UsesStaticDimension`
- `GeneratedSquareMatrixFacade_UsesStaticDimension`

The file now covers only primitive generator outputs that v2 still wants: static numeric
witnesses. Raw mathematical behavior is covered by dedicated kernel tests and scope-first
DX is covered by scope tests.

## Current Break Status

Done:

- Removed old facade attributes from `HPD.Math.Core`.
- Removed old facade model properties from the active generator model.
- Removed old facade discovery and emission from the active generator path.
- Removed unreachable old donor generator methods and model classes from `MathGenerator.cs`.
- Removed old facade tests.
- Removed old facade declarations and checks from the AOT smoke program.
- Removed `RingOpsAttribute` and the generated non-status `IRingOps<T>` path because it
  could throw from checked arithmetic. V2 operation witnesses should be explicit,
  status-first, or generated as part of a scope/context with status semantics.
- Added `test/aot-gate.sh`, which runs the test project, publishes the AOT smoke executable
  with Native AOT, and executes the published native binary.
- Defined the future operation-witness generation policy in `V2_PATTERN.md`: generated
  witnesses must be status-first for partial or bounded arithmetic, and convenient
  operators belong on scope-local handles where failures can be recorded in scope status.
- Added a boundary check to `test/aot-gate.sh` so hot-path projects cannot reference
  `HPD.Math.Managed` or `HPD.Math.Text`.
- Added generated mathematical context slices for `FinitePowerSetContextAttribute`,
  `PolynomialContextAttribute`, `MatrixContextAttribute`, `ReverseDiffContextAttribute`,
  `PolynomialQuotientContextAttribute`, `RationalFunctionContextAttribute`,
  `FieldExtensionContextAttribute`, `PadicContextAttribute`, and
  `WittVectorContextAttribute`. They emit named mathematical universes with generated
  inline values and operation witnesses when values are bounded by the context. Reverse
  autodiff emits a hidden-frame runner because the bounded object is the tape/program, not
  a scalar value.
- Moved generated handle operator syntax to C# 14 extension operators in top-level static
  extension classes. Handles expose small bridge methods such as `Add`, `Sub`, `Mul`,
  and `Neg`, keeping allocation/failure semantics inside the scope while the symbolic
  syntax lives in the extension layer.
- Removed throw-based status adapters from status sparse polynomial kernels and generated
  polynomial authoring. Finite support and sparse polynomial builders now expose
  status-aware canonical validation and append paths.
- Removed `Rational32FieldOps` because bounded rational arithmetic cannot be a lawful
  non-status field witness. `Rational32StatusFieldOps` remains the correct coefficient
  witness for bounded exact rational arithmetic.
- Broke `Padic32Ops<P,N>` into a status-first operation witness instead of a throwing
  non-status ring witness. Generated p-adic handles still provide clean operator DX while
  recording failures in scope status.
- Added status-aware polynomial quotient reduction/add/multiply kernels. Generated
  quotient and field-extension scopes now select these kernels when the coefficient
  witness implements `IStatusFieldOps<T>`, so `Rational32StatusFieldOps` can power
  bounded-rational number-field scopes without a throw-based `IFieldOps<T>` bridge.
- Broke public scope extraction overloads that required output spans. Generated scopes now
  emit inline-storage `Result` structs and `Run(ref Result)`. Generated bounded contexts
  now expose first-class inline values as the ordinary path; kernels and nested low-level
  internals still expose caller-owned span APIs where explicit storage is the point.
- Broke `ReverseDiffContext` away from the old `CreateScope(...)`-as-main-DX model. It now
  emits `Run(ref Result)` with hidden stack allocation, matching reverse autodiff's
  tape-first nature while keeping user code allocation-free and buffer-free.

Still to do:

- Keep applying the managed/text boundary as those packages grow beyond marker projects.

## Required Breaks Before Calling V2 Done

No known compatibility breaks remain in the current v2 public/generator surface.

## Completion Standard

V2 is done when the normal user experience looks like this:

```csharp
partial void Build(ref Scope q)
{
    var x = q.Variable();
    var p = x * x + q.Const(3) * x + q.Const(1);

    q.Return(p.Derivative);
}
```

with extraction like this:

```csharp
var result = default(Example.Result);
var status = new Example().Run(ref result);
```

and not like this:

```csharp
Zx.TryMul(left, right, ref destination, workspaceDegrees, workspaceCoefficients);
```

or like this for normal scope callers:

```csharp
new Example().Run(outputDegrees, outputCoefficients, out var termCount);
```

The second form can remain internally valuable, but it must not define the v2 public
authoring experience.
