using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Notea.Helpers;
using Notea.Modules.Monthly.Models;

namespace Notea.Modules.Monthly.ViewModels
{
    public class YearMonthListViewModel : INotifyPropertyChanged
    {
        private int _year;
        private ObservableCollection<YearMonthViewModel> _months;

        public int Year
        {
            get => _year;
            set
            {
                _year = value;
                OnPropertyChanged();
                LoadMonthComments();
            }
        }

        public ObservableCollection<YearMonthViewModel> Months
        {
            get => _months;
            set
            {
                _months = value;
                OnPropertyChanged();
            }
        }

        public YearMonthListViewModel()
        {
            Year = DateTime.Now.Year;
            Months = new ObservableCollection<YearMonthViewModel>();
            InitializeMonths();
            LoadMonthComments();
        }

        private void InitializeMonths()
        {
            for (int i = 1; i <= 12; i++)
            {
                Months.Add(new YearMonthViewModel { Month = i, Comment = "comment" });
            }
        }

        private void LoadMonthComments()
        {
            try
            {
                var comments = MonthlyCommentRepository.GetYearComments(Year);

                foreach (var month in Months)
                {
                    if (comments.ContainsKey(month.Month))
                    {
                        month.Comment = comments[month.Month];
                    }
                    else
                    {
                        month.Comment = "comment";
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] 연간 코멘트 로드 실패: {ex.Message}");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}