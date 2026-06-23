using Microsoft.AspNetCore.SignalR;

namespace ProximaLMSAPI.Hubs
{
    public class NotificationHub : Hub
    {
        // client calls this right after connecting, passing its userId
        public async Task Register(string userId)
        {
            if (!string.IsNullOrWhiteSpace(userId))
                await Groups.AddToGroupAsync(Context.ConnectionId, $"user-{userId}");
        }
    }

    // thin helper so controllers/services can push without referencing SignalR types everywhere
    public interface INotificationPush
    {
        Task PushToUser(int userId, object payload);
    }

    public class NotificationPush : INotificationPush
    {
        private readonly IHubContext<NotificationHub> _hub;
        public NotificationPush(IHubContext<NotificationHub> hub) => _hub = hub;

        public Task PushToUser(int userId, object payload)
            => _hub.Clients.Group($"user-{userId}").SendAsync("notify", payload);
    }
}
