namespace SiloSupport.Application.Dtos;

/// <summary>Outcome of an Application use case that can fail for business/validation reasons.</summary>
public class OperationResult
{
    protected OperationResult(bool succeeded, IReadOnlyList<string> errors)
    {
        Succeeded = succeeded;
        Errors = errors;
    }

    public bool Succeeded { get; }

    public IReadOnlyList<string> Errors { get; }

    public string? FirstError => Errors.Count > 0 ? Errors[0] : null;

    public static OperationResult Success() => new(true, Array.Empty<string>());

    public static OperationResult Failure(params string[] errors) => new(false, errors);

    public static OperationResult Failure(IEnumerable<string> errors) => new(false, errors.ToArray());
}

/// <summary>Outcome of a use case that returns a value on success.</summary>
public sealed class OperationResult<T> : OperationResult
{
    private OperationResult(bool succeeded, T? value, IReadOnlyList<string> errors) : base(succeeded, errors)
    {
        Value = value;
    }

    public T? Value { get; }

    public static OperationResult<T> Success(T value) => new(true, value, Array.Empty<string>());

    public static new OperationResult<T> Failure(params string[] errors) => new(false, default, errors);

    public static new OperationResult<T> Failure(IEnumerable<string> errors) => new(false, default, errors.ToArray());
}
