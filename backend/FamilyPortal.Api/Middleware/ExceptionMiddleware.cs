using System.Net;
using System.Text.Json;
using FamilyPortal.Application.DTOs;

namespace FamilyPortal.Api.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "未處理的例外: {Message} on {Method} {Path}", 
                ex.Message, context.Request.Method, context.Request.Path);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        context.Response.ContentType = "application/json";
        
        var (statusCode, message) = ex switch
        {
            ArgumentException or InvalidOperationException =>
                ((int)HttpStatusCode.BadRequest, ex.Message),
            UnauthorizedAccessException =>
                ((int)HttpStatusCode.Unauthorized, "未授權的存取"),
            KeyNotFoundException =>
                ((int)HttpStatusCode.NotFound, "請求的資源不存在"),
            _ =>
                ((int)HttpStatusCode.InternalServerError, "伺服器發生未預期的錯誤，請稍後重試")
        };

        context.Response.StatusCode = statusCode;

        var response = ApiResponse<object>.Fail(message);
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        
        await context.Response.WriteAsJsonAsync(response, options);
    }
}
