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
// 🎯 Burası UTF8 yapıldı (AuthController ile tam senkronize olması için)
var key = Encoding.UTF8.GetBytes("BurayaGisliKeyiniziYazin1234567890!");

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

    // 🔒 İŞTE ÖZELLEŞTİRİLMİŞ HATA OLAYLARI (401 ve 403 Protokolü)
    x.Events = new JwtBearerEvents
    {
        // 1. GİRİŞ YAPILMADIĞINDA TETİKLENEN HATA (401 Unauthorized)
        OnChallenge = async context =>
        {
            // .NET'in varsayılan boş 401 fırlatma davranışını engelliyoruz
            context.HandleResponse();

            // Yanıt tipimizi kurumsal JSON yapıyoruz
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;

            // Ön yüzün doğrudan yakalayıp ekrana basabileceği şık hata paketi
            var response = new
            {
                statusCode = 401,
                message = "Bu işlemi gerçekleştirmek için yetkiniz bulunmamaktadır. Lütfen sisteme giriş yapınız.",
                detailed = "JWT Bearer Token bulunamadı veya süresi dolmuş."
            };

            var jsonResponse = System.Text.Json.JsonSerializer.Serialize(response);
            await context.Response.WriteAsync(jsonResponse);
        },

        // 2. YETKİSİZ ROLLE GİRİŞ YAPILDIĞINDA TETİKLENEN HATA (403 Forbidden)
        OnForbidden = async context =>
        {
            // Yanıt tipimizi kurumsal JSON yapıyoruz
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = StatusCodes.Status403Forbidden;

            // Ön yüzün doğrudan yakalayıp ekrana basabileceği şık hata paketi
            var response = new
            {
                statusCode = 403,
                message = "Bu işlemi gerçekleştirmek için yetkiniz yetersizdir. Bu işlem sadece 'Admin' rolüne sahip kullanıcılar tarafından yapılabilir.",
                detailed = "Kullanıcı rolü bu endpoint için gerekli olan 'Admin' rolünü karşılamıyor."
            };

            var jsonResponse = System.Text.Json.JsonSerializer.Serialize(response);
            await context.Response.WriteAsync(jsonResponse);
        }
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
// 🧠 Sunum Modu: Bilgisayara Redis kurmadan, kodları değiştirmeden RAM üzerinden Cache simülasyonu yapar!
builder.Services.AddDistributedMemoryCache();
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