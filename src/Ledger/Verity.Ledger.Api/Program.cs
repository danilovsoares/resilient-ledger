using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Enrichers.Span;
using Verity.Ledger.Api.Auth;
using Verity.Ledger.Api.ErrorHandling;
using Verity.Ledger.Api.Middleware;
using Verity.Ledger.Api.RateLimiting;
using Verity.Ledger.Api.Telemetry;
using Verity.Ledger.Application;
using Verity.Ledger.Infrastructure;
using Verity.Ledger.Infrastructure.Persistence;

const string ServiceName = "verity-ledger-api";

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

    builder.Services.AddControllers();

    // Permite que a aplicação Angular (servida de uma origem diferente das APIs) consuma este
    // serviço a partir do navegador. Lista de origens fechada, não um wildcard — ver docs/security.md.
    const string CorsPolicyName = "VerityWeb";
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?? ["http://localhost:4201", "http://localhost:4200"];
    builder.Services.AddCors(options => options.AddPolicy(CorsPolicyName, policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()));

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo { Title = "Ledger API", Version = "v1" });
        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Description = "Informe: Bearer {seu token}",
            Name = "Authorization",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT"
        });
        options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
                []
            }
        });
    });

    builder.Services.AddLedgerApplication();
    builder.Services.AddLedgerInfrastructure(builder.Configuration);
    builder.Services.AddLedgerJwtAuthentication(builder.Configuration);
    builder.Services.AddLedgerRateLimiting(builder.Configuration);

    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
    builder.Services.AddProblemDetails();

    builder.Services
        .AddHealthChecks()
        .AddNpgSql(builder.Configuration.GetConnectionString("LedgerDb")!, name: "ledger-postgresql", tags: ["ready"])
        .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: ["ready"]);

    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService(ServiceName))
        .WithTracing(tracing => tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddSource("Npgsql")
            .AddSource("MassTransit")
            .AddOtlpExporterIfConfigured(builder.Configuration))
        .WithMetrics(metrics => metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddMeter(Verity.Ledger.Infrastructure.Telemetry.LedgerMetrics.MeterName)
            .AddOtlpExporterIfConfigured(builder.Configuration));

    var app = builder.Build();

    // Força a construção do singleton para que o ObservableGauge seja registrado no Meter
    // antes da primeira coleta do exportador OpenTelemetry.
    app.Services.GetRequiredService<Verity.Ledger.Infrastructure.Telemetry.LedgerMetrics>();

    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
        app.MapDevTokenEndpoint();
    }

    app.UseExceptionHandler();

    app.UseMiddleware<CorrelationIdMiddleware>();

    app.UseHttpsRedirection();

    app.UseCors(CorsPolicyName);

    app.UseRateLimiter();

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers().RequireRateLimiting(RateLimitingExtensions.PolicyName);

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
        var dbContext = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    if (app.Configuration.GetValue<bool>("Auth:SeedDefaultUser"))
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<Verity.Ledger.Application.Abstractions.IPasswordHasher>();
        var seedOptions = scope.ServiceProvider.GetRequiredService<Verity.Ledger.Infrastructure.Persistence.DefaultUserSeedOptions>();
        await Verity.Ledger.Infrastructure.Persistence.DefaultUserSeeder.SeedAsync(dbContext, passwordHasher, seedOptions, CancellationToken.None);
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

/// <summary>Ponto de entrada exposto para os testes de integração (WebApplicationFactory).</summary>
public partial class Program;
