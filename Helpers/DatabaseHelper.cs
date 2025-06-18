// 파일: Helpers/DatabaseHelper.cs
// 🚨 SQLiteConnection 오류 해결 및 사용하지 않는 메소드 삭제

using System;
using System.Data;
using System.Data.SQLite;
using System.Diagnostics;

namespace Notea.Helpers
{
    /// <summary>
    /// 정적 메소드 기반 데이터베이스 헬퍼
    /// NoteRepository에서 사용하는 간단한 쿼리 실행용
    /// </summary>
    public static class DatabaseHelper
    {
        // ✅ 수정: connectionString을 메소드로 변경하여 오류 해결
        private static string GetConnectionString()
        {
            return Notea.Database.DatabaseInitializer.GetConnectionString();
        }

        // ✅ SELECT 쿼리 실행 (NoteRepository에서 사용)
        public static DataTable ExecuteSelect(string query)
        {
            var dt = new DataTable();

            try
            {
                using (var connection = new SQLiteConnection(GetConnectionString()))
                {
                    connection.Open();

                    // 외래 키 활성화
                    using (var pragmaCmd = new SQLiteCommand("PRAGMA foreign_keys = ON;", connection))
                    {
                        pragmaCmd.ExecuteNonQuery();
                    }

                    using (var command = new SQLiteCommand(query, connection))
                    using (var adapter = new SQLiteDataAdapter(command))
                    {
                        adapter.Fill(dt);
                    }
                }

                Debug.WriteLine($"[Helpers.DB] SELECT 성공: {dt.Rows.Count}행");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Helpers.DB ERROR] SELECT 실패: {ex.Message}");
                Debug.WriteLine($"쿼리: {query}");
            }

            return dt;
        }

        // ✅ INSERT, UPDATE, DELETE 쿼리 실행 (NoteRepository에서 사용)
        public static int ExecuteNonQuery(string query)
        {
            int result = 0;

            try
            {
                using (var connection = new SQLiteConnection(GetConnectionString()))
                {
                    connection.Open();

                    // 외래 키 활성화
                    using (var pragmaCmd = new SQLiteCommand("PRAGMA foreign_keys = ON;", connection))
                    {
                        pragmaCmd.ExecuteNonQuery();
                    }

                    using (var command = new SQLiteCommand(query, connection))
                    {
                        result = command.ExecuteNonQuery();
                    }
                }

                Debug.WriteLine($"[Helpers.DB] NonQuery 성공: {result}행 영향받음");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Helpers.DB ERROR] NonQuery 실패: {ex.Message}");
                Debug.WriteLine($"쿼리: {query}");
            }

            return result;
        }

        // ✅ 디버깅용 테이블 구조 확인 (개발 시에만 사용)
        public static void CheckTableStructure()
        {
            try
            {
                string query = @"
                    SELECT sql FROM sqlite_master 
                    WHERE type='table' AND name IN ('category', 'noteContent', 'Subject')";

                var result = ExecuteSelect(query);
                Debug.WriteLine("[Helpers.DB] 테이블 구조:");
                foreach (DataRow row in result.Rows)
                {
                    Debug.WriteLine($"  {row["sql"]}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Helpers.DB ERROR] 테이블 구조 확인 실패: {ex.Message}");
            }
        }

        // ✅ 연결 테스트 (디버깅용)
        public static bool TestConnection()
        {
            try
            {
                using (var connection = new SQLiteConnection(GetConnectionString()))
                {
                    connection.Open();
                    Debug.WriteLine("[Helpers.DB] 연결 테스트 성공");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Helpers.DB ERROR] 연결 실패: {ex.Message}");
                return false;
            }
        }

        // 🗑️ 삭제된 메소드들 (사용하지 않음):
        // - DebugPrintAllData (Modules/Common/Helpers/DatabaseHelper에서 처리)
        // - LoadSubjectsWithGroups (중복, static vs instance 충돌)
        // - AddSubjectToSubjectTable (중복)
        // - LoadTopicGroupsForSubject (중복)
    }
}