// MonthlyCommentRepository.cs
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using Notea.Helpers;

namespace Notea.Modules.Monthly.Models
{
    public static class MonthlyCommentRepository
    {
        public static Dictionary<int, string> GetYearComments(int year)
        {
            var comments = new Dictionary<int, string>();

            try
            {
                string query = $@"
                    SELECT strftime('%m', monthDate) as month, comment
                    FROM monthlyComment
                    WHERE strftime('%Y', monthDate) = '{year}'";

                var result = DatabaseHelper.ExecuteSelect(query);
                foreach (DataRow row in result.Rows)
                {
                    int month = Convert.ToInt32(row["month"]);
                    string comment = row["comment"]?.ToString() ?? "";
                    comments[month] = comment;
                }

                Debug.WriteLine($"[COMMENT] {year}년 코멘트 로드 완료. {comments.Count}개월");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] 연간 코멘트 로드 실패: {ex.Message}");
            }

            return comments;
        }

        public static void SaveMonthComment(int year, int month, string comment)
        {
            try
            {
                DateTime monthDate = new DateTime(year, month, 1);

                string query = $@"
                    INSERT OR REPLACE INTO monthlyComment (monthDate, comment)
                    VALUES ('{monthDate:yyyy-MM-dd}', '{comment?.Replace("'", "''")}')";

                DatabaseHelper.ExecuteNonQuery(query);
                Debug.WriteLine($"[COMMENT] {year}-{month:00} 코멘트 저장: {comment}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] 월별 코멘트 저장 실패: {ex.Message}");
            }
        }

        public static string GetMonthComment(int year, int month)
        {
            try
            {
                DateTime monthDate = new DateTime(year, month, 1);

                string query = $@"
                    SELECT comment 
                    FROM monthlyComment 
                    WHERE date(monthDate) = date('{monthDate:yyyy-MM-dd}')";

                var result = DatabaseHelper.ExecuteSelect(query);
                if (result.Rows.Count > 0)
                {
                    return result.Rows[0]["comment"]?.ToString() ?? "";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] 월별 코멘트 로드 실패: {ex.Message}");
            }

            return "";
        }
    }
}