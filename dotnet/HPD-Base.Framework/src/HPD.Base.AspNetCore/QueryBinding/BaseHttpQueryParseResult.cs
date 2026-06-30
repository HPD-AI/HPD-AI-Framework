namespace HPD.Base.AspNetCore.QueryBinding;

internal sealed record BaseHttpQueryParseResult(bool Succeeded, string? ErrorCode = null, string? ErrorMessage = null, string? Target = null);
