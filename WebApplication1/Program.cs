using Microsoft.OpenApi.Models;
using Serilog;
using System.IO; // Klasör oluþturma komutu için bunu ekledik

var builder = WebApplication.CreateBuilder(args);

// GEÇEN SEFERKÝ GÝBÝ KLASÖRÜ VE TXT DOSYASINI ZORLA OLUÞTURAN KOD
var logDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Logs");
if (!Directory.Exists(logDirectory))
{
    Directory.CreateDirectory(logDirectory);
}

builder.Host.UseSerilog((context, configuration) =>
    configuration.WriteTo.Console()
                 .WriteTo.File("Logs/log-.txt", rollingInterval: RollingInterval.Day)); builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Swagger Kilit Konfigürasyonu
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "GameStoreAPI", Version = "v1" });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Lütfen Bearer token deðerini giriniz (Örn: Bearer eyJhbGci...)",
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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<GameStoreAPI.Middlewares.ExceptionHandlingMiddleware>();

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();