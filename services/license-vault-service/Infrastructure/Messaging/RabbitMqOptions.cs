
using Contracts.Events;

namespace LicenseVault.Api.Infrastructure.Messaging;

public sealed class RabbitMqOptions
{
    public string Host { get; set; } = "rabbitmq";

    public int Port { get; set; } = 5672;

    public string User { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string Exchange { get; set; } =
        EventRoutes.Exchange;
}
