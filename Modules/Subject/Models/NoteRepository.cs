using Microsoft.Data.Sqlite;
using Notea.Helpers;
using Notea.Modules.Subject.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
namespace Notea.Modules.Subject.Models
{
    public static class NoteRepository
    {
        public class Transaction : IDisposable
        {
            private SqliteConnection _connection;
            private SqliteTransaction _transaction;
            public SqliteConnection Connection => _connection;
            public SqliteTransaction SqliteTransaction => _transaction;

            public Transaction(SqliteConnection connection, SqliteTransaction transaction)
            {
                _connection = connection;
                _transaction = transaction;
            }

            public void Commit() => _transaction.Commit();
            public void Rollback() => _transaction.Rollback();
            public void Dispose()
            {
                _transaction?.Dispose();
                _connection?.Dispose();
            }
        }

        public static Transaction BeginTransaction()
        {
            var conn = new SqliteConnection(GetConnectionString());
            conn.Open();
            var trans = conn.BeginTransaction();
            return new Transaction(conn, trans);
        }

        /// <summary>
        /// 새로운 카테고리(제목) 삽입 - 레벨과 부모 ID 포함
        /// </summary>
        public static int InsertCategory(string content, int subjectId, int displayOrder = -1,
            int level = 1, int? parentCategoryId = null, Transaction transaction = null)
        {
            try
            {
                Debug.WriteLine($"[SAVE] 카테고리 넣는 중이다 임마");

                if (displayOrder == -1)
                {
                    displayOrder = GetNextDisplayOrder(subjectId);
                    Debug.WriteLine($"[SAVE] 이새낀 새거라 자리 찾는다");
                }

                // 헤딩 레벨 자동 감지
                int detectedLevel = GetHeadingLevel(content);

                if (detectedLevel > 0)
                {
                    level = detectedLevel;
                }

                SqliteConnection conn;
                SqliteTransaction trans = null;
                bool shouldDispose = false;

                if (transaction != null)
                {
                    conn = transaction.Connection;
                    trans = transaction.SqliteTransaction;
                }
                else
                {
                    conn = new SqliteConnection(GetConnectionString());
                    conn.Open();
                    shouldDispose = true;
                }

                try
                {
                    var cmd = conn.CreateCommand();
                    cmd.Transaction = trans;
                    cmd.CommandText = @"
                    INSERT INTO category (title, subjectId, timeId, displayOrder, level, parentCategoryId)
                    VALUES (@title, @subjectId, @timeId, @displayOrder, @level, @parentCategoryId);
                    SELECT last_insert_rowid();";

                    cmd.Parameters.AddWithValue("@title", content);
                    cmd.Parameters.AddWithValue("@subjectId", subjectId);
                    cmd.Parameters.AddWithValue("@timeId", 1);
                    cmd.Parameters.AddWithValue("@displayOrder", displayOrder);
                    cmd.Parameters.AddWithValue("@level", level);
                    cmd.Parameters.AddWithValue("@parentCategoryId", parentCategoryId ?? (object)DBNull.Value);

                    var result = cmd.ExecuteScalar();
                    int categoryId = Convert.ToInt32(result);

                    Debug.WriteLine($"[DB] 새 카테고리 삽입 완료. CategoryId: {categoryId}, Level: {level}");
                    return categoryId;
                }
                finally
                {
                    if (shouldDispose)
                    {
                        conn.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] InsertCategory 실패: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// 카테고리의 부모를 찾는 메서드
        /// </summary>
        public static int? FindParentCategory(int subjectId, int currentDisplayOrder, int currentLevel)
        {
            try
            {
                string query = $@"
                    SELECT categoryId, level, displayOrder
                    FROM category 
                    WHERE subjectId = {subjectId} 
                    AND displayOrder < {currentDisplayOrder}
                    AND level < {currentLevel}
                    ORDER BY displayOrder DESC
                    LIMIT 1";

                var result = DatabaseHelper.ExecuteSelect(query);
                if (result.Rows.Count > 0)
                {
                    return Convert.ToInt32(result.Rows[0]["categoryId"]);
                }
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] FindParentCategory 실패: {ex.Message}");
                return null;
            }
        }

        // DB 경로는 DatabaseHelper에서 관리
        private static string GetConnectionString()
        {
            return $"Data Source={DatabaseHelper.GetDatabasePath()};";
        }

        public static List<NoteCategory> LoadNotesBySubject(int subjectId)
        {
            var result = new List<NoteCategory>();
            var categoryMap = new Dictionary<int, NoteCategory>();

            try
            {
                // 카테고리 로드 (기존과 동일)
                string categoryQuery = $@"
            SELECT categoryId, title, displayOrder, level
            FROM category 
            WHERE subJectId = {subjectId}
            ORDER BY displayOrder";

                DataTable categoryTable = DatabaseHelper.ExecuteSelect(categoryQuery);

                foreach (DataRow row in categoryTable.Rows)
                {
                    var category = new NoteCategory
                    {
                        CategoryId = Convert.ToInt32(row["categoryId"]),
                        Title = row["title"].ToString(),
                        Level = row["level"] != DBNull.Value ? Convert.ToInt32(row["level"]) : 1
                    };
                    result.Add(category);
                    categoryMap[category.CategoryId] = category;
                }

                // 텍스트 로드 - contentType과 imageUrl 포함
                string textQuery = $@"
            SELECT textId, content, categoryId, displayOrder, 
                   COALESCE(contentType, 'text') as contentType,
                   imageUrl
            FROM noteContent 
            WHERE subJectId = {subjectId}
            ORDER BY displayOrder";

                DataTable textTable = DatabaseHelper.ExecuteSelect(textQuery);

                foreach (DataRow row in textTable.Rows)
                {
                    int categoryId = Convert.ToInt32(row["categoryId"]);

                    if (categoryMap.ContainsKey(categoryId))
                    {
                        var noteLine = new NoteLine
                        {
                            Index = Convert.ToInt32(row["textId"]),
                            Content = row["content"].ToString(),
                            ContentType = row["contentType"].ToString(),
                            ImageUrl = row["imageUrl"] != DBNull.Value ? row["imageUrl"].ToString() : null
                        };

                        categoryMap[categoryId].Lines.Add(noteLine);
                    }
                }

                Debug.WriteLine($"[DB] LoadNotesBySubject 완료. 카테고리 수: {result.Count}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] LoadNotesBySubject 실패: {ex.Message}");
            }

            return result;
        }

        public static void RecalculateDisplayOrders(int subjectId)
        {
            try
            {
                using var conn = new SqliteConnection(GetConnectionString());
                conn.Open();
                using var transaction = conn.BeginTransaction();

                // 모든 라인을 현재 displayOrder 순으로 가져오기
                var cmd = conn.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = @"
            SELECT 'category' as type, categoryId as id, displayOrder 
            FROM category WHERE subjectId = @subjectId
            UNION ALL
            SELECT 'text' as type, textId as id, displayOrder 
            FROM noteContent WHERE subjectId = @subjectId
            ORDER BY displayOrder, id";
                cmd.Parameters.AddWithValue("@subjectId", subjectId);

                var lines = new List<(string type, int id, int oldOrder)>();
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lines.Add((
                            reader.GetString(0),
                            reader.GetInt32(1),
                            reader.GetInt32(2)
                        ));
                    }
                }

                // 새로운 순서 할당
                int newOrder = 1;
                foreach (var line in lines)
                {
                    var updateCmd = conn.CreateCommand();
                    updateCmd.Transaction = transaction;

                    if (line.type == "category")
                    {
                        updateCmd.CommandText = @"
                    UPDATE category SET displayOrder = @order 
                    WHERE categoryId = @id";
                    }
                    else
                    {
                        updateCmd.CommandText = @"
                    UPDATE noteContent SET displayOrder = @order 
                    WHERE TextId = @id";
                    }

                    updateCmd.Parameters.AddWithValue("@order", newOrder++);
                    updateCmd.Parameters.AddWithValue("@id", line.id);
                    updateCmd.ExecuteNonQuery();
                }

                transaction.Commit();
                Debug.WriteLine($"[DB] DisplayOrder 재정렬 완료. 총 {lines.Count}개 라인");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] RecalculateDisplayOrders 실패: {ex.Message}");
            }
        }

        public static int GetNextDisplayOrder(int subjectId)
        {
            try
            {
                // 카테고리와 텍스트 중 최대 displayOrder 찾기
                string query = $@"
                SELECT MAX(displayOrder) as maxOrder FROM (
                    SELECT displayOrder FROM category WHERE subjectId = {subjectId}
                    UNION ALL
                    SELECT displayOrder FROM noteContent WHERE subjectId = {subjectId}
                )";

                var result = DatabaseHelper.ExecuteSelect(query);
                if (result.Rows.Count > 0 && result.Rows[0]["maxOrder"] != DBNull.Value)
                {
                    return Convert.ToInt32(result.Rows[0]["maxOrder"]) + 1;
                }
                return 1;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] GetNextDisplayOrder 실패: {ex.Message}");
                return 1;
            }
        }

        public static void ShiftDisplayOrdersAfter(int subjectId, int afterOrder)
        {
            try
            {
                using var conn = new SqliteConnection(GetConnectionString());
                conn.Open();
                using var transaction = conn.BeginTransaction();

                // 카테고리 순서 업데이트
                var categoryCmd = conn.CreateCommand();
                categoryCmd.Transaction = transaction;
                categoryCmd.CommandText = @"
                UPDATE category 
                SET displayOrder = displayOrder + 1 
                WHERE subjectId = @subjectId AND displayOrder > @afterOrder";
                categoryCmd.Parameters.AddWithValue("@subjectId", subjectId);
                categoryCmd.Parameters.AddWithValue("@afterOrder", afterOrder);
                categoryCmd.ExecuteNonQuery();

                // 텍스트 순서 업데이트
                var contentCmd = conn.CreateCommand();
                contentCmd.Transaction = transaction;
                contentCmd.CommandText = @"
                UPDATE noteContent 
                SET displayOrder = displayOrder + 1 
                WHERE subjectId = @subjectId AND displayOrder > @afterOrder";
                contentCmd.Parameters.AddWithValue("@subjectId", subjectId);
                contentCmd.Parameters.AddWithValue("@afterOrder", afterOrder);
                contentCmd.ExecuteNonQuery();

                transaction.Commit();
                Debug.WriteLine($"[DB] displayOrder 시프트 완료. afterOrder: {afterOrder}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] ShiftDisplayOrdersAfter 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 카테고리로 저장할 제목인지 확인하는 메서드 - # 하나로 시작하는 경우만 카테고리로 저장
        /// </summary>
        public static bool IsCategoryHeading(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return false;
            // ^#\s+ : 라인 시작(^) + # 하나 + 공백(\s+) + 아무 문자
            // (?!#) : # 다음에 또 #이 오지 않는 경우만
            return Regex.IsMatch(content.Trim(), @"^#(?!#)\s+.+");
        }

        /// <summary>
        /// 마크다운 헤딩인지 확인 (# ~ ######)
        /// </summary>
        public static bool IsMarkdownHeading(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return false;
            return Regex.IsMatch(content.Trim(), @"^#{1,6}\s+.+");
        }

        /// <summary>
        /// 헤딩 레벨 추출 (1~6)
        /// </summary>
        public static int GetHeadingLevel(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return 0;

            var match = Regex.Match(content.Trim(), @"^(#{1,6})\s+");
            return match.Success ? match.Groups[1].Value.Length : 0;
        }

        /// <summary>
        /// 제목에서 # 기호를 제거하고 실제 제목 텍스트만 추출
        /// </summary>
        public static string ExtractHeadingText(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return "";

            var match = Regex.Match(content.Trim(), @"^#{1,6}\s+(.+)");
            return match.Success ? match.Groups[1].Value.Trim() : content;
        }

        /// <summary>
        /// 카테고리(제목) 업데이트 - 마크다운 문법 그대로 저장
        /// </summary>
        public static void UpdateCategory(int categoryId, string content, Transaction transaction = null)
        {
            if (categoryId <= 0) return;

            try
            {
                SqliteConnection conn;
                SqliteTransaction trans = null;
                bool shouldDispose = false;

                if (transaction != null)
                {
                    conn = transaction.Connection;
                    trans = transaction.SqliteTransaction;
                }
                else
                {
                    conn = new SqliteConnection(GetConnectionString());
                    conn.Open();
                    shouldDispose = true;
                }

                try
                {
                    var cmd = conn.CreateCommand();
                    cmd.Transaction = trans;
                    cmd.CommandText = @"
                        UPDATE category 
                        SET title = @title
                        WHERE categoryId = @categoryId";

                    cmd.Parameters.AddWithValue("@title", content);
                    cmd.Parameters.AddWithValue("@categoryId", categoryId);

                    int rowsAffected = cmd.ExecuteNonQuery();
                    Debug.WriteLine($"[DB] 카테고리 업데이트 완료. CategoryId: {categoryId}");
                }
                finally
                {
                    if (shouldDispose)
                    {
                        conn.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] UpdateCategory 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 카테고리(제목) 삭제 및 관련 noteContent도 함께 삭제
        /// </summary>
        public static void DeleteCategory(int categoryId, bool deleteTexts = true)
        {
            if (categoryId <= 0)
            {
                Debug.WriteLine($"[WARNING] DeleteCategory 호출됐지만 CategoryId가 유효하지 않음: {categoryId}");
                return;
            }

            try
            {
                using var conn = new SqliteConnection(GetConnectionString());
                conn.Open();

                using var transaction = conn.BeginTransaction();

                if (deleteTexts)
                {
                    // 관련 noteContent도 삭제
                    var deleteNotesCmd = conn.CreateCommand();
                    deleteNotesCmd.Transaction = transaction;
                    deleteNotesCmd.CommandText = "DELETE FROM noteContent WHERE categoryId = @categoryId";
                    deleteNotesCmd.Parameters.AddWithValue("@categoryId", categoryId);
                    int notesDeleted = deleteNotesCmd.ExecuteNonQuery();
                    Debug.WriteLine($"[DB] 삭제된 노트: {notesDeleted}개");
                }

                // 카테고리만 삭제
                var deleteCategoryCmd = conn.CreateCommand();
                deleteCategoryCmd.Transaction = transaction;
                deleteCategoryCmd.CommandText = "DELETE FROM category WHERE categoryId = @categoryId";
                deleteCategoryCmd.Parameters.AddWithValue("@categoryId", categoryId);
                int categoryDeleted = deleteCategoryCmd.ExecuteNonQuery();

                transaction.Commit();
                Debug.WriteLine($"[DB] 카테고리 삭제 완료. CategoryId: {categoryId}, 텍스트 삭제 여부: {deleteTexts}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] DeleteCategory 실패: {ex.Message}");
            }
        }

        public static void ReassignTextsToCategory(int fromCategoryId, int toCategoryId)
        {
            if (fromCategoryId <= 0 || toCategoryId <= 0)
            {
                Debug.WriteLine($"[WARNING] ReassignTextsToCategory - 유효하지 않은 CategoryId: from={fromCategoryId}, to={toCategoryId}");
                return;
            }

            try
            {
                string query = $@"
            UPDATE noteContent 
            SET categoryId = {toCategoryId}
            WHERE categoryId = {fromCategoryId}";

                int rowsAffected = DatabaseHelper.ExecuteNonQuery(query);
                Debug.WriteLine($"[DB] 텍스트 재할당 완료. {fromCategoryId} -> {toCategoryId}, 영향받은 행: {rowsAffected}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] ReassignTextsToCategory 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 새로운 일반 텍스트 라인 삽입
        /// </summary>
        public static int InsertNewLine(string content, int subjectId, int categoryId, int displayOrder = -1,
    string contentType = "text", string imageUrl = null, Transaction transaction = null)
        {
            try
            {
                if (displayOrder == -1)
                {
                    displayOrder = GetNextDisplayOrder(subjectId);
                }

                SqliteConnection conn;
                SqliteTransaction trans = null;
                bool shouldDispose = false;

                if (transaction != null)
                {
                    conn = transaction.Connection;
                    trans = transaction.SqliteTransaction;
                }
                else
                {
                    conn = new SqliteConnection(GetConnectionString());
                    conn.Open();
                    shouldDispose = true;
                }

                try
                {
                    var cmd = conn.CreateCommand();
                    cmd.Transaction = trans;

                    cmd.CommandText = @"
                INSERT INTO noteContent (content, subjectId, categoryId, displayOrder, contentType, imageUrl)
                VALUES (@content, @subjectId, @categoryId, @displayOrder, @contentType, @imageUrl);
                SELECT last_insert_rowid();";

                    cmd.Parameters.AddWithValue("@content", content ?? "");
                    cmd.Parameters.AddWithValue("@subjectId", subjectId);
                    cmd.Parameters.AddWithValue("@categoryId", categoryId);
                    cmd.Parameters.AddWithValue("@displayOrder", displayOrder);
                    cmd.Parameters.AddWithValue("@contentType", contentType);
                    cmd.Parameters.AddWithValue("@imageUrl", imageUrl ?? (object)DBNull.Value);

                    Debug.WriteLine($"[DB] InsertNewLine 실행 - Type: {contentType}, ImageUrl: {imageUrl}");

                    var result = cmd.ExecuteScalar();

                    if (result != null && result != DBNull.Value)
                    {
                        int textId = Convert.ToInt32(result);
                        Debug.WriteLine($"[DB] 새 라인 삽입 완료. TextId: {textId}");
                        return textId;
                    }
                    else
                    {
                        Debug.WriteLine($"[DB ERROR] InsertNewLine - last_insert_rowid() 반환값 없음");
                        return 0;
                    }
                }
                finally
                {
                    if (shouldDispose)
                    {
                        conn.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] InsertNewLine 실패: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// 기존 일반 텍스트 라인 업데이트
        /// </summary>
        public static void UpdateLine(MarkdownLineViewModel line, Transaction transaction = null)
        {
            if (line.TextId <= 0)
            {
                Debug.WriteLine($"[WARNING] UpdateLine 호출됐지만 TextId가 유효하지 않음: {line.TextId}");
                return;
            }

            try
            {
                SqliteConnection conn;
                SqliteTransaction trans = null;
                bool shouldDispose = false;

                if (transaction != null)
                {
                    conn = transaction.Connection;
                    trans = transaction.SqliteTransaction;
                }
                else
                {
                    conn = new SqliteConnection(GetConnectionString());
                    conn.Open();
                    shouldDispose = true;
                }

                try
                {
                    var cmd = conn.CreateCommand();
                    cmd.Transaction = trans;
                    cmd.CommandText = @"
                UPDATE noteContent 
                SET content = @content,
                    contentType = @contentType,
                    imageUrl = @imageUrl,
                    categoryId = @categoryId,
                    displayOrder = @displayOrder
                WHERE textId = @textId";

                    cmd.Parameters.AddWithValue("@content", line.Content ?? "");
                    cmd.Parameters.AddWithValue("@contentType", line.ContentType);
                    cmd.Parameters.AddWithValue("@imageUrl", line.ImageUrl ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@categoryId", line.CategoryId);
                    cmd.Parameters.AddWithValue("@displayOrder", line.DisplayOrder);
                    cmd.Parameters.AddWithValue("@textId", line.TextId);

                    int rowsAffected = cmd.ExecuteNonQuery();
                    Debug.WriteLine($"[DB] 라인 업데이트 완료. TextId: {line.TextId}, 영향받은 행: {rowsAffected}");
                }
                finally
                {
                    if (shouldDispose)
                    {
                        conn.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] UpdateLine 실패: {ex.Message}");
            }
        }

        public static void UpdateLineDisplayOrder(int textId, int displayOrder, Transaction transaction = null)
        {
            try
            {
                SqliteConnection conn;
                SqliteTransaction trans = null;
                bool shouldDispose = false;

                if (transaction != null)
                {
                    conn = transaction.Connection;
                    trans = transaction.SqliteTransaction;
                }
                else
                {
                    conn = new SqliteConnection(GetConnectionString());
                    conn.Open();
                    shouldDispose = true;
                }

                try
                {
                    var cmd = conn.CreateCommand();
                    cmd.Transaction = trans;
                    cmd.CommandText = @"
                UPDATE noteContent 
                SET displayOrder = @displayOrder
                WHERE textId = @textId";

                    cmd.Parameters.AddWithValue("@displayOrder", displayOrder);
                    cmd.Parameters.AddWithValue("@textId", textId);

                    cmd.ExecuteNonQuery();
                    Debug.WriteLine($"[DB] 텍스트 DisplayOrder 업데이트: TextId={textId}, Order={displayOrder}");
                }
                finally
                {
                    if (shouldDispose)
                    {
                        conn.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] UpdateLineDisplayOrder 실패: {ex.Message}");
            }
        }

        public static void UpdateCategoryDisplayOrder(int categoryId, int displayOrder, Transaction transaction = null)
        {
            try
            {
                SqliteConnection conn;
                SqliteTransaction trans = null;
                bool shouldDispose = false;

                if (transaction != null)
                {
                    conn = transaction.Connection;
                    trans = transaction.SqliteTransaction;
                }
                else
                {
                    conn = new SqliteConnection(GetConnectionString());
                    conn.Open();
                    shouldDispose = true;
                }

                try
                {
                    var cmd = conn.CreateCommand();
                    cmd.Transaction = trans;
                    cmd.CommandText = @"
                UPDATE category 
                SET displayOrder = @displayOrder
                WHERE categoryId = @categoryId";

                    cmd.Parameters.AddWithValue("@displayOrder", displayOrder);
                    cmd.Parameters.AddWithValue("@categoryId", categoryId);

                    cmd.ExecuteNonQuery();
                    Debug.WriteLine($"[DB] 카테고리 DisplayOrder 업데이트: CategoryId={categoryId}, Order={displayOrder}");
                }
                finally
                {
                    if (shouldDispose)
                    {
                        conn.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] UpdateCategoryDisplayOrder 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 일반 텍스트 라인 삭제
        /// </summary>
        public static void DeleteLine(int textId)
        {
            if (textId <= 0)
            {
                Debug.WriteLine($"[WARNING] DeleteLine 호출됐지만 TextId가 유효하지 않음: {textId}");
                return;
            }

            try
            {
                string query = $"DELETE FROM noteContent WHERE textId = {textId}";
                int rowsAffected = DatabaseHelper.ExecuteNonQuery(query);
                Debug.WriteLine($"[DB] 라인 삭제 완료. TextId: {textId}, 영향받은 행: {rowsAffected}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] DeleteLine 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 라인이 제목인지 일반 텍스트인지 판단하여 적절히 저장
        /// </summary>
        public static void SaveOrUpdateLine(MarkdownLineViewModel line)
        {
            try
            {
                if (IsCategoryHeading(line.Content))  // # 하나만 카테고리로 저장
                {
                    // 카테고리(제목)인 경우
                    if (line.IsHeadingLine && line.CategoryId > 0)
                    {
                        // 기존 제목 업데이트
                        UpdateCategory(line.CategoryId, line.Content);
                    }
                    else
                    {
                        // 새로운 제목 삽입
                        int newCategoryId = InsertCategory(line.Content, line.SubjectId, line.DisplayOrder);
                        line.CategoryId = newCategoryId;
                        line.IsHeadingLine = true;
                    }
                }
                else
                {
                    // CategoryId가 없으면 저장하지 않음
                    if (line.CategoryId <= 0)
                    {
                        Debug.WriteLine($"[WARNING] CategoryId가 유효하지 않아 저장 건너뜀. CategoryId: {line.CategoryId}");
                        return;
                    }

                    // 일반 텍스트인 경우 (##, ### 등도 포함)
                    if (line.TextId <= 0)
                    {
                        // 새로운 라인 삽입
                        int newTextId = InsertNewLine(line.Content, line.SubjectId, line.CategoryId);
                        line.TextId = newTextId;
                    }
                    else
                    {
                        // 기존 라인 업데이트
                        UpdateLine(line);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] SaveOrUpdateLine 실패: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 여러 라인을 한 번에 처리 (트랜잭션)
        /// </summary>
        public static void SaveLinesInTransaction(List<MarkdownLineViewModel> lines)
        {
            using var conn = new SqliteConnection(GetConnectionString());
            conn.Open();

            using var transaction = conn.BeginTransaction();
            try
            {
                foreach (var line in lines)
                {
                    if (line.CategoryId <= 0)
                    {
                        Debug.WriteLine($"[WARNING] 트랜잭션 중 CategoryId가 유효하지 않은 라인 건너뜀. Content: {line.Content}");
                        continue;
                    }

                    if (IsCategoryHeading(line.Content))  // # 하나만 카테고리로 저장
                    {
                        // 제목 처리
                        if (line.IsHeadingLine && line.CategoryId > 0)
                        {
                            var cmd = conn.CreateCommand();
                            cmd.Transaction = transaction;
                            cmd.CommandText = @"
                                UPDATE category 
                                SET title = @title
                                WHERE categoryId = @categoryId";

                            cmd.Parameters.AddWithValue("@title", line.Content);
                            cmd.Parameters.AddWithValue("@categoryId", line.CategoryId);
                            cmd.ExecuteNonQuery();
                        }
                    }
                    else
                    {
                        // 일반 텍스트 처리
                        if (line.TextId > 0)
                        {
                            var cmd = conn.CreateCommand();
                            cmd.Transaction = transaction;
                            cmd.CommandText = @"
                                UPDATE noteContent 
                                SET content = @content
                                WHERE textId = @textId";

                            cmd.Parameters.AddWithValue("@content", line.Content ?? "");
                            cmd.Parameters.AddWithValue("@textId", line.TextId);
                            cmd.ExecuteNonQuery();
                        }
                    }
                }

                transaction.Commit();
                Debug.WriteLine($"[DB] 트랜잭션으로 {lines.Count}개 라인 저장 완료");
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                Debug.WriteLine($"[DB ERROR] 트랜잭션 실패, 롤백됨: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 카테고리 로드 시 계층 구조 포함
        /// </summary>
        public static List<NoteCategory> LoadNotesBySubjectWithHierarchy(int subjectId)
        {
            var allCategories = new List<NoteCategory>();
            var categoryMap = new Dictionary<int, NoteCategory>();
            var rootCategories = new List<NoteCategory>();

            try
            {
                // 1. 모든 카테고리 로드
                string categoryQuery = $@"
            SELECT categoryId, title, displayOrder, level, parentCategoryId
            FROM category 
            WHERE subJectId = {subjectId}
            ORDER BY displayOrder";

                DataTable categoryTable = DatabaseHelper.ExecuteSelect(categoryQuery);

                foreach (DataRow row in categoryTable.Rows)
                {
                    var category = new NoteCategory
                    {
                        CategoryId = Convert.ToInt32(row["categoryId"]),
                        Title = row["title"].ToString(),
                        Level = row["level"] != DBNull.Value ? Convert.ToInt32(row["level"]) : 1
                    };

                    allCategories.Add(category);
                    categoryMap[category.CategoryId] = category;

                    // parentCategoryId가 없으면 루트 카테고리
                    if (row["parentCategoryId"] == DBNull.Value)
                    {
                        rootCategories.Add(category);
                    }
                }

                // 2. 계층 구조 구성
                foreach (DataRow row in categoryTable.Rows)
                {
                    if (row["parentCategoryId"] != DBNull.Value)
                    {
                        int categoryId = Convert.ToInt32(row["categoryId"]);
                        int parentId = Convert.ToInt32(row["parentCategoryId"]);

                        if (categoryMap.ContainsKey(parentId) && categoryMap.ContainsKey(categoryId))
                        {
                            categoryMap[parentId].SubCategories.Add(categoryMap[categoryId]);
                        }
                    }
                }

                // 3. 모든 텍스트 로드
                string textQuery = $@"
            SELECT textId, content, categoryId, displayOrder
            FROM noteContent 
            WHERE subJectId = {subjectId}
            ORDER BY displayOrder";

                DataTable textTable = DatabaseHelper.ExecuteSelect(textQuery);

                foreach (DataRow row in textTable.Rows)
                {
                    int categoryId = Convert.ToInt32(row["categoryId"]);

                    if (categoryMap.ContainsKey(categoryId))
                    {
                        categoryMap[categoryId].Lines.Add(new NoteLine
                        {
                            Index = Convert.ToInt32(row["textId"]),
                            Content = row["content"].ToString()
                        });
                    }
                    else
                    {
                        Debug.WriteLine($"[WARNING] 텍스트의 카테고리를 찾을 수 없음. CategoryId: {categoryId}, TextId: {row["textId"]}");
                    }
                }

                Debug.WriteLine($"[DB] LoadNotesBySubjectWithHierarchy 완료. 루트 카테고리 수: {rootCategories.Count}");
                foreach (var cat in allCategories)
                {
                    Debug.WriteLine($"  카테고리 '{cat.Title}' (ID: {cat.CategoryId}) - 텍스트 수: {cat.Lines.Count}");
                }

                // 계층 구조가 없는 단순한 경우 모든 카테고리 반환
                return rootCategories.Count > 0 ? rootCategories : allCategories;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] LoadNotesBySubjectWithHierarchy 실패: {ex.Message}");
            }

            return rootCategories;
        }

        private static NoteCategory FindCategoryById(List<NoteCategory> categories, int categoryId)
        {
            foreach (var category in categories)
            {
                if (category.CategoryId == categoryId)
                    return category;

                var found = FindCategoryById(category.SubCategories, categoryId);
                if (found != null)
                    return found;
            }
            return null;
        }

        public static void EnsureDefaultCategory(int subjectId)
        {
            try
            {
                // 기본 카테고리가 있는지 확인
                string checkQuery = $"SELECT COUNT(*) as count FROM category WHERE categoryId = 1 AND subJectId = {subjectId}";
                var result = DatabaseHelper.ExecuteSelect(checkQuery);

                if (result.Rows.Count > 0 && Convert.ToInt32(result.Rows[0]["count"]) == 0)
                {
                    // 기본 카테고리 생성
                    string insertQuery = $@"
                INSERT INTO category (categoryId, title, subJectId, timeId, displayOrder, level) 
                VALUES (1, ' ', {subjectId}, 1, 0, 1)";
                    DatabaseHelper.ExecuteNonQuery(insertQuery);
                    Debug.WriteLine("[DB] 기본 카테고리 생성됨");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] 기본 카테고리 생성 실패: {ex.Message}");
            }
        }
    }
}