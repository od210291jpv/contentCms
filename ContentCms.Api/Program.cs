using ContentCms.Api.Data;
using ContentCms.Api.Middleware;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add DbContext with SQLite in-memory for now (will be replaced with file-based)
builder.Services.AddDbContext<ImageDbContext>(options =>
    options.UseSqlite("Data Source=:memory:"));

builder.Services.AddControllers();

var app = builder.Build();

// Use Base64 Auth Middleware before authorization
app.UseMiddleware<Base64AuthMiddleware>();

app.UseAuthorization();
app.MapControllers();

app.Run();
