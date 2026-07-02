namespace GameStoreAPI.DTOs
{
    public class CreateGameDto
    {
        public string Name { get; set; }
        public string Genre { get; set; }
        public double Rating { get; set; }
        public bool IsInstalled { get; set; }

    }
}
