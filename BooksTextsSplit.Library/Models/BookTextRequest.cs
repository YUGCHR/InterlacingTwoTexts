using System.Collections.Generic;
using Shared.Library.Models;

namespace BooksTextsSplit.Library.Models
{
    public class BookTextRequest
    {
        public int LanguageId { get; set; }
        public List<TextSentence> Text { get; set; }
    }
}
