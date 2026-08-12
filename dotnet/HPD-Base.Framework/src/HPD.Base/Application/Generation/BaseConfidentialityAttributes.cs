namespace HPD.Base;

/// <summary>Declares a generated field's confidentiality class.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class BaseFieldConfidentialityAttribute(BaseFieldConfidentiality confidentiality) : Attribute
{
    /// <summary>Gets the declared class.</summary>
    public BaseFieldConfidentiality Confidentiality { get; } = confidentiality;
}

/// <summary>Declares all seven narrowable generated-field disclosure channels.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class BaseFieldDisclosureAttribute : Attribute
{
    /// <summary>Gets or sets ordinary record disclosure.</summary>
    public BaseRecordDisclosure RecordRead { get; set; }
    /// <summary>Gets or sets event disclosure.</summary>
    public BaseProjectionDisclosure Event { get; set; }
    /// <summary>Gets or sets realtime disclosure.</summary>
    public BaseProjectionDisclosure Realtime { get; set; }
    /// <summary>Gets or sets diagnostic disclosure.</summary>
    public BaseProjectionDisclosure Diagnostic { get; set; }
    /// <summary>Gets or sets administrative-export disclosure.</summary>
    public BaseProjectionDisclosure AdministrativeDataExport { get; set; }
    /// <summary>Gets or sets ordinary-export disclosure.</summary>
    public BaseProjectionDisclosure OrdinaryDataExport { get; set; }
    /// <summary>Gets or sets index disclosure.</summary>
    public BaseIndexDisclosure Indexing { get; set; }
}

/// <summary>Declares one reflection-free generated collection storage requirement.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class BaseCollectionStorageProtectionAttribute(Type declaringType, string propertyName) : Attribute
{
    /// <summary>Gets the declaring type.</summary>
    public Type DeclaringType { get; } = declaringType;
    /// <summary>Gets the static property name.</summary>
    public string PropertyName { get; } = propertyName;
}
