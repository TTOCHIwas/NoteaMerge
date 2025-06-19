using Notea.Helpers;
using Notea.Modules.Monthly.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Notea.Modules.Monthly.ViewModels
{
    public class MonthlyPlanViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<MonthlyPlan> Plans { get; set; } = new ObservableCollection<MonthlyPlan>();

        public MonthlyPlanViewModel()
        {
            LoadPlans();
        }

        public void LoadPlans()
        {
            string query = "SELECT * FROM plan ORDER BY startDate ASC";
            DataTable dt = DatabaseHelper.ExecuteSelect(query);

            Plans.Clear();

            foreach (DataRow row in dt.Rows)
            {
                Plans.Add(new MonthlyPlan
                {
                    PlanId = Convert.ToInt32(row["planId"]),
                    Title = row["title"].ToString(),
                    Description = row["detail"]?.ToString(),
                    IsDday = Convert.ToBoolean(row["dday"]),
                    StartDate = Convert.ToDateTime(row["startDate"]),
                    EndDate = Convert.ToDateTime(row["endDate"]),
                    Color = row["color"]?.ToString()
                });
            }

            OnPropertyChanged(nameof(Plans));
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public void AddPlan(MonthlyPlan newPlan)
        {
            string insertQuery = $@"
            INSERT INTO plan (createDate, title, detail, dday, startDate, endDate, color)
            VALUES (
                '{newPlan.Title}',
                '{newPlan.Description}',
                {Convert.ToInt32(newPlan.IsDday)},
                '{newPlan.StartDate:yyyy-MM-dd HH:mm:ss}',
                '{newPlan.EndDate:yyyy-MM-dd HH:mm:ss}',
                '{newPlan.Color}'
            );
        ";

            int result = DatabaseHelper.ExecuteNonQuery(insertQuery);
            if (result > 0)
            {
                LoadPlans(); // 저장 후 목록 갱신
            }
        }
        private string _monthComment = string.Empty;
        // ✅ 추가: MonthComment 속성
        public string MonthComment
        {
            get => _monthComment;
            set
            {
                _monthComment = value;
                OnPropertyChanged(nameof(MonthComment));
            }
        }

        // ✅ 추가: 월 코멘트 로드 메서드
        public void LoadMonthComment(DateTime currentDate)
        {
            try
            {
                DateTime monthDate = new DateTime(currentDate.Year, currentDate.Month, 1);

                string query = $@"
                SELECT comment 
                FROM monthlyComment 
                WHERE date(monthDate) = date('{monthDate:yyyy-MM-dd}')";

                var result = Helpers.DatabaseHelper.ExecuteSelect(query);
                if (result.Rows.Count > 0)
                {
                    MonthComment = result.Rows[0]["comment"]?.ToString() ?? "";
                }
                else
                {
                    MonthComment = "";
                }

                System.Diagnostics.Debug.WriteLine($"[MonthlyPlanVM] {currentDate:yyyy-MM} 코멘트 로드: {MonthComment}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MonthlyPlanVM] 월별 코멘트 로드 실패: {ex.Message}");
                MonthComment = "";
            }
        }

        // ✅ 추가: 월 코멘트 저장 메서드
        public void SaveMonthComment(DateTime currentDate)
        {
            try
            {
                DateTime monthDate = new DateTime(currentDate.Year, currentDate.Month, 1);

                string query = $@"
                INSERT OR REPLACE INTO monthlyComment (monthDate, comment)
                VALUES ('{monthDate:yyyy-MM-dd}', '{MonthComment?.Replace("'", "''")}')";

                Helpers.DatabaseHelper.ExecuteNonQuery(query);
                System.Diagnostics.Debug.WriteLine($"[MonthlyPlanVM] {currentDate:yyyy-MM} 코멘트 저장: {MonthComment}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MonthlyPlanVM] 월별 코멘트 저장 실패: {ex.Message}");
            }
        }
    }
}

