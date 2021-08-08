using Microsoft.EntityFrameworkCore;
using Shared.Library.Models;

namespace BooksTextsSplit.Library.Models
{
    public class BookContext : DbContext
    {
        public BookContext(DbContextOptions<BookContext> options)
            : base(options)
        {
        }

        public DbSet<TextSentence> BookTexts { get; set; }
        
    }
}
