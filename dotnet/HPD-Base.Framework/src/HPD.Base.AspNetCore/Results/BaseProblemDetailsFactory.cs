using HPD.Base;
using Microsoft.AspNetCore.Mvc;

namespace HPD.Base.AspNetCore;

internal sealed class BaseProblemDetailsFactory
{
    public ProblemDetails Create(
        OperationStatus status,
        BaseError? error,
        OperationWarning[]? warnings,
        OperationDiagnostics? diagnostics,
        string path,
        bool includeDiagnostics)
    {
        var httpStatus = BaseHttpStatusCodeMapper.ToStatusCode(status);
        var problem = new ProblemDetails
        {
            Status = httpStatus,
            Title = Title(status),
            Type = Type(status),
            Detail = error?.Message,
            Instance = path
        };

        problem.Extensions["hpd.status"] = ToLowerCamel(status.ToString());
        if (error is not null)
        {
            problem.Extensions["hpd.error.code"] = error.Code;
            problem.Extensions["hpd.error.category"] = ToLowerCamel(error.Category.ToString());
            if (error.Target is not null)
                problem.Extensions["hpd.error.target"] = error.Target;
            if (error.CorrelationId is not null)
                problem.Extensions["hpd.error.correlationId"] = error.CorrelationId;
            if (error.Validation is not null)
                problem.Extensions["hpd.validation"] = error.Validation;
            if (error.Conflict is not null)
                problem.Extensions["hpd.conflict"] = error.Conflict;
            if (error.Capability is not null)
                problem.Extensions["hpd.capability"] = error.Capability;
            if (error.Policy is not null)
                problem.Extensions["hpd.policy"] = error.Policy;
            if (error.Store is not null)
                problem.Extensions["hpd.store"] = error.Store;
        }

        if (warnings is { Length: > 0 })
            problem.Extensions["hpd.warnings"] = warnings;

        if (includeDiagnostics && diagnostics?.SafeData is not null)
            problem.Extensions["hpd.diagnostics"] = diagnostics.SafeData;

        return problem;
    }

    private static string Title(OperationStatus status) =>
        status switch
        {
            OperationStatus.NotFound => "Not found",
            OperationStatus.Conflict => "Conflict",
            OperationStatus.ValidationFailed => "Validation failed",
            OperationStatus.PolicyDenied => "Policy denied",
            OperationStatus.Unauthorized => "Unauthorized",
            OperationStatus.Unsupported => "Unsupported operation",
            OperationStatus.CapabilityUnavailable => "Capability unavailable",
            OperationStatus.StoreError => "Store error",
            _ => "BASE operation failed"
        };

    private static string Type(OperationStatus status) =>
        status switch
        {
            OperationStatus.ValidationFailed => "urn:hpd:base:error:validation",
            OperationStatus.NotFound => "urn:hpd:base:error:not-found",
            OperationStatus.Conflict => "urn:hpd:base:error:conflict",
            OperationStatus.PolicyDenied => "urn:hpd:base:error:policy-denied",
            OperationStatus.Unauthorized => "urn:hpd:base:error:unauthorized",
            OperationStatus.Unsupported => "urn:hpd:base:error:unsupported",
            OperationStatus.CapabilityUnavailable => "urn:hpd:base:error:capability-unavailable",
            OperationStatus.StoreError => "urn:hpd:base:error:store",
            _ => "urn:hpd:base:error:operation"
        };

    private static string ToLowerCamel(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToLowerInvariant(value[0]) + value[1..];
}
