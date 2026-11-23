using System.Text.Json;
using Contracts.Events;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace OrderService.Infrastructure.Messaging;

public sealed class RabbitMqOptions
{
    public string Host { get; set; } = "rabbitmq";
    public int Port { get; set; } = 5672;
    public string User { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string Exchange { get; set; } = EventRoutes.Exchange;
}

public interface IEventPublisher
{
    Task PublishAsync<T>(
        string routingKey, T message, CancellationToken ct);
}

public sealed class RabbitMqPublisher(
    IOptions<RabbitMqOptions> options
) : IEventPublisher, IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public async Task PublishAsync<T>(
        string routingKey, T message, CancellationToken ct)
    {
        var o = options.Value;
        var body = JsonSerializer.SerializeToUtf8Bytes(message, Json);

        await _gate.WaitAsync(ct);
        try
        {
            var channel = await EnsureChannelAsync(o, ct);

            var props = new BasicProperties
            {
                Persistent = true,
                ContentType = "application/json",
                Type = typeof(T).Name
            };

            await channel.BasicPublishAsync(
                exchange: o.Exchange,
                routingKey: routingKey,
                // true: nếu chưa có queue nào bind với routingKey, broker trả
                // tin lại để PublishAsync báo lỗi thay vì âm thầm bỏ tin.
                mandatory: true,
                basicProperties: props,
                body: body,
                cancellationToken: ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IChannel> EnsureChannelAsync(
        RabbitMqOptions o, CancellationToken ct)
    {
        if (_channel is { IsOpen: true }) return _channel;

        if (_channel is not null) await _channel.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();

        var factory = new ConnectionFactory
        {
            HostName = o.Host,
            Port = o.Port,
            UserName = o.User,
            Password = o.Password
        };

        _connection = await factory.CreateConnectionAsync(ct);

        _channel = await _connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true),
            ct);

        await _channel.ExchangeDeclareAsync(
            exchange: o.Exchange,
            type: ExchangeType.Topic,
            durable: true,
            cancellationToken: ct);

        return _channel;
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null) await _channel.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();
        _gate.Dispose();
    }
}