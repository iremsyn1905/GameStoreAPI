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

namespace GameStoreAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class GameController : ControllerBase
    {
        private readonly AppDbContext _context;

        // Statik listeyi sildik, yerine SQL köprümüzü constructor ile içeri aldık
        public GameController(AppDbContext context)
        {
            _context = context;
        }

        // 1. GET: api/Game (Dinamik Filtreleme, Sıralama ve Sayfalama ile Güçlendirildi)
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetAll([FromQuery] GameQueryParameters queryParams)
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

            var result = new PagedResult<GameItem>
            {
                TotalCount = totalCount,
                PageNumber = queryParams.PageNumber,
                PageSize = queryParams.PageSize,
                Data = pagedData
            };

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
                // Id vermiyoruz, SQL Server (Identity alanı) otomatik 1, 2, 3 diye kendi artıracak
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