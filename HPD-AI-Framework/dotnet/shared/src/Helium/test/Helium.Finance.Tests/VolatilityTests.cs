using Helium.Finance.Curves;
using Helium.Finance.Volatility;

namespace Helium.Finance.Tests;

public class VolatilityTests
{
    [Fact]
    public void ConstantVolatilityReturnsSameValue()
    {
        var volatility = new ConstantVolatility(0.25);

        AssertClose(0.25, volatility.Value(1.0), 1e-15);
        AssertClose(0.25, volatility.Value(2.0, 100.0), 1e-15);
    }

    [Fact]
    public void VolatilityConstructorsRejectNullInputs()
    {
        Assert.Throws<ArgumentNullException>(() => new BlackVolatilityCurve(null!));
        Assert.Throws<ArgumentNullException>(() => new BlackVarianceCurve(null!));
        Assert.Throws<ArgumentNullException>(() => new BlackVolatilitySurface(null!, [100.0], new double[,] { { 0.20 } }));
        Assert.Throws<ArgumentNullException>(() => new BlackVolatilitySurface([1.0], null!, new double[,] { { 0.20 } }));
        Assert.Throws<ArgumentNullException>(() => new BlackVolatilitySurface([1.0], [100.0], null!));
        Assert.Throws<ArgumentNullException>(() => new BlackVarianceSurface(null!, [100.0], new double[,] { { 0.04 } }));
        Assert.Throws<ArgumentNullException>(() => new BlackVarianceSurface([1.0], null!, new double[,] { { 0.04 } }));
        Assert.Throws<ArgumentNullException>(() => new BlackVarianceSurface([1.0], [100.0], null!));
    }

    [Fact]
    public void SurfacePointRejectsInvalidCoordinates()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SurfacePoint(-0.1, 100.0, 0.20));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SurfacePoint(1.0, double.NaN, 0.20));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SurfacePoint(1.0, 100.0, double.PositiveInfinity));
    }

    [Fact]
    public void BlackVolatilityCurveHitsPillarsAndInterpolates()
    {
        var curve = new BlackVolatilityCurve(
        [
            new CurvePoint(0.5, 0.20),
            new CurvePoint(1.0, 0.24),
            new CurvePoint(2.0, 0.30)
        ]);

        AssertClose(0.24, curve.Volatility(1.0), 1e-15);
        AssertClose(0.27, curve.Volatility(1.5), 1e-15);
    }

    [Fact]
    public void BlackVolatilityCurveComputesStandardDeviation()
    {
        var curve = new BlackVolatilityCurve(
        [
            new CurvePoint(1.0, 0.25),
            new CurvePoint(2.0, 0.25)
        ]);

        AssertClose(0.25 * Math.Sqrt(1.5), curve.StandardDeviation(1.5), 1e-15);
    }

    [Fact]
    public void BlackVolatilityCurveRejectsInvalidQueryTimesBeforeExtrapolation()
    {
        var curve = new BlackVolatilityCurve(
        [
            new CurvePoint(1.0, 0.20),
            new CurvePoint(2.0, 0.30)
        ],
        extrapolationPolicy: ExtrapolationPolicy.Flat);

        Assert.Throws<ArgumentOutOfRangeException>(() => curve.Volatility(-0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => curve.Volatility(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => curve.StandardDeviation(double.PositiveInfinity));
    }

    [Fact]
    public void BlackVolatilityCurveRejectsNegativePillarsAndNonfiniteProjections()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BlackVolatilityCurve(
        [
            new CurvePoint(-1.0, 0.20),
            new CurvePoint(1.0, 0.30)
        ]));

        var extrapolated = new BlackVolatilityCurve(
        [
            new CurvePoint(1.0, 0.0),
            new CurvePoint(2.0, double.MaxValue)
        ],
        extrapolationPolicy: ExtrapolationPolicy.Linear);

        Assert.Throws<ArgumentOutOfRangeException>(() => extrapolated.Volatility(3.0));

        var flat = new BlackVolatilityCurve(
        [
            new CurvePoint(1.0, double.MaxValue)
        ],
        extrapolationPolicy: ExtrapolationPolicy.Flat);

        Assert.Throws<ArgumentOutOfRangeException>(() => flat.StandardDeviation(double.MaxValue));
    }

    [Fact]
    public void BlackVolatilityCurveRejectsUnorderedTimes()
    {
        Assert.Throws<ArgumentException>(() => new BlackVolatilityCurve(
        [
            new CurvePoint(2.0, 0.30),
            new CurvePoint(1.0, 0.20)
        ]));
    }

    [Fact]
    public void VolatilityCurveConstructorsRejectUnsupportedPolicies()
    {
        var points = new[]
        {
            new CurvePoint(0.0, 0.0),
            new CurvePoint(1.0, 0.20)
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => new BlackVolatilityCurve(
            points,
            interpolationPolicy: (InterpolationPolicy)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BlackVolatilityCurve(
            points,
            extrapolationPolicy: (ExtrapolationPolicy)999));

        Assert.Throws<ArgumentOutOfRangeException>(() => new BlackVarianceCurve(
            points,
            interpolationPolicy: (InterpolationPolicy)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BlackVarianceCurve(
            points,
            extrapolationPolicy: (ExtrapolationPolicy)999));
    }

    [Fact]
    public void BlackVarianceCurveConvertsVolatilitiesToVariance()
    {
        var curve = BlackVarianceCurve.FromVolatilities(
        [
            new CurvePoint(1.0, 0.20),
            new CurvePoint(2.0, 0.30)
        ]);

        AssertClose(0.04, curve.Variance(1.0), 1e-15);
        AssertClose(0.18, curve.Variance(2.0), 1e-15);
        AssertClose(0.30, curve.Volatility(2.0), 1e-15);
        AssertClose(Math.Sqrt(0.18), curve.StandardDeviation(2.0), 1e-15);
    }

    [Fact]
    public void BlackVarianceCurveInterpolatesVarianceNotVolatility()
    {
        var curve = BlackVarianceCurve.FromVolatilities(
        [
            new CurvePoint(1.0, 0.20),
            new CurvePoint(2.0, 0.30)
        ]);

        AssertClose(0.11, curve.Variance(1.5), 1e-15);
        AssertClose(Math.Sqrt(0.11 / 1.5), curve.Volatility(1.5), 1e-15);
    }

    [Fact]
    public void BlackVarianceCurveRejectsDecreasingVarianceByDefault()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BlackVarianceCurve.FromVolatilities(
        [
            new CurvePoint(1.0, 0.40),
            new CurvePoint(2.0, 0.20)
        ]));
    }

    [Fact]
    public void BlackVarianceCurveRejectsUnorderedTimes()
    {
        Assert.Throws<ArgumentException>(() => BlackVarianceCurve.FromVolatilities(
        [
            new CurvePoint(2.0, 0.30),
            new CurvePoint(1.0, 0.20)
        ]));
    }

    [Fact]
    public void BlackVarianceCurveComputesForwardVarianceAndVolatility()
    {
        var curve = new BlackVarianceCurve(
        [
            new CurvePoint(0.0, 0.00),
            new CurvePoint(1.0, 0.04),
            new CurvePoint(2.0, 0.13)
        ]);

        AssertClose(0.09, curve.ForwardVariance(1.0, 2.0), 1e-15);
        AssertClose(0.30, curve.ForwardVolatility(1.0, 2.0), 1e-15);
    }

    [Fact]
    public void BlackVarianceCurveFlatTimeExtrapolationPreservesTerminalVolatility()
    {
        var curve = BlackVarianceCurve.FromVolatilities(
        [
            new CurvePoint(1.0, 0.20),
            new CurvePoint(2.0, 0.30)
        ],
        extrapolationPolicy: ExtrapolationPolicy.Flat);

        AssertClose(0.27, curve.Variance(3.0), 1e-15);
        AssertClose(0.30, curve.Volatility(3.0), 1e-15);
    }

    [Fact]
    public void BlackVarianceCurveRejectsNonFiniteForwardInterval()
    {
        var curve = new BlackVarianceCurve(
        [
            new CurvePoint(0.0, 0.00),
            new CurvePoint(1.0, 0.04)
        ]);

        Assert.Throws<ArgumentOutOfRangeException>(() => curve.ForwardVariance(double.NaN, 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => curve.ForwardVolatility(0.0, double.PositiveInfinity));
    }

    [Fact]
    public void BlackVarianceCurveRejectsNonfiniteFlatExtrapolatedVariance()
    {
        var curve = new BlackVarianceCurve(
        [
            new CurvePoint(1.0, double.MaxValue)
        ],
        extrapolationPolicy: ExtrapolationPolicy.Flat);

        Assert.Throws<ArgumentOutOfRangeException>(() => curve.Variance(double.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => curve.StandardDeviation(double.MaxValue));
    }

    [Fact]
    public void BlackVarianceCurveRejectsNonfiniteForwardVolatilityProjection()
    {
        var curve = new BlackVarianceCurve(
        [
            new CurvePoint(0.0, 0.0),
            new CurvePoint(double.Epsilon, double.MaxValue)
        ]);

        Assert.Throws<ArgumentOutOfRangeException>(() => curve.ForwardVolatility(0.0, double.Epsilon));
    }

    [Fact]
    public void BlackVarianceCurveRejectsVolatilityConversionOverflow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BlackVarianceCurve.FromVolatilities(
        [
            new CurvePoint(1.0, double.MaxValue)
        ]));
    }

    [Fact]
    public void BlackVolatilitySurfaceBilinearlyInterpolates()
    {
        var surface = new BlackVolatilitySurface(
            times: [1.0, 2.0],
            strikes: [90.0, 110.0],
            values: new[,]
            {
                { 0.20, 0.24 },
                { 0.30, 0.34 }
            });

        AssertClose(0.27, surface.Volatility(1.5, 100.0), 1e-15);
    }

    [Fact]
    public void BlackVolatilitySurfaceFlatExtrapolationClampsToBoundary()
    {
        var surface = new BlackVolatilitySurface(
            times: [1.0, 2.0],
            strikes: [90.0, 110.0],
            values: new[,]
            {
                { 0.20, 0.24 },
                { 0.30, 0.34 }
            },
            extrapolationPolicy: ExtrapolationPolicy.Flat);

        AssertClose(0.20, surface.Volatility(0.5, 80.0), 1e-15);
        AssertClose(0.34, surface.Volatility(3.0, 120.0), 1e-15);
    }

    [Fact]
    public void BlackVolatilitySurfaceFailsOutsideRangeWhenExtrapolationDisabled()
    {
        var surface = new BlackVolatilitySurface(
            times: [1.0, 2.0],
            strikes: [90.0, 110.0],
            values: new[,]
            {
                { 0.20, 0.24 },
                { 0.30, 0.34 }
            });

        Assert.Throws<ArgumentOutOfRangeException>(() => surface.Volatility(3.0, 100.0));
    }

    [Fact]
    public void BlackVolatilitySurfaceRejectsNegativeAxesAndQueries()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BlackVolatilitySurface(
            times: [-1.0, 1.0],
            strikes: [90.0, 110.0],
            values: new[,]
            {
                { 0.20, 0.24 },
                { 0.30, 0.34 }
            }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BlackVolatilitySurface(
            times: [1.0, 2.0],
            strikes: [-90.0, 110.0],
            values: new[,]
            {
                { 0.20, 0.24 },
                { 0.30, 0.34 }
            }));

        Assert.Throws<ArgumentOutOfRangeException>(() => new BlackVolatilitySurface(
            times: [0.0, 1.0],
            strikes: [0.0, 100.0],
            values: new[,]
            {
                { 0.20, 0.24 },
                { 0.30, 0.34 }
            },
            extrapolationPolicy: ExtrapolationPolicy.Flat));

        var surface = new BlackVolatilitySurface(
            times: [0.0, 1.0],
            strikes: [1.0, 100.0],
            values: new[,]
            {
                { 0.20, 0.24 },
                { 0.30, 0.34 }
            },
            extrapolationPolicy: ExtrapolationPolicy.Flat);

        Assert.Throws<ArgumentOutOfRangeException>(() => surface.Volatility(-0.5, 100.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => surface.Volatility(0.5, -100.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => surface.StandardDeviation(double.NaN, 100.0));
    }

    [Fact]
    public void BlackVolatilitySurfaceRejectsNonfiniteStandardDeviationProjection()
    {
        var surface = new BlackVolatilitySurface(
            times: [1.0],
            strikes: [100.0],
            values: new[,] { { double.MaxValue } },
            extrapolationPolicy: ExtrapolationPolicy.Flat);

        Assert.Throws<ArgumentOutOfRangeException>(() => surface.StandardDeviation(double.MaxValue, 100.0));
    }

    [Fact]
    public void BlackVolatilitySurfaceRejectsUnsupportedExtrapolationPolicies()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BlackVolatilitySurface(
            times: [1.0],
            strikes: [100.0],
            values: new[,] { { 0.20 } },
            extrapolationPolicy: ExtrapolationPolicy.Linear));

        Assert.Throws<ArgumentOutOfRangeException>(() => new BlackVolatilitySurface(
            times: [1.0],
            strikes: [100.0],
            values: new[,] { { 0.20 } },
            extrapolationPolicy: (ExtrapolationPolicy)999));
    }

    [Fact]
    public void BlackVarianceSurfaceConvertsVolatilitiesToVariance()
    {
        var surface = BlackVarianceSurface.FromVolatilities(
            times: [1.0, 2.0],
            strikes: [90.0, 110.0],
            volatilities: new[,]
            {
                { 0.20, 0.30 },
                { 0.25, 0.35 }
            });

        AssertClose(0.04, surface.Variance(1.0, 90.0), 1e-15);
        AssertClose(2.0 * 0.35 * 0.35, surface.Variance(2.0, 110.0), 1e-15);
        AssertClose(0.35, surface.Volatility(2.0, 110.0), 1e-15);
        AssertClose(Math.Sqrt(2.0 * 0.35 * 0.35), surface.StandardDeviation(2.0, 110.0), 1e-15);
    }

    [Fact]
    public void BlackVarianceSurfaceInterpolatesVarianceNotVolatility()
    {
        var surface = BlackVarianceSurface.FromVolatilities(
            times: [1.0, 2.0],
            strikes: [100.0, 110.0],
            volatilities: new[,]
            {
                { 0.20, 0.20 },
                { 0.30, 0.30 }
            });

        AssertClose(0.11, surface.Variance(1.5, 100.0), 1e-15);
        AssertClose(Math.Sqrt(0.11 / 1.5), surface.Volatility(1.5, 100.0), 1e-15);
    }

    [Fact]
    public void BlackVarianceSurfaceComputesForwardVarianceAndVolatility()
    {
        var surface = new BlackVarianceSurface(
            times: [0.0, 1.0, 2.0],
            strikes: [100.0],
            variances: new[,]
            {
                { 0.00 },
                { 0.04 },
                { 0.13 }
            });

        AssertClose(0.09, surface.ForwardVariance(1.0, 2.0, 100.0), 1e-15);
        AssertClose(0.30, surface.ForwardVolatility(1.0, 2.0, 100.0), 1e-15);
    }

    [Fact]
    public void BlackVarianceSurfaceRejectsDecreasingVarianceByDefault()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BlackVarianceSurface.FromVolatilities(
            times: [1.0, 2.0],
            strikes: [100.0],
            volatilities: new[,]
            {
                { 0.40 },
                { 0.20 }
            }));
    }

    [Fact]
    public void BlackVarianceSurfaceFlatTimeExtrapolationPreservesTerminalVolatility()
    {
        var surface = BlackVarianceSurface.FromVolatilities(
            times: [1.0, 2.0],
            strikes: [100.0],
            volatilities: new[,]
            {
                { 0.20 },
                { 0.30 }
            },
            extrapolationPolicy: ExtrapolationPolicy.Flat);

        AssertClose(0.27, surface.Variance(3.0, 100.0), 1e-15);
        AssertClose(0.30, surface.Volatility(3.0, 100.0), 1e-15);
    }

    [Fact]
    public void BlackVarianceSurfaceFlatExtrapolationFromOnlyTimeZeroStaysZero()
    {
        var surface = new BlackVarianceSurface(
            times: [0.0],
            strikes: [100.0],
            variances: new[,] { { 0.0 } },
            extrapolationPolicy: ExtrapolationPolicy.Flat);

        AssertClose(0.0, surface.Variance(1.0, 100.0), 0.0);
        AssertClose(0.0, surface.Volatility(1.0, 100.0), 0.0);
        AssertClose(0.0, surface.StandardDeviation(1.0, 100.0), 0.0);
    }

    [Fact]
    public void BlackVarianceSurfaceRejectsNonfiniteFlatExtrapolatedVariance()
    {
        var surface = new BlackVarianceSurface(
            times: [1.0],
            strikes: [100.0],
            variances: new[,] { { double.MaxValue } },
            extrapolationPolicy: ExtrapolationPolicy.Flat);

        Assert.Throws<ArgumentOutOfRangeException>(() => surface.Variance(double.MaxValue, 100.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => surface.StandardDeviation(double.MaxValue, 100.0));
    }

    [Fact]
    public void BlackVarianceSurfaceRejectsNonfiniteForwardVolatilityProjection()
    {
        var surface = new BlackVarianceSurface(
            times: [0.0, double.Epsilon],
            strikes: [100.0],
            variances: new[,]
            {
                { 0.0 },
                { double.MaxValue }
            });

        Assert.Throws<ArgumentOutOfRangeException>(() => surface.ForwardVolatility(0.0, double.Epsilon, 100.0));
    }

    [Fact]
    public void BlackVarianceSurfaceRejectsVolatilityConversionOverflow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BlackVarianceSurface.FromVolatilities(
            times: [1.0],
            strikes: [100.0],
            volatilities: new[,] { { double.MaxValue } }));
    }

    [Fact]
    public void BlackVarianceSurfaceFailsOutsideRangeWhenExtrapolationDisabled()
    {
        var surface = BlackVarianceSurface.FromVolatilities(
            times: [1.0, 2.0],
            strikes: [90.0, 110.0],
            volatilities: new[,]
            {
                { 0.20, 0.24 },
                { 0.30, 0.34 }
            });

        Assert.Throws<ArgumentOutOfRangeException>(() => surface.Variance(3.0, 100.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => surface.Variance(1.5, 120.0));
    }

    [Fact]
    public void BlackVarianceSurfaceRejectsUnsupportedExtrapolationPolicies()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BlackVarianceSurface(
            times: [1.0],
            strikes: [100.0],
            variances: new[,] { { 0.04 } },
            extrapolationPolicy: ExtrapolationPolicy.Linear));

        Assert.Throws<ArgumentOutOfRangeException>(() => BlackVarianceSurface.FromVolatilities(
            times: [1.0],
            strikes: [100.0],
            volatilities: new[,] { { 0.20 } },
            extrapolationPolicy: (ExtrapolationPolicy)999));
    }

    [Fact]
    public void VolatilitySurfaceValidationAcceptsCleanGrid()
    {
        var result = VolatilitySurfaceValidation.ValidateBlackSurface(
            times: [0.5, 1.0],
            strikes: [90.0, 110.0],
            values: new[,]
            {
                { 0.20, 0.24 },
                { 0.30, 0.34 }
            });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void VolatilitySurfaceValidationReportsMultipleDiagnostics()
    {
        var result = VolatilitySurfaceValidation.ValidateBlackSurface(
            times: [1.0, 1.0, double.NaN],
            strikes: [90.0, 0.0],
            values: new[,]
            {
                { 0.20, -0.24 },
                { double.PositiveInfinity, 0.34 }
            });

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VolatilitySurfaceDiagnosticCode.DimensionMismatch);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VolatilitySurfaceDiagnosticCode.DuplicateOrUnorderedTime);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VolatilitySurfaceDiagnosticCode.NonFiniteTime);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VolatilitySurfaceDiagnosticCode.DuplicateOrUnorderedStrike);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VolatilitySurfaceDiagnosticCode.NonPositiveStrike);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VolatilitySurfaceDiagnosticCode.NegativeVolatility);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VolatilitySurfaceDiagnosticCode.NonFiniteVolatility);
    }

    [Fact]
    public void VolatilitySurfaceValidationRejectsZeroBlackStrike()
    {
        var result = VolatilitySurfaceValidation.ValidateBlackSurface(
            times: [0.5],
            strikes: [0.0],
            values: new[,] { { 0.20 } });

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VolatilitySurfaceDiagnosticCode.NonPositiveStrike);
    }

    [Fact]
    public void VolatilitySurfaceValidationReportsDecreasingTotalVariance()
    {
        var result = VolatilitySurfaceValidation.ValidateBlackSurface(
            times: [1.0, 2.0],
            strikes: [100.0],
            values: new[,]
            {
                { 0.40 },
                { 0.20 }
            });

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VolatilitySurfaceDiagnosticCode.DecreasingTotalVariance);
    }

    [Fact]
    public void VolatilitySurfaceValidationCanAllowDecreasingTotalVarianceForDiagnosticsOnly()
    {
        var result = VolatilitySurfaceValidation.ValidateBlackSurface(
            times: [1.0, 2.0],
            strikes: [100.0],
            values: new[,]
            {
                { 0.40 },
                { 0.20 }
            },
            requireNondecreasingTotalVariance: false);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void VolatilitySurfaceValidationResultSnapshotsDiagnostics()
    {
        var diagnostics = new List<VolatilitySurfaceDiagnostic>
        {
            new(VolatilitySurfaceDiagnosticCode.NonFiniteVolatility, 0, 0, "bad volatility")
        };

        var result = new VolatilitySurfaceValidationResult(diagnostics);
        diagnostics.Clear();

        Assert.False(result.IsValid);
        Assert.Single(result.Diagnostics);
        Assert.Equal(VolatilitySurfaceDiagnosticCode.NonFiniteVolatility, result.Diagnostics[0].Code);
    }

    [Fact]
    public void VolatilitySurfaceValidationResultRejectsMalformedDiagnostics()
    {
        Assert.Throws<ArgumentException>(() => new VolatilitySurfaceValidationResult([default]));
    }

    [Fact]
    public void VolatilitySurfaceDiagnosticRejectsEmptyMessage()
    {
        Assert.Throws<ArgumentException>(() => new VolatilitySurfaceDiagnostic(
            VolatilitySurfaceDiagnosticCode.NonFiniteVolatility,
            0,
            0,
            ""));
    }

    [Fact]
    public void VolatilitySurfaceDiagnosticRejectsInvalidCode()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new VolatilitySurfaceDiagnostic(
            (VolatilitySurfaceDiagnosticCode)999,
            0,
            0,
            "bad code"));
    }

    [Fact]
    public void VolatilitySurfaceDiagnosticRejectsImpossibleIndices()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new VolatilitySurfaceDiagnostic(
            VolatilitySurfaceDiagnosticCode.NonFiniteVolatility,
            -2,
            0,
            "bad time index"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VolatilitySurfaceDiagnostic(
            VolatilitySurfaceDiagnosticCode.NonFiniteVolatility,
            0,
            -2,
            "bad strike index"));
    }

    [Fact]
    public void VolatilitySurfaceValidationCanAllowNegativeStrikesForNormalModels()
    {
        var result = VolatilitySurfaceValidation.ValidateBlackSurface(
            times: [0.5, 1.0],
            strikes: [-1.0, 0.0, 1.0],
            values: new[,]
            {
                { 0.20, 0.21, 0.22 },
                { 0.23, 0.24, 0.25 }
            },
            requireNonnegativeStrikes: false);

        Assert.True(result.IsValid);
    }

    private static void AssertClose(double expected, double actual, double tolerance) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"Expected {expected:R}, actual {actual:R}, tolerance {tolerance:R}.");
}
