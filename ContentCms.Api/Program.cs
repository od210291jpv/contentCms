using ContentCms.Api.Data;
using ContentCms.Api.Middleware;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add DbContext with MySQL
builder.Services.AddDbContext<ImageDbContext>(options =>
    options.UseMySql(builder.Configuration.GetConnectionString("DefaultConnection") ?? "server=localhost;database=contentcms;user=root;password=password", 
        new MySqlServerVersion(new Version(8, 0, 35))));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Use Base64 Auth Middleware before authorization
app.UseMiddleware<Base64AuthMiddleware>();

app.UseAuthorization();

app.MapControllers();

app.Run();
