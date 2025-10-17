using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Universal.Chat.Abstractions;

namespace TaskManager.Web.Chat
{
    /// Decorator quấn IChatService để bắn observers (ví dụ: @mention) sau khi gửi tin
    public sealed class MentioningChatServiceDecorator : IChatService
    {
        private readonly IChatService _inner;
        private readonly System.Collections.Generic.IEnumerable<IChatMessageObserver> _observers;
        private readonly ILogger<MentioningChatServiceDecorator> _log;

        public MentioningChatServiceDecorator(
            IChatService inner,
            System.Collections.Generic.IEnumerable<IChatMessageObserver> observers,
            ILogger<MentioningChatServiceDecorator> log)
        {
            _inner = inner;
            _observers = observers;
            _log = log;
        }

        public async Task<MessageDto> SendAsync(NewMessageDto input, string actorUserId, CancellationToken ct = default)
        {
            var dto = await _inner.SendAsync(input, actorUserId, ct);

            // gọi toàn bộ observers sau khi đã lưu + realtime
            foreach (var obs in _observers)
            {
                try
                {
                    _log.LogDebug("MentionDecorator: firing observer {Obs} for message {Id}", obs.GetType().Name, dto.Id);
                    await obs.OnMessageSentAsync(dto, ct);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "MentionDecorator: observer failed for message {Id}", dto.Id);
                }
            }
            return dto;
        }

        public Task<ConversationDto> CreateConversationAsync(NewConversationDto input, string actorUserId, CancellationToken ct = default)
            => _inner.CreateConversationAsync(input, actorUserId, ct);

        // KHỚP đúng chữ ký IChatService của bạn
        public Task<MessageDto> EditAsync(EditMessageDto input, string actorUserId, CancellationToken ct = default)
            => _inner.EditAsync(input, actorUserId, ct);

        public Task DeleteMessageAsync(Guid id, string actorUserId, CancellationToken ct = default)
            => _inner.DeleteMessageAsync(id, actorUserId, ct);

        // IChatService của bạn có tham số DateTimeOffset ở MarkReadAsync
        public Task MarkReadAsync(Guid conversationId, string actorUserId, DateTimeOffset since, CancellationToken ct = default)
            => _inner.MarkReadAsync(conversationId, actorUserId, since, ct);
    }
}
