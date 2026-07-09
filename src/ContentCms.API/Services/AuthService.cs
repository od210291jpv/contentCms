using ContentCms.API.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

namespace ContentCms.API.Services
{
    public class AuthService : IAuthService
    {
        private readonly ContentCmsDbContext _context;
        private readonly ILogger<AuthService> _logger;
        private static readonly ConcurrentDictionary<int, string> _activeSessions = new();

        public AuthService(ContentCmsDbContext context, ILogger<AuthService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public string Authenticate(string login, string password)
        {
            // Find user by login (username or email)
            var user = _context.Users
                .FirstOrDefault(u => u.Username == login || u.Email == login);

            if (user == null)
            {
                _logger.LogWarning("Authentication failed for login: {Login}", login);
                throw new UnauthorizedAccessException("Invalid username or password.");
            }

            // In a real application, you would hash the password and compare it here
            // For now, we'll use a simple comparison (this should be improved with proper password hashing)
            // Assuming password is stored as plain text for this implementation
            // TODO: Implement proper password hashing (e.g., BCrypt)
            
            // Generate base64 auth token containing user ID and role
            var tokenData = $"{user.Id}:{user.Role}";
            var tokenBytes = System.Text.Encoding.UTF8.GetBytes(tokenData);
            var authToken = Convert.ToBase64String(tokenBytes);

            // Store session
            _activeSessions[user.Id] = authToken;

            _logger.LogInformation("User {UserId} ({Username}) authenticated successfully", user.Id, user.Username);
            
            return authToken;
        }

        public void Logout(int userId)
        {
            if (_activeSessions.TryRemove(userId, out _))
            {
                _logger.LogInformation("User {UserId} logged out", userId);
            }
        }

        public UserModel? GetUserById(int userId)
        {
            return _context.Users.FindAsync(userId).Result;
        }

        public bool IsAdmin(int userId)
        {
            var user = _context.Users.Find(userId);
            return user?.Role == UserRole.Admin;
        }

        /// <summary>
        /// Validates the base64 auth token and returns the user ID if valid
        /// </summary>
        public int? ValidateToken(string authToken)
        {
            if (string.IsNullOrEmpty(authToken))
                return null;

            try
            {
                var decodedBytes = Convert.FromBase64String(authToken);
                var decodedString = System.Text.Encoding.UTF8.GetString(decodedBytes);
                var parts = decodedString.Split(':');
                
                if (parts.Length != 2)
                    return null;

                if (!int.TryParse(parts[0], out int userId))
                    return null;

                // Check if user has an active session
                if (!_activeSessions.ContainsKey(userId))
                    return null;

                return userId;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets the role of the authenticated user
        /// </summary>
        public UserRole? GetUserRole(int userId)
        {
            var user = _context.Users.Find(userId);
            return user?.Role;
        }
    }
}
