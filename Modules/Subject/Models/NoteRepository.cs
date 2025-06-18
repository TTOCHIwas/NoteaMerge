using System.Data.SQLite;
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
            private SQLiteConnection _connection;
            private SQLiteTransaction _transaction;
            public SQLiteConnection Connection => _connection;
            public SQLiteTransaction SqliteTransaction => _transaction;

            public Transaction(SQLiteConnection connection, SQLiteTransaction transaction)
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

        public static SubjectData GetSubjectById(int subjectId)
        {
            try
            {
                string query = @"
                    SELECT subjectId, Name, TotalStudyTimeSeconds, createdDate, lastModifiedDate 
                    FROM Subject 
                    WHERE subjectId = @subjectId";

                // 파라미터를 직접 치환 (Helpers.DatabaseHelper는 파라미터 지원 안함)
                query = query.Replace("@subjectId", subjectId.ToString());

                var result = DatabaseHelper.ExecuteSelect(query);
                if (result.Rows.Count > 0)
                {
                    var row = result.Rows[0];
                    return new SubjectData
                    {
                        SubjectId = Convert.ToInt32(row["subjectId"]),
                        SubjectName = row["Name"].ToString(),
                        TotalStudyTimeSeconds = Convert.ToInt32(row["TotalStudyTimeSeconds"]),
                        CreatedDate = DateTime.Parse(row["createdDate"].ToString()),
                        LastModifiedDate = DateTime.Parse(row["lastModifiedDate"].ToString())
                    };
                }

                Debug.WriteLine($"[NoteRepository] SubjectId {subjectId}를 찾을 수 없음");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NoteRepository ERROR] GetSubjectById 실패: {ex.Message}");
                return null;
            }
        }

        public static Transaction BeginTransaction()
        {
            var conn = new SQLiteConnection(GetConnectionString());
            conn.Open();
            var trans = conn.BeginTransaction();
            return new Transaction(conn, trans);
        }

        /// <summary>
        /// 새로운 카테고리(제목) 삽입 - 레벨과 부모 ID 포함
        /// </summary>
        public static int InsertCategory(string content, int subjectId, int displayOrder = -1, int level = 1,
    int? parentCategoryId = null, Transaction transaction = null)
        {
            try
            {
                Debug.WriteLine($"[SAVE] 새 카테고리 생성 시작: {content}");

                if (displayOrder == -1)
                {
                    displayOrder = GetNextDisplayOrder(subjectId);
                }

                // 헤딩 레벨 자동 감지
                int detectedLevel = GetHeadingLevel(content);
                if (detectedLevel > 0)
                {
                    level = detectedLevel;
                }

                SQLiteConnection conn;
                SQLiteTransaction trans = null;
                bool shouldDispose = false;

                if (transaction != null)
                {
                    conn = transaction.Connection;
                    trans = transaction.SqliteTransaction;
                }
                else
                {
                    conn = new SQLiteConnection(GetConnectionString());
                    conn.Open();
                    shouldDispose = true;
                }

                try
                {
                    // 1. time 레코드 생성
                    var timeCmd = conn.CreateCommand();
                    timeCmd.Transaction = trans;
                    timeCmd.CommandText = @"
                INSERT INTO time (createdDate, lastModifiedDate) 
                VALUES (@createdDate, @lastModifiedDate);
                SELECT last_insert_rowid();";

                    timeCmd.Parameters.AddWithValue("@createdDate", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    timeCmd.Parameters.AddWithValue("@lastModifiedDate", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                    var timeResult = timeCmd.ExecuteScalar();
                    int timeId = Convert.ToInt32(timeResult);

                    // 2. category 삽입 (timeId 포함)
                    var cmd = conn.CreateCommand();
                    cmd.Transaction = trans;
                    cmd.CommandText = @"
                INSERT INTO category (title, subjectId, timeId, displayOrder, level, parentCategoryId)
                VALUES (@title, @subjectId, @timeId, @displayOrder, @level, @parentCategoryId);
                SELECT last_insert_rowid();";

                    cmd.Parameters.AddWithValue("@title", content);
                    cmd.Parameters.AddWithValue("@subjectId", subjectId);
                    cmd.Parameters.AddWithValue("@timeId", timeId);
                    cmd.Parameters.AddWithValue("@displayOrder", displayOrder);
                    cmd.Parameters.AddWithValue("@level", level);
                    cmd.Parameters.AddWithValue("@parentCategoryId", parentCategoryId ?? (object)DBNull.Value);

                    var result = cmd.ExecuteScalar();
                    int categoryId = Convert.ToInt32(result);

                    Debug.WriteLine($"[DB] 새 카테고리 삽입 완료. CategoryId: {categoryId}, Level: {level}, TimeId: {timeId}");
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
            return Notea.Database.DatabaseInitializer.GetConnectionString();
        }

        public static List<NoteCategory> LoadNotesBySubject(int subjectId)
        {
            return LoadNotesBySubjectByDisplayOrder(subjectId);
        }

        public static void RecalculateDisplayOrders(int subjectId)
        {
            try
            {
                using var conn = new SQLiteConnection(GetConnectionString());
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
                using var conn = new SQLiteConnection(GetConnectionString());
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
            // ✅ 수정: 모든 레벨의 마크다운 제목 (#, ##, ###, ####, #####, ######) 허용
            return Regex.IsMatch(content.Trim(), @"^#{1,6}\s+.+");
        }

        public static int? FindParentCategoryByLevel(int subjectId, int currentLevel, int currentDisplayOrder)
        {
            try
            {
                if (currentLevel <= 1) return null; // 최상위 레벨은 부모 없음

                string query = @"
            SELECT categoryId 
            FROM category 
            WHERE subjectId = @subjectId 
              AND level < @currentLevel 
              AND displayOrder < @currentDisplayOrder
            ORDER BY displayOrder DESC 
            LIMIT 1";

                using var conn = new SQLiteConnection(GetConnectionString());
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = query;
                cmd.Parameters.AddWithValue("@subjectId", subjectId);
                cmd.Parameters.AddWithValue("@currentLevel", currentLevel);
                cmd.Parameters.AddWithValue("@currentDisplayOrder", currentDisplayOrder);

                var result = cmd.ExecuteScalar();
                return result != null ? Convert.ToInt32(result) : (int?)null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] FindParentCategoryByLevel 실패: {ex.Message}");
                return null;
            }
        }

        public static void UpdateSubsequentCategoryHierarchy(int subjectId, int fromDisplayOrder, Transaction transaction = null)
        {
            try
            {
                SQLiteConnection conn;
                SQLiteTransaction trans = null;
                bool shouldDispose = false;

                if (transaction != null)
                {
                    conn = transaction.Connection;
                    trans = transaction.SqliteTransaction;
                }
                else
                {
                    conn = new SQLiteConnection(GetConnectionString());
                    conn.Open();
                    shouldDispose = true;
                }

                try
                {
                    // 1. 영향받는 모든 카테고리들의 부모 관계 재구성
                    var categoriesCmd = conn.CreateCommand();
                    categoriesCmd.Transaction = trans;
                    categoriesCmd.CommandText = @"
                SELECT categoryId, level, displayOrder, title
                FROM category 
                WHERE subjectId = @subjectId AND displayOrder > @fromDisplayOrder
                ORDER BY displayOrder";

                    categoriesCmd.Parameters.AddWithValue("@subjectId", subjectId);
                    categoriesCmd.Parameters.AddWithValue("@fromDisplayOrder", fromDisplayOrder);

                    var categories = new List<(int CategoryId, int Level, int DisplayOrder, string Title)>();
                    using var reader = categoriesCmd.ExecuteReader();
                    while (reader.Read())
                    {
                        categories.Add((
                            Convert.ToInt32(reader["categoryId"]),
                            Convert.ToInt32(reader["level"]),
                            Convert.ToInt32(reader["displayOrder"]),
                            reader["title"].ToString()
                        ));
                    }
                    reader.Close();

                    // 2. 각 카테고리의 새로운 부모 찾기 및 업데이트
                    foreach (var category in categories)
                    {
                        int? newParentId = FindParentCategoryByLevel(subjectId, category.Level, category.DisplayOrder);

                        var updateCmd = conn.CreateCommand();
                        updateCmd.Transaction = trans;
                        updateCmd.CommandText = @"
                    UPDATE category 
                    SET parentCategoryId = @parentId 
                    WHERE categoryId = @categoryId";

                        updateCmd.Parameters.AddWithValue("@parentId", newParentId ?? (object)DBNull.Value);
                        updateCmd.Parameters.AddWithValue("@categoryId", category.CategoryId);
                        updateCmd.ExecuteNonQuery();

                        Debug.WriteLine($"[DB] 카테고리 '{category.Title}' 부모 업데이트: {newParentId}");
                    }

                    // 3. 영향받는 텍스트들의 CategoryId 업데이트
                    UpdateTextCategoriesAfterHierarchyChange(subjectId, fromDisplayOrder, trans);

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
                Debug.WriteLine($"[DB ERROR] UpdateSubsequentCategoryHierarchy 실패: {ex.Message}");
            }
        }

        private static void UpdateTextCategoriesAfterHierarchyChange(int subjectId, int fromDisplayOrder, SQLiteTransaction trans)
        {
            try
            {
                // displayOrder 순으로 모든 텍스트 가져오기
                var textsCmd = new SQLiteCommand(@"
            SELECT textId, displayOrder
            FROM noteContent 
            WHERE subjectId = @subjectId AND displayOrder > @fromDisplayOrder
            ORDER BY displayOrder", trans.Connection, trans);

                textsCmd.Parameters.AddWithValue("@subjectId", subjectId);
                textsCmd.Parameters.AddWithValue("@fromDisplayOrder", fromDisplayOrder);

                var texts = new List<(int TextId, int DisplayOrder)>();
                using var reader = textsCmd.ExecuteReader();
                while (reader.Read())
                {
                    texts.Add((
                        Convert.ToInt32(reader["textId"]),
                        Convert.ToInt32(reader["displayOrder"])
                    ));
                }
                reader.Close();

                // 각 텍스트의 새로운 CategoryId 찾기
                foreach (var text in texts)
                {
                    var categoryCmd = new SQLiteCommand(@"
                SELECT categoryId 
                FROM category 
                WHERE subjectId = @subjectId AND displayOrder < @textDisplayOrder
                ORDER BY displayOrder DESC 
                LIMIT 1", trans.Connection, trans);

                    categoryCmd.Parameters.AddWithValue("@subjectId", subjectId);
                    categoryCmd.Parameters.AddWithValue("@textDisplayOrder", text.DisplayOrder);

                    var newCategoryId = categoryCmd.ExecuteScalar();
                    if (newCategoryId != null)
                    {
                        var updateCmd = new SQLiteCommand(@"
                    UPDATE noteContent 
                    SET categoryId = @categoryId 
                    WHERE textId = @textId", trans.Connection, trans);

                        updateCmd.Parameters.AddWithValue("@categoryId", Convert.ToInt32(newCategoryId));
                        updateCmd.Parameters.AddWithValue("@textId", text.TextId);
                        updateCmd.ExecuteNonQuery();
                    }
                }

                Debug.WriteLine($"[DB] {texts.Count}개 텍스트의 CategoryId 재할당 완료");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] UpdateTextCategoriesAfterHierarchyChange 실패: {ex.Message}");
            }
        }


        /// <summary>
        /// 마크다운 헤딩인지 확인 (# ~ ######)
        /// </summary>
        public static bool IsMarkdownHeading(string content)
        {
            return IsCategoryHeading(content); // 통일된 로직 사용
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
                SQLiteConnection conn;
                SQLiteTransaction trans = null;
                bool shouldDispose = false;

                if (transaction != null)
                {
                    conn = transaction.Connection;
                    trans = transaction.SqliteTransaction;
                }
                else
                {
                    conn = new SQLiteConnection(GetConnectionString());
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
                using var conn = new SQLiteConnection(GetConnectionString());
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

                SQLiteConnection conn;
                SQLiteTransaction trans = null;
                bool shouldDispose = false;

                if (transaction != null)
                {
                    conn = transaction.Connection;
                    trans = transaction.SqliteTransaction;
                }
                else
                {
                    conn = new SQLiteConnection(GetConnectionString());
                    conn.Open();
                    shouldDispose = true;
                }

                try
                {
                    // 1. time 레코드 생성
                    var timeCmd = conn.CreateCommand();
                    timeCmd.Transaction = trans;
                    timeCmd.CommandText = @"
                INSERT INTO time (createdDate, lastModifiedDate) 
                VALUES (@createdDate, @lastModifiedDate);
                SELECT last_insert_rowid();";

                    timeCmd.Parameters.AddWithValue("@createdDate", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    timeCmd.Parameters.AddWithValue("@lastModifiedDate", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                    var timeResult = timeCmd.ExecuteScalar();
                    int timeId = Convert.ToInt32(timeResult);

                    // 2. noteContent 삽입 (timeId 포함)
                    var cmd = conn.CreateCommand();
                    cmd.Transaction = trans;
                    cmd.CommandText = @"
                INSERT INTO noteContent (content, subjectId, categoryId, displayOrder, contentType, imageUrl, timeId)
                VALUES (@content, @subjectId, @categoryId, @displayOrder, @contentType, @imageUrl, @timeId);
                SELECT last_insert_rowid();";

                    cmd.Parameters.AddWithValue("@content", content ?? "");
                    cmd.Parameters.AddWithValue("@subjectId", subjectId);
                    cmd.Parameters.AddWithValue("@categoryId", categoryId);
                    cmd.Parameters.AddWithValue("@displayOrder", displayOrder);
                    cmd.Parameters.AddWithValue("@contentType", contentType);
                    cmd.Parameters.AddWithValue("@imageUrl", imageUrl ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@timeId", timeId);

                    Debug.WriteLine($"[DB] InsertNewLine 실행 - Type: {contentType}, TimeId: {timeId}");

                    var result = cmd.ExecuteScalar();

                    if (result != null && result != DBNull.Value)
                    {
                        int textId = Convert.ToInt32(result);
                        Debug.WriteLine($"[DB] 새 라인 삽입 완료. TextId: {textId}, TimeId: {timeId}");
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
                SQLiteConnection conn;
                SQLiteTransaction trans = null;
                bool shouldDispose = false;

                if (transaction != null)
                {
                    conn = transaction.Connection;
                    trans = transaction.SqliteTransaction;
                }
                else
                {
                    conn = new SQLiteConnection(GetConnectionString());
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
                SQLiteConnection conn;
                SQLiteTransaction trans = null;
                bool shouldDispose = false;

                if (transaction != null)
                {
                    conn = transaction.Connection;
                    trans = transaction.SqliteTransaction;
                }
                else
                {
                    conn = new SQLiteConnection(GetConnectionString());
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
                SQLiteConnection conn;
                SQLiteTransaction trans = null;
                bool shouldDispose = false;

                if (transaction != null)
                {
                    conn = transaction.Connection;
                    trans = transaction.SqliteTransaction;
                }
                else
                {
                    conn = new SQLiteConnection(GetConnectionString());
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
                if (IsCategoryHeading(line.Content))  // 모든 레벨의 제목 지원
                {
                    // 제목인 경우
                    if (line.IsHeadingLine && line.CategoryId > 0)
                    {
                        // ✅ 기존 제목 업데이트 (새 카테고리 생성 X)
                        UpdateCategory(line.CategoryId, line.Content);

                        // 제목 레벨 변경시 부모-자식 관계 재구성
                        int newLevel = GetHeadingLevel(line.Content);
                        UpdateCategoryLevel(line.CategoryId, newLevel);

                        // 하위 요소들의 부모 관계 업데이트
                        UpdateSubsequentCategoryHierarchy(line.SubjectId, line.DisplayOrder);

                        Debug.WriteLine($"[DB] 기존 제목 업데이트 완료. CategoryId: {line.CategoryId}");
                    }
                    else
                    {
                        // 새로운 제목 삽입
                        int level = GetHeadingLevel(line.Content);
                        int? parentId = FindParentCategoryByLevel(line.SubjectId, level, line.DisplayOrder);

                        int newCategoryId = InsertCategory(line.Content, line.SubjectId, line.DisplayOrder, level, parentId);
                        line.CategoryId = newCategoryId;
                        line.IsHeadingLine = true;

                        // 새 제목 추가 후 하위 요소들의 부모 관계 업데이트
                        UpdateSubsequentCategoryHierarchy(line.SubjectId, line.DisplayOrder);

                        Debug.WriteLine($"[DB] 새 제목 생성 완료. CategoryId: {newCategoryId}");
                    }
                }
                else
                {
                    // 일반 텍스트인 경우
                    if (line.CategoryId <= 0)
                    {
                        // CategoryId가 없으면 가장 가까운 이전 카테고리 찾기
                        line.CategoryId = FindNearestPreviousCategory(line.SubjectId, line.DisplayOrder);
                        if (line.CategoryId <= 0)
                        {
                            Debug.WriteLine($"[WARNING] CategoryId를 찾을 수 없어 저장 건너뜀. DisplayOrder: {line.DisplayOrder}");
                            return;
                        }
                    }

                    if (line.TextId <= 0)
                    {
                        // 새로운 라인 삽입
                        int newTextId = InsertNewLine(line.Content, line.SubjectId, line.CategoryId, line.DisplayOrder, line.ContentType, line.ImageUrl);
                        line.TextId = newTextId;
                        Debug.WriteLine($"[DB] 새 텍스트 생성 완료. TextId: {newTextId}");
                    }
                    else
                    {
                        // 기존 라인 업데이트
                        UpdateLine(line);
                        Debug.WriteLine($"[DB] 기존 텍스트 업데이트 완료. TextId: {line.TextId}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] SaveOrUpdateLine 실패: {ex.Message}");
                throw;
            }
        }

        public static void UpdateCategoryLevel(int categoryId, int newLevel, Transaction transaction = null)
        {
            try
            {
                SQLiteConnection conn;
                SQLiteTransaction trans = null;
                bool shouldDispose = false;

                if (transaction != null)
                {
                    conn = transaction.Connection;
                    trans = transaction.SqliteTransaction;
                }
                else
                {
                    conn = new SQLiteConnection(GetConnectionString());
                    conn.Open();
                    shouldDispose = true;
                }

                try
                {
                    var cmd = conn.CreateCommand();
                    cmd.Transaction = trans;
                    cmd.CommandText = @"
                UPDATE category 
                SET level = @level
                WHERE categoryId = @categoryId";

                    cmd.Parameters.AddWithValue("@level", newLevel);
                    cmd.Parameters.AddWithValue("@categoryId", categoryId);

                    cmd.ExecuteNonQuery();
                    Debug.WriteLine($"[DB] 카테고리 레벨 업데이트: CategoryId={categoryId}, Level={newLevel}");
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
                Debug.WriteLine($"[DB ERROR] UpdateCategoryLevel 실패: {ex.Message}");
            }
        }

        public static int FindNearestPreviousCategory(int subjectId, int displayOrder)
        {
            try
            {
                string query = @"
            SELECT categoryId 
            FROM category 
            WHERE subjectId = @subjectId AND displayOrder < @displayOrder
            ORDER BY displayOrder DESC 
            LIMIT 1";

                using var conn = new SQLiteConnection(GetConnectionString());
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = query;
                cmd.Parameters.AddWithValue("@subjectId", subjectId);
                cmd.Parameters.AddWithValue("@displayOrder", displayOrder);

                var result = cmd.ExecuteScalar();
                if (result != null)
                {
                    return Convert.ToInt32(result);
                }

                // 이전 카테고리가 없으면 기본 카테고리 생성
                return CreateDefaultCategory(subjectId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] FindNearestPreviousCategory 실패: {ex.Message}");
                return CreateDefaultCategory(subjectId);
            }
        }

        private static int CreateDefaultCategory(int subjectId)
        {
            try
            {
                return InsertCategory("# 기본 섹션", subjectId, 0, 1, null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] CreateDefaultCategory 실패: {ex.Message}");
                return 1; // 최소한의 fallback
            }
        }

        public static List<NoteCategory> LoadNotesBySubjectByDisplayOrder(int subjectId)
        {
            var allCategories = new List<NoteCategory>();
            var categoryMap = new Dictionary<int, NoteCategory>();

            try
            {
                Debug.WriteLine($"[DB] LoadNotesBySubjectByDisplayOrder 시작: SubjectId={subjectId}");

                using var conn = new SQLiteConnection(GetConnectionString());
                conn.Open();

                // 1. 모든 카테고리와 텍스트를 displayOrder 순으로 조회
                var allElementsCmd = conn.CreateCommand();
                allElementsCmd.CommandText = @"
            SELECT 'category' as type, categoryId as id, title as content, displayOrder, level, 
                   parentCategoryId, null as contentType, null as imageUrl
            FROM category 
            WHERE subjectId = @subjectId
            
            UNION ALL
            
            SELECT 'text' as type, textId as id, content, displayOrder, 1 as level,
                   categoryId as parentCategoryId, contentType, imageUrl
            FROM noteContent 
            WHERE subjectId = @subjectId
            
            ORDER BY displayOrder";

                allElementsCmd.Parameters.AddWithValue("@subjectId", subjectId);

                var allElements = new List<(string Type, int Id, string Content, int DisplayOrder, int Level, int? ParentId, string ContentType, string ImageUrl)>();

                using var reader = allElementsCmd.ExecuteReader();
                while (reader.Read())
                {
                    allElements.Add((
                        reader["type"].ToString(),
                        Convert.ToInt32(reader["id"]),
                        reader["content"].ToString(),
                        Convert.ToInt32(reader["displayOrder"]),
                        Convert.ToInt32(reader["level"]),
                        reader["parentCategoryId"] == DBNull.Value ? null : Convert.ToInt32(reader["parentCategoryId"]),
                        reader["contentType"]?.ToString(),
                        reader["imageUrl"]?.ToString()
                    ));
                }
                reader.Close();

                Debug.WriteLine($"[DB] 총 {allElements.Count}개 요소 로드됨");

                // 2. displayOrder 순으로 순회하며 계층 구조 구성
                NoteCategory currentCategory = null;
                var categoryStack = new Stack<NoteCategory>(); // 계층 구조 추적용

                foreach (var element in allElements)
                {
                    if (element.Type == "category")
                    {
                        // 카테고리 생성
                        var category = new NoteCategory
                        {
                            CategoryId = element.Id,
                            Title = element.Content,
                            DisplayOrder = element.DisplayOrder,
                            Level = element.Level,
                            ParentCategoryId = element.ParentId,
                            Lines = new List<NoteLine>(),
                            SubCategories = new List<NoteCategory>()
                        };

                        categoryMap[element.Id] = category;

                        // 계층 구조 설정
                        if (element.Level == 1 || categoryStack.Count == 0)
                        {
                            // 최상위 레벨이거나 스택이 비어있으면 루트에 추가
                            allCategories.Add(category);
                            categoryStack.Clear();
                            categoryStack.Push(category);
                        }
                        else
                        {
                            // 적절한 부모 찾기
                            while (categoryStack.Count > 0 && categoryStack.Peek().Level >= element.Level)
                            {
                                categoryStack.Pop();
                            }

                            if (categoryStack.Count > 0)
                            {
                                var parent = categoryStack.Peek();
                                parent.SubCategories.Add(category);
                                category.ParentCategoryId = parent.CategoryId;
                            }
                            else
                            {
                                allCategories.Add(category);
                            }

                            categoryStack.Push(category);
                        }

                        currentCategory = category;
                        Debug.WriteLine($"[LOAD] 카테고리 추가: '{category.Title}' (Level: {category.Level}, ID: {category.CategoryId})");
                    }
                    else if (element.Type == "text")
                    {
                        // 텍스트 요소 생성
                        var line = new NoteLine
                        {
                            Index = element.Id,
                            Content = element.Content,
                            ContentType = element.ContentType ?? "text",
                            ImageUrl = element.ImageUrl,
                            DisplayOrder = element.DisplayOrder
                        };

                        // 현재 카테고리에 추가 (없으면 기본 카테고리 생성)
                        if (currentCategory == null)
                        {
                            currentCategory = CreateDefaultCategoryForLoading(subjectId);
                            allCategories.Add(currentCategory);
                            categoryMap[currentCategory.CategoryId] = currentCategory;
                        }

                        currentCategory.Lines.Add(line);
                        Debug.WriteLine($"[LOAD] 텍스트 추가: '{line.Content.Substring(0, Math.Min(30, line.Content.Length))}...' → 카테고리 '{currentCategory.Title}'");
                    }
                }

                Debug.WriteLine($"[DB] LoadNotesBySubjectByDisplayOrder 완료. 루트 카테고리 수: {allCategories.Count}");
                return allCategories;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] LoadNotesBySubjectByDisplayOrder 실패: {ex.Message}");
                return allCategories;
            }
        }

        private static NoteCategory CreateDefaultCategoryForLoading(int subjectId)
        {
            return new NoteCategory
            {
                CategoryId = 0, // 임시 ID
                Title = "# 내용",
                DisplayOrder = 0,
                Level = 1,
                ParentCategoryId = null,
                Lines = new List<NoteLine>(),
                SubCategories = new List<NoteCategory>()
            };
        }

        public static List<NoteCategory> LoadNotesBySubjectWithHierarchy(int subjectId)
        {
            // ✅ 새로운 displayOrder 기반 로딩 방식 사용
            return LoadNotesBySubjectByDisplayOrder(subjectId);
        }

        public static void SaveLinesInTransaction(List<MarkdownLineViewModel> lines)
        {
            using var conn = new SQLiteConnection(GetConnectionString());
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

        public static int GetSubjectIdByName(string subjectName)
        {
            try
            {
                string query = "SELECT subjectId FROM Subject WHERE Name = @name";

                using (var connection = new SQLiteConnection(GetConnectionString()))
                {
                    connection.Open();
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = query;
                    cmd.Parameters.AddWithValue("@name", subjectName);

                    var result = cmd.ExecuteScalar();
                    return result != null ? Convert.ToInt32(result) : 0;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NoteRepository] GetSubjectIdByName 오류: {ex.Message}");
                return 0;
            }
        }

        // ✅ 새로 추가: Subject 테이블에서 과목명 조회
        public static string GetSubjectNameById(int subjectId)
        {
            try
            {
                // ✅ 수정: title → Name (올바른 컬럼명 사용)
                string query = $"SELECT Name FROM Subject WHERE subjectId = {subjectId}";
                var result = DatabaseHelper.ExecuteSelect(query);

                if (result.Rows.Count > 0)
                {
                    return result.Rows[0]["Name"]?.ToString() ?? "";
                }
                else
                {
                    Debug.WriteLine($"[NoteRepository] SubjectId {subjectId}에 해당하는 과목 없음");
                    return "";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NoteRepository] GetSubjectNameById 오류: {ex.Message}");
                return "";
            }
        }

        public static int CreateCategory(int subjectId, string title, int level = 1, int? parentCategoryId = null)
        {
            try
            {
                // 1. Subject 존재 확인
                string subjectExistsQuery = $"SELECT COUNT(*) as count FROM Subject WHERE subjectId = {subjectId}";
                var subjectResult = DatabaseHelper.ExecuteSelect(subjectExistsQuery);

                if (subjectResult.Rows.Count == 0 || Convert.ToInt32(subjectResult.Rows[0]["count"]) == 0)
                {
                    Debug.WriteLine($"[NoteRepository ERROR] SubjectId {subjectId}가 Subject 테이블에 없음");
                    return 0;
                }

                // 2. time 레코드 생성
                string timeQuery = $@"
                    INSERT INTO time (createdDate, lastModifiedDate) 
                    VALUES ('{DateTime.Now:yyyy-MM-dd HH:mm:ss}', '{DateTime.Now:yyyy-MM-dd HH:mm:ss}')";
                DatabaseHelper.ExecuteNonQuery(timeQuery);

                // 3. timeId 조회
                string getTimeIdQuery = "SELECT last_insert_rowid() as timeId";
                var timeResult = DatabaseHelper.ExecuteSelect(getTimeIdQuery);
                int timeId = timeResult.Rows.Count > 0 ? Convert.ToInt32(timeResult.Rows[0]["timeId"]) : 1;

                // 4. 카테고리 생성
                string insertQuery = $@"
                    INSERT INTO category (title, subjectId, timeId, level, parentCategoryId, displayOrder) 
                    VALUES ('{title.Replace("'", "''")}', {subjectId}, {timeId}, {level}, 
                            {(parentCategoryId.HasValue ? parentCategoryId.Value.ToString() : "NULL")}, 
                            (SELECT COALESCE(MAX(displayOrder), 0) + 1 FROM category WHERE subjectId = {subjectId}))";

                DatabaseHelper.ExecuteNonQuery(insertQuery);

                // 5. categoryId 조회
                string getCategoryIdQuery = "SELECT last_insert_rowid() as categoryId";
                var categoryResult = DatabaseHelper.ExecuteSelect(getCategoryIdQuery);
                int categoryId = categoryResult.Rows.Count > 0 ? Convert.ToInt32(categoryResult.Rows[0]["categoryId"]) : 0;

                Debug.WriteLine($"[NoteRepository] 카테고리 생성: ID={categoryId}, Title={title}, SubjectId={subjectId}");
                return categoryId;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NoteRepository ERROR] CreateCategory 실패: {ex.Message}");
                return 0;
            }
        }

        public static int CreateText(int subjectId, int categoryId, string content, string contentType = "text", string imageUrl = null)
        {
            try
            {
                // 1. time 레코드 생성
                string timeQuery = $@"
                    INSERT INTO time (createdDate, lastModifiedDate) 
                    VALUES ('{DateTime.Now:yyyy-MM-dd HH:mm:ss}', '{DateTime.Now:yyyy-MM-dd HH:mm:ss}')";
                DatabaseHelper.ExecuteNonQuery(timeQuery);

                // 2. timeId 조회
                string getTimeIdQuery = "SELECT last_insert_rowid() as timeId";
                var timeResult = DatabaseHelper.ExecuteSelect(getTimeIdQuery);
                int timeId = timeResult.Rows.Count > 0 ? Convert.ToInt32(timeResult.Rows[0]["timeId"]) : 1;

                // 3. 텍스트 저장
                string imageUrlPart = imageUrl != null ? $"'{imageUrl.Replace("'", "''")}'" : "NULL";
                string insertQuery = $@"
                    INSERT INTO noteContent (content, categoryId, subjectId, timeId, contentType, imageUrl, displayOrder) 
                    VALUES ('{content.Replace("'", "''")}', {categoryId}, {subjectId}, {timeId}, 
                            '{contentType}', {imageUrlPart},
                            (SELECT COALESCE(MAX(displayOrder), 0) + 1 FROM noteContent WHERE subjectId = {subjectId}))";

                DatabaseHelper.ExecuteNonQuery(insertQuery);

                // 4. textId 조회
                string getTextIdQuery = "SELECT last_insert_rowid() as textId";
                var textResult = DatabaseHelper.ExecuteSelect(getTextIdQuery);
                int textId = textResult.Rows.Count > 0 ? Convert.ToInt32(textResult.Rows[0]["textId"]) : 0;

                Debug.WriteLine($"[NoteRepository] 텍스트 생성: ID={textId}, Type={contentType}");
                return textId;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NoteRepository ERROR] CreateText 실패: {ex.Message}");
                return 0;
            }
        }

        public static void EnsureDefaultCategory(int subjectId)
        {
            try
            {
                // 1. Subject 테이블에 해당 과목이 있는지 확인
                string subjectExistsQuery = $"SELECT COUNT(*) as count FROM Subject WHERE subjectId = {subjectId}";
                var subjectResult = DatabaseHelper.ExecuteSelect(subjectExistsQuery);

                if (subjectResult.Rows.Count == 0 || Convert.ToInt32(subjectResult.Rows[0]["count"]) == 0)
                {
                    Debug.WriteLine($"[NoteRepository] SubjectId {subjectId}가 Subject 테이블에 없음");
                    return;
                }

                // 2. 해당 과목에 대한 기본 카테고리 확인
                string checkQuery = $"SELECT COUNT(*) as count FROM category WHERE subjectId = {subjectId}";
                var result = DatabaseHelper.ExecuteSelect(checkQuery);

                if (result.Rows.Count > 0 && Convert.ToInt32(result.Rows[0]["count"]) == 0)
                {
                    // 3. time 테이블에 기본 레코드 추가
                    string timeQuery = $@"
                INSERT INTO time (createdDate, lastModifiedDate) 
                VALUES ('{DateTime.Now:yyyy-MM-dd HH:mm:ss}', '{DateTime.Now:yyyy-MM-dd HH:mm:ss}')";
                    DatabaseHelper.ExecuteNonQuery(timeQuery);

                    // 4. 해당 과목에 대한 기본 카테고리 생성 (categoryId는 자동 증가)
                    string insertQuery = $@"
                INSERT INTO category (title, subjectId, timeId, displayOrder, level) 
                VALUES (' ', {subjectId}, 1, 0, 1)";
                    DatabaseHelper.ExecuteNonQuery(insertQuery);
                    Debug.WriteLine($"[NoteRepository] 기본 카테고리 생성: SubjectId={subjectId}");
                }
                else
                {
                    Debug.WriteLine($"[NoteRepository] SubjectId {subjectId}에 이미 카테고리 존재함");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NoteRepository ERROR] EnsureDefaultCategory 실패: {ex.Message}");
            }
        }

        public static void UpdateLineCategoryId(int textId, int newCategoryId, Transaction transaction = null)
        {
            try
            {
                SQLiteConnection conn;
                SQLiteTransaction trans = null;
                bool shouldDispose = false;

                if (transaction != null)
                {
                    conn = transaction.Connection;
                    trans = transaction.SqliteTransaction;
                }
                else
                {
                    conn = new SQLiteConnection(GetConnectionString());
                    conn.Open();
                    shouldDispose = true;
                }

                try
                {
                    var cmd = conn.CreateCommand();
                    cmd.Transaction = trans;
                    cmd.CommandText = @"
                UPDATE noteContent 
                SET categoryId = @categoryId
                WHERE textId = @textId";

                    cmd.Parameters.AddWithValue("@categoryId", newCategoryId);
                    cmd.Parameters.AddWithValue("@textId", textId);

                    int rowsAffected = cmd.ExecuteNonQuery();
                    Debug.WriteLine($"[DB] 텍스트 CategoryId 업데이트 완료. TextId: {textId}, 새 CategoryId: {newCategoryId}");
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
                Debug.WriteLine($"[DB ERROR] UpdateLineCategoryId 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// DisplayOrder 기반으로 이전 카테고리 찾기
        /// </summary>
        public static int FindPreviousCategoryIdByDisplayOrder(int subjectId, int currentDisplayOrder)
        {
            try
            {
                string query = @"
            SELECT categoryId 
            FROM category 
            WHERE subjectId = @subjectId AND displayOrder < @currentDisplayOrder
            ORDER BY displayOrder DESC 
            LIMIT 1";

                using var conn = new SQLiteConnection(GetConnectionString());
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = query;
                cmd.Parameters.AddWithValue("@subjectId", subjectId);
                cmd.Parameters.AddWithValue("@currentDisplayOrder", currentDisplayOrder);

                var result = cmd.ExecuteScalar();
                return result != null ? Convert.ToInt32(result) : 1; // 기본값 1
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] FindPreviousCategoryIdByDisplayOrder 실패: {ex.Message}");
                return 1;
            }
        }

        /// <summary>
        /// 특정 카테고리 이후의 모든 텍스트 요소들을 새로운 카테고리로 재할당
        /// </summary>
        public static void ReassignSubsequentTextsToCategory(int subjectId, int fromDisplayOrder, int newCategoryId, Transaction transaction = null)
        {
            try
            {
                SQLiteConnection conn;
                SQLiteTransaction trans = null;
                bool shouldDispose = false;

                if (transaction != null)
                {
                    conn = transaction.Connection;
                    trans = transaction.SqliteTransaction;
                }
                else
                {
                    conn = new SQLiteConnection(GetConnectionString());
                    conn.Open();
                    shouldDispose = true;
                }

                try
                {
                    var cmd = conn.CreateCommand();
                    cmd.Transaction = trans;
                    cmd.CommandText = @"
                UPDATE noteContent 
                SET categoryId = @newCategoryId
                WHERE subjectId = @subjectId 
                  AND displayOrder > @fromDisplayOrder
                  AND displayOrder < (
                      SELECT MIN(displayOrder) 
                      FROM category 
                      WHERE subjectId = @subjectId AND displayOrder > @fromDisplayOrder
                  )";

                    cmd.Parameters.AddWithValue("@newCategoryId", newCategoryId);
                    cmd.Parameters.AddWithValue("@subjectId", subjectId);
                    cmd.Parameters.AddWithValue("@fromDisplayOrder", fromDisplayOrder);

                    int rowsAffected = cmd.ExecuteNonQuery();
                    Debug.WriteLine($"[DB] {rowsAffected}개 텍스트의 CategoryId 재할당 완료");
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
                Debug.WriteLine($"[DB ERROR] ReassignSubsequentTextsToCategory 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 카테고리와 그 하위 모든 텍스트 삭제
        /// </summary>
        public static void DeleteCategoryAndTexts(int categoryId, Transaction transaction = null)
        {
            try
            {
                SQLiteConnection conn;
                SQLiteTransaction trans = null;
                bool shouldDispose = false;

                if (transaction != null)
                {
                    conn = transaction.Connection;
                    trans = transaction.SqliteTransaction;
                }
                else
                {
                    conn = new SQLiteConnection(GetConnectionString());
                    conn.Open();
                    shouldDispose = true;
                }

                try
                {
                    // 1. 해당 카테고리의 모든 텍스트 삭제
                    var deleteTextsCmd = conn.CreateCommand();
                    deleteTextsCmd.Transaction = trans;
                    deleteTextsCmd.CommandText = "DELETE FROM noteContent WHERE categoryId = @categoryId";
                    deleteTextsCmd.Parameters.AddWithValue("@categoryId", categoryId);
                    int textCount = deleteTextsCmd.ExecuteNonQuery();

                    // 2. 카테고리 삭제
                    var deleteCategoryCmd = conn.CreateCommand();
                    deleteCategoryCmd.Transaction = trans;
                    deleteCategoryCmd.CommandText = "DELETE FROM category WHERE categoryId = @categoryId";
                    deleteCategoryCmd.Parameters.AddWithValue("@categoryId", categoryId);
                    int categoryCount = deleteCategoryCmd.ExecuteNonQuery();

                    Debug.WriteLine($"[DB] 카테고리 삭제 완료. CategoryId: {categoryId}, 삭제된 텍스트: {textCount}개");
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
                Debug.WriteLine($"[DB ERROR] DeleteCategoryAndTexts 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 빈 카테고리들 정리 (텍스트가 없는 카테고리 삭제)
        /// </summary>
        public static void CleanupEmptyCategories(int subjectId, Transaction transaction = null)
        {
            try
            {
                SQLiteConnection conn;
                SQLiteTransaction trans = null;
                bool shouldDispose = false;

                if (transaction != null)
                {
                    conn = transaction.Connection;
                    trans = transaction.SqliteTransaction;
                }
                else
                {
                    conn = new SQLiteConnection(GetConnectionString());
                    conn.Open();
                    shouldDispose = true;
                }

                try
                {
                    var cmd = conn.CreateCommand();
                    cmd.Transaction = trans;
                    cmd.CommandText = @"
                DELETE FROM category 
                WHERE subjectId = @subjectId 
                  AND categoryId NOT IN (
                      SELECT DISTINCT categoryId 
                      FROM noteContent 
                      WHERE subjectId = @subjectId
                  )";

                    cmd.Parameters.AddWithValue("@subjectId", subjectId);

                    int deletedCount = cmd.ExecuteNonQuery();
                    if (deletedCount > 0)
                    {
                        Debug.WriteLine($"[DB] {deletedCount}개 빈 카테고리 정리 완료");
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
                Debug.WriteLine($"[DB ERROR] CleanupEmptyCategories 실패: {ex.Message}");
            }
        }
    }
}