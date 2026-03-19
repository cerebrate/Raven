using ArkaneSystems.Raven.Core.Bus.Contracts;
using ArkaneSystems.Raven.Core.Bus.Handlers;
using Microsoft.Extensions.Options;
using System.Reflection;
using System.Threading.Channels;

namespace ArkaneSystems.Raven.Core.Bus.Dispatch;

// In-process message bus skeleton using a bounded channel and single reader loop.
public sealed class InProcMessageBus : BackgroundService, IMessageBus
{
  private static readonly MethodInfo DispatchTypedMethod = typeof(InProcMessageBus)
      .GetMethod(nameof(DispatchTypedAsync), BindingFlags.Instance | BindingFlags.NonPublic)
      ?? throw new InvalidOperationException($"{nameof(InProcMessageBus)} is missing {nameof(DispatchTypedAsync)} method.");

  private readonly IServiceScopeFactory _scopeFactory;
  private readonly IMessageTypeRegistry _messageTypeRegistry;
  private readonly IDeadLetterSink _deadLetterSink;
  private readonly ILogger<InProcMessageBus> _logger;
  private readonly Channel<DispatchMessage> _channel;

  public InProcMessageBus (
      IServiceScopeFactory scopeFactory,
      IMessageTypeRegistry messageTypeRegistry,
      IDeadLetterSink deadLetterSink,
      IOptions<BusDispatchOptions> options,
      ILogger<InProcMessageBus> logger)
  {
    _scopeFactory = scopeFactory;
    _messageTypeRegistry = messageTypeRegistry;
    _deadLetterSink = deadLetterSink;
    _logger = logger;

    var channelCapacity = options.Value.ChannelCapacity;
    if (channelCapacity <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(options), "Channel capacity must be greater than zero.");
    }

    _channel = Channel.CreateBounded<DispatchMessage>(new BoundedChannelOptions(channelCapacity)
    {
      FullMode = BoundedChannelFullMode.Wait,
      SingleReader = true,
      SingleWriter = false
    });
  }

  public async Task PublishAsync<TPayload> (MessageEnvelope<TPayload> envelope, CancellationToken cancellationToken = default)
      where TPayload : notnull
  {
    ArgumentNullException.ThrowIfNull(envelope);

    var payloadType = envelope.Payload.GetType();

    await _channel.Writer.WriteAsync(
        new DispatchMessage(envelope.Metadata, envelope.Payload, payloadType),
        cancellationToken);
  }

  protected override async Task ExecuteAsync (CancellationToken stoppingToken)
  {
    await foreach (var message in _channel.Reader.ReadAllAsync(stoppingToken))
    {
      await DispatchAsync(message, stoppingToken);
    }
  }

  public override Task StopAsync (CancellationToken cancellationToken)
  {
    _channel.Writer.TryComplete();
    return base.StopAsync(cancellationToken);
  }

  private async Task DispatchAsync (DispatchMessage message, CancellationToken cancellationToken)
  {
    using var _ = _logger.BeginScope(new Dictionary<string, object?>
    {
      ["MessageId"] = message.Metadata.MessageId,
      ["CorrelationId"] = message.Metadata.CorrelationId,
      ["CausationId"] = message.Metadata.CausationId,
      ["SessionId"] = message.Metadata.SessionId,
      ["UserId"] = message.Metadata.UserId,
      ["MessageType"] = message.Metadata.Type,
      ["Priority"] = message.Metadata.Priority
    });

    if (!_messageTypeRegistry.IsAllowed(message.Metadata.Type, message.PayloadType))
    {
      _logger.LogWarning(
          "Dispatch contract mismatch for message type {MessageType}. PayloadType: {PayloadType}",
          message.Metadata.Type,
          message.PayloadType.FullName ?? message.PayloadType.Name);

      await _deadLetterSink.WriteAsync(
          new DeadLetterEntry(
              message.Metadata,
              message.PayloadType.FullName ?? message.PayloadType.Name,
              $"Message type '{message.Metadata.Type}' is not registered for payload type '{message.PayloadType.FullName}'.",
              DateTimeOffset.UtcNow),
          cancellationToken);

      return;
    }

    try
    {
      var dispatchTask = (Task?)DispatchTypedMethod
          .MakeGenericMethod(message.PayloadType)
          .Invoke(this, [message, cancellationToken]);

      if (dispatchTask is null)
      {
        throw new InvalidOperationException("Dispatch invocation returned null task.");
      }

      await dispatchTask;
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      var rootException = ex is TargetInvocationException { InnerException: not null }
          ? ex.InnerException
          : ex;

      _logger.LogError(
          rootException,
          "Dispatch failed for message type {MessageType}",
          message.Metadata.Type);

      await _deadLetterSink.WriteAsync(
          new DeadLetterEntry(
              message.Metadata,
              message.PayloadType.FullName ?? message.PayloadType.Name,
              "Handler execution failed.",
              DateTimeOffset.UtcNow,
              rootException.GetType().FullName,
              rootException.Message),
          cancellationToken);
    }
  }

  private async Task DispatchTypedAsync<TPayload> (DispatchMessage message, CancellationToken cancellationToken)
      where TPayload : notnull
  {
    await using var scope = _scopeFactory.CreateAsyncScope();
    var handlers = scope.ServiceProvider.GetServices<IMessageHandler<TPayload>>();

    var envelope = new MessageEnvelope<TPayload>(message.Metadata, (TPayload)message.Payload);

    foreach (var handler in handlers)
    {
      await handler.HandleAsync(envelope, cancellationToken);
    }
  }

  private sealed record DispatchMessage(
      MessageMetadata Metadata,
      object Payload,
      Type PayloadType);
}
