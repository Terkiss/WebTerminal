using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WebPowerShell.Domain.Entities;
using WebPowerShell.Infrastructure.Persistence;

namespace WebPowerShell.WebAPI.IntegrationTests
{
    public class TestWebApplicationFactory : WebApplicationFactory<Program>
    {
        public FakeTimeProvider TimeProvider { get; } = new FakeTimeProvider(new DateTimeOffset(2026, 6, 29, 3, 0, 0, TimeSpan.Zero));
        public string DbName { get; } = Guid.NewGuid().ToString();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                // Remove existing DbContextOptions
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                
                // Add InMemory database context for tests
                services.AddDbContext<AppDbContext>(options =>
                    options.UseInMemoryDatabase(DbName));

                // Replace TimeProvider with FakeTimeProvider
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(TimeProvider);
            });
        }

        public async Task SeedUserAsync(User user)
        {
            using var scope = Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var existing = await context.Users.FindAsync(user.Id);
            if (existing != null)
            {
                context.Users.Remove(existing);
            }
            await context.Users.AddAsync(user);
            await context.SaveChangesAsync();
        }

        public async Task<User?> GetUserAsync(Guid userId)
        {
            using var scope = Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await context.Users.FindAsync(userId);
        }
    }
}
