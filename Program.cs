// Connect backend to PostgreSQL

using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// DB connection
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
    Environment.GetEnvironmentVariable("DATABASE_URL") ?? "fallback_connection_string"
));

// CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();


// 🔥 IMPORTANT for deployment (real IP)
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor
});

app.UseCors();


// 🔥 Helper function to get real IP
string GetIP(HttpContext http)
{
    var forwarded = http.Request.Headers["X-Forwarded-For"].FirstOrDefault();

    if (!string.IsNullOrEmpty(forwarded))
        return forwarded.Split(',')[0].Trim();

    return http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}


// ✅ GET all messages
app.MapGet("/messages", async (AppDbContext db) =>
{
    return await db.Messages
                   .OrderBy(m => m.CreatedAt)
                   .ToListAsync();
});


// ✅ GET current user (by IP)
app.MapGet("/me", async (HttpContext http, AppDbContext db) =>
{
    var ip = GetIP(http);

    var user = await db.Users.FirstOrDefaultAsync(u => u.IP == ip);

    if (user == null)
        return Results.NotFound();

    return Results.Ok(user);
});


// ✅ SET NAME (only once per IP)
app.MapPost("/set-name", async (HttpContext http, User input, AppDbContext db) =>
{
    var ip = GetIP(http);

    var exists = await db.Users.AnyAsync(u => u.IP == ip);
    if (exists)
        return Results.BadRequest("Name already set.");

    if (string.IsNullOrWhiteSpace(input.Name))
        return Results.BadRequest("Name required.");

    var user = new User
    {
        IP = ip,
        Name = input.Name
    };

    db.Users.Add(user);
    await db.SaveChangesAsync();

    return Results.Ok(user);
});


// ✅ SEND MESSAGE (1 per day per IP)
app.MapPost("/messages", async (HttpContext http, Message msg, AppDbContext db) =>
{
    var ip = GetIP(http);

    var user = await db.Users.FirstOrDefaultAsync(u => u.IP == ip);
    if (user == null)
        return Results.BadRequest("Set your name first.");

    var today = DateTime.UtcNow.Date;

    var sentToday = await db.Messages
        .AnyAsync(m => m.IP == ip && m.CreatedAt.Date == today);

    if (sentToday)
        return Results.BadRequest("You already sent a message today.");

    msg.IP = ip;
    msg.Name = user.Name;

    db.Messages.Add(msg);
    await db.SaveChangesAsync();

    return Results.Ok(msg);
});


// 🔥 Optional debug endpoint
app.MapGet("/ip", (HttpContext http) =>
{
    var forwarded = http.Request.Headers["X-Forwarded-For"].FirstOrDefault();
    var remote = http.Connection.RemoteIpAddress?.ToString();

    return new { forwarded, remote };
});


app.Run();