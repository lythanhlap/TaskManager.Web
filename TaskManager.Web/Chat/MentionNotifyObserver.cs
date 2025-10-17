using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Universal.Chat.Abstractions;
using Universal.Chat.Persistence.EFCore.Entities;
using TaskManager.Users.Abstractions;
using TaskManager.Notifications.Abstractions;
using TaskManager.Notifications.Abstractions.Events;
using TaskManager.Projects.Persistence.EFCore;
using Universal.Chat.Persistence.EFCore;

namespace TaskManager.Web.Chat;

//public sealed class MentionNotifyObserver : IChatMessageObserver
//{
//    private static readonly Regex Rx = new(@"@([A-Za-z0-9_.\-]+)", RegexOptions.Compiled);

//    private readonly IUserReadOnly _users;
//    private readonly INotificationClient _noti;
//    private readonly ProjectsDbContext _projects;
//    private readonly ILogger<MentionNotifyObserver> _log;

//    public MentionNotifyObserver(
//        IUserReadOnly users,
//        INotificationClient noti,
//        ProjectsDbContext projects,
//        ILogger<MentionNotifyObserver> log)
//    {
//        _users = users; _noti = noti; _projects = projects; _log = log;
//    }

//    public async Task OnMessageSentAsync(MessageDto msg, CancellationToken ct = default)
//    {
//        var text = msg.Content ?? string.Empty;
//        var hits = Rx.Matches(text);
//        if (hits.Count == 0) { _log.LogDebug("No @mention in message {Id}", msg.Id); return; }

//        var sender = await _users.GetUserByIdAsync(msg.SenderUserId, ct);
//        var senderName = sender?.FullName ?? sender?.Username ?? "Someone";

//        // context từ conversation name: proj:{guid}
//        string projectId = "", projectName = "", contextUrl = "/";
//        var convName = await _projects.Set<Conversation>().AsNoTracking()
//                          .Where(c => c.Id == msg.ConversationId)
//                          .Select(c => c.Name)
//                          .SingleOrDefaultAsync(ct);

//        if (!string.IsNullOrWhiteSpace(convName) &&
//            convName.StartsWith("proj:", StringComparison.OrdinalIgnoreCase))
//        {
//            var idPart = convName.Substring(5);
//            if (Guid.TryParse(idPart, out var pid))
//            {
//                projectId = pid.ToString();
//                projectName = await _projects.Projects
//                    .Where(p => p.Id == pid)
//                    .Select(p => p.Name)
//                    .SingleOrDefaultAsync(ct) ?? "";
//                contextUrl = $"/projects/{projectId}";
//            }
//        }

//        var excerpt = text.Length > 200 ? text[..200] + "…" : text;

//        var usernames = hits.Select(m => m.Groups[1].Value.Trim())
//                            .Where(u => u.Length > 0)
//                            .Distinct(StringComparer.OrdinalIgnoreCase)
//                            .ToList();

//        _log.LogInformation("Mentions detected in message {Id}: {Usernames}", msg.Id, string.Join(",", usernames));

//        foreach (var uname in usernames)
//        {
//            var u = await _users.FindByUsernameAsync(uname, ct);  // đảm bảo method này có trong adapter
//            if (u == null) { _log.LogWarning("Username '{uname}' not found", uname); continue; }
//            if (u.Id == msg.SenderUserId) { _log.LogDebug("Skip self mention {UserId}", u.Id); continue; }
//            if (string.IsNullOrWhiteSpace(u.Email)) { _log.LogWarning("User '{uname}' has no email; skip", uname); continue; }

//            await _noti.EnqueueAsync(new UserMentionedInComment(
//                RecipientEmail: u.Email,
//                RecipientUserId: u.Id,
//                TaskId: "",
//                TaskName: "",
//                ProjectId: projectId,
//                ProjectName: projectName,
//                CommentId: msg.Id.ToString(),
//                CommentExcerpt: excerpt,
//                ContextUrl: contextUrl,
//                MentionedByUserId: sender?.Id ?? msg.SenderUserId,
//                MentionedByUserName: senderName
//            ), ct);

//            _log.LogInformation("Enqueued mention notify for {User} from message {MsgId}", uname, msg.Id);
//        }
//    }
//}

public sealed class MentionNotifyObserver : IChatMessageObserver
{
    private static readonly Regex Rx = new(@"@([A-Za-z0-9_.\-]+)", RegexOptions.Compiled);

    private readonly IUserReadOnly _users;
    private readonly INotificationClient _noti;
    private readonly ProjectsDbContext _projects;
    private readonly ChatDbContext _chatDb;               // <-- NEW

    public MentionNotifyObserver(
        IUserReadOnly users,
        INotificationClient noti,
        ProjectsDbContext projects,
        ChatDbContext chatDb)                              // <-- NEW
    {
        _users = users; _noti = noti; _projects = projects; _chatDb = chatDb;
    }

    public async Task OnMessageSentAsync(MessageDto msg, CancellationToken ct = default)
    {
        var text = msg.Content ?? string.Empty;
        var hits = Rx.Matches(text);
        if (hits.Count == 0) return;

        var sender = await _users.GetUserByIdAsync(msg.SenderUserId, ct);
        var senderName = sender?.FullName ?? sender?.Username ?? "Someone";

        // LẤY CONVERSATION NAME TỪ ChatDbContext (đúng context)
        string projectId = "", projectName = "", contextUrl = "/";
        var convName = await _chatDb.Conversations              // <-- sửa ở đây
            .AsNoTracking()
            .Where(c => c.Id == msg.ConversationId)
            .Select(c => c.Name)
            .SingleOrDefaultAsync(ct);

        if (!string.IsNullOrWhiteSpace(convName) &&
            convName.StartsWith("proj:", StringComparison.OrdinalIgnoreCase))
        {
            var idPart = convName.Substring(5);
            if (Guid.TryParse(idPart, out var pid))
            {
                projectId = pid.ToString();
                projectName = await _projects.Projects
                    .Where(p => p.Id == pid)
                    .Select(p => p.Name)
                    .SingleOrDefaultAsync(ct) ?? "";
                contextUrl = $"/projects/{projectId}";
            }
        }

        var excerpt = text.Length > 200 ? text.Substring(0, 200) + "…" : text;

        var usernames = hits.Select(m => m.Groups[1].Value.Trim())
                            .Where(u => u.Length > 0)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();

        foreach (var uname in usernames)
        {
            var u = await _users.FindByUsernameAsync(uname, ct);
            if (u == null || u.Id == msg.SenderUserId) continue;
            if (string.IsNullOrWhiteSpace(u.Email)) continue;

            await _noti.EnqueueAsync(new UserMentionedInComment(
                RecipientEmail: u.Email,
                RecipientUserId: u.Id,
                TaskId: "", TaskName: "",
                ProjectId: projectId,
                ProjectName: projectName,
                CommentId: msg.Id.ToString(),
                CommentExcerpt: excerpt,
                ContextUrl: contextUrl,
                MentionedByUserId: sender?.Id ?? msg.SenderUserId,
                MentionedByUserName: senderName
            ), ct);
        }
    }
}