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
using Microsoft.Extensions.Caching.Distributed; // Distributed Cache için kütüphane
using System.Text.Json; // Verileri JSON formatına çevirmek için
using System;

namespace GameStoreAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [EnableRateLimiting("FixedPolicy")]
    public class GameController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IDistributedCache _distributedCache; // IMemoryCache yerine IDistributedCache kullanıyoruz

        // Constructor'a yeni servisi enjekte ediyoruz
        public GameController(AppDbContext context, IDistributedCache distributedCache)
        {
            _context = context;
            _distributedCache = distributedCache;
        }

        // 🤖 SIFIR MALİYETLİ YAPAY ZEKA METODU (api/Game/ask-ai)
        // Bu metot bakiye istemez, OpenAI mantığını tamamen lokalde simüle eder!
        [HttpGet("ask-ai")]
        [AllowAnonymous] // Yapay zeka asistanıyla herkes konuşabilsin diye açık bıraktık
        public async Task<IActionResult> AskAI([FromQuery] string userMessage)
        {
            if (string.IsNullOrWhiteSpace(userMessage))
            {
                return BadRequest("Mesaj boş olamaz.");
            }

            string messageLower = userMessage.ToLower();
            string aiResponse = "";

            // 1. PROMPT ENGINEERING (Sistem Rolü) SİMÜLASYONU
            if (messageLower.Contains("makarna") || messageLower.Contains("yemek") || messageLower.Contains("hava durumu"))
            {
                aiResponse = "Ben GameStoreAPI asistanıyım. Sistem rolüm gereği sadece oyunlar ve yazılımla ilgili soruları cevaplandırabilirim. Size başka bir oyun konusunda yardımcı olabilir miyim? 🎮";
            }
            else if (messageLower.Contains("selam") || messageLower.Contains("merhaba") || messageLower.Contains("naber"))
            {
                aiResponse = "Harika bir gün İrem! 🌟 Ben GameStoreAPI yapay zeka asistanıyım. Sana oyun önerileri sunabilir, dünkü Redis cache mimarimiz hakkında bilgi verebilirim. Ne hakkında konuşalım?";
            }
            else if (messageLower.Contains("gta") || messageLower.Contains("grand theft auto"))
            {
                aiResponse = "Ooo, GTA V harika bir seçim! 🚗 PC platformunda oynaması inanılmaz keyiflidir. Özellikle soygun görevlerinde takım arkadaşlarınla harika bir deneyim yaşayabilirsin. Başka bir oyun önerisi ister misin?";
            }
            else if (messageLower.Contains("öneri") || messageLower.Contains("oyun öner"))
            {
                aiResponse = "Sana hemen 3 harika oyun önereyim:\n1. Witcher 3 (Harika bir RPG)\n2. Red Dead Redemption 2 (Muhteşem bir açık dünya)\n3. Cyberpunk 2077 (Geleceğin dünyası)\nHangisi ilgini çekti?";
            }
            else if (messageLower.Contains("redis") || messageLower.Contains("hız"))
            {
                aiResponse = "Dün kurduğumuz Redis cache yapısı sayesinde oyun listeleme isteğin artık SQL veri tabanına gitmeden mikro saniyeler içinde (ışık hızında ⚡) yanıtlanıyor!";
            }
            else
            {
                aiResponse = $"Gönderdiğin '{userMessage}' mesajını oyun kütüphanemde inceledim! Harika bir konu. Sana bu konuda destek olmaktan mutluluk duyarım. 🎮";
            }

            // 2. TOKEN LİMİTİ SİMÜLASYONU
            int maxAllowedWords = 40; // Maksimum 40 kelime sınırı
            var words = aiResponse.Split(' ');
            if (words.Length > maxAllowedWords)
            {
                aiResponse = string.Join(" ", words.Take(maxAllowedWords)) + "... [Cevap Token Limitine Ulaştığı İçin Sınırlandırıldı]";
            }

            // 3. YANIT FORMATLAMA VE TOKEN HESAPLAMA
            int promptTokens = userMessage.Length / 4;
            int completionTokens = aiResponse.Length / 4;
            int totalTokens = promptTokens + completionTokens;

            // Gerçekçilik katmak için 500ms küçük bir gecikme (Yapay zeka düşünüyor gibi)
            await Task.Delay(500);

            // OpenAI API çıktı formatıyla birebir aynı JSON yapısı
            return Ok(new
            {
                author = "AI Assistant (Simulated)",
                response = aiResponse,
                promptTokens = promptTokens,
                completionTokens = completionTokens,
                totalTokens = totalTokens
            });
        }

        // 1. GET: api/Game (Redis Entegrasyonlu - Ortak Dağıtık Cache ⚡🌐)
        [HttpGet]
        [Authorize] // 🔒 Sadece Login olmuş kayıtlı kullanıcılar (User veya Admin) oyunları listeleyebilir!
        public async Task<IActionResult> GetAll([FromQuery] GameQueryParameters queryParams)
        {
            string cacheKey = $"games_{queryParams.SearchName}_{queryParams.Genre}_{queryParams.SortBy}_{queryParams.IsDescending}_{queryParams.PageNumber}_{queryParams.PageSize}";

            string cachedDataJson = await _distributedCache.GetStringAsync(cacheKey);
            PagedResult<GameItem> result;

            if (!string.IsNullOrEmpty(cachedDataJson))
            {
                result = JsonSerializer.Deserialize<PagedResult<GameItem>>(cachedDataJson);
            }
            else
            {
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

                string jsonToCache = JsonSerializer.Serialize(result);

                var cacheOptions = new DistributedCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromMinutes(2))
                    .SetSlidingExpiration(TimeSpan.FromSeconds(45));

                await _distributedCache.SetStringAsync(cacheKey, jsonToCache, cacheOptions);
            }

            return Ok(result);
        }

        // 2. GET: api/Game/1
        [HttpGet("{id}")]
        [Authorize] // 🔒 Sadece Login olmuş kullanıcılar ID ile oyun detayına bakabilir!
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
        [Authorize(Roles = "Admin")] // 🛡️ Sadece Admin yetkisi olan girişler (İrem) oyun ekleyebilir!
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

            _context.Games.Add(game);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = game.Id }, game);
        }

        // 4. PUT: api/Game/1
        [HttpPut("{id}")]
        [Authorize(Roles = "Admin")] // 🛡️ Sadece Admin yetkisi olan girişler oyun güncelleyebilir!
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

            await _context.SaveChangesAsync();
            return Ok(game);
        }

        // 5. DELETE: api/Game/1
        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")] // 🛡️ Sadece Admin yetkisi olan girişler oyun silebilir!
        public async Task<IActionResult> Delete(int id)
        {
            var game = await _context.Games.FirstOrDefaultAsync(g => g.Id == id);
            if (game == null)
            {
                return NotFound("Silmek istediğiniz oyun bulunamadı");
            }

            _context.Games.Remove(game);
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }
}