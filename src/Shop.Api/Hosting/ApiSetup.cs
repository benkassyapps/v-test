using System.Diagnostics;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Scalar.AspNetCore;
using Shop.Api.Auth;
using Shop.Api.Endpoints;
using Shop.Api.Errors;
using Shop.Application.Common;
using Shop.Domain.Common;

namespace Shop.Api.Hosting;

/// <summary>Enregistrement des services et construction du pipeline HTTP (specs/platform/design.md §3).</summary>
public static class ApiSetup
{
    public const string InstanceHeader = "X-Instance";

    public static IServiceCollection AddShopApi(this IServiceCollection services, IConfiguration configuration)
    {
        // Lecture différée : la configuration finale (dont celle des tests) n'est complète qu'à la construction de l'hôte.
        services.AddSingleton(provider => new InstanceIdentity(
            provider.GetRequiredService<IConfiguration>()["Instance:Id"] is { Length: > 0 } id ? id : Environment.MachineName));
        services.AddSingleton(TimeProvider.System);

        services.AddProblemDetails(options => options.CustomizeProblemDetails = Customize);
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddValidation();

        services.AddAuthentication(UserIdAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, UserIdAuthenticationHandler>(UserIdAuthenticationHandler.SchemeName, null);
        services.AddAuthorization();
        services.Configure<AdminOptions>(configuration.GetSection(AdminOptions.SectionName));

        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.AddOpenApi();

        return services;
    }

    public static WebApplication UseShopApi(this WebApplication app)
    {
        var instance = app.Services.GetRequiredService<InstanceIdentity>();
        app.Use((context, next) =>
        {
            // OnStarting et non affectation immédiate : le gestionnaire d'exceptions vide les en-têtes
            // avant d'écrire la réponse d'erreur, qui doit elle aussi indiquer l'instance (PLT-1).
            context.Response.OnStarting(() =>
            {
                context.Response.Headers[InstanceHeader] = instance.Id;
                return Task.CompletedTask;
            });
            return next(context);
        });

        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseAuthentication();
        app.UseAuthorization();

        var docsEnabled = app.Configuration.GetValue<bool?>("Api:EnableDocs") ?? app.Environment.IsDevelopment();
        if (docsEnabled)
        {
            app.MapOpenApi();
            app.MapScalarApiReference();
        }

        app.MapHealthChecks("/health");
        app.MapCatalogEndpoints();

        return app;
    }

    /// <summary>
    /// Complète toute réponse ProblemDetails (métier, validation, routage, authentification) :
    /// code stable, type, titre en français, chemin et identifiant de trace.
    /// </summary>
    private static void Customize(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;
        var http = context.HttpContext;

        problem.Instance ??= http.Request.Path;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? http.TraceIdentifier;

        if (!problem.Extensions.ContainsKey("code"))
        {
            var code = ProblemMapping.DefaultCodeFor(problem.Status ?? http.Response.StatusCode);
            var (status, title) = ProblemMapping.Describe(code);
            problem.Extensions["code"] = code;
            problem.Status ??= status;
            problem.Title = title;
            problem.Type = ProblemMapping.TypeFor(code);
            problem.Detail ??= code switch
            {
                ErrorCodes.ValidationFailed => "Un ou plusieurs paramètres de la requête sont invalides.",
                ErrorCodes.NotFound => "La ressource demandée n'existe pas.",
                ErrorCodes.MethodNotAllowed => "Cette méthode HTTP n'est pas acceptée pour cette ressource.",
                _ => null,
            };
        }
    }
}
