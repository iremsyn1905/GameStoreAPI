namespace GameStoreAPI.DTOs
{
    public class GameQueryParameters

    {
        public string? SearchName { get; set; }
        public string? Genre { get; set; }
        public string? SortBy { get; set; }
        public bool IsDescending { get; set; } = false;
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 5;
    }
}
