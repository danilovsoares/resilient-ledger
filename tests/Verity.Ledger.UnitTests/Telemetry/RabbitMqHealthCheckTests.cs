using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Verity.Ledger.Api.Telemetry;
using Verity.Ledger.Infrastructure.Messaging;

namespace Verity.Ledger.UnitTests.Telemetry;

public sealed class RabbitMqHealthCheckTests
{
    [Fact]
    public async Task Broker_inalcancavel_retorna_Unhealthy()
    {
        var options = Options.Create(new RabbitMqOptions { Host = "127.0.0.1", Port = 1 });
        var healthCheck = new RabbitMqHealthCheck(options);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().NotBeNull("o motivo da falha deve ficar disponível para diagnóstico, mas nunca é exposto ao cliente HTTP");
    }
}
