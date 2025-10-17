using System.Threading;
using System.Threading.Tasks;
using Universal.Chat.Abstractions;
using TaskManager.Users.Abstractions;

namespace TaskManager.Web.Adapters
{
    /// <summary>
    /// Adapter kết nối Chat component với hệ thống người dùng (Identity)
    /// </summary>
    public sealed class UserDirectoryAdapter : IUserDirectory
    {
        private readonly IUserReadOnly _users;

        public UserDirectoryAdapter(IUserReadOnly users)
        {
            _users = users;
        }

        public async Task<ChatUserDto?> GetAsync(string userId, CancellationToken ct = default)
        {
            // Sử dụng hàm GetUserByIdAsync từ IdentityUserReadOnlyAdapter
            var user = await _users.GetUserByIdAsync(userId, ct);
            if (user == null)
                return null;

            // Map dữ liệu sang ChatUserDto
            return new ChatUserDto(
                user.Id,
                user.FullName ?? user.Username ?? user.Email,
                AvatarUrl: null // nếu DB có cột AvatarUrl thì thay ở đây
            );
        }
    }
}
