using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using WebPowerShell.Domain.Common;

namespace WebPowerShell.WebAPI.Hubs
{
    public class HubExceptionFilter : IHubFilter
    {
        private readonly ILogger<HubExceptionFilter> _logger;

        public HubExceptionFilter(ILogger<HubExceptionFilter> logger)
        {
            _logger = logger;
        }

        public async ValueTask<object?> InvokeMethodAsync(
            HubInvocationContext context,
            Func<HubInvocationContext, ValueTask<object?>> next)
        {
            try
            {
                return await next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unhandled exception occurred while invoking hub method '{MethodName}' on hub '{HubName}'", 
                    context.HubMethodName, context.Hub.GetType().Name);

                if (context.HubMethod.ReturnType == typeof(Task<HubResponse>))
                {
                    return HubResponse.Fail(AppFailure.InternalError);
                }

                throw;
            }
        }

        public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
        {
            await next(context);
        }

        public async Task OnDisconnectedAsync(HubLifetimeContext context, Exception? exception, Func<HubLifetimeContext, Exception?, Task> next)
        {
            await next(context, exception);
        }
    }
}
