using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Authentication.Cookies;
using NoteSharingApp.Repository;
using NoteSharingApp.Hubs;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

// 1. CONTROLLERS
builder.Services.AddControllersWithViews();
builder.Services.AddControllers(); // API için gerekli

// 2. CORS — mobil ve web için tek bir genel policy
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader()
              .WithExposedHeaders("Content-Disposition", "Content-Length");
    });
});

// 3. SIGNALR
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
});

// 4. FILE UPLOAD LİMİT
builder.Services.Configure<IISServerOptions>(options =>
{
    options.MaxRequestBodySize = 30 * 1024 * 1024; // 30 MB
});

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 30 * 1024 * 1024;
});

// 5. MONGODB
var connectionString = builder.Configuration.GetConnectionString("MongoDb");

try
{
    var client = new MongoClient(connectionString);
    client.ListDatabaseNames().ToList(); // bağlantıyı test et
    Console.WriteLine("MongoDB connection successful!");
    builder.Services.AddSingleton<DatabaseContext>();
}
catch (Exception ex)
{
    Console.WriteLine($"MongoDB connection error: {ex.Message}");
}

// 6. REPOSITORY
builder.Services.AddScoped<CategoryRepository>();

// 7. AUTH
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// 8. MIDDLEWARE PIPELINE
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStaticFiles();

app.UseRouting();

app.UseCors("AllowAll"); // CORS burada aktifleşiyor

app.UseAuthentication();
app.UseAuthorization();

app.UseEndpoints(endpoints =>
{
    endpoints.MapControllers(); // REST API controller'lar
    endpoints.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}");
    endpoints.MapHub<ChatHub>("/chatHub"); // SignalR hub
});

app.Run();
