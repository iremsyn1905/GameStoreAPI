using Microsoft.EntityFrameworkCore;
using GameStoreAPI.Models;

namespace GameStoreAPI.Data
{
    // İsmini AppDbContext yapıyoruz ki klasör olan Data ile karışmasın
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<GameItem> Games { get; set; }
        public DbSet<User> Users { get; set; } // Bu satırı ekledik!
    }
}