using GameStoreAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;

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

        // 1. ADIM: REGISTER (KAYIT OLMA) METODU
        [HttpPost("register")]
        public IActionResult Register([FromBody] RegisterRequest request)
        {
            if (_users.Any(u => u.Username.ToLower() == request.Username.ToLower()))
            {
                return BadRequest("Bu kullanıcı zaten mevcut!");
            }

            var hashedPassword = BCrypt.Net.BCrypt.HashPassword(request.Password);

            var newUser = new User
            {
                Id = _users.Count + 1,
                Username = request.Username,
                Password = hashedPassword,
                Role = request.Username.ToLower() == "irem" ? "Admin" : "User"
            };

            _users.Add(newUser);
            return Ok("Kullanıcı başarıyla kaydedildi.");
        }

        // 2. ADIM: LOGIN (GİRİŞ YAPMA) METODU
        [HttpPost("login")]
        public IActionResult Login([FromBody] User request)
        {
            var varOlanKullanici = _users.FirstOrDefault(u => u.Username.ToLower() == request.Username.ToLower());
            if (varOlanKullanici == null)
            {
                return BadRequest("Kullanıcı adı veya şifre hatalı!");
            }

            bool sifreDogruMu = BCrypt.Net.BCrypt.Verify(request.Password, varOlanKullanici.Password);
            if (!sifreDogruMu)
            {
                return BadRequest("Kullanıcı adı veya şifre hatalı!");
            }

            // DOĞRU TOKEN ÜRETİMİ: Burayı düzelttik, artık gerçek şifreli bilet üretiyor!
            var accessToken = GenerateJwtToken(varOlanKullanici);
            var refreshToken = GenerateRefreshToken();

            varOlanKullanici.RefreshToken = refreshToken;
            varOlanKullanici.RefreshTokenExpiration = DateTime.Now.AddDays(7);

            return Ok(new
            {
                Message = "Giriş başarılı!",
                Token = accessToken,
                RefreshToken = refreshToken,
                Expiration = varOlanKullanici.RefreshTokenExpiration
            });
        }

        // 3. ADIM: REFRESH (TOKEN YENİLEME) METODU
        [HttpPost("refresh")]
        public IActionResult Refresh([FromBody] string refreshToken)
        {
            if (string.IsNullOrEmpty(refreshToken))
            {
                return BadRequest("Refresh token boş olamaz!");
            }

            // Swagger'ın önbellekten getirdiği tüm pislikleri temizleyen sihirli filtre
            var cleanToken = refreshToken.Replace("\"", "").Replace("string", "").Trim();

            // Temizlenmiş tokenı hafızada arıyoruz
            var user = _users.FirstOrDefault(u => u.RefreshToken == cleanToken);

            if (user == null || user.RefreshTokenExpiration <= DateTime.Now)
            {
                return Unauthorized("Geçersiz veya süresi dolmuş Refresh Token!");
            }

            var newAccessToken = GenerateJwtToken(user);
            var newRefreshToken = GenerateRefreshToken();

            user.RefreshToken = newRefreshToken;
            user.RefreshTokenExpiration = DateTime.Now.AddDays(7);

            return Ok(new
            {
                Token = newAccessToken,
                RefreshToken = newRefreshToken,
                Expiration = user.RefreshTokenExpiration
            });
        }

        // JTW TOKEN ÜRETEN ARKA PLAN FABRİKASI
        private string GenerateJwtToken(User user)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes("BurayaGisliKeyiniziYazin1234567890!");

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.Name, user.Username),
                    new Claim(ClaimTypes.Role, user.Role) // Rolü pürüzsüzce gömdük
                }),
                Expires = DateTime.UtcNow.AddMinutes(15),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        // REFRESH TOKEN ÜRETEN ARKA PLAN FABRİKASI
        private string GenerateRefreshToken()
        {
            return Guid.NewGuid().ToString();
        }
    }
}
