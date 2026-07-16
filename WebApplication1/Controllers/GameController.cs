using GameStoreAPI.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using GameStoreAPI.Data; // Bizim AppDbContext burada yaşıyor
using GameStoreAPI.Models;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Distributed; // 🚀 YENİ: Distributed Cache için kütüphane
using System.Text.Json; // 🚀 YENİ: Verileri JSON formatına çevirmek için

namespace GameStoreAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [EnableRateLimiting("FixedPolicy")]
    public class GameController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IDistributedCache _distributedCache; // 🚀 GÜNCELLENDİ: IMemoryCache yerine IDistributedCache kullanıyoruz

        // Constructor'a yeni servisi enjekte ediyoruz
        public GameController(AppDbContext context, IDistributedCache distributedCache)
        {
            _context = context;
            _distributedCache = distributedCache;
        }

        // 1. GET: api/Game (Redis Entegrasyonlu - Ortak Dağıtık Cache ⚡🌐)
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetAll([FromQuery] GameQueryParameters queryParams)
        {
            string cacheKey = $"games_{queryParams.SearchName}_{queryParams.Genre}_{queryParams.SortBy}_{queryParams.IsDescending}_{queryParams.PageNumber}_{queryParams.PageSize}";

            // 1. Adım: Redis'ten bu anahtara ait veriyi (yazı olarak) çekmeye çalışıyoruz
            string cachedDataJson = await _distributedCache.GetStringAsync(cacheKey);
            PagedResult<GameItem> result;

            if (!string.IsNullOrEmpty(cachedDataJson))
            {
                // 2. Adım: Eğer veri Redis'te VARSA, JSON yazısını tekrar C# objesine dönüştürüyoruz (Işık hızı! ⚡)
                result = JsonSerializer.Deserialize<PagedResult<GameItem>>(cachedDataJson);
            }
            else
            {
                // 3. Adım: Eğer veri Redis'te YOKSA, SQL veri tabanına gidip sorguluyoruz
                var query = _context.Games.AsQueryable();

                if (!string.IsNullOrWhiteSpace(queryParams.SearchName))
                {
                    query = query.Where(g => g.Name.Contains(queryParams.SearchName));
                }

                if (!string.IsNullOrWhiteSpace(queryParams.Genre))
                {
                    query = query.Where(g => g.Genre == queryParams.Genre);
                }

                int totalCount = await query.CountAsync();

                if (!string.IsNullOrWhiteSpace(queryParams.SortBy))
                {
                    if (queryParams.SortBy.Equals("Rating", StringComparison.OrdinalIgnoreCase))
                    {
                        query = queryParams.IsDescending
                            ? query.OrderByDescending(g => g.Rating)
                            : query.OrderBy(g => g.Rating);
                    }
                    else if (queryParams.SortBy.Equals("Name", StringComparison.OrdinalIgnoreCase))
                    {
                        query = queryParams.IsDescending
                            ? query.OrderByDescending(g => g.Name)
                            : query.OrderBy(g => g.Name);
                    }
                }
                else
                {
                    query = query.OrderBy(g => g.Id);
                }

                var pagedData = await query
                    .Skip((queryParams.PageNumber - 1) * queryParams.PageSize)
                    .Take(queryParams.PageSize)
                    .ToListAsync();

                result = new PagedResult<GameItem>
                {
                    TotalCount = totalCount,
                    PageNumber = queryParams.PageNumber,
                    PageSize = queryParams.PageSize,
                    Data = pagedData
                };

                // 4. Adım: SQL'den aldığımız bu veriyi JSON yazısına dönüştürüp Redis'e yazıyoruz
                string jsonToCache = JsonSerializer.Serialize(result);

                var cacheOptions = new DistributedCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromMinutes(2)) // 2 dakika boyunca Redis'te saklansın
                    .SetSlidingExpiration(TimeSpan.FromSeconds(45)); // 45 saniye istek gelmezse silinsin

                await _distributedCache.SetStringAsync(cacheKey, jsonToCache, cacheOptions);
            }

            // 5. Adım: Kullanıcıya sonucu dönüyoruz
            return Ok(result);
        }

        // 2. GET: api/Game/1
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var game = await _context.Games.FirstOrDefaultAsync(g => g.Id == id);
            if (game == null)
            {
                return NotFound(new { Message = $"{id} ID'li oyun bulunmuyor" });
            }
            return Ok(game);
        }

        // 3. POST: api/Game/oyun-ekle (Sadece Admin ekleyebilir)
        [Authorize(Roles = "Admin")]
        [HttpPost("oyun-ekle")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Create([FromBody] CreateGameDto newGameDto)
        {
            if (newGameDto == null)
            {
                return BadRequest();
            }

            var game = new GameItem
            {
                Name = newGameDto.Name,
                Genre = newGameDto.Genre,
                Rating = newGameDto.Rating,
                IsInstalled = newGameDto.IsInstalled
            };

            _context.Games.Add(game); // Veriyi sıraya ekle
            await _context.SaveChangesAsync(); // SQL'e kalıcı olarak yaz!

            return CreatedAtAction(nameof(GetById), new { id = game.Id }, game);
        }

        // 4. PUT: api/Game/1
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] CreateGameDto updatedGameDto)
        {
            var game = await _context.Games.FirstOrDefaultAsync(g => g.Id == id);
            if (game == null || updatedGameDto == null)
            {
                return NotFound("Güncellemek istenilen oyun bulunamadı");
            }

            game.Name = updatedGameDto.Name;
            game.Genre = updatedGameDto.Genre;
            game.Rating = updatedGameDto.Rating;
            game.IsInstalled = updatedGameDto.IsInstalled;

            await _context.SaveChangesAsync(); // Değişiklikleri SQL'e işle
            return Ok(game);
        }

        // 5. DELETE: api/Game/1
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var game = await _context.Games.FirstOrDefaultAsync(g => g.Id == id);
            if (game == null)
            {
                return NotFound("Silmek istediğiniz oyun bulunamadı");
            }

            _context.Games.Remove(game);
            await _context.SaveChangesAsync(); // SQL'den kalıcı olarak uçur!
            return NoContent();
        }
    }
}