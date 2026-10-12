using MerfitApi.Business.Common.Notifications;
using MerfitApi.Business.Interfaces.Services.Admin;
using Microsoft.Extensions.Options;

namespace MerfitApi.Api.BackgroundJobs;

/// <summary>
/// Kampanya ve aylik odul bildirim islerinin zamanlayicisi. Is mantigi INotificationJobProcessor'dadir;
/// her tur kendi DI scope'unda calisir. Tum durum veritabanindadir (kilitler kosullu UPDATE ile alinir),
/// bu nedenle birden fazla API ornegi ayni anda calisabilir ve yeniden baslatma kaldigi yerden devam eder.
/// Push gonderimi MerfitCustomerApi'deki NotificationBackgroundWorker tarafindan yapilir.
/// </summary>
public class NotificationJobWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly NotificationAdminOptions _options;
    private readonly ILogger<NotificationJobWorker> _logger;

    public NotificationJobWorker(IServiceScopeFactory scopeFactory, IOptions<NotificationAdminOptions> options, ILogger<NotificationJobWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.WorkerEnabled)
        {
            _logger.LogInformation("NotificationJobWorker disabled (Notifications:WorkerEnabled=false).");
            return;
        }

        _logger.LogInformation("NotificationJobWorker started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = await RunStepAsync("campaigns", p => p.ProcessDueCampaignsAsync(DateTime.UtcNow, stoppingToken), stoppingToken)
                + await RunStepAsync("monthly rewards", p => p.ProcessDueMonthlyRunsAsync(DateTime.UtcNow, stoppingToken), stoppingToken);

            try
            {
                await Task.Delay(processed > 0 ? TimeSpan.FromSeconds(1) : _options.WorkerInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private async Task<int> RunStepAsync(string name, Func<INotificationJobProcessor, Task<int>> step, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            return await step(scope.ServiceProvider.GetRequiredService<INotificationJobProcessor>());
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NotificationJobWorker step '{Step}' failed.", name);
            return 0;
        }
    }
}
