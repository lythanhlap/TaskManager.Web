using TaskManager.Identity.Core;          // AddIdentityComponent
using TaskManager.Identity.Core.Options; // IdentityOptions
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using TaskManager.Identity.Persistence.EFCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskManager.Projects.Core;                 // AddProjectsComponent
using TaskManager.Projects.Persistence.EFCore;   // ProjectsDbContext (migrate)
using TaskManager.Users.Abstractions;   // IUserReadOnly
using TaskManager.Web.Adapters;
using TaskManager.Notifications.Core;
using TaskManager.Notifications.Email.Smtp;
using TaskManager.Notifications.Templating.Scriban;
using TaskManager.Notifications.Persistence.EFCore;
using TaskManager.Notifications.Abstractions; // INotificationClient


namespace TaskManager.Web
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            var services = builder.Services;
            var cfg = builder.Configuration;

            // 1. Gắn component Identity (DbContext + IAuthService + Options)
            services.AddIdentityComponent(cfg);
            // Gắn component Projects (DbContext + Repositories)
            services.AddProjectsComponent(cfg);

            
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

            // Facade gọn để gọi từ nghiệp vụ
            services.AddScoped<NotificationFacade>();


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

                // Lấy token từ cookie "access_token"
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

            // 3. MVC
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

                // (tuỳ chọn) template nhắc sắp đến hạn
                if (!t.Any(e => e.Key == "task_due_soon"))
                    t.Add(new TaskManager.Notifications.Persistence.EFCore.Entities.NotificationTemplate
                    {
                        Key = "task_due_soon",
                        Subject = "⏰ Sắp đến hạn: {{ TaskName }} — {{ DueAtUtc | date.to_string format='%d/%m/%Y %H:%M' }} UTC",
                        HtmlBody = "<p>Nhiệm vụ <b>{{ TaskName }}</b>{{ if ProjectName }} (dự án <b>{{ ProjectName }}</b>){{ end }} đến hạn lúc <b>{{ DueAtUtc | date.to_string format='%d/%m/%Y %H:%M' }} UTC</b>.</p>"
                    });

                await notiDb.SaveChangesAsync();


            }

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            // 4. Thêm Authentication trước Authorization
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            app.Run();
        }
    }
}
