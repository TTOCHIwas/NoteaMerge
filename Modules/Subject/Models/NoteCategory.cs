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
        public int Level { get; set; } = 1;
        public ObservableCollection<NoteLine> Lines { get; set; } = new();
        public List<NoteCategory> SubCategories { get; set; } = new();
    }
}
