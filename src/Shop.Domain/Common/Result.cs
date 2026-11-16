using System.Diagnostics.CodeAnalysis;

namespace Shop.Domain.Common;

/// <summary>
/// Résultat d'une opération métier : succès, ou échec porteur d'une <see cref="DomainError"/>.
/// Les cas métier attendus ne lèvent pas d'exception (constitution §5).
/// </summary>
public class Result
{
    protected Result(DomainError? error) => Error = error;

    public DomainError? Error { get; }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    public static Result Success() => new(null);

    public static Result Failure(DomainError error) => new(error);

    public static implicit operator Result(DomainError error) => Failure(error);
}

/// <summary>Résultat porteur d'une valeur en cas de succès.</summary>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T value)
        : base(null) => _value = value;

    private Result(DomainError error)
        : base(error)
    {
    }

    /// <summary>Valeur du succès. Lève une exception si le résultat est un échec (erreur de programmation).</summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Accès à la valeur d'un résultat en échec ({Error.Code}).");

    public static implicit operator Result<T>(T value) => new(value);

    public static implicit operator Result<T>(DomainError error) => new(error);

#pragma warning disable CA1000 // Fabriques statiques volontaires sur le type générique, pour la lisibilité des appels.
    public static Result<T> Ok(T value) => new(value);

    public static Result<T> Fail(DomainError error) => new(error);
#pragma warning restore CA1000
}
