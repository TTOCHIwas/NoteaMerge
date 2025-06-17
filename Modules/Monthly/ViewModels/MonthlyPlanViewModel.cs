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
    }
}
