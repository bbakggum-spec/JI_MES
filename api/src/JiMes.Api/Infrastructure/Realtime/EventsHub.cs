using Microsoft.AspNetCore.SignalR;

namespace JiMes.Api.Infrastructure.Realtime;

/// <summary>
/// 서버 → 웹 알림 전용 허브 (/hubs/events). 클라이언트는 받기만 하고, 변경은 REST API 로 한다.
/// 알림을 받으면 해당 데이터를 다시 조회한다 (payload 는 대상 식별 정보만).
/// </summary>
public sealed class EventsHub : Hub
{
    public const string Route = "/hubs/events";
}

/// <summary>SignalR 이벤트 이름 (웹 클라이언트와 공유하는 계약).</summary>
public static class RealtimeEvents
{
    /// <summary>payload: { key }</summary>
    public const string SettingChanged = "settingChanged";

    /// <summary>payload: { groupCode, code }</summary>
    public const string CommonCodeChanged = "commonCodeChanged";
}

public sealed class EventPublisher(IHubContext<EventsHub> hub)
{
    public Task PublishAsync(string eventName, object payload, CancellationToken ct = default) =>
        hub.Clients.All.SendAsync(eventName, payload, ct);
}
