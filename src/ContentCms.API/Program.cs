using ContentCms.API.Models;
using ContentCms.API.Services;
using ContentCms.API.Services.Plugins;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddRazorPages();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Введіть 'Bearer' [пробіл] і далі ваш токен.\r\n\r\nНаприклад: \"Bearer eyJhbGciOiJIUzI1Ni...\""
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
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
    options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.LoginPath = "/Login";
    options.LogoutPath = "/Logout";
    options.AccessDeniedPath = "/AccessDenied";
})
.AddJwtBearer(options =>
{
    var keyBytes = Encoding.UTF8.GetBytes(jwtSettings.SecretKey);
    
    // Ensure we have at least 2lag bits for security (32 bytes)
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

// Add DbContext configuration with MySQL provider
var connectionString = builder.Configuration.GetConnectionString("ContentCmsDb");
if (!string.IsNullOrEmpty(connectionString))
{
    builder.Services.AddDbContext<ContentCmsDbContext>(options =>
        options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));
}

// Register authentication service
builder.Services.AddScoped<IAuthService, AuthService>();

// Register Users service
builder.Services.AddScoped<IUsersService, UsersService>();
builder.Services.AddScoped<IContentService, ContentService>();
builder.Services.AddScoped<IGroupsService, GroupsService>();

// Register AuditLog service
builder.Services.AddScoped<IAuditLogService, AuditLogService>();

// Register RabbitMQ Event Bus and Background Service
builder.Services.AddSingleton(System.Threading.Channels.Channel.CreateUnbounded<ContentCms.API.DTOs.Events.ContentUpdateEvent>());
builder.Services.AddSingleton<IRabbitMqEventBus, RabbitMqEventBus>();
builder.Services.AddHostedService<RabbitMqBackgroundService>();

// Plugins: scoped tokens, services, script runtime and event dispatcher
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IPluginTokenService, PluginTokenService>();
builder.Services.AddSingleton<PluginEventQueue>();
builder.Services.AddScoped<IPluginService, PluginService>();
builder.Services.AddScoped<IPluginRuntime, PluginRuntime>();
builder.Services.AddHostedService<PluginEventDispatcher>();
// Loopback client talks only to this application, so its (possibly self-signed) certificate is accepted.
builder.Services.AddHttpClient(PluginRuntime.LoopbackClientName)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        AllowAutoRedirect = false
    })
    .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddHttpClient(PluginRuntime.ExternalClientName)
    .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(10));

// Create app instance
var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Content CMS API"));
app.UseStaticFiles();
//app.UseHttpsRedirection();

// Apply CORS policy before authentication/authorization middleware
app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapRazorPages();

app.Run();

public class JwtSettings
{
    public string SecretKey { get; set; } = "";

    public string Issuer { get; set; } = "ContentCmsAPI";
    public string? Audience { get; internal set; }
}
