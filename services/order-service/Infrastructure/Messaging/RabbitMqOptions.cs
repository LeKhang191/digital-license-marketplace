using Contracts.Events;

namespace OrderService.Infrastructure.Messaging;

public sealed class RabbitMqOptions
{
    public string Host { get; set; } = "rabbitmq";
    public int Port { get; set; } = 5672;
    public string User { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string Exchange { get; set; } = EventRoutes.Exchange;
}
