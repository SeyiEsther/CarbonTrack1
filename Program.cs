using Microsoft.EntityFrameworkCore;
using CarbonTrack.Models;

var builder = WebApplication.CreateBuilder(args);

// Add MVC with session-backed TempData
builder.Services.AddControllersWithViews()
    .AddSessionStateTempDataProvider();

// Session (required for TempData)
builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.IdleTimeout = TimeSpan.FromMinutes(30);
});

// Database with SQL retry on failure
builder.Services.AddDbContext<CarbonTrackContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions => sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorNumbersToAdd: null)));

// Google Maps typed HTTP client — do NOT also AddScoped; AddHttpClient handles lifetime
builder.Services.AddHttpClient<GoogleMapsService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Database initialisation — apply migrations and seed default organisation
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    try
    {
        var db = services.GetRequiredService<CarbonTrackContext>();
        db.Database.Migrate();

        if (!db.Organisations.Any())
        {
            db.Organisations.Add(new Organisation
            {
                Name = "Default Organisation",
                ContactEmail = "admin@example.com",
                Plan = "Free Trial",
                CreatedAt = DateTime.UtcNow
            });
            db.SaveChanges();
            logger.LogInformation("Default organisation seeded");
        }

        logger.LogInformation("Database ready");
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Database initialisation failed — application cannot start");
        throw;
    }
}

// Warn early if Google Maps key is missing
var mapsKey = app.Configuration["GoogleMaps:ApiKey"];
if (string.IsNullOrWhiteSpace(mapsKey))
    app.Logger.LogWarning("GoogleMaps:ApiKey is not configured — distance calculations will fail");

app.Run();
