using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

namespace GameStoreAPI.Middlewares
{
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;

        public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                // Her şey yolundaysa isteğin normal şekilde devam etmesini sağla
                await _next(context);
            }
            catch (Exception ex)
            {
                // Eğer kodun herhangi bir yerinde hata çıkarsa BURAYA düşecek!
                _logger.LogError(ex, "Beklenmedik bir hata oluştu: {Message}", ex.Message);
                await HandleExceptionAsync(context, ex);
            }
        }

        private static Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            context.Response.ContentType = "application/json";
            // Durum kodunu 500 Internal Server Error (Sunucu Hatası) yapıyoruz
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

            // Kullanıcıya döneceğimiz şık ve gizlilik içeren mesaj paketi
            var response = new
            {
                StatusCode = context.Response.StatusCode,
                Message = "Sunucu taraflı bir hata oluştu. Lütfen sistem yöneticisi ile iletişime geçiniz.",
                Detailed = exception.Message // Geliştirme aşamasında hatayı görmek için ekledik
            };

            var jsonResponse = JsonSerializer.Serialize(response);
            return context.Response.WriteAsync(jsonResponse);
        }
    }
}