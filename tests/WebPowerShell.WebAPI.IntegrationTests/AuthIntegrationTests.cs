using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using WebPowerShell.Application.Users.Commands.ChangePassword;
using WebPowerShell.Application.Users.Commands.Login;
using WebPowerShell.Application.Users.Common;
using WebPowerShell.Domain.Common;
using WebPowerShell.Domain.Entities;
using WebPowerShell.Infrastructure.Security;
using Xunit;

namespace WebPowerShell.WebAPI.IntegrationTests
{
    public class AuthIntegrationTests : IClassFixture<TestWebApplicationFactory>
    {
        private readonly TestWebApplicationFactory _factory;
        private readonly BCryptPasswordHasher _passwordHasher;

        public AuthIntegrationTests(TestWebApplicationFactory factory)
        {
            _factory = factory;
            _passwordHasher = new BCryptPasswordHasher(Substitute.For<ILogger<BCryptPasswordHasher>>());
        }

        private async Task<User> SeedUserHelperAsync(string username, string plaintextPassword, DateTimeOffset? lastPasswordChange = null)
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = username,
                PasswordHash = _passwordHasher.HashPassword(plaintextPassword),
                IsActive = true,
                CreatedAt = _factory.TimeProvider.GetUtcNow(),
                UpdatedAt = _factory.TimeProvider.GetUtcNow(),
                LastPasswordChangeDate = lastPasswordChange ?? _factory.TimeProvider.GetUtcNow(),
                FailedLoginCount = 0,
                LockedUntil = null
            };

            await _factory.SeedUserAsync(user);
            return user;
        }

        private async Task<HttpResponseMessage> PostLoginAsync(HttpClient client, string ip, LoginCommand command)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = JsonContent.Create(command)
            };
            request.Headers.Add("X-Forwarded-For", ip);
            return await client.SendAsync(request);
        }

        [Fact]
        public async Task Scenario1_NormalLogin_ReturnsCookieAndAccessesWeatherForecast()
        {
            // Arrange
            var client = _factory.CreateClient();
            var username = "normaluser";
            var password = "CorrectPassword123!";
            await SeedUserHelperAsync(username, password);

            var loginCommand = new LoginCommand
            {
                Username = username,
                Password = password
            };

            // Act - Login using IP 10.0.0.1
            var loginResponse = await PostLoginAsync(client, "10.0.0.1", loginCommand);

            // Assert - Login
            Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
            var loginResult = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>();
            Assert.NotNull(loginResult);
            Assert.Equal(username, loginResult.Username);

            // Act - Get weather forecast (using same client with cookies)
            var weatherResponse = await client.GetAsync("/api/weatherforecast");

            // Assert - Weather forecast
            Assert.Equal(HttpStatusCode.OK, weatherResponse.StatusCode);
        }

        [Fact]
        public async Task Scenario2_LoginFailureAccountLock_LocksAccountOnFifthFailure()
        {
            // Arrange
            var client = _factory.CreateClient();
            var username = "lockuser";
            var correctPassword = "CorrectPassword123!";
            var wrongPassword = "WrongPassword123!";
            var user = await SeedUserHelperAsync(username, correctPassword);

            var wrongLoginCommand = new LoginCommand
            {
                Username = username,
                Password = wrongPassword
            };

            // Act & Assert - Try 5 times and get Locked (using IP 10.0.0.2)
            for (int i = 1; i <= 5; i++)
            {
                var response = await PostLoginAsync(client, "10.0.0.2", wrongLoginCommand);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

                // Verify db updates
                var updatedUser = await _factory.GetUserAsync(user.Id);
                Assert.NotNull(updatedUser);
                Assert.Equal(i, updatedUser.FailedLoginCount);

                if (i == 5)
                {
                    Assert.NotNull(updatedUser.LockedUntil);
                    Assert.Equal(_factory.TimeProvider.GetUtcNow().AddMinutes(1), updatedUser.LockedUntil.Value);
                }
                else
                {
                    Assert.Null(updatedUser.LockedUntil);
                }
            }

            // 6th attempt (while locked) - Using IP 10.0.0.20 to avoid Rate Limiter 429
            var lockedResponse = await PostLoginAsync(client, "10.0.0.20", new LoginCommand
            {
                Username = username,
                Password = correctPassword
            });
            Assert.Equal(HttpStatusCode.Unauthorized, lockedResponse.StatusCode);

            // Advance time past the 1-minute lock
            _factory.TimeProvider.Advance(TimeSpan.FromMinutes(1.1));

            // Try correct login after lock expired - Using IP 10.0.0.21 to avoid Rate Limiter 429
            var normalLoginResponse = await PostLoginAsync(client, "10.0.0.21", new LoginCommand
            {
                Username = username,
                Password = correctPassword
            });
            Assert.Equal(HttpStatusCode.OK, normalLoginResponse.StatusCode);

            // Verify db reset
            var finalUser = await _factory.GetUserAsync(user.Id);
            Assert.NotNull(finalUser);
            Assert.Equal(0, finalUser.FailedLoginCount);
            Assert.Null(finalUser.LockedUntil);
        }

        [Fact]
        public async Task Scenario3_LoginApiRateLimiting_Returns429OnSixthRequest()
        {
            // Arrange
            var client = _factory.CreateClient();
            var username = "rateuser";
            var password = "CorrectPassword123!";
            await SeedUserHelperAsync(username, password);

            var loginCommand = new LoginCommand
            {
                Username = username,
                Password = "WrongPassword!"
            };

            // Act & Assert - Make 5 requests rapidly from IP 10.0.0.3
            for (int i = 0; i < 5; i++)
            {
                var response = await PostLoginAsync(client, "10.0.0.3", loginCommand);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            }

            // 6th request from IP 10.0.0.3 should trigger 429 Too Many Requests
            var rateLimitedResponse = await PostLoginAsync(client, "10.0.0.3", loginCommand);
            Assert.Equal(HttpStatusCode.TooManyRequests, rateLimitedResponse.StatusCode);

            var failureBody = await rateLimitedResponse.Content.ReadFromJsonAsync<AppFailure>();
            Assert.NotNull(failureBody);
            Assert.Equal(AppFailure.RateLimitExceeded.ErrorCode, failureBody.ErrorCode);
            Assert.Equal(AppFailure.RateLimitExceeded.Message, failureBody.Message);
        }

        [Fact]
        public async Task Scenario4_PasswordExpiryPolicy_BlocksForecastAndAllowsChange()
        {
            // Arrange
            var client = _factory.CreateClient();
            var username = "expiryuser";
            var currentPassword = "CurrentPassword123!";
            var newPassword = "NewPassword123!";
            
            // Seed user whose last password change was 8 days ago
            var expiredDate = _factory.TimeProvider.GetUtcNow().AddDays(-8);
            var user = await SeedUserHelperAsync(username, currentPassword, expiredDate);

            // Act - Login using IP 10.0.0.4
            var loginResponse = await PostLoginAsync(client, "10.0.0.4", new LoginCommand
            {
                Username = username,
                Password = currentPassword
            });

            Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
            var loginResult = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>();
            Assert.NotNull(loginResult);
            Assert.True(loginResult.IsPasswordExpired);

            // Act - Access weather forecast
            var weatherResponse = await client.GetAsync("/api/weatherforecast");

            // Assert - Weather forecast should be 403 Forbidden and return PasswordExpired
            Assert.Equal(HttpStatusCode.Forbidden, weatherResponse.StatusCode);
            var failureBody = await weatherResponse.Content.ReadFromJsonAsync<AppFailure>();
            Assert.NotNull(failureBody);
            Assert.Equal(AppFailure.PasswordExpired.ErrorCode, failureBody.ErrorCode);
            Assert.Equal(AppFailure.PasswordExpired.Message, failureBody.Message);

            // Act - Change password
            var changePasswordCommand = new ChangePasswordCommand
            {
                UserId = user.Id,
                CurrentPassword = currentPassword,
                NewPassword = newPassword
            };
            var changeResponse = await client.PostAsJsonAsync("/api/auth/change-password", changePasswordCommand);
            Assert.Equal(HttpStatusCode.OK, changeResponse.StatusCode);

            // Verify DB update
            var updatedUser = await _factory.GetUserAsync(user.Id);
            Assert.NotNull(updatedUser);
            Assert.Equal(_factory.TimeProvider.GetUtcNow(), updatedUser.LastPasswordChangeDate);

            // Act - Retry accessing weather forecast
            var weatherSuccessResponse = await client.GetAsync("/api/weatherforecast");

            // Assert - Access allowed
            Assert.Equal(HttpStatusCode.OK, weatherSuccessResponse.StatusCode);
        }
    }
}
