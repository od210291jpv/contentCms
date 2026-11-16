using ContentCms.API.Services;
using Microsoft.AspNetCore.Mvc;

namespace ContentCms.API.Controllers
{
    /// <summary>The user behind an API token, and JSON error helpers for the management API.</summary>
    internal readonly record struct ApiCaller(int UserId, bool IsAdmin)
    {
        /// <summary>Resolves the caller from the Authorization header ("Bearer &lt;token&gt;" or the raw token).</summary>
        public static ApiCaller? From(IAuthService auth, string? header)
        {
            int? userId = auth.ValidateToken(AuthHeader.Token(header));
            return userId is int id ? new ApiCaller(id, auth.IsAdmin(id)) : null;
        }

        public static ObjectResult Error(int status, string message) => new(new { error = message }) { StatusCode = status };

        public static ObjectResult From(ContentAccessResult result, string notFound) => result switch
        {
            ContentAccessResult.NotFound => Error(StatusCodes.Status404NotFound, notFound),
            _ => Error(StatusCodes.Status403Forbidden, "You are not allowed to change this content or group.")
        };

        public static ObjectResult From(TagOperationResult result) => result switch
        {
            TagOperationResult.NotFound => Error(StatusCodes.Status404NotFound, TagRules.ErrorMessage(result)),
            TagOperationResult.Forbidden => Error(StatusCodes.Status403Forbidden, TagRules.ErrorMessage(result)),
            TagOperationResult.DuplicateName => Error(StatusCodes.Status409Conflict, TagRules.ErrorMessage(result)),
            _ => Error(StatusCodes.Status400BadRequest, TagRules.ErrorMessage(result))
        };
    }
}
