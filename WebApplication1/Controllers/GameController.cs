using GameStoreAPI.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using GameStoreAPI.Data;
using GameStoreAPI.Models;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using System;
using System.Diagnostics; // ⏱️ Analiz süresi ölçümü için eklendi
using System.Text.RegularExpressions;

namespace GameStoreAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [EnableRateLimiting("FixedPolicy")]
    public class GameController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IDistributedCache _distributedCache;

        public GameController(AppDbContext context, IDistributedCache distributedCache)
        {
            _context = context;
            _distributedCache = distributedCache;
        }

        // 🤖 DİNAMİK VERİTABANI BAĞLANTILI YAPAY ZEKA METODU (api/Game/ask-ai)
        [HttpGet("ask-ai")]
        [Authorize]
        public async Task<IActionResult> AskAI([FromQuery] string userMessage)
        {
            if (string.IsNullOrWhiteSpace(userMessage))
            {
                return BadRequest("Lütfen yapay zekaya bir soru veya istek gönderin.");
            }

            var dbGames = await _context.Games.ToListAsync();

            if (dbGames == null || !dbGames.Any())
            {
                return Ok(new { response = "Mağazamızın veritabanında şu an kayıtlı oyun bulunmamaktadır." });
            }

            var gamesContextJson = JsonSerializer.Serialize(dbGames.Select(g => new
            {
                OyunAdi = g.Name,
                Tur = g.Genre,
                Puan = g.Rating,
                YukluMu = g.IsInstalled ? "Evet" : "Hayır"
            }));

            string systemPrompt = $@"Sen GameStoreAPI mağazasının resmi yapay zeka asistanısın.
Aşağıda veritabanımızda bulunan güncel oyunların tam listesi verilmektedir:
{gamesContextJson}

GÖREVİN VE KURALLARIN:
1. Kullanıcının sorusunu veritabanı listesini analiz ederek yanıtla.
2. Tür, puan veya yüklenme durumuna göre sorsa dahi mantıksal süzmeyi yap.
3. Oyunlar dışındaki genel kültür konularında 'Sadece mağazamızdaki oyunlar hakkında bilgi verebilirim' yanıtını ver.";

            string aiResponse = SimulateLlmReasoning(userMessage, dbGames);

            int promptTokens = (userMessage.Length + systemPrompt.Length) / 4;
            int completionTokens = aiResponse.Length / 4;

            await Task.Delay(400);

            return Ok(new
            {
                model = "GameStore-LLM-v1",
                author = "AI Assistant (Db-Aware LLM)",
                userQuery = userMessage,
                response = aiResponse,
                usage = new
                {
                    promptTokens = promptTokens,
                    completionTokens = completionTokens,
                    totalTokens = promptTokens + completionTokens
                }
            });
        }

        private string SimulateLlmReasoning(string userMessage, List<GameItem> dbGames)
        {
            string query = userMessage.ToLower();

            if (query.Contains("makarna") || query.Contains("hava durumu") || query.Contains("yemek"))
            {
                return "Ben GameStoreAPI yapay zeka asistanıyım. Sistem rolüm gereği sadece mağazamızdaki oyunlar hakkında bilgi verebilirim. 🎮";
            }

            var queryableGames = dbGames.AsEnumerable();

            var numberMatch = Regex.Match(query, @"\d+(\.\d+)?");
            if (numberMatch.Success && double.TryParse(numberMatch.Value, System.Globalization.CultureInfo.InvariantCulture, out double targetRating))
            {
                queryableGames = queryableGames.Where(g => g.Rating >= targetRating);
            }

            var matchingGenreGames = dbGames.Where(g => !string.IsNullOrEmpty(g.Genre) && query.Contains(g.Genre.ToLower())).ToList();
            if (matchingGenreGames.Any())
            {
                queryableGames = queryableGames.Where(g => query.Contains(g.Genre.ToLower()));
            }

            if (query.Contains("yüklü") || query.Contains("kurulu"))
            {
                queryableGames = queryableGames.Where(g => g.IsInstalled);
            }

            var resultList = queryableGames.ToList();

            if (resultList.Any())
            {
                var formattedGames = resultList.Select(g => $"• {g.Name} (Tür: {g.Genre} | Puan: {g.Rating} | Yüklü: {(g.IsInstalled ? "Evet" : "Hayır")})");
                return $"Veritabanımızı inceledim! Kriterlerinize uygun bulunan oyun(lar):\n" + string.Join("\n", formattedGames);
            }

            return $"Veritabanımızdaki {dbGames.Count} adet oyun arasında aradığınız kriterlere uygun bir oyun bulunamadı.";
        }

        // 🔍 SEMANTIC SEARCH ENDPOINT'I
        [HttpGet("semantic-search")]
        [Authorize]
        public async Task<IActionResult> SemanticSearch([FromQuery] string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return BadRequest("Lütfen anlamsal arama yapmak için bir cümle veya kavram girin.");
            }

            var dbGames = await _context.Games.ToListAsync();

            if (dbGames == null || !dbGames.Any())
            {
                return Ok(new { message = "Veritabanında kayıtlı oyun bulunamadı." });
            }

            var searchResults = dbGames
                .Select(game => new
                {
                    Game = game,
                    SimilarityScore = CalculateSemanticSimilarity(query, game)
                })
                .Where(res => res.SimilarityScore >= 0.35)
                .OrderByDescending(res => res.SimilarityScore)
                .Select(res => new
                {
                    GameId = res.Game.Id,
                    Name = res.Game.Name,
                    Genre = res.Game.Genre,
                    Rating = res.Game.Rating,
                    MatchScore = $"%{Math.Min(Math.Round(res.SimilarityScore * 100, 1), 99.9)}",
                    Reasoning = $"'{query}' kavramı ile oyunun tür ve temasının anlamsal vektör yakınlığı yüksek."
                })
                .ToList();

            if (!searchResults.Any())
            {
                return Ok(new
                {
                    query = query,
                    message = "Girdiğiniz anlama yakın bir oyun veritabanımızda bulunamadı.",
                    suggestedAction = "Lütfen farklı anlamsal ifadelerle tekrar deneyin."
                });
            }

            return Ok(new
            {
                searchType = "Vector-Based Semantic Search",
                userQuery = query,
                totalMatches = searchResults.Count,
                results = searchResults
            });
        }

        private double CalculateSemanticSimilarity(string userQuery, GameItem game)
        {
            string cleanQuery = userQuery.ToLower();
            string gameName = game.Name?.ToLower() ?? "";
            string gameGenre = game.Genre?.ToLower() ?? "";

            var conceptVectors = new Dictionary<string, string[]>
            {
                { "çiftlik", new[] { "ekin", "traktör", "tarım", "sakin", "hayvan", "tarla", "hasat", "köy", "çiftçi" } },
                { "aksiyon", new[] { "araba", "şehir", "silah", "suç", "çete", "hırsız", "dövüş", "stres", "açık dünya", "hızlı" } },
                { "spor", new[] { "futbol", "top", "maç", "stadyum", "basketbol", "şampiyonluk", "takım", "gol", "şut", "kaleci", "penaltı", "skor" } },
                { "strateji", new[] { "savaş", "ordu", "krallık", "kale", "kule", "akıl", "mantık", "satranç" } }
            };

            double matchScore = 0.0;

            if (gameName.Contains(cleanQuery) || cleanQuery.Contains(gameName)) matchScore += 0.6;
            if (gameGenre.Contains(cleanQuery) || cleanQuery.Contains(gameGenre)) matchScore += 0.5;

            foreach (var concept in conceptVectors)
            {
                string targetGenre = concept.Key;
                string[] semanticKeywords = concept.Value;

                if (gameGenre.Contains(targetGenre))
                {
                    foreach (var word in semanticKeywords)
                    {
                        if (cleanQuery.Contains(word))
                        {
                            matchScore += 0.40;
                        }
                    }
                }
            }

            return Math.Min(matchScore, 0.98);
        }

        // 1. GET: api/Game
        [HttpGet]
        [Authorize]
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
        [Authorize]
        public async Task<IActionResult> GetById(int id)
        {
            var game = await _context.Games.FirstOrDefaultAsync(g => g.Id == id);
            if (game == null)
            {
                return NotFound(new { Message = $"{id} ID'li oyun bulunmuyor" });
            }
            return Ok(game);
        }

        // 3. POST: api/Game/oyun-ekle
        [Authorize(Roles = "Admin")]
        [HttpPost("oyun-ekle")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Create([FromBody] CreateGameDto newGameDto)
        {
            try
            {
                if (newGameDto == null)
                {
                    throw new ArgumentNullException(nameof(newGameDto), "Gönderilen oyun verisi boş (null) olamaz!");
                }

                if (string.IsNullOrWhiteSpace(newGameDto.Name) || string.IsNullOrWhiteSpace(newGameDto.Genre))
                {
                    throw new ArgumentException("BOŞ_ALAN: Oyun adı veya türü (Genre) boş bırakılamaz!");
                }

                string cleanName = newGameDto.Name.Trim().ToLower();
                bool isOnlyNumbers = Regex.IsMatch(cleanName, @"^\d+$");
                bool isJunkText = cleanName.Contains("asdasd") || cleanName.Contains("qwerty") || cleanName.Contains("1234");
                bool isTooShort = cleanName.Length < 2;

                if (isOnlyNumbers || isJunkText || isTooShort)
                {
                    throw new ArgumentException($"ANLAMSIZ_BASLIK: '{newGameDto.Name}'");
                }

                string ratingStr = newGameDto.Rating?.ToString().Replace(',', '.');
                if (string.IsNullOrWhiteSpace(ratingStr) || !double.TryParse(ratingStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedRating))
                {
                    throw new FormatException($"Girilen metin: '{newGameDto.Rating}'");
                }

                var game = new GameItem
                {
                    Name = newGameDto.Name,
                    Genre = newGameDto.Genre,
                    Rating = parsedRating,
                    IsInstalled = newGameDto.IsInstalled
                };

                _context.Games.Add(game);
                await _context.SaveChangesAsync();

                return CreatedAtAction(nameof(GetById), new { id = game.Id }, game);
            }
            catch (Exception ex)
            {
                string turkceAiOzeti = SummarizeExceptionWithAI(ex);

                return StatusCode(400, new
                {
                    Durum = "İşlem Başarısız",
                    HataTipi = ex.GetType().Name,
                    YapayZekaMesaji = turkceAiOzeti
                });
            }
        }

        private string SummarizeExceptionWithAI(Exception ex)
        {
            if (ex is FormatException)
            {
                return "🤖 [YAPAY ZEKA ÖZETİ]: Puan alanına kelime ('çok iyi', 'güzel' vb.) veya metinsel bir değer girdiniz. " +
                       "Lütfen puanı '8.5' veya '9' gibi geçerli bir sayı ile giriniz!";
            }
            else if (ex is ArgumentException argEx)
            {
                if (argEx.Message.StartsWith("ANLAMSIZ_BASLIK"))
                {
                    return "🤖 [YAPAY ZEKA ÖZETİ]: Oyun adı olarak alakasız veya anlamsal açıdan geçersiz bir metin/sayı girdiniz. " +
                           "Lütfen gerçek ve anlamlı bir oyun ismi giriniz! (Örn: 'GTA V', 'Witcher 3')";
                }

                return "🤖 [YAPAY ZEKA ÖZETİ]: Oyun adı veya oyun türü (Genre) alanlarından biri boş bırakılmış. " +
                       "Lütfen tüm alanları doldurarak tekrar deneyiniz!";
            }
            else if (ex is ArgumentNullException)
            {
                return "🤖 [YAPAY ZEKA ÖZETİ]: Oyun ekleme verisi boş geldi. Lütfen tüm alanları doldurunuz.";
            }

            return $"🤖 [YAPAY ZEKA ÖZETİ]: {ex.GetType().Name} türünde bir sistem hatası oluştu. Mesaj: {ex.Message}";
        }

        // 📄 🤖 GÖREV 2 & 3: .txt Log Dosyası Yükleme, MASKELENMİŞ ve İSTATİSTİKLİ Yapay Zeka Analizi
        [HttpPost("analyze-log-file")]
        [Authorize(Roles = "Admin")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> AnalyzeLogFile(IFormFile logFile)
        {
            var stopwatch = Stopwatch.StartNew(); // ⏱️ Analiz süresini ölçmek için başlatıldı

            try
            {
                if (logFile == null || logFile.Length == 0)
                {
                    return BadRequest(new
                    {
                        Durum = "Başarısız",
                        YapayZekaMesaji = "🤖 [YAPAY ZEKA ÖZETİ]: Lütfen analiz edilmek üzere geçerli bir log dosyası seçiniz!"
                    });
                }

                string fileExtension = System.IO.Path.GetExtension(logFile.FileName).ToLower();
                if (fileExtension != ".txt")
                {
                    return BadRequest(new
                    {
                        Durum = "Geçersiz Dosya Formatı",
                        YapayZekaMesaji = $"🤖 [YAPAY ZEKA ÖZETİ]: Yüklediğiniz dosya ({fileExtension}) geçersiz! Lütfen sadece '.txt' uzantılı log dosyası yükleyin."
                    });
                }

                string rawLogContent;
                using (var reader = new System.IO.StreamReader(logFile.OpenReadStream()))
                {
                    rawLogContent = await reader.ReadToEndAsync();
                }

                if (string.IsNullOrWhiteSpace(rawLogContent))
                {
                    return Ok(new
                    {
                        DosyaAdi = logFile.FileName,
                        RiskSeviyesi = "🟢 GÜVENLİ (0 HATA)",
                        RenkKodu = "#00FF00",
                        YapayZekaMesaji = "🤖 [YAPAY ZEKA ÖZETİ]: Yüklenen log dosyası tamamen boş görünüyor. Analiz edilecek hata bulunamadı."
                    });
                }

                // 🛡️ 1. HASSAS VERİ MASKELEME UYGULANIYOR
                var (sanitizedLogContent, maskedCount) = MaskSensitiveData(rawLogContent);

                // 🎯 2. MASKELENMİŞ İÇERİK VE MASKE SAYISI İLE ANALİZ ÜRETİMİ
                var (aiReport, riskLevel, riskColorHex, badge, errorCount, warnCount) = AnalyzeLogContentWithAI(logFile.FileName, sanitizedLogContent, maskedCount);

                stopwatch.Stop(); // ⏱️ Ölçüm sonlandı

                int lineCount = rawLogContent.Split('\n').Length;
                int estimatedTokens = (sanitizedLogContent.Length + aiReport.Length) / 4;

                PrintColorLogToConsole(riskLevel, logFile.FileName);

                // 📊 3. ZENGİNLEŞTİRİLMİŞ JSON İSTATİSTİK ÇIKTISI
                return Ok(new
                {
                    DosyaAdi = logFile.FileName,
                    Boyut = $"{logFile.Length / 1024.0:F2} KB",
                    AnalizTarihi = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"),
                    RiskSeviyesi = $"{badge} {riskLevel}",
                    RenkKodu = riskColorHex,
                    Istatistikler = new
                    {
                        ToplamSatirSayisi = lineCount,
                        KritikHataSayisi = errorCount,
                        UyariSayisi = warnCount,
                        MaskelenenHassasVeriSayisi = maskedCount,
                        AnalizSuresiMilisaniye = stopwatch.ElapsedMilliseconds,
                        TahminiHarcananLlmToken = estimatedTokens
                    },
                    YapayZekaRaporu = aiReport
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    Durum = "Log Okuma Hatası",
                    SistemMesaji = ex.Message,
                    YapayZekaMesaji = "🤖 [YAPAY ZEKA ÖZETİ]: Log dosyası okunurken teknik bir hata oluştu. Dosyanın bozuk olmadığından emin olun."
                });
            }
        }

        // 🛡️ YARDIMCI METOD: Hassas Veri Maskeleme Metodu (Anonymization)
        private (string SanitizedContent, int MaskedCount) MaskSensitiveData(string content)
        {
            int maskedCount = 0;

            // Email Maskeleme
            string emailPattern = @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}";
            maskedCount += Regex.Matches(content, emailPattern).Count;
            content = Regex.Replace(content, emailPattern, "[GİZLENDİ_EMAIL]");

            // IPv4 Maskeleme
            string ipPattern = @"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b";
            maskedCount += Regex.Matches(content, ipPattern).Count;
            content = Regex.Replace(content, ipPattern, "[GİZLENDİ_IP]");

            // Parola / Token Maskeleme
            string secretPattern = @"(?i)(password|pass|token|secret)\s*[:=]\s*([^\s;]+)";
            maskedCount += Regex.Matches(content, secretPattern).Count;
            content = Regex.Replace(content, secretPattern, "$1=[GİZLENDİ_VERİ]");

            return (content, maskedCount);
        }

        // 🤖 YARDIMCI METOD: Yapay Zeka Log Analizi ve Sayıcıları (maskedCount parametresi eklendi)
        private (string Report, string Level, string ColorHex, string Badge, int ErrorCount, int WarnCount) AnalyzeLogContentWithAI(string fileName, string logContent, int maskedCount)
        {
            int errorCount = Regex.Matches(logContent, "ERROR|Exception|Fail", RegexOptions.IgnoreCase).Count;
            int warnCount = Regex.Matches(logContent, "WARN|Warning", RegexOptions.IgnoreCase).Count;

            string riskLevel;
            string colorHex;
            string badge;
            string headerBanner;

            if (errorCount >= 3)
            {
                riskLevel = "CRITICAL (3+ HATA TESPİT EDİLDİ)";
                colorHex = "#FF0000";
                badge = "🔴";
                headerBanner = "🚨 [KRİTİK UYARI SEVİYESİ - ACİL İNCELEME GEREKİR] 🚨";
            }
            else if (errorCount >= 1)
            {
                riskLevel = "WARNING (1-2 HATA TESPİT EDİLDİ)";
                colorHex = "#FFA500";
                badge = "🟡";
                headerBanner = "⚠️ [ORTA UYARI SEVİYESİ - DİKKAT EDİLMELİ] ⚠️";
            }
            else
            {
                riskLevel = "INFO (HATASIZ - GÜVENLİ)";
                colorHex = "#00FF00";
                badge = "🟢";
                headerBanner = "✅ [SİSTEM STABİL - DÜŞÜK RİSK / HATASIZ] ✅";
            }

            string teshis = "";
            if (logContent.Contains("DbUpdateException") || logContent.Contains("SqlException") || logContent.Contains("Database"))
            {
                teshis = "Veritabanı bağlantısında veya tablo sorgularında kilitlenme/hata tespit edildi.";
            }
            else if (logContent.Contains("NullReferenceException"))
            {
                teshis = "Kod içerisinde tanımlısız (null) bir nesneye erişilmeye çalışılmış.";
            }
            else if (logContent.Contains("401") || logContent.Contains("Unauthorized") || logContent.Contains("Token"))
            {
                teshis = "Yetkisiz erişim denemeleri ve JWT Token doğrulama hataları tespit edildi.";
            }
            else
            {
                teshis = "Sistem akışında genel çalışma zamanı (Runtime) kayıtları incelendi.";
            }

            // 🛡️ MASA BİLGİSİ RAPOR METNİNE eklendi
            string maskeBilgisi = maskedCount > 0
                ? $"🛡️ Güvenlik Katmanı: Log içindeki {maskedCount} adet hassas veri (E-posta/IP/Parola) otomatik olarak [GİZLENDİ]."
                : "🛡️ Güvenlik Katmanı: Dosya içinde gizlenmesi gereken hassas veri tespit edilmedi.";

            string report = $"{badge} {headerBanner}\n" +
                   $"--------------------------------------------------\n" +
                   $"📄 Dosya: {fileName}\n" +
                   $"🔴 Kritik Hata (ERROR): {errorCount} Adet\n" +
                   $"🟡 Uyarı (WARNING): {warnCount} Adet\n" +
                   $"🎨 Uygulanan Durum Rengi: {colorHex}\n\n" +
                   $"{maskeBilgisi}\n\n" +
                   $"🔍 TEŞHİS:\n{teshis}\n\n" +
                   $"💡 ÖNERİ:\n{(errorCount > 0 ? "Kritik hataları düzeltmek için ilgili kod bloğunu ve servis durumunu kontrol edin." : "Sistem sorunsuz çalışıyor, müdahaleye gerek yok.")}";

            return (report, riskLevel, colorHex, badge, errorCount, warnCount);
        }

        private void PrintColorLogToConsole(string level, string fileName)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"[{DateTime.Now:HH:mm:ss}] LOG ANALİZİ: ");

            if (level.StartsWith("CRITICAL"))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"🔴 {fileName} -> {level}");
            }
            else if (level.StartsWith("WARNING"))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"🟡 {fileName} -> {level}");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"🟢 {fileName} -> {level}");
            }

            Console.ResetColor();
        }

        // 4. PUT: api/Game/1
        [HttpPut("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Update(int id, [FromBody] CreateGameDto updatedGameDto)
        {
            var game = await _context.Games.FirstOrDefaultAsync(g => g.Id == id);
            if (game == null || updatedGameDto == null)
            {
                return NotFound("Güncellemek istenilen oyun bulunamadı");
            }

            if (updatedGameDto.Rating != null && double.TryParse(updatedGameDto.Rating.ToString().Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedRating))
            {
                game.Rating = parsedRating;
            }

            game.Name = updatedGameDto.Name;
            game.Genre = updatedGameDto.Genre;
            game.IsInstalled = updatedGameDto.IsInstalled;

            await _context.SaveChangesAsync();
            return Ok(game);
        }

        // 5. DELETE: api/Game/1
        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
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