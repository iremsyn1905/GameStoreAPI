namespace GameStoreAPI.DTOs
{
    public class CreateGameDto
    {
        public string Name { get; set; }
        public string Genre { get; set; }
        public object Rating { get; set; } // 👈 Kelimeleri içeri alabilmek için object!
        public bool IsInstalled { get; set; }
    }
}