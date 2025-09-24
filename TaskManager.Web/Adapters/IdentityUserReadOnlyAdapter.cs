using Microsoft.EntityFrameworkCore;
using TaskManager.Users.Abstractions;
using TaskManager.Identity.Persistence.EFCore;

namespace TaskManager.Web.Adapters
{
    public sealed class IdentityUserReadOnlyAdapter : IUserReadOnly
    {
        private readonly IdentityDbContext _db;
        public IdentityUserReadOnlyAdapter(IdentityDbContext db) => _db = db;

        public async Task<UserDto?> GetUserByIdAsync(string id, CancellationToken ct = default)
            => await _db.Users.AsNoTracking()
               .Where(u => u.Id == id)
               .Select(u => new UserDto(u.Id, u.Email, u.FullName, u.Username))
               .SingleOrDefaultAsync(ct);

        public async Task<UserDto?> FindByUsernameAsync(string username, CancellationToken ct = default)
            => await _db.Users.AsNoTracking()
               .Where(u => u.Username == username)
               .Select(u => new UserDto(u.Id, u.Email, u.FullName, u.Username))
               .SingleOrDefaultAsync(ct);
    }
}
