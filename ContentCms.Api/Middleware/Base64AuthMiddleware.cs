using System.Security.Claims;
using System.Text;

namespace ContentCms.Api.Middleware;

public class Base64AuthMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly Dictionary<string, string> ValidCredentials = new()
    {
        ["admin:password"] = "true",
        ["user:testpass"] = "true"
    };

    public Base64AuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();

        if (string.IsNullOrEmpty(authHeader))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Unauthorized");
            return;
        }

        // Extract base64 credentials from Authorization header
        var encodedCredentials = authHeader.Replace("Basic ", string.Empty);
        var decodedCredentials = Encoding.UTF8.GetString(Convert.FromBase64String(encodedCredentials));

        if (ValidCredentials.TryGetValue(decodedCredentials, out var isValid))
        {
            if (isValid is not null)
            {
                // Set claims for authenticated user
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, "AuthenticatedUser"),
                     new Claim(ClaimTypes.Role, "Admin")],
                    "Base64Auth"));

                await _next(context);
            }
            else
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("Invalid credentials");
            }
        }
        else
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Unauthorized");
        }
    }
}
