namespace Merfit.Application.Common.Models;

/// <summary>Lightweight result type for expected (non-exceptional) failures — validation-adjacent business rule outcomes that a controller maps to a specific HTTP status rather than a 500.</summary>
public class Result
{
    public bool Succeeded { get; }
    public string? Error { get; }
    public string? ErrorCode { get; }

    protected Result(bool succeeded, string? error, string? errorCode)
    {
        Succeeded = succeeded;
        Error = error;
        ErrorCode = errorCode;
    }

    public static Result Success() => new(true, null, null);
    public static Result Failure(string error, string? errorCode = null) => new(false, error, errorCode);
}

public sealed class Result<T> : Result
{
    public T? Value { get; }

    private Result(bool succeeded, T? value, string? error, string? errorCode)
        : base(succeeded, error, errorCode)
    {
        Value = value;
    }

    public static Result<T> Success(T value) => new(true, value, null, null);
    public static new Result<T> Failure(string error, string? errorCode = null) => new(false, default, error, errorCode);
}
