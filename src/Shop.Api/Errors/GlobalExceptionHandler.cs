using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Shop.Application.Common;
using Shop.Domain.Common;
using Shop.Infrastructure.Persistence;

namespace Shop.Api.Errors;

/// <summary>
/// Dernier rempart pour les exceptions non prévues (PLT-3, CART-R12) :
/// panne transitoire de la base → 503 ; requête mal formée → 400 ; tout le reste → 500.
/// Le détail réel est journalisé, jamais renvoyé au client.
/// </summary>
internal sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (code, detail, statusOverride) = exception switch
        {
            // Le statut porté par l'exception est conservé (413 corps trop volumineux, 408, 431…).
            BadHttpRequestException badRequest => (ErrorCodes.ValidationFailed, "Le corps ou les paramètres de la requête sont invalides ou trop volumineux.", (int?)badRequest.StatusCode),
            _ when DatabaseFailures.IsTransient(exception) =>
                (ErrorCodes.ServiceUnavailable, "La base de données est momentanément indisponible. Aucune modification n'a été enregistrée ; réessayez plus tard.", null),
            _ => (ErrorCodes.InternalError, "Une erreur inattendue est survenue. Aucune information interne n'est communiquée.", null),
        };

        if (code == ErrorCodes.ValidationFailed)
        {
            LogBadRequest(logger, exception.Message);
        }
        else
        {
            LogUnhandled(logger, exception, httpContext.Request.Method, httpContext.Request.Path);
        }

        var (mappedStatus, title) = ProblemMapping.Describe(code);
        var status = statusOverride ?? mappedStatus;
        httpContext.Response.StatusCode = status;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Type = ProblemMapping.TypeFor(code),
                Detail = detail,
                Extensions = { ["code"] = code },
            },
        });
    }

    [LoggerMessage(EventId = 9000, EventName = nameof(LogEvents.UnhandledException), Level = LogLevel.Error,
        Message = "Exception non gérée lors du traitement de {Method} {Path}.")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string method, string path);

    [LoggerMessage(EventId = 9001, Level = LogLevel.Information, Message = "Requête mal formée rejetée : {Reason}")]
    private static partial void LogBadRequest(ILogger logger, string reason);
}
