using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using WebPowerShell.Application.Common.Interfaces;
using WebPowerShell.Domain.Entities;
using WebPowerShell.Infrastructure.Persistence;

namespace WebPowerShell.WebAPI.IntegrationTests
{
    public class TestWebApplicationFactory : WebApplicationFactory<Program>
    {
        public TestWebApplicationFactory()
        {
            ClientOptions.BaseAddress = new Uri("https://localhost");
        }

        public FakeTimeProvider TimeProvider { get; } = new FakeTimeProvider(new DateTimeOffset(2026, 6, 29, 3, 0, 0, TimeSpan.Zero));
        public string DbName { get; } = Guid.NewGuid().ToString();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AgentRuntime:InternalEventSecret"] = "integration-test-agent-event-secret"
                });
            });

            builder.ConfigureTestServices(services =>
            {
                // Remove existing DbContextOptions
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                
                // Add InMemory database context for tests (AuditLog)
                services.AddDbContext<AppDbContext>(options =>
                    options.UseInMemoryDatabase(DbName));

                // Replace TimeProvider with FakeTimeProvider
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(TimeProvider);

                // Keep MemoryPersistenceService injectable for hubs, but prevent
                // background/final SQLite persistence from racing between test hosts.
                services.RemoveAll<IHostedService>();
            });
        }

        public async Task SeedUserAsync(User user)
        {
            // Seed via IUserRepository (TeruTeruPandas DataFrame)
            var userRepo = Services.GetRequiredService<IUserRepository>();
            await userRepo.SaveAsync(user);
        }

        public async Task<User?> GetUserAsync(Guid userId)
        {
            // Retrieve via IUserRepository (TeruTeruPandas DataFrame)
            var userRepo = Services.GetRequiredService<IUserRepository>();
            var result = await userRepo.GetByIdAsync(userId);
            return result.IsSuccess ? result.Value : null;
        }

        public async Task<IReadOnlyList<AuditLog>> GetAuditLogsAsync()
        {
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await dbContext.AuditLogs
                .OrderBy(log => log.Id)
                .ToListAsync();
        }
    }
}
