using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Notea.Modules.Subject.ViewModels
{
    public class MarkdownNoteViewModel
    {
        public ObservableCollection<MarkdownLineViewModel> Lines { get; set; }

        public MarkdownNoteViewModel()
        {
            Lines = new ObservableCollection<MarkdownLineViewModel>
        {
            new MarkdownLineViewModel { Content = "" } // 시작 시 1줄
        };
        }
    }
}
