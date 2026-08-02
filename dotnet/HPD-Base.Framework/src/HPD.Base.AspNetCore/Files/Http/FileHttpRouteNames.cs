namespace HPD.Base.AspNetCore;

/// <summary>Represents a file HTTP route names.</summary>
public static class FileHttpRouteNames
{
    /// <summary>Provides the upload value.</summary>
    public const string Upload = "base.files.objects.upload";
    /// <summary>Provides the download value.</summary>
    public const string Download = "base.files.objects.download";
    /// <summary>Provides the head value.</summary>
    public const string Head = "base.files.objects.head";
    /// <summary>Provides the metadata get value.</summary>
    public const string MetadataGet = "base.files.objects.metadata.get";
    /// <summary>Provides the delete value.</summary>
    public const string Delete = "base.files.objects.delete";
    /// <summary>Provides the list value.</summary>
    public const string List = "base.files.objects.list";
    /// <summary>Provides the access create value.</summary>
    public const string AccessCreate = "base.files.objects.access.create";
}

/// <summary>Represents a file HTTP headers.</summary>
public static class FileHttpHeaders
{
    /// <summary>Provides the object key value.</summary>
    public const string ObjectKey = "X-HPD-File-Key";
    /// <summary>Provides the object name value.</summary>
    public const string ObjectName = "X-HPD-File-Name";
    /// <summary>Provides the checksum value.</summary>
    public const string Checksum = "X-HPD-File-Checksum";
}
