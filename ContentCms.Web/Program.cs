using ContentCms.Web.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add DbContext for Web UI (same SQLite database)
builder.Services.AddDbContext<ImageWebDbContext>(options =>
    options.UseSqlite("Data Source=:memory:"));

builder.Services.AddRazorPages();

var app = builder.Build();

app.MapRazorPages();

app.Run();
