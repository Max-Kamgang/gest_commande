using GestionCommandes.BuildingBlocks.Correlation;
using GestionCommandes.BuildingBlocks.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace GestionCommandes.BuildingBlocks.Observability;

/// <summary>Configuration Serilog commune : console (JSON-friendly) + fichier journalier dans le volume.</summary>
public static class SerilogExtensions
{
    /// <summary>
    /// Branche Serilog comme unique fournisseur de logs. Le Correlation-ID est
    /// present sur chaque ligne grace a <see cref="CorrelationIdMiddleware"/>.
    /// </summary>
    public static WebApplicationBuilder AddSerilogLogging(
        this WebApplicationBuilder builder,
        string serviceName,
        StoragePaths paths)
    {
        var logFile = Path.Combine(paths.LogsPath, $"{serviceName}-.log");

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(builder.Configuration)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithProperty("Service", serviceName)
            .WriteTo.Console(
                outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] [{Service}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                path: logFile,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true,
                outputTemplate:
                "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{Service}] [{MachineName}] [{CorrelationId}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        builder.Logging.ClearProviders();
        builder.Host.UseSerilog(Log.Logger, dispose: true);

        return builder;
    }

    /// <summary>Journalisation d'une ligne par requete HTTP, enrichie du Correlation-ID.</summary>
    public static IApplicationBuilder UseRequestLogging(this WebApplication app)
    {
        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate =
                "HTTP {RequestMethod} {RequestPath} repond {StatusCode} en {Elapsed:0.0} ms";

            options.GetLevel = (httpContext, elapsed, ex) =>
            {
                // Les sondes de sante ne polluent pas les logs.
                if (httpContext.Request.Path.StartsWithSegments("/health"))
                {
                    return LogEventLevel.Verbose;
                }

                return ex is not null || httpContext.Response.StatusCode >= 500
                    ? LogEventLevel.Error
                    : httpContext.Response.StatusCode >= 400
                        ? LogEventLevel.Warning
                        : LogEventLevel.Information;
            };

            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set("RemoteIp", httpContext.Connection.RemoteIpAddress?.ToString() ?? "-");
                diagnosticContext.Set("UserName", httpContext.User.Identity?.Name ?? "anonyme");
            };
        });

        return app;
    }
}
