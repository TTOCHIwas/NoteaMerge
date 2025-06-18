using Notea.Modules.Subject.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Notea.Modules.Subject.Models
{
    public class NoteCategory
    {
        public int CategoryId { get; set; }
        public string Title { get; set; }
        public int DisplayOrder { get; set; }

        public int Level { get; set; }
        public int ParentCategoryId { get; set; }

        public List<NoteLine> Lines { get; set; } = new List<NoteLine>();
        public List<NoteCategory> SubCategories { get; set; } = new List<NoteCategory>();

    }
}
