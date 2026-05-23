using System.Reflection;
using System.Reflection.Emit;
using Hast.Transformer.SimpleMemory;
using Helium.Hardware;
using Helium.Hastlayer;
using Helium.Primitives;

namespace Helium.Hastlayer.Tests;

public class HastlayerCompatibilityTests
{
    private static readonly Dictionary<short, OpCode> OneByteOpCodes = BuildOpCodeMap(oneByte: true);
    private static readonly Dictionary<short, OpCode> TwoByteOpCodes = BuildOpCodeMap(oneByte: false);

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
    public void Kernels_AreConcreteNonGenericClasses()
    {
        var offenders = KernelTypes()
            .Where(type => !type.IsClass || type.IsAbstract || type.IsGenericTypeDefinition || type.ContainsGenericParameters)
            .Select(type => type.FullName)
            .OrderBy(name => name)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Kernels_ExposeVirtualExecuteOverSimpleMemory32()
    {
        var offenders = KernelTypes()
            .Where(type =>
            {
                var execute = type.GetMethod(
                    "Execute",
                    BindingFlags.Public | BindingFlags.Instance,
                    binder: null,
                    types: [typeof(SimpleMemory32)],
                    modifiers: null);

                return execute is null || !execute.IsVirtual || execute.IsFinal || execute.ReturnType != typeof(void);
            })
            .Select(type => type.FullName)
            .OrderBy(name => name)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Kernels_ExposeVirtualExecuteOverRealHastlayerSimpleMemory()
    {
        var offenders = KernelTypes()
            .Where(type =>
            {
                var execute = type.GetMethod(
                    "Execute",
                    BindingFlags.Public | BindingFlags.Instance,
                    binder: null,
                    types: [typeof(SimpleMemory)],
                    modifiers: null);

                return execute is null || !execute.IsVirtual || execute.IsFinal || execute.ReturnType != typeof(void);
            })
            .Select(type => type.FullName)
            .OrderBy(name => name)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Kernels_DoNotExposeRawFloatingPointSurface()
    {
        var offenders = KernelTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(method => ContainsRawFloat(method.ReturnType) ||
                                 method.GetParameters().Any(parameter => ContainsRawFloat(parameter.ParameterType)))
                .Select(method => $"{type.FullName}.{method.Name}"))
            .OrderBy(name => name)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void HastlayerAssembly_DoesNotImplementExactAlgebraInterfaces()
    {
        var offenders = typeof(HelloKernel).Assembly.GetExportedTypes()
            .Where(ImplementsExactAlgebraInterface)
            .Select(type => type.FullName)
            .OrderBy(name => name)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Kernels_DoNotExposeHardwareTensorOrBackendTypes()
    {
        var offenders = KernelTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(method => IsHardwareBackendSurface(method.ReturnType) ||
                                 method.GetParameters().Any(parameter => IsHardwareBackendSurface(parameter.ParameterType)))
                .Select(method => $"{type.FullName}.{method.Name}"))
            .OrderBy(name => name)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void FpgaKernelCore_DoesNotReferenceUnsupportedWideIntegerTypes()
    {
        var forbiddenTypes = new[]
        {
            typeof(System.Int128),
            typeof(System.UInt128),
            typeof(Fix64)
        };

        var offenders = KernelCoreMethods()
            .SelectMany(method => ReferencedMembers(method)
                .Where(member => ReferencesForbiddenType(member, forbiddenTypes))
                .Select(member => $"{method.DeclaringType!.FullName}.{method.Name} -> {DescribeMember(member)}"))
            .OrderBy(name => name)
            .ToArray();

        Assert.Empty(offenders);
    }

    private static Type[] KernelTypes() =>
        typeof(HelloKernel).Assembly.GetExportedTypes()
            .Where(type => type.Namespace == "Helium.Hastlayer" && type.Name.EndsWith("Kernel", StringComparison.Ordinal))
            .ToArray();

    private static IEnumerable<MethodInfo> KernelCoreMethods() =>
        KernelTypes().SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(method => method.Name.Contains("Core", StringComparison.Ordinal) ||
                             method.Name.Contains("Mod", StringComparison.Ordinal) ||
                             method.Name.Contains("Raw", StringComparison.Ordinal) ||
                             method.Name.Contains("Mul", StringComparison.Ordinal) ||
                             method.Name.Contains("Abs", StringComparison.Ordinal) ||
                             method.Name.Contains("Discarded", StringComparison.Ordinal)));

    private static bool ContainsRawFloat(Type type)
    {
        if (type == typeof(double) || type == typeof(float))
            return true;

        if (type.IsByRef || type.IsPointer || type.IsArray)
            return ContainsRawFloat(type.GetElementType()!);

        return type.IsGenericType && type.GetGenericArguments().Any(ContainsRawFloat);
    }

    private static bool IsHardwareBackendSurface(Type type)
    {
        var definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
        return definition.FullName is "Helium.Hardware.IHardwareTensor`1" or "Helium.Hardware.IExecutionBackend`1";
    }

    private static bool ImplementsExactAlgebraInterface(Type type) =>
        ExactAlgebraInterfaces.Any(interfaceDefinition => ImplementsGenericInterface(type, interfaceDefinition));

    private static bool ImplementsGenericInterface(Type type, Type interfaceDefinition) =>
        type.GetInterfaces().Any(i =>
            i.IsGenericType && i.GetGenericTypeDefinition() == interfaceDefinition);

    private static Dictionary<short, OpCode> BuildOpCodeMap(bool oneByte)
    {
        var opCodes = typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!);

        return opCodes
            .Where(opCode => oneByte ? opCode.Size == 1 : opCode.Size == 2)
            .ToDictionary(opCode => opCode.Value);
    }

    private static IEnumerable<MemberInfo> ReferencedMembers(MethodInfo method)
    {
        var body = method.GetMethodBody();
        var il = body?.GetILAsByteArray();
        if (il is null)
            yield break;

        var module = method.Module;
        for (var offset = 0; offset < il.Length;)
        {
            var opCode = ReadOpCode(il, ref offset);
            if (!TryReadMetadataToken(il, opCode, ref offset, out var token))
            {
                SkipOperand(il, opCode, ref offset);
                continue;
            }

            MemberInfo? member = null;
            try
            {
                member = module.ResolveMember(token);
            }
            catch (ArgumentException)
            {
            }

            if (member is not null)
                yield return member;
        }
    }

    private static OpCode ReadOpCode(byte[] il, ref int offset)
    {
        var value = il[offset++];
        if (value != 0xFE)
            return OneByteOpCodes[(short)value];

        return TwoByteOpCodes[(short)(0xFE00 | il[offset++])];
    }

    private static bool TryReadMetadataToken(byte[] il, OpCode opCode, ref int offset, out int token)
    {
        if (opCode.OperandType is OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineTok or OperandType.InlineType)
        {
            token = BitConverter.ToInt32(il, offset);
            offset += 4;
            return true;
        }

        token = 0;
        return false;
    }

    private static void SkipOperand(byte[] il, OpCode opCode, ref int offset)
    {
        offset += opCode.OperandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget => 1,
            OperandType.ShortInlineI => 1,
            OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineBrTarget => 4,
            OperandType.InlineI => 4,
            OperandType.InlineR => 8,
            OperandType.InlineI8 => 8,
            OperandType.ShortInlineR => 4,
            OperandType.InlineString => 4,
            OperandType.InlineSig => 4,
            OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, offset)),
            _ => 0
        };
    }

    private static bool ReferencesForbiddenType(MemberInfo member, IReadOnlyCollection<Type> forbiddenTypes)
    {
        if (member is Type type)
            return forbiddenTypes.Contains(type);

        if (member.DeclaringType is not null && forbiddenTypes.Contains(member.DeclaringType))
            return true;

        return member switch
        {
            MethodInfo method => ContainsForbiddenType(method.ReturnType, forbiddenTypes) ||
                                 method.GetParameters().Any(parameter => ContainsForbiddenType(parameter.ParameterType, forbiddenTypes)),
            ConstructorInfo ctor => ctor.GetParameters().Any(parameter => ContainsForbiddenType(parameter.ParameterType, forbiddenTypes)),
            FieldInfo field => ContainsForbiddenType(field.FieldType, forbiddenTypes),
            PropertyInfo property => ContainsForbiddenType(property.PropertyType, forbiddenTypes),
            _ => false
        };
    }

    private static bool ContainsForbiddenType(Type type, IReadOnlyCollection<Type> forbiddenTypes)
    {
        if (forbiddenTypes.Contains(type))
            return true;

        if (type.IsByRef || type.IsPointer || type.IsArray)
            return ContainsForbiddenType(type.GetElementType()!, forbiddenTypes);

        return type.IsGenericType && type.GetGenericArguments().Any(argument => ContainsForbiddenType(argument, forbiddenTypes));
    }

    private static string DescribeMember(MemberInfo member) =>
        $"{member.DeclaringType?.FullName ?? "<module>"}.{member.Name}";
}
