using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Enrichers.Span;
using Verity.DailyBalance.Application;
using Verity.DailyBalance.Infrastructure;
using Verity.DailyBalance.Infrastructure.Persistence;
using Verity.DailyBalance.Worker.Telemetry;

const string ServiceName = "verity-daily-balance-worker";

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", ServiceName)
    .WriteTo.Console(new Serilog.Formatting.Compact.CompactJsonFormatter())
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithSpan()
        .Enrich.WithProperty("Service", ServiceName)
        .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName));

    builder.Services.AddDailyBalanceApplication();
    builder.Services.AddDailyBalancePersistence(builder.Configuration);
    builder.Services.AddDailyBalanceMessaging(builder.Configuration);

    builder.Services
        .AddHealthChecks()
        .AddNpgSql(builder.Configuration.GetConnectionString("DailyBalanceDb")!, name: "daily-balance-postgresql", tags: ["ready"])
        .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: ["ready"]);

    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService(ServiceName))
        .WithTracing(tracing => tracing
            .AddHttpClientInstrumentation()
            .AddSource("Npgsql")
            .AddSource("MassTransit")
            .AddOtlpExporterIfConfigured(builder.Configuration))
        .WithMetrics(metrics => metrics
            .AddHttpClientInstrumentation()
            .AddOtlpExporterIfConfigured(builder.Configuration));

    var app = builder.Build();

    app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = _ => false
    });

    app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready")
    });

    if (app.Configuration.GetValue<bool>("Database:AutoMigrate"))
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DailyBalanceDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "{Service} encerrado de forma inesperada durante a inicialização", ServiceName);
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}

static class OpenTelemetryConfigExtensions
{
    public static TracerProviderBuilder AddOtlpExporterIfConfigured(this TracerProviderBuilder builder, IConfiguration configuration)
    {
        var endpoint = configuration["OpenTelemetry:OtlpEndpoint"];
        return string.IsNullOrWhiteSpace(endpoint)
            ? builder
            : builder.AddOtlpExporter(o => o.Endpoint = new Uri(endpoint));
    }

    public static MeterProviderBuilder AddOtlpExporterIfConfigured(this MeterProviderBuilder builder, IConfiguration configuration)
    {
        var endpoint = configuration["OpenTelemetry:OtlpEndpoint"];
        return string.IsNullOrWhiteSpace(endpoint)
            ? builder
            : builder.AddOtlpExporter(o => o.Endpoint = new Uri(endpoint));
    }
}
