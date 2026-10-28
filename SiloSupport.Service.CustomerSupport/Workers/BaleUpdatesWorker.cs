using Microsoft.Extensions.Options;
using SiloSupport.Application.Dtos;
using SiloSupport.Application.Interfaces;
using SiloSupport.Application.Services;

namespace SiloSupport.Service.CustomerSupport.Workers;

/// <summary>
/// Long-polls the Bale Bot API (getUpdates) and hands each text-bearing update to <see cref="CustomerMessageProcessor"/>.
/// This is the same receiving mechanism the existing SiloSupport.Service.CustomerChat service uses. It never answers
/// the originating chat.
/// </summary>
public class BaleUpdatesWorker : BackgroundService
{
    private const int MaxConcurrentUpdates = 5;

    private readonly IBaleBotClient _bale;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BaleBotOptions _options;
    private readonly ILogger<BaleUpdatesWorker> _logger;
    private readonly SemaphoreSlim _concurrency = new(MaxConcurrentUpdates, MaxConcurrentUpdates);

    public BaleUpdatesWorker(
        IBaleBotClient bale,
        IServiceScopeFactory scopeFactory,
        IOptions<BaleBotOptions> options,
        ILogger<BaleUpdatesWorker> logger)
    {
        _bale = bale;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Bale updates worker is disabled (Bale:Enabled=false)");
            return;
        }

        if (!_options.HasToken)
        {
            _logger.LogWarning("Bale updates worker not started: Bale:BotToken is not configured");
            return;
        }

        _logger.LogInformation("Bale updates worker started");
        long offset = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var updates = await _bale.GetUpdatesAsync(offset, _options.LongPollTimeoutSeconds, stoppingToken);

                // Advance before processing so a failing update is never fetched and processed again.
                foreach (var update in updates)
                {
                    if (update.UpdateId >= offset)
                        offset = update.UpdateId + 1;
                }

                var tasks = updates
                    .Where(u => u.Message is not null)
                    .Select(u => ProcessAsync(u.Message!, stoppingToken))
                    .ToList();

                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while polling Bale updates; retrying in 5 seconds");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("Bale updates worker stopped");
    }

    private async Task ProcessAsync(IncomingBaleMessage message, CancellationToken stoppingToken)
    {
        await _concurrency.WaitAsync(stoppingToken);
        try
        {
            // One scope (and DbContext) per message.
            using var scope = _scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<CustomerMessageProcessor>();
            await processor.ProcessAsync(message, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error while processing Bale message {MessageId}", message.MessageId);
        }
        finally
        {
            _concurrency.Release();
        }
    }
}
