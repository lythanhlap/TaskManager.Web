using TaskManager.Identity.Core;          
using TaskManager.Identity.Core.Options; 
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
// Identity
using TaskManager.Identity.Persistence.EFCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
// Projects
using TaskManager.Projects.Core;
using TaskManager.Projects.Persistence.EFCore;
using TaskManager.Users.Abstractions;  
using TaskManager.Web.Adapters;
// Notifications
using TaskManager.Notifications.Core;
using TaskManager.Notifications.Email.Smtp;
using TaskManager.Notifications.Templating.Scriban;
using TaskManager.Notifications.Persistence.EFCore;
using TaskManager.Notifications.Abstractions;
// dang ki su dung task 
using TaskManager.Tasks.Persistence.EFCore;
using TaskManager.Tasks.Abstractions;
using TaskManager.Tasks.Core;
// report
using TaskManager.Reports.Core.Typed;
using TaskManager.Tasks.Persistence.EFCore.Entities;
using TaskManager.Projects.Persistence.EFCore.Entities;
using TaskManager.Reports.Abstractions;
using TaskManager.Reports.Mvc;


namespace TaskManager.Web
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            var services = builder.Services;
            var cfg = builder.Configuration;

            //component Identity (DbContext + IAuthService + Options)
            services.AddIdentityComponent(cfg);
            //component Projects (DbContext + Repositories)
            services.AddProjectsComponent(cfg);

            builder.Services.AddDbContext<TasksDbContext>(options =>
                options.UseSqlServer(
                    cfg.GetConnectionString("DefaultConnection"),
                    x => x.MigrationsAssembly("TaskManager.Tasks.Persistence.EFCore")
                )
            );

            // Notifications: DbContext
            services.AddNotificationsDb(db =>
                db.UseSqlServer(
                    cfg.GetConnectionString("DefaultConnection"),
                    x => x.MigrationsAssembly("TaskManager.Notifications.Persistence.EFCore")
                )
            );

            // Options + Service + Worker
            services.Configure<NotificationsOptions>(cfg.GetSection("Notifications"));
            services.AddScoped<INotificationClient, NotificationService>();
            services.AddHostedService<OutboxProcessor>();

            // Adapters
            services.Configure<SmtpOptions>(cfg.GetSection("Smtp"));
            services.AddScoped<TaskManager.Notifications.Core.IEmailSender, SmtpEmailSender>();
            services.AddScoped<TaskManager.Notifications.Core.ITemplateRenderer, ScribanTemplateRenderer>();

            services.AddScoped<ITaskService, TaskService>();

           
            services.AddScoped<NotificationFacade>();
            //SmtpOptions smtpOptions = cfg.GetSection("Smtp").Get<SmtpOptions>()!;
            //smtpOptions.FromName ??= "Task Manager";


            services.AddReportsCoreTwoDb<ProjectsDbContext, TasksDbContext, Project, TaskItem>(cfg =>
            {
                cfg.ProjectKey = p => p.Id;
                cfg.ProjectName = p => p.Name;
                cfg.TaskProjectKey = t => t.ProjectId;

                cfg.IsCompletedByPredicate(t => t.Status == (int)Tasks.Abstractions.TaskStatus.Complete);
            });


            var idOpt = cfg.GetSection("Identity").Get<IdentityOptions>()!;
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = idOpt.JwtIssuer,
                    ValidAudience = idOpt.JwtAudience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(idOpt.JwtSigningKey)),
                    ValidateIssuerSigningKey = true,
                    ValidateIssuer = true,
                    ValidateAudience = true,

                    NameClaimType = "username"
                };

                // Lấy token từ cookie access_token
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        if (string.IsNullOrEmpty(ctx.Token) &&
                            ctx.Request.Cookies.TryGetValue("access_token", out var token))
                        {
                            ctx.Token = token;
                        }
                        return Task.CompletedTask;
                    }
                };
            });

            services.AddAuthorization();

            services.AddScoped<IUserReadOnly, IdentityUserReadOnlyAdapter>();
            
            services.AddControllersWithViews();

            var app = builder.Build();
            // Tự áp dụng migrations cho Identity ngay khi app start
            using (var scope = app.Services.CreateScope())
            {
                var sp = scope.ServiceProvider;

                var idDb = sp.GetRequiredService<IdentityDbContext>();
                await idDb.Database.MigrateAsync();

                await scope.ServiceProvider.GetRequiredService<ProjectsDbContext>().Database.MigrateAsync();

                // Migrations cho Notifications
                var notiDb = sp.GetRequiredService<NotificationsDbContext>();
                await notiDb.Database.MigrateAsync();

                // Seed templates nếu thiếu
                var t = notiDb.Templates;
                if (!t.Any(e => e.Key == "project_member_added"))
                    t.Add(new TaskManager.Notifications.Persistence.EFCore.Entities.NotificationTemplate
                    {
                        Key = "project_member_added",
                        Subject = "Bạn đã được thêm vào dự án {{ ProjectName }}",
                        HtmlBody = "<p>Bạn đã được <b>{{ AddedBy }}</b> thêm vào dự án <b>{{ ProjectName }}</b>.</p>"
                    });

                if (!t.Any(e => e.Key == "task_assigned"))
                    t.Add(new TaskManager.Notifications.Persistence.EFCore.Entities.NotificationTemplate
                    {
                        Key = "task_assigned",
                        Subject = "Bạn được giao nhiệm vụ: {{ TaskName }}",
                        HtmlBody = "<p>Bạn vừa được <b>{{ AssignedBy }}</b> giao <b>{{ TaskName }}</b>. {{ if DueDate }}Hạn: <b>{{ DueDate }}</b>{{ end }}</p>"
                    });

                if (!t.Any(e => e.Key == "user_mentioned"))
                    t.Add(new TaskManager.Notifications.Persistence.EFCore.Entities.NotificationTemplate
                    {
                        Key = "user_mentioned",
                        Subject = "Bạn được nhắc tới trong bình luận",
                        HtmlBody = "<p><b>{{ MentionedBy }}</b> nhắc tới bạn:</p><blockquote>{{ CommentExcerpt }}</blockquote><p>Xem: <a href='{{ ContextUrl }}'>liên kết</a></p>"
                    });

                await notiDb.SaveChangesAsync();

                await sp.GetRequiredService<TasksDbContext>().Database.MigrateAsync();


            }

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            app.Run();
        }
    }
}
