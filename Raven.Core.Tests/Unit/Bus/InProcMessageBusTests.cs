using ArkaneSystems.Raven.Core.Bus.Contracts;
using ArkaneSystems.Raven.Core.Bus.Dispatch;
using ArkaneSystems.Raven.Core.Bus.Handlers;
using Microsoft.Extensions.DependencyInjection;

namespace ArkaneSystems.Raven.Core.Tests.Unit.Bus;

public sealed class InProcMessageBusTests
{
  [Fact]
  public async Task PublishAsync_DispatchesToRegisteredHandler_WhenContractMatches ()
  {
    var services = new ServiceCollection();
    _ = services.AddLogging ();
    _ = services.AddOptions ();
    _ = services.Configure<BusDispatchOptions> (options => options.ChannelCapacity = 8);
    _ = services.AddSingleton<IMessageTypeRegistry, InMemoryMessageTypeRegistry> ();
    _ = services.AddSingleton<RecordingDeadLetterSink> ();
    _ = services.AddSingleton<IDeadLetterSink> (sp => sp.GetRequiredService<RecordingDeadLetterSink> ());
    _ = services.AddSingleton<RecordingHandler> ();
    _ = services.AddSingleton<IMessageHandler<TestPayload>> (sp => sp.GetRequiredService<RecordingHandler> ());
    _ = services.AddSingleton<InProcMessageBus> ();

    await using var provider = services.BuildServiceProvider();

    var registry = provider.GetRequiredService<IMessageTypeRegistry>();
    registry.Register ("test.message.v1", typeof (TestPayload));

    var bus = provider.GetRequiredService<InProcMessageBus>();
    var handler = provider.GetRequiredService<RecordingHandler>();
    var deadLetters = provider.GetRequiredService<RecordingDeadLetterSink>();

    await bus.StartAsync (CancellationToken.None);

    try
    {
      await bus.PublishAsync (new MessageEnvelope<TestPayload> (
          MessageMetadata.Create (type: "test.message.v1"),
          new TestPayload ("payload")), TestContext.Current.CancellationToken);

      var handled = await handler.WaitForMessageAsync(TimeSpan.FromSeconds(2));

      Assert.NotNull (handled);
      Assert.Equal ("payload", handled.Payload.Value);
      Assert.Empty (deadLetters.Entries);
    }
    finally
    {
      await bus.StopAsync (CancellationToken.None);
    }
  }

  [Fact]
  public async Task PublishAsync_WritesDeadLetter_WhenContractMismatchOccursAtDispatchTime ()
  {
    var services = new ServiceCollection();
    _ = services.AddLogging ();
    _ = services.AddOptions ();
    _ = services.Configure<BusDispatchOptions> (options => options.ChannelCapacity = 8);
    _ = services.AddSingleton<IMessageTypeRegistry, InMemoryMessageTypeRegistry> ();
    _ = services.AddSingleton<RecordingDeadLetterSink> ();
    _ = services.AddSingleton<IDeadLetterSink> (sp => sp.GetRequiredService<RecordingDeadLetterSink> ());
    _ = services.AddSingleton<RecordingHandler> ();
    _ = services.AddSingleton<IMessageHandler<TestPayload>> (sp => sp.GetRequiredService<RecordingHandler> ());
    _ = services.AddSingleton<InProcMessageBus> ();

    await using var provider = services.BuildServiceProvider();

    var bus = provider.GetRequiredService<InProcMessageBus>();
    var deadLetters = provider.GetRequiredService<RecordingDeadLetterSink>();
    var handler = provider.GetRequiredService<RecordingHandler>();

    await bus.StartAsync (CancellationToken.None);

    try
    {
      await bus.PublishAsync (new MessageEnvelope<TestPayload> (
          MessageMetadata.Create (type: "test.message.v1"),
          new TestPayload ("payload")), TestContext.Current.CancellationToken);

      var deadLetter = await deadLetters.WaitForEntryAsync(TimeSpan.FromSeconds(2));

      Assert.NotNull (deadLetter);
      Assert.Contains ("not registered", deadLetter.Reason, StringComparison.OrdinalIgnoreCase);
      Assert.Empty (handler.HandledMessages);
    }
    finally
    {
      await bus.StopAsync (CancellationToken.None);
    }
  }

  private sealed record TestPayload (string Value);

  private sealed class RecordingHandler : IMessageHandler<TestPayload>
  {
    private readonly TaskCompletionSource<MessageEnvelope<TestPayload>> _messageReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<MessageEnvelope<TestPayload>> HandledMessages { get; } = [];

    public Task HandleAsync (MessageEnvelope<TestPayload> message, CancellationToken cancellationToken)
    {
      this.HandledMessages.Add (message);
      _ = this._messageReceived.TrySetResult (message);
      return Task.CompletedTask;
    }

    public async Task<MessageEnvelope<TestPayload>?> WaitForMessageAsync (TimeSpan timeout)
    {
      using var cts = new CancellationTokenSource(timeout);

      try
      {
        return await this._messageReceived.Task.WaitAsync (cts.Token);
      }
      catch (OperationCanceledException)
      {
        return null;
      }
    }
  }

  private sealed class RecordingDeadLetterSink : IDeadLetterSink
  {
    private readonly TaskCompletionSource<DeadLetterEntry> _entryRecorded = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<DeadLetterEntry> Entries { get; } = [];

    public Task WriteAsync (DeadLetterEntry entry, CancellationToken cancellationToken = default)
    {
      this.Entries.Add (entry);
      _ = this._entryRecorded.TrySetResult (entry);
      return Task.CompletedTask;
    }

    public async Task<DeadLetterEntry?> WaitForEntryAsync (TimeSpan timeout)
    {
      using var cts = new CancellationTokenSource(timeout);

      try
      {
        return await this._entryRecorded.Task.WaitAsync (cts.Token);
      }
      catch (OperationCanceledException)
      {
        return null;
      }
    }
  }
}
