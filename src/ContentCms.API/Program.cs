using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Content CMS API", Version = "v1" });
});

// Configure CORS - Allow All origins (including credentials for SignalR)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.SetIsOriginAllowed(origin => true) // Dynamically allows any Origin
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();                // Allows cookies/tokens for SignalR
    });
});

// Configure JWT Authentication with Base64 encoded secret key from appsettings.json
var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>() ?? new JwtSettings();
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    var keyBytes = Encoding.UTF8.GetBytes(jwtSettings.SecretKey);
    
    // Ensure we have at least 256 bits for security (32 bytes)
    if (keyBytes.Length < 32)
    {
        throw new Exception("JWT Secret Key must be at least 32 characters long.");
    }

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwtSettings.Issuer ?? "ContentCmsAPI",
        
        ValidateAudience = true,
        ValidAudience = jwtSettings.Audience ?? "ContentCmsClients",
        
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
        
        RequireExpirationTime = true,
        ClockSkew = TimeSpan.Zero // No clock skew for security
        
    };
});

builder.Services.AddAuthorization();

// Add DbContext and other services here (will be added in next steps)
var connectionString = builder.Configuration.GetConnectionString("ContentCmsDb");
if (!string.IsNullOrEmpty(connectionString))
{
    // Database configuration will be added later
}

// Create app instance
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Content CMS API"));
}

app.UseHttpsRedirection();

// Apply CORS policy before authentication/authorization middleware
app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public class JwtSettings
{
    public string SecretKey { get; set; } = "";

    public string Issuer { get; set; } = "ContentCmsAPI";
    public string? Audience { get; internal set; }
}
