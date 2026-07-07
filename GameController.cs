using GameStoreAPI.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Data;
using System.Linq;


namespace GameStoreAPI.Controllers
{
    [Route("API/Controller")]
    [ApiController]
    [Authorize]
    public class GameController : ControllerBase
    {
        private static List<GameItem> _gamelist = new List<GameItem>
        {
            new GameItem{ Id= 1, Name="GTA V", Genre="Suç/Aksiyon", Rating=9.0, IsInstalled=true},
            new GameItem{ Id=2, Name="Cyberpunk 2077", Genre="Akisyon/Suç", Rating=8.8, IsInstalled=false}

        };


        //Get:Api/Game
        [HttpGet]
        public IActionResult GetAll()
        {
            return Ok(_gamelist);
        }
        //Get:Api/Game/1
        [HttpGet("{id}")]
        public IActionResult GetById(int id)
        {
            var game = _gamelist.FirstOrDefault(g => g.Id == id);
            if (game == null)
            {
                return NotFound(new { Message = $"{id} ID'li oyun bulunmuyor" });
            }
            return Ok(game);

        }

        //Post:Api/Game
        [HttpPost]
        [Authorize(Roles = "Admin")]
        [HttpPost("oyun-ekle")]
        ///<summary>
        ///Yeni oyun ekle.
        /// </summary>
        /// <remarks>
        /// Bu metotu tetiklemek için sağ üstteki kilit butonundan (Bearer Token) giriş yapılmış olması gerekir.
        /// </remarks>
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public IActionResult Create([FromBody] CreateGameDto newGameDto)
        {
            if (newGameDto == null)
            {
                return BadRequest(newGameDto);
            }
            var game = new GameItem
            {
                Id = _gamelist.Count > 1 ? _gamelist.Max(g => g.Id) + 1 : 1,
                Name = newGameDto.Name,
                Genre = newGameDto.Genre,
                Rating = newGameDto.Rating,
                IsInstalled = newGameDto.IsInstalled

            };
            _gamelist.Add(game);
            return CreatedAtAction(nameof(GetById), new { id = game.Id }, game);
        }


        //Put:Api/Game/1
        [HttpPut("{id}")]
        public IActionResult Update(int id, [FromBody] CreateGameDto updatedGameDto)
        {
            var game = _gamelist.FirstOrDefault(g => g.Id == id);
            if (updatedGameDto == null)
            {
                return NotFound("Güncellemek istenilen oyun bulunamadı");
            }

            game.Name = updatedGameDto.Name;
            game.Genre = updatedGameDto.Genre;
            game.Rating = updatedGameDto.Rating;
            game.IsInstalled = updatedGameDto.IsInstalled;

            return NotFound();
        }

        //Delete:Api/Game/1
        [HttpDelete("{id}")]
        public IActionResult Delete(int id)
        {
            var game = _gamelist.FirstOrDefault(g => g.Id == id);
            if (game == null)
            {
                return NotFound("Silmek istediğiniz oyun bulunamadı");
            }
            _gamelist.Remove(game);
            return NotFound();
            
        }


         }
    } 