using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Notea.Modules.Common.Helpers;
using Notea.Modules.Common.Models;
using Notea.ViewModels;
using System.Windows.Input;

namespace Notea.Modules.Common.ViewModels
{
    public class NoteManagerViewModel : ViewModelBase
    {
        private readonly DatabaseHelper _db = DatabaseHelper.Instance;

        public ObservableCollection<Note> Notes { get; } = new();

        private Note? _selectedNote;
        public Note? SelectedNote
        {
            get => _selectedNote;
            set
            {
                _selectedNote = value;
                OnPropertyChanged();
                IsNoteOpen = value != null;
            }
        }

        private bool _isNoteOpen;
        public bool IsNoteOpen
        {
            get => _isNoteOpen;
            set { _isNoteOpen = value; OnPropertyChanged(); }
        }

        public ICommand NewNoteCommand => new RelayCommand(CreateNewNote);
        public ICommand SaveNoteCommand => new RelayCommand(SaveNote);
        public ICommand CloseNoteCommand => new RelayCommand(() => SelectedNote = null);

        public void LoadNotes()
        {
            Notes.Clear();
            foreach (var note in _db.GetAllNotes())
                Notes.Add(note);
        }

        private void CreateNewNote()
        {
            var newNote = new Note { Content = "" };
            _db.SaveNote(newNote);
            LoadNotes();
            SelectedNote = Notes.FirstOrDefault(n => n.Content == "");
        }

        private void SaveNote()
        {
            if (SelectedNote != null)
            {
                _db.SaveNote(SelectedNote);
                LoadNotes();
                SelectedNote = Notes.FirstOrDefault(n => n.NoteId == SelectedNote.NoteId); // 다시 선택
            }
        }
    }
}
