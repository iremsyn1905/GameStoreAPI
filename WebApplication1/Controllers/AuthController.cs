using Microsoft.AspNetCore.Mvc;
using GameStoreAPI.Models;
using System.Collections.Generic;
using System.Linq;
using Microsoft.OpenApi.Any;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace GameStoreAPI.Controllers
{
    [Route("api/Controller")]
    [ApiController]
    public class AuthController : ControllerBase
    {

        // Test amaçlı kullanıcıları geçici olarak bellekte tutuyoruz
        private static List<User> _users = new List<User>();
        private readonly IConfiguration _configuration;

        // Ayarları okuyabilmek için Constructor ekliyoruz
        public AuthController(IConfiguration configuration)
        {
            _configuration = configuration;
        }
        // Kayıt istek modeli
        public class RegisterRequest
        {
            public string Username { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }
        // 1. KAYIT OLMA (REGISTER) FONKSİYONU
        [HttpPost("register")]
        public IActionResult Register([FromBody] RegisterRequest request)
        {
            if (_users.Any(u => u.Username == request.Username))
            {
                return BadRequest("Bu kullanıcı adı zaten alınmış");
            }

            // Şifreyi hashliyoruz
            string passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

            // Yeni kullanıcıyı listeye ekliyoruz
            var newUser = new User
            {
                Id = _users.Count + 1,
                Username = request.Username,
                Password = passwordHash
            };

            _users.Add(newUser);

            return Ok("Kullanıcı başarıyla kaydedildi!");
        }
        // Giriş istek modeli
        public class LoginRequest
        {
            public string Username { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }
        // 2. GİRİŞ YAPMA (LOGIN) FONKSİYONU
        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest request)
        {
            // 1. ADIM: Giriş yapılmak istenen kullanıcı adını listede arıyoruz
            var varOlanKullanici = _users.FirstOrDefault(u => u.Username == request.Username);

            // Eğer kullanıcı listede bulunamadıysa hata dönüyoruz
            if (varOlanKullanici == null)
            {
                return BadRequest("Kullanıcı adı veya şifre hatalı!");
            }

            // 2. ADIM: İŞTE İKİNCİ SİHİRLİ BCRYPT KODU! Şifre kontrolü:
            bool sifreDogruMu = BCrypt.Net.BCrypt.Verify(request.Password, varOlanKullanici.Password);

            // Eğer şifreler eşleşmiyorsa yine hata dönüyoruz
            if (!sifreDogruMu)
            {
                return BadRequest("Kullanıcı adı veya şifre hatalı!");
            }

            // İki kontrolü de geçtiyse giriş başarılıdır!
            // Eğer şifreler eşleşmiyorsa yine hata dönüyoruz
            if (!sifreDogruMu)
            {
                return BadRequest("Kullanıcı adı veya şifre hatalı!");
            }

          
            // Giriş başarılıysa kullanıcıya özel token üretiyoruz
            var token = GenerateJwtToken(varOlanKullanici);

            // Kullanıcıya hem mesajı hem de dijital anahtarını teslim ediyoruz
            return Ok(new { Message = "Giriş başarılı!", Token = token });
        }
        
        // TOKEN ÜRETEN YARDIMCI METOD
        private string GenerateJwtToken(User user)
        {
            var jwtSettings = _configuration.GetSection("Jwt");
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["Key"]!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            // Anahtarın içine kullanıcının hangi bilgilerini gömeceğimizi seçiyoruz (Claim)
            var claims = new[]
            {
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Name, user.Username)
    };

            // Anahtarın özelliklerini ekiyoruz (Kim üretti, kimler kullanabilir, ne zaman bitecek?)
            var token = new JwtSecurityToken(
                issuer: jwtSettings["Issuer"],
                audience: jwtSettings["Audience"],
                claims: claims,
                expires: DateTime.Now.AddDays(1), // Bu anahtar 1 gün boyunca geçerli olsun
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }


    }
   
}

    