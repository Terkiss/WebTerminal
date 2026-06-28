using WebPowerShell.Domain.Common;

namespace WebPowerShell.WebAPI.Hubs
{
    public class HubResponse
    {
        public bool Success { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }

        public static HubResponse Ok() => new() { Success = true };
        public static HubResponse Fail(string code, string message) => new() { Success = false, ErrorCode = code, ErrorMessage = message };
        public static HubResponse Fail(AppFailure failure) => Fail(failure.ErrorCode, failure.Message);
    }
}
