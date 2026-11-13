namespace ContentCms.API.Controllers
{
    internal static class AuthHeader
    {
        /// <summary>Accepts both "Bearer &lt;token&gt;" and a raw token (older endpoints expect the raw form).</summary>
        public static string Token(string? header)
        {
            if (string.IsNullOrWhiteSpace(header)) return string.Empty;
            header = header.Trim();
            return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header["Bearer ".Length..].Trim() : header;
        }
    }
}
