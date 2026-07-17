using System;

namespace GameStoreAPI.DTOs
{
    public class ErrorResponseDto
    {
        public int StatusCode { get; set; }       // 400, 401, 500 gibi HTTP durum kodu
        public string Message { get; set; }       // Kullanıcıya gösterilecek temiz mesaj
        public string DetailedMessage { get; set; } // Hatanın teknik detayı (Geliştirme aşaması için)
        public DateTime Timestamp { get; set; }   // Hatanın gerçekleştiği zaman

        public ErrorResponseDto()
        {
            Timestamp = DateTime.UtcNow;
        }
    }
}