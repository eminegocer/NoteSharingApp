using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http.Features;
using NoteSharingApp.Repository;
using NoteSharingApp.Hubs;
using MongoDB.Driver;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.SignalR;
using NoteSharingApp.Models;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://0.0.0.0:5000");

// 1. CONTROLLERS
builder.Services.AddControllersWithViews();
builder.Services.AddControllers(); // API için gerekli

// IConfiguration servisini ekle
builder.Services.AddSingleton<IConfiguration>(builder.Configuration);

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
    options.MultipartBodyLengthLimit = 30 * 1024 * 1024; // 30 MB
});

// 5. MONGODB
var connectionString = builder.Configuration.GetConnectionString("MongoDb");
builder.Services.AddSingleton<IMongoClient>(s => new MongoClient(connectionString));
builder.Services.AddSingleton<DatabaseContext>();
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

// 7. AUTH - SADECE JWT
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.LoginPath = "/Home/Login";
    options.LogoutPath = "/Home/Logout";
    options.AccessDeniedPath = "/Home/AccessDenied";
    options.Cookie.Name = "NoteSharingApp.Auth";
    options.Cookie.HttpOnly = true;
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
    options.SlidingExpiration = true;
})
.AddJwtBearer("Bearer", options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = "NoteSharingApp",
        ValidAudience = "NoteSharingAppMobile",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("MySuperStrongSecretKeyForJWTAuth123456789"))
    };
});

builder.Services.AddAuthorization();
builder.Services.AddHttpClient();

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

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// CORS middleware'ini ekle
app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

app.UseEndpoints(endpoints =>
{
    endpoints.MapHub<ChatHub>("/chatHub");
    endpoints.MapControllers(); // REST API controller'lar
    endpoints.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}");
});

app.Run();