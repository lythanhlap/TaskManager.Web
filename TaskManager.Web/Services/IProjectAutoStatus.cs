using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TaskManager.Web.Services
{
    public interface IProjectAutoStatus
    {
        Task<bool> RecalcAsync(Guid projectId, CancellationToken ct = default);
        Task<int> RecalcManyAsync(IEnumerable<Guid> projectIds, CancellationToken ct = default);
    }
}