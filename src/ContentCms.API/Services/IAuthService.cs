namespace ContentCms.API.Services
{
    public interface IAuthService
    {
        /// <summary>
        /// Authenticates user with login and password, returns base64 auth token
        /// </summary>
        string Authenticate(string login, string password);

        /// <summary>
        /// Logs out the user and erases user token (cleans session)
        /// </summary>
        void Logout(int userId);

        /// <summary>
                /// Gets user information by ID
        /// </summary>
        Models.UserModel? GetUserById(int userId);

        /// <summary>
        /// Validates if user has admin role
        /// </summary>
        bool IsAdmin(int userId);

        /// <summary>
        /// Validates the base64 auth token and returns the user ID if valid
        /// </summary>
        int? ValidateToken(string authToken);

        /// <summary>
        /// Gets the role of the authenticated user
        /// </summary>
        Models.UserRole? GetUserRole(int userId);
    }
}
