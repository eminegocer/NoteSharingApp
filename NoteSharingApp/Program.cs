using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using NoteSharingApp.Repository;
using NoteSharingApp.Hubs;
using Microsoft.AspNetCore.Http.Features;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// CORS politikalarını ekle
builder.Services.AddCors(options =>
{
    // Web uygulaması için CORS politikası (SignalR için gerekli)
    options.AddPolicy("WebAppPolicy", builder =>
    {
        builder.AllowAnyMethod()
               .AllowAnyHeader()
               .AllowCredentials()
               .SetIsOriginAllowed(_ => true);
    });

    // Mobil uygulama için CORS politikası
    options.AddPolicy("MobileAppPolicy", builder =>
    {
        builder.WithOrigins("*") // Tüm originlere izin ver
               .AllowAnyMethod()
               .AllowAnyHeader()
               .WithExposedHeaders("Content-Disposition", "Content-Length") // Dosya indirme için gerekli headerlar
               .SetIsOriginAllowed(_ => true);
    });
});

// SignalR servisini ekle
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
});

// Configure file upload settings
builder.Services.Configure<IISServerOptions>(options =>
{
    options.MaxRequestBodySize = 30 * 1024 * 1024; // 30 MB
});

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 30 * 1024 * 1024; // 30 MB
});

var connectionString = builder.Configuration.GetConnectionString("MongoDb");

// MongoDB için bağımlılığı ekle
try
{
    var client = new MongoClient(connectionString);
    // Test connection
    client.ListDatabaseNames().ToList();
    Console.WriteLine("MongoDB connection successful!");
    
    builder.Services.AddSingleton<DatabaseContext>();
}
catch (Exception ex)
{
    Console.WriteLine($"MongoDB connection error: {ex.Message}");
}

builder.Services.AddScoped<CategoryRepository>();

builder.Services.AddAuthentication("Cookies")
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";  // Kullanıcı giriş sayfası
        options.LogoutPath = "/Account/Logout";
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline.
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

// HTTPS yönlendirmesini geçici olarak kaldırıyoruz
// app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// CORS middleware'ini ekle
app.UseCors(policy =>
{
    policy.SetIsOriginAllowed(origin => true) // Tüm originlere izin ver
          .AllowAnyHeader()
          .AllowAnyMethod()
          .AllowCredentials();
});

// API'lar için özel CORS politikası
app.Map("/api", api =>
{
    api.UseCors(policy =>
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader()
              .WithExposedHeaders("Content-Disposition", "Content-Length"));
});

app.UseAuthentication();
app.UseAuthorization();

app.UseEndpoints(endpoints =>
{
    endpoints.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}");
    endpoints.MapHub<ChatHub>("/chatHub");
});

app.Run();
