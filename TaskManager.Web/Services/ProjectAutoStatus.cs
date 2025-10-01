using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TaskManager.Tasks.Persistence.EFCore;     
using TaskManager.Projects.Persistence.EFCore;        
using TTaskStatus = TaskManager.Tasks.Abstractions.TaskStatus;
using PjStatus = TaskManager.Projects.Abstractions.ProjectStatus;

namespace TaskManager.Web.Services
{
    public sealed class ProjectAutoStatus : IProjectAutoStatus
    {
        private readonly TasksDbContext _tdb;
        private readonly ProjectsDbContext _pdb;

        public ProjectAutoStatus(TasksDbContext tdb, ProjectsDbContext pdb)
        { _tdb = tdb; _pdb = pdb; }

        public async Task<bool> RecalcAsync(Guid projectId, CancellationToken ct = default)
        {
            // count prj task
            var q = _tdb.Tasks.AsNoTracking().Where(t => t.ProjectId == projectId);
            var total = await q.CountAsync(ct);

            // lay project
            var proj = await _pdb.Projects.FirstOrDefaultAsync(x => x.Id == projectId, ct);
            if (proj is null) return false;

            var old = proj.Status;

            if (total == 0)
            {
                proj.Status = (int)PjStatus.Planned;               
            }
            else
            {
                var completedValue = (int)TTaskStatus.Complete;     
                var notCompleted = await q.CountAsync(t => t.Status != completedValue, ct);
                proj.Status = notCompleted == 0
                    ? (int)PjStatus.Completed                        // tất cả Complete
                    : (int)PjStatus.InProgress;                      // còn task chưa xong
            }

            if (proj.Status != old)
            {
                await _pdb.SaveChangesAsync(ct);
                return true;
            }
            return false;
        }

        public async Task<int> RecalcManyAsync(IEnumerable<Guid> projectIds, CancellationToken ct = default)
        {
            int changed = 0;
            foreach (var pid in projectIds.Distinct())
            {
                if (await RecalcAsync(pid, ct)) changed++;
            }
            return changed;
        }
    }
}
