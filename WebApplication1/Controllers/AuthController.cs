using GameStoreAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using GameStoreAPI.Data; // Bizim veri tabanı köprümüz

namespace GameStoreAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;

        // Listeyi sildik, yerine AppDbContext enjekte ettik
        public AuthController(AppDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        public class RegisterRequest
        {
            public string Username { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }

        // 1. ADIM: REGISTER (KAYIT OLMA)
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            // SQL tablosunda bu kullanıcı adı var mı diye bakıyoruz
            var userExists = await _context.Users.AnyAsync(u => u.Username.ToLower() == request.Username.ToLower());
            if (userExists)
            {
                return BadRequest("Bu kullanıcı zaten mevcut!");
            }

            var hashedPassword = BCrypt.Net.BCrypt.HashPassword(request.Password);

            var newUser = new User
            {
                Username = request.Username,
                Password = hashedPassword,
                Role = request.Username.ToLower() == "irem" ? "Admin" : "User"
            };

            _context.Users.Add(newUser);
            await _context.SaveChangesAsync(); // SQL'e kalıcı olarak kaydet!

            return Ok("Kullanıcı başarıyla kaydedildi.");
        }

        // 2. ADIM: LOGIN (GİRİŞ YAPMA)
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] User request)
        {
            // Kullanıcıyı SQL'den çekiyoruz
            var varOlanKullanici = await _context.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == request.Username.ToLower());
            if (varOlanKullanici == null)
            {
                return BadRequest("Kullanıcı adı veya şifre hatalı!");
            }

            bool sifreDogruMu = BCrypt.Net.BCrypt.Verify(request.Password, varOlanKullanici.Password);
            if (!sifreDogruMu)
            {
                return BadRequest("Kullanıcı adı veya şifre hatalı!");
            }

            var accessToken = GenerateJwtToken(varOlanKullanici);
            var refreshToken = GenerateRefreshToken();

            varOlanKullanici.RefreshToken = refreshToken;
            varOlanKullanici.RefreshTokenExpiration = DateTime.Now.AddDays(7);

            await _context.SaveChangesAsync(); // Refresh token bilgilerini SQL'de güncelle!

            return Ok(new
            {
                Message = "Giriş başarılı!",
                Token = accessToken,
                RefreshToken = refreshToken,
                Expiration = varOlanKullanici.RefreshTokenExpiration
            });
        }

        // 3. ADIM: REFRESH (TOKEN YENİLEME)
        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh([FromBody] string refreshToken)
        {
            if (string.IsNullOrEmpty(refreshToken))
            {
                return BadRequest("Refresh token boş olamaz!");
            }

            var cleanToken = refreshToken.Replace("\"", "").Replace("string", "").Trim();

            // Temizlenen token değerini SQL Server tablosunda arıyoruz
            var user = await _context.Users.FirstOrDefaultAsync(u => u.RefreshToken == cleanToken);

            if (user == null || user.RefreshTokenExpiration <= DateTime.Now)
            {
                return Unauthorized("Geçersiz veya süresi dolmuş Refresh Token!");
            }

            var newAccessToken = GenerateJwtToken(user);
            var newRefreshToken = GenerateRefreshToken();

            user.RefreshToken = newRefreshToken;
            user.RefreshTokenExpiration = DateTime.Now.AddDays(7);

            await _context.SaveChangesAsync(); // Yeni refresh token'ı SQL'e işle!

            return Ok(new
            {
                Token = newAccessToken,
                RefreshToken = newRefreshToken,
                Expiration = user.RefreshTokenExpiration
            });
        }

        private string GenerateJwtToken(User user)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes("BurayaGisliKeyiniziYazin1234567890!");

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.Name, user.Username),
                    new Claim(ClaimTypes.Role, user.Role)
                }),
                Expires = DateTime.UtcNow.AddMinutes(15),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        private string GenerateRefreshToken()
        {
            return Guid.NewGuid().ToString();
        }
    }
}