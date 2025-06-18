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
            Months = new ObservableCollection<YearMonthViewModel>();
            Year = DateTime.Now.Year;
            
            InitializeMonths();
            LoadMonthComments();
        }

        private void InitializeMonths()
        {
            for (int i = 1; i <= 12; i++)
            {
                Months.Add(new YearMonthViewModel { Month = i, Year = Year,Comment = "comment" });
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
                        month.Comment = "";
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] 연간 코멘트 로드 실패: {ex.Message}");
            }
        }

        public void RefreshYearData()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[YearMonthListViewModel] {Year}년 데이터 새로고침");

                // 현재 년도의 월별 코멘트 다시 로드
                LoadMonthComments();

                // 각 월 항목의 Year도 업데이트
                foreach (var month in Months)
                {
                    month.Year = Year;
                }

                System.Diagnostics.Debug.WriteLine($"[YearMonthListViewModel] 데이터 새로고침 완료");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[YearMonthListViewModel] 데이터 새로고침 오류: {ex.Message}");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}