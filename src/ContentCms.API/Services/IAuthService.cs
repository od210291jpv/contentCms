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
    }
}
