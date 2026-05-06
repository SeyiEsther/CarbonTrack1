using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using CarbonTrack.Models;

var builder = WebApplication.CreateBuilder(args);

// MVC with session-backed TempData
builder.Services.AddControllersWithViews()
    .AddSessionStateTempDataProvider();

builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.IdleTimeout = TimeSpan.FromMinutes(30);
});

// Database
builder.Services.AddDbContext<CarbonTrackContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions => sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorNumbersToAdd: null)));

// ASP.NET Core Identity
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 8;
    options.SignIn.RequireConfirmedAccount = false;
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<CarbonTrackContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath    = "/Account/Login";
    options.LogoutPath   = "/Account/Logout";
    options.AccessDeniedPath = "/Account/Login";
    options.SlidingExpiration = true;
    options.ExpireTimeSpan    = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly   = true;
    options.Cookie.IsEssential = true;
});

// Google Maps HTTP client
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
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Database initialisation — migrations + role seeding
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger   = services.GetRequiredService<ILogger<Program>>();
    try
    {
        var db = services.GetRequiredService<CarbonTrackContext>();
        db.Database.Migrate();

        // Seed a default org placeholder if the database is completely empty.
        // The first admin user will claim and rename this org during registration.
        if (!db.Organisations.Any())
        {
            db.Organisations.Add(new Organisation
            {
                Name         = "Default Organisation",
                ContactEmail = "",
                Plan         = "Free Trial",
                CreatedAt    = DateTime.UtcNow,
            });
            db.SaveChanges();
            logger.LogInformation("Default organisation seeded");
        }

        // Seed roles
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { "Admin", "Employee", "Consultant" })
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        logger.LogInformation("Database and roles ready");
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Database initialisation failed");
        throw;
    }
}

var mapsKey = app.Configuration["GoogleMaps:ApiKey"];
if (string.IsNullOrWhiteSpace(mapsKey))
    app.Logger.LogWarning("GoogleMaps:ApiKey not configured — distance calculations will fail");

app.Run();
