using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Notea.Modules.Subject.ViewModels;

namespace Notea.Modules.Subject.Models
{
    public static class NoteRepository
    {
        // DB 경로는 DatabaseHelper에서 관리
        private static string GetConnectionString()
        {
            return Notea.Database.DatabaseInitializer.GetConnectionString();
        }

        #region 제목 판별 및 헤딩 관련 메서드

        /// <summary>
        /// 카테고리로 저장할 제목인지 확인하는 메서드 - 모든 레벨의 마크다운 제목 허용
        /// </summary>
        public static bool IsCategoryHeading(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return false;
            // ✅ 수정: 모든 레벨의 마크다운 제목 (#, ##, ###, ####, #####, ######) 허용
            return Regex.IsMatch(content.Trim(), @"^#{1,6}\s+.+");
        }

        /// <summary>
        /// 마크다운 헤딩인지 확인 (# ~ ######) - IsCategoryHeading과 동일하게 통일
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

        #endregion

        #region 부모-자식 관계 및 계층 구조 메서드

        /// <summary>
        /// 제목에서 부모 카테고리 찾기 (레벨 기반)
        /// </summary>
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

        /// <summary>
        /// 가장 가까운 이전 카테고리 찾기 (displayOrder 기반)
        /// </summary>
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
        /// 기본 카테고리 생성 (필요한 경우)
        /// </summary>
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

        #endregion

        #region 카테고리 관련 메서드

        /// <summary>
        /// 새로운 카테고리 삽입 (timeId 올바르게 생성)
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
        /// 카테고리 레벨 업데이트
        /// </summary>
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

        /// <summary>
        /// 카테고리 DisplayOrder 업데이트
        /// </summary>
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
                    Debug.WriteLine($"[DB] 카테고리 DisplayOrder 업데이트: CategoryId={categoryId}, DisplayOrder={displayOrder}");
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
        /// 카테고리 삭제
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

        #endregion

        #region 텍스트 관련 메서드

        /// <summary>
        /// 새로운 일반 텍스트 라인 삽입 (timeId 올바르게 생성)
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
                    Debug.WriteLine($"[DB] 라인 업데이트 완료. TextId: {line.TextId}");
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

        /// <summary>
        /// 텍스트 라인의 CategoryId 업데이트
        /// </summary>
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
        /// 텍스트 라인 DisplayOrder 업데이트
        /// </summary>
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
                    Debug.WriteLine($"[DB] 텍스트 DisplayOrder 업데이트: TextId={textId}, DisplayOrder={displayOrder}");
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

        /// <summary>
        /// 텍스트 라인 삭제
        /// </summary>
        public static void DeleteLine(int textId)
        {
            try
            {
                string query = $"DELETE FROM noteContent WHERE textId = {textId}";
                int rowsAffected = Notea.Helpers.DatabaseHelper.ExecuteNonQuery(query);
                Debug.WriteLine($"[DB] 라인 삭제 완료. TextId: {textId}, 영향받은 행: {rowsAffected}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] DeleteLine 실패: {ex.Message}");
            }
        }

        #endregion

        #region 계층 구조 업데이트 메서드

        /// <summary>
        /// 특정 displayOrder 이후의 모든 요소의 부모 카테고리 업데이트
        /// </summary>
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

        /// <summary>
        /// 계층 구조 변경 후 텍스트들의 CategoryId 재할당
        /// </summary>
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

        #endregion

        #region 저장 로직

        /// <summary>
        /// 라인이 제목인지 일반 텍스트인지 판단하여 적절히 저장 (개선된 버전)
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

        #endregion

        #region 데이터 로딩 메서드

        /// <summary>
        /// displayOrder 기반으로 모든 요소를 순차적으로 로드 (새로운 방식)
        /// </summary>
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

        /// <summary>
        /// 로딩 시 기본 카테고리 생성 (메모리에서만)
        /// </summary>
        private static NoteCategory CreateDefaultCategoryForLoading(int subjectId)
        {
            return new NoteCategory
            {
                CategoryId = 0, // 임시 ID
                Title = "# 내용",
                DisplayOrder = 0,
                Level = 1,
                ParentCategoryId = 0,
                Lines = new List<NoteLine>(),
                SubCategories = new List<NoteCategory>()
            };
        }

        /// <summary>
        /// 기존 LoadNotesBySubjectWithHierarchy를 새로운 방식으로 리다이렉트
        /// </summary>
        public static List<NoteCategory> LoadNotesBySubjectWithHierarchy(int subjectId)
        {
            // ✅ 새로운 displayOrder 기반 로딩 방식 사용
            return LoadNotesBySubjectByDisplayOrder(subjectId);
        }

        /// <summary>
        /// 기존 LoadNotesBySubject도 새로운 방식으로 리다이렉트
        /// </summary>
        public static List<NoteCategory> LoadNotesBySubject(int subjectId)
        {
            // ✅ 새로운 displayOrder 기반 로딩 방식 사용
            return LoadNotesBySubjectByDisplayOrder(subjectId);
        }

        #endregion

        #region 유틸리티 메서드

        /// <summary>
        /// 다음 DisplayOrder 값 생성
        /// </summary>
        public static int GetNextDisplayOrder(int subjectId)
        {
            try
            {
                string query = @"
                    SELECT COALESCE(MAX(displayOrder), 0) + 1 as nextOrder
                    FROM (
                        SELECT displayOrder FROM category WHERE subjectId = @subjectId
                        UNION ALL
                        SELECT displayOrder FROM noteContent WHERE subjectId = @subjectId
                    )";

                using var conn = new SQLiteConnection(GetConnectionString());
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = query;
                cmd.Parameters.AddWithValue("@subjectId", subjectId);

                var result = cmd.ExecuteScalar();
                return result != null ? Convert.ToInt32(result) : 1;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] GetNextDisplayOrder 실패: {ex.Message}");
                return 1;
            }
        }

        /// <summary>
        /// Subject 관련 메서드들
        /// </summary>
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

        public static string GetSubjectNameById(int subjectId)
        {
            try
            {
                string query = "SELECT Name FROM Subject WHERE subjectId = @subjectId";

                using (var connection = new SQLiteConnection(GetConnectionString()))
                {
                    connection.Open();
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = query;
                    cmd.Parameters.AddWithValue("@subjectId", subjectId);

                    var result = cmd.ExecuteScalar();
                    return result?.ToString() ?? "";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NoteRepository] GetSubjectNameById 오류: {ex.Message}");
                return "";
            }
        }

        /// <summary>
        /// 트랜잭션 래퍼 클래스
        /// </summary>
        public class Transaction : IDisposable
        {
            public SQLiteConnection Connection { get; }
            public SQLiteTransaction SqliteTransaction { get; }

            public Transaction(SQLiteConnection connection, SQLiteTransaction transaction)
            {
                Connection = connection;
                SqliteTransaction = transaction;
            }

            public void Commit()
            {
                SqliteTransaction.Commit();
            }

            public void Rollback()
            {
                SqliteTransaction.Rollback();
            }

            public void Dispose()
            {
                SqliteTransaction?.Dispose();
                Connection?.Dispose();
            }
        }

        /// <summary>
        /// 트랜잭션 시작
        /// </summary>
        public static Transaction BeginTransaction()
        {
            var conn = new SQLiteConnection(GetConnectionString());
            conn.Open();
            var trans = conn.BeginTransaction();
            return new Transaction(conn, trans);
        }

        #endregion

        #region 레거시 메서드들 (호환성을 위해 유지)

        /// <summary>
        /// 텍스트 재할당 (호환성용)
        /// </summary>
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

                int rowsAffected = Notea.Helpers.DatabaseHelper.ExecuteNonQuery(query);
                Debug.WriteLine($"[DB] 텍스트 재할당 완료. {fromCategoryId} -> {toCategoryId}, 영향받은 행: {rowsAffected}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] ReassignTextsToCategory 실패: {ex.Message}");
            }
        }

        #endregion
    }
}