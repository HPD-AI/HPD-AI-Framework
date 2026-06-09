using System;
using HPD.Math.Algebra;
using HPD.Math.Autodiff;
using HPD.Math.Core;
using HPD.Math.Finite;
using HPD.Math.LinearAlgebra;
using HPD.Math.Numerics;

ReadOnlySpan<int> leftDegrees = [0, 1];
ReadOnlySpan<int> leftCoefficients = [1, 2];
ReadOnlySpan<int> rightDegrees = [0, 2];
ReadOnlySpan<int> rightCoefficients = [3, 4];

var left = new SparsePolynomialView<int>(new FinsuppView<int, int>(leftDegrees, leftCoefficients));
var right = new SparsePolynomialView<int>(new FinsuppView<int, int>(rightDegrees, rightCoefficients));

Span<int> destinationDegrees = stackalloc int[4];
Span<int> destinationCoefficients = stackalloc int[4];
Span<int> workspaceDegrees = stackalloc int[4];
Span<int> workspaceCoefficients = stackalloc int[4];

var destination = new SparsePolynomialBuilder<int>(destinationDegrees, destinationCoefficients);
var status = left.TryMul(
    right,
    ref destination,
    workspaceDegrees,
    workspaceCoefficients,
    new CheckedInt32RingOps());

if (status != AlgebraStatus.Ok)
    return (int)status;

var result = destination.AsView();
if (result.TermCount != 4 || result.Degree != 3)
    return 1;

var aotPowersetOps = new AotBoolPowerSet.Ops();
AotBoolPowerSet.TrySingletonIndex(0, out var aotPowersetLeft);
AotBoolPowerSet.TrySingletonIndex(1, out var aotPowersetRight);
var aotPowersetResult = default(AotBoolPowerSet.Set);
aotPowersetOps.Join(ref aotPowersetResult, aotPowersetLeft, aotPowersetRight);
if (!aotPowersetOps.Eq(aotPowersetResult, aotPowersetOps.Top))
    return 1;

aotPowersetOps.Complement(ref aotPowersetResult, aotPowersetLeft);
if (!aotPowersetOps.Eq(aotPowersetResult, aotPowersetRight))
    return 1;

ReadOnlySpan<AotBoolPowerSet.Set> aotPowersets = [aotPowersetLeft, aotPowersetRight];
if (aotPowersetOps.TrySupremum(ref aotPowersetResult, aotPowersets) != AlgebraStatus.Ok ||
    !aotPowersetOps.Eq(aotPowersetResult, aotPowersetOps.Top))
    return 1;

var aotHomStatus = OrderHomomorphismKernels.TryValidateMonotone<bool, bool, AotIdentityBoolHomOps, BoolAlgebraOps, BoolAlgebraOps, BoolAlgebraOps>(
    new AotIdentityBoolHomOps(),
    new BoolAlgebraOps(),
    new BoolAlgebraOps(),
    new BoolAlgebraOps());
if (aotHomStatus != AlgebraStatus.Ok)
    return (int)aotHomStatus;

var aotLargePowersetOps = new AotLargePowerSet.Ops();
AotLargePowerSet.TrySingletonIndex(0, out var aotLargePowersetLow);
AotLargePowerSet.TrySingletonIndex(199, out var aotLargePowersetHigh);
var aotLargePowersetResult = default(AotLargePowerSet.Set);
aotLargePowersetOps.Join(ref aotLargePowersetResult, aotLargePowersetLow, aotLargePowersetHigh);
if (!aotLargePowersetResult.ContainsIndex(199) || !aotLargePowersetResult.ContainsIndex(0))
    return 1;

var v2PolynomialResult = default(AotRationalPolynomialScope.Result);
var v2PolynomialStatus = new AotRationalPolynomialScope().Run(ref v2PolynomialResult);
if (v2PolynomialStatus != AlgebraStatus.Ok ||
    v2PolynomialResult.TermCount != 2 ||
    v2PolynomialResult.DegreeAt(0) != 0 ||
    v2PolynomialResult.CoefficientAt(0) != new Rational32(3, 1) ||
    v2PolynomialResult.DegreeAt(1) != 1 ||
    v2PolynomialResult.CoefficientAt(1) != new Rational32(2, 1))
    return 1;

var v2PolynomialContextOps = default(AotRationalPolynomialContext.Ops);
var v2PolynomialContextStatus = AotRationalPolynomialContext.TryVariable(out var v2PolynomialContextX);
var v2PolynomialContextThree = default(AotRationalPolynomialContext.Poly);
var v2PolynomialContextOne = default(AotRationalPolynomialContext.Poly);
var v2PolynomialContextXSquared = default(AotRationalPolynomialContext.Poly);
var v2PolynomialContextThreeX = default(AotRationalPolynomialContext.Poly);
var v2PolynomialContextPartial = default(AotRationalPolynomialContext.Poly);
var v2PolynomialContextP = default(AotRationalPolynomialContext.Poly);
var v2PolynomialContextDerivative = default(AotRationalPolynomialContext.Poly);
if (v2PolynomialContextStatus == AlgebraStatus.Ok)
    v2PolynomialContextStatus = AotRationalPolynomialContext.TryConst(3, out v2PolynomialContextThree);
if (v2PolynomialContextStatus == AlgebraStatus.Ok)
    v2PolynomialContextStatus = AotRationalPolynomialContext.TryConst(1, out v2PolynomialContextOne);
if (v2PolynomialContextStatus == AlgebraStatus.Ok)
    v2PolynomialContextStatus = v2PolynomialContextOps.TryMul(v2PolynomialContextX, v2PolynomialContextX, out v2PolynomialContextXSquared);
if (v2PolynomialContextStatus == AlgebraStatus.Ok)
    v2PolynomialContextStatus = v2PolynomialContextOps.TryMul(v2PolynomialContextThree, v2PolynomialContextX, out v2PolynomialContextThreeX);
if (v2PolynomialContextStatus == AlgebraStatus.Ok)
    v2PolynomialContextStatus = v2PolynomialContextOps.TryAdd(v2PolynomialContextXSquared, v2PolynomialContextThreeX, out v2PolynomialContextPartial);
if (v2PolynomialContextStatus == AlgebraStatus.Ok)
    v2PolynomialContextStatus = v2PolynomialContextOps.TryAdd(v2PolynomialContextPartial, v2PolynomialContextOne, out v2PolynomialContextP);
if (v2PolynomialContextStatus == AlgebraStatus.Ok)
    v2PolynomialContextStatus = v2PolynomialContextOps.TryDerivative(v2PolynomialContextP, out v2PolynomialContextDerivative);
if (v2PolynomialContextStatus != AlgebraStatus.Ok ||
    v2PolynomialContextDerivative.CoefficientCount != 2 ||
    v2PolynomialContextDerivative.Degree != 1 ||
    v2PolynomialContextDerivative.CoefficientAt(0) != new Rational32(3, 1) ||
    v2PolynomialContextDerivative.CoefficientAt(1) != new Rational32(2, 1))
    return 1;

var v2SparsePolynomialContextOps = default(AotRationalSparsePolynomialContext.Ops);
var v2SparsePolynomialContextStatus = AotRationalSparsePolynomialContext.TryVariable(out var v2SparsePolynomialContextX);
var v2SparsePolynomialContextThree = default(AotRationalSparsePolynomialContext.Poly);
var v2SparsePolynomialContextXSquared = default(AotRationalSparsePolynomialContext.Poly);
var v2SparsePolynomialContextThreeX = default(AotRationalSparsePolynomialContext.Poly);
var v2SparsePolynomialContextP = default(AotRationalSparsePolynomialContext.Poly);
var v2SparsePolynomialContextDerivative = default(AotRationalSparsePolynomialContext.Poly);
if (v2SparsePolynomialContextStatus == AlgebraStatus.Ok)
    v2SparsePolynomialContextStatus = AotRationalSparsePolynomialContext.TryConst(3, out v2SparsePolynomialContextThree);
if (v2SparsePolynomialContextStatus == AlgebraStatus.Ok)
    v2SparsePolynomialContextStatus = v2SparsePolynomialContextOps.TryMul(v2SparsePolynomialContextX, v2SparsePolynomialContextX, out v2SparsePolynomialContextXSquared);
if (v2SparsePolynomialContextStatus == AlgebraStatus.Ok)
    v2SparsePolynomialContextStatus = v2SparsePolynomialContextOps.TryMul(v2SparsePolynomialContextThree, v2SparsePolynomialContextX, out v2SparsePolynomialContextThreeX);
if (v2SparsePolynomialContextStatus == AlgebraStatus.Ok)
    v2SparsePolynomialContextStatus = v2SparsePolynomialContextOps.TryAdd(v2SparsePolynomialContextXSquared, v2SparsePolynomialContextThreeX, out v2SparsePolynomialContextP);
if (v2SparsePolynomialContextStatus == AlgebraStatus.Ok)
    v2SparsePolynomialContextStatus = v2SparsePolynomialContextOps.TryDerivative(v2SparsePolynomialContextP, out v2SparsePolynomialContextDerivative);
if (v2SparsePolynomialContextStatus != AlgebraStatus.Ok ||
    v2SparsePolynomialContextDerivative.TermCount != 2 ||
    v2SparsePolynomialContextDerivative.DegreeAt(0) != 0 ||
    v2SparsePolynomialContextDerivative.CoefficientAt(0) != new Rational32(3, 1) ||
    v2SparsePolynomialContextDerivative.DegreeAt(1) != 1 ||
    v2SparsePolynomialContextDerivative.CoefficientAt(1) != new Rational32(2, 1))
    return 1;

var v2MatrixResult = default(AotIntMatrixScope.Result);
var v2MatrixStatus = new AotIntMatrixScope().Run(ref v2MatrixResult);
if (v2MatrixStatus != AlgebraStatus.Ok ||
    v2MatrixResult.RowCount != 2 ||
    v2MatrixResult.ColumnCount != 2 ||
    v2MatrixResult[0, 0] != 2 ||
    v2MatrixResult[0, 1] != 2 ||
    v2MatrixResult[1, 0] != 3 ||
    v2MatrixResult[1, 1] != 5)
    return 1;

var v2MatrixContextOps = default(AotIntMatrixContext.Ops);
var v2MatrixContextStatus = AotIntMatrixContext.TryFromValues([1, 2, 3, 4], out var v2MatrixContextA);
var v2MatrixContextI = default(AotIntMatrixContext.Matrix);
var v2MatrixContextSum = default(AotIntMatrixContext.Matrix);
var v2MatrixContextTransposedIdentity = default(AotIntMatrixContext.Matrix);
var v2MatrixContextResult = default(AotIntMatrixContext.Matrix);
if (v2MatrixContextStatus == AlgebraStatus.Ok)
    v2MatrixContextStatus = v2MatrixContextOps.TryIdentity(out v2MatrixContextI);
if (v2MatrixContextStatus == AlgebraStatus.Ok)
    v2MatrixContextStatus = v2MatrixContextOps.TryAdd(v2MatrixContextA, v2MatrixContextI, out v2MatrixContextSum);
if (v2MatrixContextStatus == AlgebraStatus.Ok)
    v2MatrixContextStatus = v2MatrixContextOps.TryTranspose(v2MatrixContextI, out v2MatrixContextTransposedIdentity);
if (v2MatrixContextStatus == AlgebraStatus.Ok)
    v2MatrixContextStatus = v2MatrixContextOps.TryMul(v2MatrixContextSum, v2MatrixContextTransposedIdentity, out v2MatrixContextResult);
if (v2MatrixContextStatus != AlgebraStatus.Ok ||
    v2MatrixContextResult.RowCount != 2 ||
    v2MatrixContextResult.ColumnCount != 2 ||
    v2MatrixContextResult[0, 0] != 2 ||
    v2MatrixContextResult[0, 1] != 2 ||
    v2MatrixContextResult[1, 0] != 3 ||
    v2MatrixContextResult[1, 1] != 5)
    return 1;

var v2DiffResult = default(AotRationalReverseDiffScope.Result);
var v2DiffStatus = new AotRationalReverseDiffScope().Run(ref v2DiffResult);
if (v2DiffStatus != AlgebraStatus.Ok ||
    v2DiffResult.Primal != new Rational32(12, 1) ||
    v2DiffResult.GradientCount != 4 ||
    v2DiffResult.GradientAt(0) != new Rational32(6, 1) ||
    v2DiffResult.GradientAt(1) != new Rational32(2, 1))
    return 1;

var v2DiffContextResult = default(AotRationalReverseDiffContext.Result);
var v2DiffContextStatus = new AotRationalReverseDiffContext().Run(ref v2DiffContextResult);
if (v2DiffContextStatus != AlgebraStatus.Ok ||
    v2DiffContextResult.Primal != new Rational32(12, 1) ||
    v2DiffContextResult.GradientCount != 4 ||
    v2DiffContextResult.GradientAt(0) != new Rational32(6, 1) ||
    v2DiffContextResult.GradientAt(1) != new Rational32(2, 1))
    return 1;

var v2QuotientResult = default(AotMod7PolynomialQuotientScope.Result);
var v2QuotientStatus = new AotMod7PolynomialQuotientScope().Run(ref v2QuotientResult);
if (v2QuotientStatus != AlgebraStatus.Ok ||
    v2QuotientResult.TermCount != 1 ||
    v2QuotientResult.DegreeAt(0) != 0 ||
    v2QuotientResult.CoefficientAt(0) != 6)
    return 1;

var v2QuotientContextStatus = AotMod7PolynomialQuotientContext.TryCreateOps([0, 2], [1, 1], out var v2QuotientContext);
var v2QuotientContextX = default(AotMod7PolynomialQuotientContext.Element);
var v2QuotientContextResult = default(AotMod7PolynomialQuotientContext.Element);
if (v2QuotientContextStatus == AlgebraStatus.Ok)
    v2QuotientContextStatus = v2QuotientContext.TryGenerator(out v2QuotientContextX);
if (v2QuotientContextStatus == AlgebraStatus.Ok)
    v2QuotientContextStatus = v2QuotientContext.TryMul(v2QuotientContextX, v2QuotientContextX, out v2QuotientContextResult);
if (v2QuotientContextStatus != AlgebraStatus.Ok ||
    v2QuotientContextResult.TermCount != 1 ||
    v2QuotientContextResult.DegreeAt(0) != 0 ||
    v2QuotientContextResult.CoefficientAt(0) != 6)
    return 1;

var v2RationalResult = default(AotMod7RationalFunctionScope.Result);
var v2RationalStatus = new AotMod7RationalFunctionScope().Run(ref v2RationalResult);
if (v2RationalStatus != AlgebraStatus.Ok ||
    v2RationalResult.NumeratorTermCount != 2 ||
    v2RationalResult.NumeratorDegreeAt(0) != 0 ||
    v2RationalResult.NumeratorCoefficientAt(0) != 1 ||
    v2RationalResult.NumeratorDegreeAt(1) != 1 ||
    v2RationalResult.NumeratorCoefficientAt(1) != 1 ||
    v2RationalResult.DenominatorTermCount != 1 ||
    v2RationalResult.DenominatorDegreeAt(0) != 0 ||
    v2RationalResult.DenominatorCoefficientAt(0) != 1)
    return 1;

var v2RationalContext = AotMod7RationalFunctionContext.CreateOps();
var v2RationalContextStatus = v2RationalContext.TryFromPolynomials(
    [0, 2],
    [6, 1],
    [0, 1],
    [6, 1],
    out var v2RationalContextValue);
var v2RationalContextResult = default(AotMod7RationalFunctionContext.Value);
if (v2RationalContextStatus == AlgebraStatus.Ok)
    v2RationalContextStatus = v2RationalContext.TryNormalize(v2RationalContextValue, out v2RationalContextResult);
if (v2RationalContextStatus != AlgebraStatus.Ok ||
    v2RationalContextResult.NumeratorTermCount != 2 ||
    v2RationalContextResult.NumeratorDegreeAt(0) != 0 ||
    v2RationalContextResult.NumeratorCoefficientAt(0) != 1 ||
    v2RationalContextResult.NumeratorDegreeAt(1) != 1 ||
    v2RationalContextResult.NumeratorCoefficientAt(1) != 1 ||
    v2RationalContextResult.DenominatorTermCount != 1 ||
    v2RationalContextResult.DenominatorDegreeAt(0) != 0 ||
    v2RationalContextResult.DenominatorCoefficientAt(0) != 1)
    return 1;

var v2FiniteFieldResult = default(AotFiniteFieldScope.Result);
var v2FiniteFieldStatus = new AotFiniteFieldScope().Run(ref v2FiniteFieldResult);
if (v2FiniteFieldStatus != AlgebraStatus.Ok ||
    v2FiniteFieldResult.TermCount != 1 ||
    v2FiniteFieldResult.DegreeAt(0) != 0 ||
    v2FiniteFieldResult.CoefficientAt(0) != new ModInt<P7>(6))
    return 1;

var v2FiniteFieldContextStatus = AotFiniteFieldContext.TryCreateOps(
    [0, 2],
    [new ModInt<P7>(1), new ModInt<P7>(1)],
    out var v2FiniteFieldContext);
var v2FiniteFieldContextAlpha = default(AotFiniteFieldContext.Element);
var v2FiniteFieldContextResult = default(AotFiniteFieldContext.Element);
if (v2FiniteFieldContextStatus == AlgebraStatus.Ok)
    v2FiniteFieldContextStatus = v2FiniteFieldContext.TryGenerator(out v2FiniteFieldContextAlpha);
if (v2FiniteFieldContextStatus == AlgebraStatus.Ok)
    v2FiniteFieldContextStatus = v2FiniteFieldContext.TryMul(v2FiniteFieldContextAlpha, v2FiniteFieldContextAlpha, out v2FiniteFieldContextResult);
if (v2FiniteFieldContextStatus != AlgebraStatus.Ok ||
    v2FiniteFieldContextResult.TermCount != 1 ||
    v2FiniteFieldContextResult.DegreeAt(0) != 0 ||
    v2FiniteFieldContextResult.CoefficientAt(0) != new ModInt<P7>(6))
    return 1;

var v2NumberFieldResult = default(AotNumberFieldScope.Result);
var v2NumberFieldStatus = new AotNumberFieldScope().Run(ref v2NumberFieldResult);
if (v2NumberFieldStatus != AlgebraStatus.Ok ||
    v2NumberFieldResult.TermCount != 1 ||
    v2NumberFieldResult.DegreeAt(0) != 0 ||
    v2NumberFieldResult.CoefficientAt(0) != new Rational32(2, 1))
    return 1;

var v2NumberFieldContextStatus = AotNumberFieldContext.TryCreateOps(
    [0, 2],
    [new Rational32(-2, 1), new Rational32(1, 1)],
    out var v2NumberFieldContext);
var v2NumberFieldContextAlpha = default(AotNumberFieldContext.Element);
var v2NumberFieldContextResult = default(AotNumberFieldContext.Element);
if (v2NumberFieldContextStatus == AlgebraStatus.Ok)
    v2NumberFieldContextStatus = v2NumberFieldContext.TryGenerator(out v2NumberFieldContextAlpha);
if (v2NumberFieldContextStatus == AlgebraStatus.Ok)
    v2NumberFieldContextStatus = v2NumberFieldContext.TryMul(v2NumberFieldContextAlpha, v2NumberFieldContextAlpha, out v2NumberFieldContextResult);
if (v2NumberFieldContextStatus != AlgebraStatus.Ok ||
    v2NumberFieldContextResult.TermCount != 1 ||
    v2NumberFieldContextResult.DegreeAt(0) != 0 ||
    v2NumberFieldContextResult.CoefficientAt(0) != new Rational32(2, 1))
    return 1;

var v2PadicResult = default(AotPadicScope.Result);
var v2PadicStatus = new AotPadicScope().Run(ref v2PadicResult);
if (v2PadicStatus != AlgebraStatus.Ok || v2PadicResult.Residue != 1)
    return 1;

var v2PadicContext = AotPadicContext.CreateOps();
var v2PadicContextStatus = v2PadicContext.TryConst(10, out var v2PadicContextUnit);
var v2PadicContextInverse = default(AotPadicContext.Value);
var v2PadicContextValue = default(AotPadicContext.Value);
if (v2PadicContextStatus == AlgebraStatus.Ok)
    v2PadicContextStatus = v2PadicContext.TryInv(v2PadicContextUnit, out v2PadicContextInverse);
if (v2PadicContextStatus == AlgebraStatus.Ok)
    v2PadicContextStatus = v2PadicContext.TryMul(v2PadicContextInverse, v2PadicContextUnit, out v2PadicContextValue);
if (v2PadicContextStatus != AlgebraStatus.Ok || v2PadicContextValue.Residue != 1)
    return 1;

var v2WittResult = default(AotWittVectorScope.Result);
var v2WittStatus = new AotWittVectorScope().Run(ref v2WittResult);
if (v2WittStatus != AlgebraStatus.Ok ||
    v2WittResult.ComponentCount != 2 ||
    v2WittResult.ComponentAt(0) != 3 ||
    v2WittResult.ComponentAt(1) != 38)
    return 1;

var v2WittContextOps = default(AotWittVectorContext.Ops);
var v2WittContextStatus = AotWittVectorContext.TryFromComponents([1, 2], out var v2WittContextLeft);
var v2WittContextRight = default(AotWittVectorContext.Vector);
var v2WittContextResult = default(AotWittVectorContext.Vector);
if (v2WittContextStatus == AlgebraStatus.Ok)
    v2WittContextStatus = AotWittVectorContext.TryFromComponents([3, 4], out v2WittContextRight);
if (v2WittContextStatus == AlgebraStatus.Ok)
    v2WittContextStatus = v2WittContextOps.TryMul(v2WittContextLeft, v2WittContextRight, out v2WittContextResult);
if (v2WittContextStatus != AlgebraStatus.Ok ||
    v2WittContextResult.ComponentCount != 2 ||
    v2WittContextResult[0] != 3 ||
    v2WittContextResult[1] != 38)
    return 1;

var autodiffOps = new Rational32StatusFieldOps();
Span<ReverseNode<Rational32>> autodiffNodes = stackalloc ReverseNode<Rational32>[8];
Span<Rational32> autodiffGradients = stackalloc Rational32[8];
var autodiffTape = new ReverseTapeBuilder<Rational32>(autodiffNodes);
var autodiffStatus = autodiffTape.TryInput(new Rational32(2, 1), out var autodiffX);
if (autodiffStatus != AlgebraStatus.Ok)
    return (int)autodiffStatus;
autodiffStatus = autodiffTape.TryConstant(new Rational32(3, 1), out var autodiffThree);
if (autodiffStatus != AlgebraStatus.Ok)
    return (int)autodiffStatus;
autodiffStatus = autodiffTape.TryMul(autodiffX, autodiffX, autodiffOps, out var autodiffXSquared);
if (autodiffStatus != AlgebraStatus.Ok)
    return (int)autodiffStatus;
autodiffStatus = autodiffTape.TryMul(autodiffThree, autodiffX, autodiffOps, out var autodiffThreeX);
if (autodiffStatus != AlgebraStatus.Ok)
    return (int)autodiffStatus;
autodiffStatus = autodiffTape.TryAdd(autodiffXSquared, autodiffThreeX, autodiffOps, out var autodiffOutput);
if (autodiffStatus != AlgebraStatus.Ok)
    return (int)autodiffStatus;
autodiffStatus = ReverseTapeKernels.TryBackward(
    autodiffTape.AsView(),
    autodiffOutput.Index,
    autodiffGradients,
    autodiffOps);
if (autodiffStatus != AlgebraStatus.Ok ||
    autodiffOutput.Value != new Rational32(10, 1) ||
    autodiffGradients[autodiffX.Index] != new Rational32(7, 1))
    return 1;

var dualOps = new DualStatusFieldOps<Rational32, Rational32StatusFieldOps>();
var dualX = new Dual<Rational32>(new Rational32(2, 1), Rational32.One);
var dualThree = new Dual<Rational32>(new Rational32(3, 1), Rational32.Zero);
var dualXSquared = dualOps.Zero;
var dualThreeX = dualOps.Zero;
var dualOutput = dualOps.Zero;
var dualStatus = dualOps.TryMul(ref dualXSquared, dualX, dualX);
if (dualStatus != AlgebraStatus.Ok)
    return (int)dualStatus;
dualStatus = dualOps.TryMul(ref dualThreeX, dualThree, dualX);
if (dualStatus != AlgebraStatus.Ok)
    return (int)dualStatus;
dualStatus = dualOps.TryAdd(ref dualOutput, dualXSquared, dualThreeX);
if (dualStatus != AlgebraStatus.Ok ||
    dualOutput.Primal != new Rational32(10, 1) ||
    dualOutput.Tangent != new Rational32(7, 1))
    return 1;

var modOps = new ModIntOps<P7>();
var inverse = ModInt<P7>.Zero;
var inverseStatus = modOps.TryInvert(ref inverse, new ModInt<P7>(4));
if (inverseStatus != AlgebraStatus.Ok || inverse.Value != 2)
    return 1;

var padicOps = new Padic32Ops<P7, N3>();
var padicUnit = new Padic32<P7, N3>(10);
var padicInverse = Padic32<P7, N3>.Zero;
var padicStatus = padicUnit.TryValuation(out var padicValuation);
if (padicStatus != AlgebraStatus.Ok || padicValuation != 0)
    return 1;

padicStatus = padicOps.TryInvert(ref padicInverse, padicUnit);
if (padicStatus != AlgebraStatus.Ok)
    return (int)padicStatus;

var padicProduct = Padic32<P7, N3>.Zero;
padicStatus = padicOps.TryMul(ref padicProduct, padicInverse, padicUnit);
if (padicStatus != AlgebraStatus.Ok)
    return (int)padicStatus;

if (padicProduct.Value != 1)
    return 1;

ReadOnlySpan<int> wittLeftComponents = [1, 2];
ReadOnlySpan<int> wittRightComponents = [3, 4];
var wittLeft = new WittVectorView<int>(wittLeftComponents);
var wittRight = new WittVectorView<int>(wittRightComponents);
Span<int> wittProductComponents = stackalloc int[2];
var wittProduct = new WittVectorBuilder<int>(wittProductComponents);
var checkedOps = new CheckedInt32RingOps();
var wittStatus = WittVectorKernels.TryMul<P2, N2, int, CheckedInt32RingOps, CheckedInt32RingOps>(
    wittLeft,
    wittRight,
    ref wittProduct,
    checkedOps,
    checkedOps);
if (wittStatus != AlgebraStatus.Ok)
    return (int)wittStatus;
if (!wittProduct.WrittenSpan.SequenceEqual([3, 38]))
    return 1;

var wittGhost = 0;
wittStatus = WittVectorKernels.TryGhostComponent<P2, N2, int, CheckedInt32RingOps, CheckedInt32RingOps>(
    wittProduct.AsView(),
    1,
    ref wittGhost,
    checkedOps,
    checkedOps);
if (wittStatus != AlgebraStatus.Ok || wittGhost != 85)
    return 1;

ReadOnlySpan<int> matrixLeftValues = [1, 2, 3, 4];
ReadOnlySpan<int> matrixRightValues = [5, 6, 7, 8];
Span<int> matrixOutputValues = stackalloc int[4];
var matrixLeft = new MatrixView<int>(2, 2, matrixLeftValues);
var matrixRight = new MatrixView<int>(2, 2, matrixRightValues);
var matrixOutput = new MatrixBuilder<int>(matrixOutputValues);
var matrixStatus = MatrixKernels.TryMul(matrixLeft, matrixRight, ref matrixOutput, new CheckedInt32RingOps());
if (matrixStatus != AlgebraStatus.Ok)
    return (int)matrixStatus;

if (!matrixOutput.WrittenSpan.SequenceEqual([19, 22, 43, 50]))
    return 1;

ReadOnlySpan<int> mvLeftExponents = [0, 1, 1, 0];
ReadOnlySpan<int> mvRightExponents = [0, 1, 1, 0];
ReadOnlySpan<int> mvLeftCoefficients = [1, 1];
ReadOnlySpan<int> mvRightCoefficients = [-1, 1];
var mvLeft = new SparseMvPolynomialView<int>(2, mvLeftExponents, mvLeftCoefficients);
var mvRight = new SparseMvPolynomialView<int>(2, mvRightExponents, mvRightCoefficients);
Span<int> mvDestinationExponents = stackalloc int[4];
Span<int> mvDestinationCoefficients = stackalloc int[2];
Span<int> mvWorkspaceExponents = stackalloc int[8];
Span<int> mvWorkspaceCoefficients = stackalloc int[4];
var mvDestination = new SparseMvPolynomialBuilder<int>(2, mvDestinationExponents, mvDestinationCoefficients);
var mvStatus = SparseMvPolynomialKernels.TryMul(
    mvLeft,
    mvRight,
    ref mvDestination,
    mvWorkspaceExponents,
    mvWorkspaceCoefficients,
    new CheckedInt32RingOps(),
    new GradedLexMonomialOrderOps());
if (mvStatus != AlgebraStatus.Ok)
    return (int)mvStatus;

var mvResult = mvDestination.AsView();
if (mvResult.TermCount != 2 ||
    !mvResult.MonomialAt(0).SequenceEqual([0, 2]) ||
    mvResult.CoefficientAt(0) != -1 ||
    !mvResult.MonomialAt(1).SequenceEqual([2, 0]) ||
    mvResult.CoefficientAt(1) != 1)
    return 1;

ReadOnlySpan<int> mvPoint = [2, 3];
var mvValue = 0;
mvStatus = SparseMvPolynomialKernels.TryEvaluate(
    mvResult,
    mvPoint,
    ref mvValue,
    new CheckedInt32RingOps(),
    new GradedLexMonomialOrderOps());
if (mvStatus != AlgebraStatus.Ok || mvValue != -5)
    return 1;

ReadOnlySpan<int> rationalNumeratorDegrees = [0, 2];
ReadOnlySpan<int> rationalNumeratorCoefficients = [6, 1];
ReadOnlySpan<int> rationalDenominatorDegrees = [0, 1];
ReadOnlySpan<int> rationalDenominatorCoefficients = [6, 1];
var rational = new RationalFunctionView<int>(
    new SparsePolynomialView<int>(new FinsuppView<int, int>(rationalNumeratorDegrees, rationalNumeratorCoefficients)),
    new SparsePolynomialView<int>(new FinsuppView<int, int>(rationalDenominatorDegrees, rationalDenominatorCoefficients)));

Span<int> normalizedNumeratorDegrees = stackalloc int[2];
Span<int> normalizedNumeratorCoefficients = stackalloc int[2];
Span<int> normalizedDenominatorDegrees = stackalloc int[1];
Span<int> normalizedDenominatorCoefficients = stackalloc int[1];
var normalized = new RationalFunctionBuilder<int>(
    normalizedNumeratorDegrees,
    normalizedNumeratorCoefficients,
    normalizedDenominatorDegrees,
    normalizedDenominatorCoefficients);

Span<int> gcdDegrees = stackalloc int[2];
Span<int> gcdCoefficients = stackalloc int[2];
Span<int> numeratorRemainderDegrees = stackalloc int[1];
Span<int> numeratorRemainderCoefficients = stackalloc int[1];
Span<int> denominatorRemainderDegrees = stackalloc int[1];
Span<int> denominatorRemainderCoefficients = stackalloc int[1];
Span<int> rationalGcdLeftWorkspace = stackalloc int[3];
Span<int> rationalGcdRightWorkspace = stackalloc int[3];
Span<int> rationalGcdRemainderWorkspace = stackalloc int[3];
Span<int> rationalQuotientWorkspace = stackalloc int[3];
Span<int> rationalRemainderWorkspace = stackalloc int[3];
var rationalWorkspace = new RationalFunctionNormalizationWorkspace<int>(
    gcdDegrees,
    gcdCoefficients,
    numeratorRemainderDegrees,
    numeratorRemainderCoefficients,
    denominatorRemainderDegrees,
    denominatorRemainderCoefficients,
    rationalGcdLeftWorkspace,
    rationalGcdRightWorkspace,
    rationalGcdRemainderWorkspace,
    rationalQuotientWorkspace,
    rationalRemainderWorkspace);

var rationalStatus = RationalFunctionKernels.TryNormalize(
    rational,
    ref normalized,
    rationalWorkspace,
    new Mod7FieldOps());
if (rationalStatus != AlgebraStatus.Ok)
    return (int)rationalStatus;

var normalizedView = normalized.AsView();
if (normalizedView.Numerator.TermCount != 2 ||
    normalizedView.Numerator.CoefficientAt(0) != 1 ||
    normalizedView.Numerator.CoefficientAt(1) != 1 ||
    normalizedView.Denominator.TermCount != 1 ||
    normalizedView.Denominator.CoefficientAt(0) != 1)
    return 1;

ReadOnlySpan<int> fieldDefiningDegrees = [0, 2];
ReadOnlySpan<ModInt<P7>> fieldDefiningCoefficients = [new(1), new(1)];
var fieldDefiningPolynomial = new SparsePolynomialView<ModInt<P7>>(
    new FinsuppView<int, ModInt<P7>>(fieldDefiningDegrees, fieldDefiningCoefficients));
var irreducibleStatus = PolynomialIrreducibilityKernels.TryIsIrreducibleOverFiniteField(
    fieldDefiningPolynomial,
    out var fieldDefiningIsIrreducible,
    new ModIntOps<P7>(),
    new ModIntOps<P7>());
if (irreducibleStatus != AlgebraStatus.Ok || !fieldDefiningIsIrreducible)
    return 1;

return 0;

public readonly struct P7 : IPrimeModulus
{
    public static int Value => 7;
}

[PrimeModulus(2)]
public readonly partial struct P2;

[Dimension(2)]
public readonly partial struct Dim2;

[Precision(2)]
public readonly partial struct N2;

[Precision(3)]
public readonly partial struct N3;

[PolynomialScope(typeof(Rational32), typeof(Rational32StatusFieldOps), Terms = 4, Workspace = 16, Handles = 8)]
public partial struct AotRationalPolynomialScope
{
    partial void Build(ref Scope q)
    {
        var x = q.Variable();
        var p = x * x + q.Const(3) * x + q.Const(1);

        q.Return(p.Derivative);
    }
}

[PolynomialContext(typeof(Rational32), typeof(Rational32StatusFieldOps), Terms = 4, Workspace = 16, Handles = 8)]
public readonly partial struct AotRationalPolynomialContext;

[SparsePolynomialContext(typeof(Rational32), typeof(Rational32StatusFieldOps), Terms = 4)]
public readonly partial struct AotRationalSparsePolynomialContext;

[MatrixScope(typeof(int), typeof(CheckedInt32RingOps), Rows = 2, Columns = 2, Handles = 8)]
public partial struct AotIntMatrixScope
{
    partial void Build(ref Scope m)
    {
        var a = m.FromValues([1, 2, 3, 4]);
        var i = m.Identity();

        m.Return((a + i) * i.Transpose);
    }
}

[MatrixContext(typeof(int), typeof(CheckedInt32RingOps), Rows = 2, Columns = 2, Handles = 8)]
public readonly partial struct AotIntMatrixContext;

[ReverseDiffScope(typeof(Rational32), typeof(Rational32StatusFieldOps), Nodes = 8)]
public partial struct AotRationalReverseDiffScope
{
    partial void Build(ref Scope d)
    {
        var x = d.Input(new Rational32(2, 1));
        var y = d.Input(new Rational32(5, 1));

        d.Return((x * y) + x);
    }
}

[ReverseDiffContext(typeof(Rational32), typeof(Rational32StatusFieldOps), Nodes = 8)]
public readonly partial struct AotRationalReverseDiffContext
{
    partial void Build(ref Scope d)
    {
        var x = d.Input(new Rational32(2, 1));
        var y = d.Input(new Rational32(5, 1));

        d.Return((x * y) + x);
    }
}

[PolynomialQuotientScope(typeof(int), typeof(Mod7FieldOps), Terms = 3, Handles = 8, Workspace = 4)]
public partial struct AotMod7PolynomialQuotientScope
{
    partial void Build(ref Scope q)
    {
        q.SetModulus([0, 2], [1, 1]);

        var x = q.Generator();
        q.Return(x * x);
    }
}

[PolynomialQuotientContext(typeof(int), typeof(Mod7FieldOps), Terms = 3, Handles = 8, Workspace = 4)]
public readonly partial struct AotMod7PolynomialQuotientContext;

[RationalFunctionScope(typeof(int), typeof(Mod7FieldOps), Terms = 3, Handles = 4, Workspace = 4)]
public partial struct AotMod7RationalFunctionScope
{
    partial void Build(ref Scope r)
    {
        var value = r.FromPolynomials(
            [0, 2],
            [6, 1],
            [0, 1],
            [6, 1]);

        r.Return(r.Normalize(value));
    }
}

[RationalFunctionContext(typeof(int), typeof(Mod7FieldOps), Terms = 3, Handles = 4, Workspace = 4)]
public readonly partial struct AotMod7RationalFunctionContext;

[FieldExtensionScope(typeof(ModInt<P7>), typeof(ModIntOps<P7>), Terms = 3, Handles = 8, Workspace = 4)]
public partial struct AotFiniteFieldScope
{
    partial void Build(ref Scope q)
    {
        Span<ModInt<P7>> coefficients = stackalloc ModInt<P7>[2];
        coefficients[0] = new ModInt<P7>(1);
        coefficients[1] = new ModInt<P7>(1);
        q.SetDefiningPolynomial([0, 2], coefficients);

        var alpha = q.Generator();
        q.Return(alpha * alpha);
    }
}

[FieldExtensionContext(typeof(ModInt<P7>), typeof(ModIntOps<P7>), Terms = 3, Handles = 8, Workspace = 4)]
public readonly partial struct AotFiniteFieldContext;

[FieldExtensionScope(typeof(Rational32), typeof(Rational32StatusFieldOps), Terms = 3, Handles = 8, Workspace = 4)]
public partial struct AotNumberFieldScope
{
    partial void Build(ref Scope q)
    {
        Span<Rational32> coefficients = stackalloc Rational32[2];
        coefficients[0] = new Rational32(-2, 1);
        coefficients[1] = new Rational32(1, 1);
        q.SetDefiningPolynomial([0, 2], coefficients);

        var alpha = q.Generator();
        q.Return(alpha * alpha);
    }
}

[FieldExtensionContext(typeof(Rational32), typeof(Rational32StatusFieldOps), Terms = 3, Handles = 8, Workspace = 4)]
public readonly partial struct AotNumberFieldContext;

[PadicScope(typeof(P7), typeof(N3), Handles = 8)]
public partial struct AotPadicScope
{
    partial void Build(ref Scope z)
    {
        var unit = z.Const(10);
        z.Return(unit.Inv * unit);
    }
}

[PadicContext(typeof(P7), typeof(N3), Handles = 8)]
public readonly partial struct AotPadicContext;

[WittVectorScope(typeof(int), typeof(CheckedInt32RingOps), typeof(P2), typeof(N2), Handles = 8)]
public partial struct AotWittVectorScope
{
    partial void Build(ref Scope w)
    {
        var left = w.FromComponents([1, 2]);
        var right = w.FromComponents([3, 4]);

        w.Return(left * right);
    }
}

[WittVectorContext(typeof(int), typeof(CheckedInt32RingOps), typeof(P2), 2)]
public readonly partial struct AotWittVectorContext;

[FinitePowerSetContext(2)]
public readonly partial struct AotBoolPowerSet;

[FinitePowerSetContext(200)]
public readonly partial struct AotLargePowerSet;

public readonly struct AotIdentityBoolHomOps : IOrderHomOps<bool, bool>
{
    public void Apply(ref bool destination, in bool source) => destination = source;
}

public readonly struct Mod7FieldOps : IFieldOps<int>
{
    public int Zero => 0;
    public int One => 1;

    public bool Eq(in int left, in int right) => Mod(left) == Mod(right);

    public void Add(ref int destination, in int left, in int right) =>
        destination = Mod(left + right);

    public void Sub(ref int destination, in int left, in int right) =>
        destination = Mod(left - right);

    public void Mul(ref int destination, in int left, in int right) =>
        destination = Mod(left * right);

    public void Neg(ref int destination, in int value) =>
        destination = Mod(-value);

    public AlgebraStatus TryInvert(ref int destination, in int value)
    {
        var normalized = Mod(value);
        if (normalized == 0)
            return AlgebraStatus.DivisionByZero;

        for (var i = 1; i < 7; i++)
        {
            if (Mod(normalized * i) != 1)
                continue;

            destination = i;
            return AlgebraStatus.Ok;
        }

        return AlgebraStatus.NonInvertible;
    }

    private static int Mod(int value)
    {
        var result = value % 7;
        return result < 0 ? result + 7 : result;
    }
}
