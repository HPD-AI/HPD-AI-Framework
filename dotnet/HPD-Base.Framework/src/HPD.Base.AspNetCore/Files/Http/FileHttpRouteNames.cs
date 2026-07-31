namespace HPD.Base.AspNetCore;

public static class FileHttpRouteNames
{
    public const string Upload = "base.files.objects.upload";
    public const string Download = "base.files.objects.download";
    public const string Head = "base.files.objects.head";
    public const string MetadataGet = "base.files.objects.metadata.get";
    public const string Delete = "base.files.objects.delete";
    public const string List = "base.files.objects.list";
    public const string AccessCreate = "base.files.objects.access.create";
}

public static class FileHttpHeaders
{
    public const string ObjectKey = "X-HPD-File-Key";
    public const string ObjectName = "X-HPD-File-Name";
    public const string Checksum = "X-HPD-File-Checksum";
}
