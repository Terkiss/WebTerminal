using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace WebPowerShell.WebAPI.IntegrationTests
{
    public class StaticFileTests : IClassFixture<TestWebApplicationFactory>
    {
        private readonly TestWebApplicationFactory _factory;

        public StaticFileTests(TestWebApplicationFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task GetIndexHtml_ReturnsSuccessAndCorrectContentType()
        {
            // Arrange
            var client = _factory.CreateClient();

            // Act
            var response = await client.GetAsync("/index.html");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("text/html", response.Content.Headers.ContentType?.ToString());
            
            var content = await response.Content.ReadAsStringAsync();
            Assert.Contains("<title>WebPowerShell - Premium Web Terminal</title>", content);
            Assert.Contains("xterm.js", content);
            Assert.Contains("signalr.min.js", content);
        }

        [Fact]
        public async Task GetDefaultPage_ReturnsIndexHtml()
        {
            // Arrange
            var client = _factory.CreateClient();

            // Act
            var response = await client.GetAsync("/");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("text/html", response.Content.Headers.ContentType?.ToString());
            
            var content = await response.Content.ReadAsStringAsync();
            Assert.Contains("<title>WebPowerShell - Premium Web Terminal</title>", content);
        }

        [Fact]
        public async Task GetStyleCss_ReturnsSuccessAndCorrectContentType()
        {
            // Arrange
            var client = _factory.CreateClient();

            // Act
            var response = await client.GetAsync("/style.css");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("text/css", response.Content.Headers.ContentType?.ToString());
            
            var content = await response.Content.ReadAsStringAsync();
            Assert.Contains("--bg-base", content);
            Assert.Contains("--accent-primary", content);
        }

        [Fact]
        public async Task GetAppJs_ReturnsSuccessAndCorrectContentType()
        {
            // Arrange
            var client = _factory.CreateClient();

            // Act
            var response = await client.GetAsync("/app.js");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "";
            Assert.True(contentType.Contains("application/javascript") || contentType.Contains("text/javascript"));
            
            var content = await response.Content.ReadAsStringAsync();
            Assert.Contains("initSignalR", content);
            Assert.Contains("createNewTab", content);
        }
    }
}
