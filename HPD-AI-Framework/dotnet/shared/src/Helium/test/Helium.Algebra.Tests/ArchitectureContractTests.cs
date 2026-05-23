using System.Reflection;
using Helium.Hardware;
using Helium.Primitives;
using Helium.Validated;

namespace Helium.Algebra.Tests;

public class ArchitectureContractTests
{
    private static readonly Type[] ExactAlgebraInterfaces =
    [
        typeof(ISemiring<>),
        typeof(IRing<>),
        typeof(ICommRing<>),
        typeof(IField<>),
        typeof(IGcdDomain<>),
        typeof(IEuclideanDomain<>)
    ];

    [Fact]
    public void Primitives_DoesNotExposeApproximateScalarFields()
    {
        var publicTypeNames = typeof(Integer).Assembly.GetExportedTypes()
            .Select(t => t.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("Double", publicTypeNames);
        Assert.DoesNotContain("Float", publicTypeNames);
    }

    [Fact]
    public void ValidatedTypes_DoNotImplementExactAlgebraInterfaces()
    {
        var offenders = typeof(Interval).Assembly.GetExportedTypes()
            .Where(t => t.Namespace?.StartsWith("Helium.Validated", StringComparison.Ordinal) == true)
            .Where(ImplementsExactAlgebraInterface)
            .Select(t => t.FullName)
            .OrderBy(name => name)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void HardwareApproximateTypes_DoNotImplementExactAlgebraInterfaces()
    {
        var allowedExactHardwareTypes = new HashSet<Type>
        {
            typeof(GoldilocksElement)
        };

        var offenders = typeof(DoubleMatrix).Assembly.GetExportedTypes()
            .Where(t => t.Namespace?.StartsWith("Helium.Hardware", StringComparison.Ordinal) == true)
            .Where(t => !allowedExactHardwareTypes.Contains(t))
            .Where(ImplementsExactAlgebraInterface)
            .Select(t => t.FullName)
            .OrderBy(name => name)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Algebra_DoesNotExposeUnqualifiedPolynomialAlias()
    {
        var publicTypeNames = typeof(SparsePolynomial<>).Assembly.GetExportedTypes()
            .Select(t => t.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("Polynomial", publicTypeNames);
        Assert.DoesNotContain("Polynomial`1", publicTypeNames);
    }

    [Fact]
    public void Algebra_DoesNotExposeLegacyTensorProductArity()
    {
        var publicTypeNames = typeof(TensorProduct<,,>).Assembly.GetExportedTypes()
            .Select(t => t.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("TensorProduct`1", publicTypeNames);
        Assert.Contains("TensorProduct`3", publicTypeNames);
    }

    [Fact]
    public void Algebra_DoesNotExposeFullHahnSeriesOrRuntimePrecisionPadicShapes()
    {
        var publicTypeNames = typeof(SparsePolynomial<>).Assembly.GetExportedTypes()
            .Select(t => t.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("HahnSeries", publicTypeNames);
        Assert.DoesNotContain("HahnSeries`2", publicTypeNames);
        Assert.DoesNotContain("HahnSeries`3", publicTypeNames);
        Assert.DoesNotContain("TruncatedPadic`1", publicTypeNames);
        Assert.DoesNotContain("TruncatedWittVector`2", publicTypeNames);
        Assert.Contains("FiniteSupportSeries`2", publicTypeNames);
        Assert.Contains("TruncatedPadic`2", publicTypeNames);
        Assert.Contains("TruncatedWittVector`3", publicTypeNames);
    }

    [Fact]
    public void Algebra_DoesNotExposeGenericQuotientOrLocalizationMachinery()
    {
        var publicTypeNames = typeof(SparsePolynomial<>).Assembly.GetExportedTypes()
            .Select(t => t.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("QuotientContext", publicTypeNames);
        Assert.DoesNotContain("QuotientContext`1", publicTypeNames);
        Assert.DoesNotContain("QuotientRing", publicTypeNames);
        Assert.DoesNotContain("QuotientRing`1", publicTypeNames);
        Assert.DoesNotContain("Localization", publicTypeNames);
        Assert.DoesNotContain("Localization`1", publicTypeNames);
    }

    [Fact]
    public void SparsePolynomial_StoresCoefficientsByDegreeKey()
    {
        var field = typeof(SparsePolynomial<Integer>).GetField(
            "_coeffs",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);
        Assert.Equal(typeof(Finsupp<Degree, Integer>), field.FieldType);
    }

    [Fact]
    public void ExactAlgebra_DoesNotUseRawIntFinsuppKeys()
    {
        var offenders = typeof(SparsePolynomial<>).Assembly.GetTypes()
            .SelectMany(type => type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Where(field => IsRawBclPrimitiveFinsupp(field.FieldType))
            .Select(field => $"{field.DeclaringType?.FullName}.{field.Name}")
            .OrderBy(name => name)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ExactAlgebra_PublicGenericConstraints_DoNotUseBclComparable()
    {
        var offenders = typeof(SparsePolynomial<>).Assembly.GetExportedTypes()
            .SelectMany(PublicGenericParameterOwners)
            .SelectMany(owner => owner.GenericArguments.Select(argument => (owner.Name, Argument: argument)))
            .Where(item => HasBclComparableConstraint(item.Argument))
            .Select(item => item.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Var_IsRingValuedReverseMode_NotAField()
    {
        Assert.True(ImplementsGenericInterface(typeof(Var<>), typeof(ICommRing<>)));
        Assert.False(ImplementsGenericInterface(typeof(Var<>), typeof(IField<>)));
    }

    [Fact]
    public void HardwareBlas_DoesNotExposeExactMatrixApis()
    {
        var exactMatrixDefinition = typeof(Matrix<>);
        var offenders = typeof(Blas).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.GetParameters().Any(parameter => IsGenericTypeDefinition(parameter.ParameterType, exactMatrixDefinition)) ||
                             IsGenericTypeDefinition(method.ReturnType, exactMatrixDefinition))
            .Select(method => method.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void HardwareTensor_IsBulkTransferOnly()
    {
        var indexedProperties = typeof(IHardwareTensor<>).GetProperties()
            .Where(property => property.GetIndexParameters().Length != 0)
            .Select(property => property.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.Empty(indexedProperties);
    }

    [Fact]
    public void CrossDomainTypes_DoNotExposeImplicitConversions()
    {
        var assemblies = new[]
        {
            typeof(SparsePolynomial<>).Assembly,
            typeof(Interval).Assembly,
            typeof(DoubleMatrix).Assembly
        };

        var offenders = assemblies
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(IsHeliumDomainType)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.IsSpecialName && method.Name == "op_Implicit")
                .Select(method => $"{type.FullName}.{method.Name}"))
            .OrderBy(name => name)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void HardwareAndValidatedConversions_AreNamedApis()
    {
        Assert.Contains(typeof(HardwareConvert).GetMethods(BindingFlags.Public | BindingFlags.Static),
            method => method.Name == nameof(HardwareConvert.ToDoubleMatrix) &&
                      method.ReturnType == typeof(DoubleMatrix) &&
                      method.GetParameters() is [{ ParameterType: var source }] &&
                      IsGenericTypeDefinition(source, typeof(Matrix<>)));

        Assert.Contains(typeof(ValidatedConvert).GetMethods(BindingFlags.Public | BindingFlags.Static),
            method => method.Name == nameof(ValidatedConvert.ToIntervals) &&
                      method.ReturnType == typeof(IntervalMatrix) &&
                      method.GetParameters() is [{ ParameterType: var source }, { ParameterType: var radius }] &&
                      source == typeof(DoubleMatrix) &&
                      radius == typeof(double));
    }

    [Fact]
    public void ExactAlgebra_DoesNotReferenceHardwareOrValidatedAssemblies()
    {
        var forbidden = new HashSet<string>(StringComparer.Ordinal)
        {
            "Helium.Hardware",
            "Helium.Validated"
        };

        var references = typeof(Matrix<>).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null && forbidden.Contains(name))
            .ToArray();

        Assert.Empty(references);
    }

    private static bool ImplementsExactAlgebraInterface(Type type) =>
        ExactAlgebraInterfaces.Any(interfaceDefinition => ImplementsGenericInterface(type, interfaceDefinition));

    private static bool ImplementsGenericInterface(Type type, Type interfaceDefinition) =>
        type.GetInterfaces().Any(i =>
            i.IsGenericType && i.GetGenericTypeDefinition() == interfaceDefinition);

    private static bool IsGenericTypeDefinition(Type type, Type genericTypeDefinition) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == genericTypeDefinition;

    private static bool IsRawBclPrimitiveFinsupp(Type type) =>
        type.IsGenericType &&
        type.GetGenericTypeDefinition() == typeof(Finsupp<,>) &&
        IsRawBclSparseKey(type.GetGenericArguments()[0]);

    private static bool IsRawBclSparseKey(Type type) =>
        type == typeof(int) ||
        type == typeof(long) ||
        type == typeof(uint) ||
        type == typeof(ulong) ||
        type == typeof(short) ||
        type == typeof(ushort) ||
        type == typeof(byte) ||
        type == typeof(sbyte) ||
        type == typeof(string);

    private static IEnumerable<(string Name, Type[] GenericArguments)> PublicGenericParameterOwners(Type type)
    {
        if (type.IsGenericTypeDefinition)
            yield return (type.FullName ?? type.Name, type.GetGenericArguments());

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            if (method.IsGenericMethodDefinition)
                yield return ($"{type.FullName}.{method.Name}", method.GetGenericArguments());
        }
    }

    private static bool HasBclComparableConstraint(Type genericParameter) =>
        genericParameter.GetGenericParameterConstraints()
            .Any(constraint =>
                constraint.IsGenericType &&
                constraint.GetGenericTypeDefinition() == typeof(IComparable<>));

    private static bool IsHeliumDomainType(Type type) =>
        type.Namespace?.StartsWith("Helium.Algebra", StringComparison.Ordinal) == true ||
        type.Namespace?.StartsWith("Helium.Validated", StringComparison.Ordinal) == true ||
        type.Namespace?.StartsWith("Helium.Hardware", StringComparison.Ordinal) == true;
}
