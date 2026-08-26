namespace HPD.Base;

/// <summary>Identifies one generated request property and its exact scalar authority.</summary>
/// <typeparam name="TRequest">The installed operation request type.</typeparam>
/// <typeparam name="TValue">The exact property value type.</typeparam>
public sealed class BaseModuleRequestProperty<TRequest, TValue>
{
    internal BaseModuleRequestProperty(BaseModuleDtoScalarAuthority authority) => Authority = authority;
    internal BaseModuleDtoScalarAuthority Authority { get; }
    /// <summary>Gets authority for constants admitted wherever this property value is admitted.</summary>
    public BaseModuleConstantAuthority<TValue> ConstantAuthority => new(Authority.ValueType);
}

/// <summary>Identifies one generated result property and its exact scalar authority.</summary>
/// <typeparam name="TResult">The installed operation result type.</typeparam>
/// <typeparam name="TValue">The exact property value type.</typeparam>
public sealed class BaseModuleResultProperty<TResult, TValue>
{
    internal BaseModuleResultProperty(BaseModuleDtoScalarAuthority authority) => Authority = authority;
    internal BaseModuleDtoScalarAuthority Authority { get; }
    /// <summary>Gets authority for constants admitted wherever this property value is admitted.</summary>
    public BaseModuleConstantAuthority<TValue> ConstantAuthority => new(Authority.ValueType);
}

/// <summary>Identifies one generated collection field and its exact persisted scalar authority.</summary>
/// <typeparam name="TRecord">The exact collection record type.</typeparam>
/// <typeparam name="TValue">The exact persisted field value type.</typeparam>
public sealed class BaseModuleCapturedField<TRecord, TValue>
{
    internal BaseModuleCapturedField(BaseField<TRecord, TValue> field, BaseModuleValueType authority)
    {
        Field = field;
        Authority = BaseModuleValueAuthorityContract.Clone(authority);
    }
    internal BaseField<TRecord, TValue> Field { get; }
    internal BaseModuleValueType Authority { get; }
    /// <summary>Gets authority for constants admitted wherever this field value is admitted.</summary>
    public BaseModuleConstantAuthority<TValue> ConstantAuthority => new(Authority);
}

/// <summary>Contains graph-owned provenance for canonicalizing one constant value.</summary>
/// <typeparam name="TValue">The exact admitted value type.</typeparam>
public sealed class BaseModuleConstantAuthority<TValue>
{
    internal BaseModuleConstantAuthority(BaseModuleValueType valueType) =>
        ValueType = BaseModuleValueAuthorityContract.Clone(valueType);
    internal BaseModuleValueType ValueType { get; }
}

/// <summary>Contains one typed closed module-mutation value expression.</summary>
/// <typeparam name="TValue">The exact expression value type.</typeparam>
public sealed class BaseModuleValue<TValue>
{
    internal BaseModuleValue(BaseModuleValueExpression expression) => Expression = expression;
    internal BaseModuleValueExpression Expression { get; }
    internal BaseModuleValueType Authority => Expression.ResultType
        ?? throw new InvalidOperationException("base.moduleMutation.invalid");
}

/// <summary>Contains one typed persisted-field assignment.</summary>
/// <typeparam name="TRecord">The destination record type.</typeparam>
public sealed class BaseModuleFieldValue<TRecord>
{
    internal BaseModuleFieldValue(BaseModuleObjectPropertyExpression value) => Value = value;
    internal BaseModuleObjectPropertyExpression Value { get; }
}

/// <summary>Contains one typed result-property projection.</summary>
/// <typeparam name="TResult">The result DTO type.</typeparam>
public sealed class BaseModuleResultValue<TResult>
{
    internal BaseModuleResultValue(BaseModuleObjectPropertyExpression value) => Value = value;
    internal BaseModuleObjectPropertyExpression Value { get; }
}

/// <summary>Contains one typed persisted-record object.</summary>
/// <typeparam name="TRecord">The exact destination record type.</typeparam>
public sealed class BaseModuleRecordObject<TRecord>
{
    internal BaseModuleRecordObject(BaseModuleObjectExpression value) => Value = value;
    internal BaseModuleObjectExpression Value { get; }
}

/// <summary>Contains one typed operation-result object.</summary>
/// <typeparam name="TResult">The exact result DTO type.</typeparam>
public sealed class BaseModuleResultObject<TResult>
{
    internal BaseModuleResultObject(BaseModuleObjectExpression value) => Value = value;
    internal BaseModuleObjectExpression Value { get; }
}
