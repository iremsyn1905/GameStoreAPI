using Microsoft.OpenApi.Models;
using Serilog;
using System.IO;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.EntityFrameworkCore;
using GameStoreAPI.Data; // Bizim Data klasörümüzü görsün diye
using System.Threading.RateLimiting; // 🚀 YENİ: Rate Limiting için eklendi
using Microsoft.AspNetCore.RateLimiting; // 🚀 YENİ: Rate Limiting için eklendi

var builder = WebApplication.CreateBuilder(args);

// ==========================================
// VERİ TABANI BAĞLANTI AYARI (EF CORE & SQL)
// ==========================================
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ==========================================
// 🚀 RATE LIMITING (İSTEK SINIRLAMA) AYARI
// ==========================================
builder.Services.AddRateLimiter(options =>
{
    // "FixedPolicy" adında bir kural tanımlıyoruz
    options.AddFixedWindowLimiter(policyName: "FixedPolicy", fixedOptions =>
    {
        fixedOptions.PermitLimit = 2; // 30 saniyede en fazla 2 istek atabilsin
        fixedOptions.Window = TimeSpan.FromSeconds(30); // 30 saniyelik pencere
        fixedOptions.QueueLimit = 0;
    });

    // Sınırı aşan kullanıcılara 429 Too Many Requests hatası dönüyoruz
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// ==========================================
// 1. JWT KIMLIK DOĞRULAMA SERVISI (TEK VE GÜNCEL)
// ==========================================
var key = Encoding.ASCII.GetBytes("BurayaGisliKeyiniziYazin1234567890!");

builder.Services.AddAuthentication(x =>
{
    x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    x.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(x =>
{
    x.RequireHttpsMetadata = false;
    x.SaveToken = true;
    x.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
});

// ==========================================
// 2. SERILOG LOGLAMA AYARLARI
// ==========================================
var logDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Logs");
if (!Directory.Exists(logDirectory))
{
    Directory.CreateDirectory(logDirectory);
}

builder.Host.UseSerilog((context, configuration) =>
    configuration.WriteTo.Console()
                 .WriteTo.File("Logs/log-.txt", rollingInterval: RollingInterval.Day));

// ==========================================
// 3. CONTROLLER VE SWAGGER AYARLARI
// ==========================================
/// 🧠 YENİ: Ortak Redis Cache servisini projeye ekliyoruz (Varsayılan Redis portu 6379'dur)
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = "localhost:6379"; // Eğer bilgisayarında kuruluysa doğrudan bağlanır
    options.InstanceName = "GameStore_";      // Redis içindeki verilerimizin başına gelecek takı
});
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "GameStoreAPI", Version = "v1" });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Lütfen Bearer token değerini giriniz (Örn: Bearer eyJhbGci...)",
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        BearerFormat = "JWT",
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[]{}
        }
    });
});

var app = builder.Build();

// ==========================================
// 4. MIDDLEWARE (ARA KATMAN) BORU HATTI
// ==========================================
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<GameStoreAPI.Middlewares.ExceptionHandlingMiddleware>();

// app.UseHttpsRedirection();

// 🚀 YENİ: Rate Limiter middleware'ini fedailerin arasına ekliyoruz.
// Kimlik kontrolünden (Authentication) hemen önce çalışması en iyisidir.
app.UseRateLimiter();

// Doğru sıralamayla fedaileri kapıya diziyoruz
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();