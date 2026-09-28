using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MeroSwasthya.Shared.Events;

/// <summary>A fact one module announces for others to react to (e.g. Clinical's FollowUpScheduled → Reminders).</summary>
public interface IDomainEvent;

/// <summary>Registered by the subscribing module: <c>services.AddScoped&lt;IDomainEventHandler&lt;X&gt;, MyHandler&gt;()</c>.</summary>
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken ct);
}

public interface IDomainEventPublisher
{
    /// <summary>Publish after the producing module has committed. Handlers run in-process, in the same scope, in order.</summary>
    Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken ct = default) where TEvent : IDomainEvent;
}

/// <summary>
/// In-process dispatcher. Every event is logged, so an event with no subscriber yet is still visible;
/// a failing handler is logged and does not fail the request that raised the event.
/// </summary>
internal sealed class InProcessDomainEventPublisher(IServiceProvider services, ILogger<InProcessDomainEventPublisher> logger)
    : IDomainEventPublisher
{
    public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken ct = default) where TEvent : IDomainEvent
    {
        var handlers = services.GetServices<IDomainEventHandler<TEvent>>().ToList();
        logger.LogInformation("Domain event {Event} ({Handlers} handler(s)): {@Payload}",
            typeof(TEvent).Name, handlers.Count, domainEvent);

        foreach (var handler in handlers)
        {
            try
            {
                await handler.HandleAsync(domainEvent, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Handler {Handler} failed for {Event}", handler.GetType().Name, typeof(TEvent).Name);
            }
        }
    }
}
