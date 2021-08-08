using System.Collections.Generic;
using Shared.Library.Models;

namespace BooksTextsSplit.Library.Models
{
    public class BooksVersionsExistInDb
    {
        public IList<SelectedBookIdGroupByLanguageId> SelectedBookIdAllVersions { get; set; }
    }

    public class SelectedBookIdGroupByLanguageId
    {
        public int LanguageId { get; set; }
        public IList<TextSentence> Sentences { get; set; }
    }
}

