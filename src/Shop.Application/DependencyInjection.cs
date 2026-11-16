using Cronos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shop.Application.Carts;
using Shop.Application.Catalog;

namespace Shop.Application;

public static class DependencyInjection
{
    /// <summary>Enregistre les cas d'usage et les politiques de l'Application.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddOptions<CatalogSyncOptions>()
            .BindConfiguration(CatalogSyncOptions.SectionName)
            .Validate(options => IsValidCron(options.Cron), "CatalogSync:Cron n'est pas une expression cron valide (5 champs).")
            .Validate(options => options.PageSize is >= 1 and <= 100, "CatalogSync:PageSize doit être compris entre 1 et 100.")
            .Validate(options => options.MaxDiscontinuedRatio is >= 0 and <= 1, "CatalogSync:MaxDiscontinuedRatio doit être compris entre 0 et 1.")
            .Validate(options => options.TickInterval > TimeSpan.Zero && options.BaseRetryDelay > TimeSpan.Zero
                                 && options.MaxRetryDelay >= options.BaseRetryDelay, "Délais de CatalogSync invalides.")
            .ValidateOnStart();

        services.AddSingleton(provider => new SyncSchedulePolicy(provider.GetRequiredService<IOptions<CatalogSyncOptions>>().Value));
        services.AddScoped<CatalogSyncService>();
        services.AddScoped<CatalogSyncScheduler>();
        services.AddScoped<CatalogQueries>();
        services.AddScoped<CartService>();

        return services;
    }

    private static bool IsValidCron(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        try
        {
            CronExpression.Parse(expression);
            return true;
        }
        catch (CronFormatException)
        {
            return false;
        }
    }
}
