// ONLY for quick test, remove later.
using Microsoft.AspNetCore.Mvc;
using TaskManager.Notifications.Abstractions.Events;
using TaskManager.Notifications.Abstractions;

[ApiController]
[Route("dev/noti")]
public class DevNotiController : ControllerBase
{
    private readonly INotificationClient _noti;
    public DevNotiController(INotificationClient noti) => _noti = noti;

    [HttpPost("ping/{userId}")]
    public async Task<IActionResult> Ping(string userId, CancellationToken ct)
    {
        await _noti.EnqueueAsync(new UserMentionedInComment(
            RecipientEmail: "lylap080@gmail.com",
            RecipientUserId: userId,
            TaskId: "", TaskName: "",
            ProjectId: "", ProjectName: "",
            CommentId: Guid.NewGuid().ToString(),
            CommentExcerpt: "Ping test",
            ContextUrl: "/",
            MentionedByUserId: "sys",
            MentionedByUserName: "System"
        ), ct);
        return Ok("queued");
    }
}
