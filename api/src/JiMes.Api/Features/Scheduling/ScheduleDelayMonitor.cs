using JiMes.Api.Infrastructure.Settings;

namespace JiMes.Api.Features.Scheduling;

/// <summary>
/// 지연 반영 (설계 §7): 설정 주기(schedule.refresh_interval_sec)마다 계획이 있는 설비를 재계산해
/// 진행 중 작업이 길어졌거나 시작하지 못한 계획을 실제로 뒤로 옮겨 저장하고 SignalR 로 알린다.
/// 구 WinForms 는 화면에서만 밀어냈다 (PushPlanPanels). 설정 Scheduling:AutoRecalculate=false 로 끌 수 있다 (테스트).
/// </summary>
public sealed class ScheduleDelayMonitor(
    IServiceScopeFactory scopes, SettingsCache settings, IConfiguration configuration, ILogger<ScheduleDelayMonitor> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Scheduling:AutoRecalculate", true))
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(settings.GetInt(SettingKeys.ScheduleRefreshIntervalSec)), stoppingToken);
                await using var scope = scopes.CreateAsyncScope();
                var scheduling = scope.ServiceProvider.GetRequiredService<SchedulingService>();
                foreach (var equipmentId in await scheduling.EquipmentWithPlansAsync(stoppingToken))
                    await scheduling.RecalculateAsync(equipmentId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "지연 반영 재계산 실패");
            }
        }
    }
}
