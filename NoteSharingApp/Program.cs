
 using Microsoft.Extensions.DependencyInjection;
 using Microsoft.Extensions.Configuration;
 using Microsoft.EntityFrameworkCore;
 using Microsoft.AspNetCore.Http.Features;
 using Microsoft.AspNetCore.Authentication.Cookies;
 using NoteSharingApp.Repository;
 using NoteSharingApp.Hubs;
 using MongoDB.Driver;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://0.0.0.0:5000");
// Add services to the container.
// 1. CONTROLLERS
builder.Services.AddControllersWithViews();
builder.Services.AddControllers(); // API için gerekli

// CORS politikasını ekle
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

// SignalR servisini ekle
// 3. SIGNALR
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
});

// Configure file upload settings
// 4. FILE UPLOAD LİMİT
builder.Services.Configure<IISServerOptions>(options =>
{
    options.MaxRequestBodySize = 30 * 1024 * 1024; // 30 MB
});

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 30 * 1024 * 1024; // 30 MB
    options.MultipartBodyLengthLimit = 30 * 1024 * 1024;
});

// 5. MONGODB
var connectionString = builder.Configuration.GetConnectionString("MongoDb");

// MongoDB için bağımlılığı ekle
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

builder.Services.AddAuthentication("Bearer")
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
builder.Services.AddAuthentication("Cookies");
 // 7. AUTH
 builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
     .AddCookie(options =>
     {
         options.LoginPath = "/Account/Login";  // Kullanıcı giriş sayfası
         options.LoginPath = "/Account/Login";
         options.LogoutPath = "/Account/Logout";
     });

builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline.
// 8. MIDDLEWARE PIPELINE
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}


// AUTH — JWT

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// CORS middleware'ini ekle
app.UseCors("AllowAll");
app.UseCors("AllowAll"); // CORS burada aktifleşiyor

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