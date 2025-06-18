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
                Debug.WriteLine($"[FIND] FindNearestPreviousCategory 시작 - SubjectId: {subjectId}, DisplayOrder: {displayOrder}");

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
                    int categoryId = Convert.ToInt32(result);
                    Debug.WriteLine($"[FIND] 이전 카테고리 발견: CategoryId={categoryId}");
                    return categoryId;
                }
                else
                {
                    Debug.WriteLine($"[FIND] 이전 카테고리 없음 - SubjectId {subjectId}에 대한 카테고리가 아직 없음");

                    // ✅ 중요: 기본 카테고리를 생성하지 말고 -1 반환하여 저장 로직에서 처리하도록 함
                    return -1; // 카테고리가 없음을 명시적으로 표시
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FIND ERROR] FindNearestPreviousCategory 실패: {ex.Message}");
                return -1; // 에러 시에도 -1 반환
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
            try
            {
                string title = ExtractHeadingText(content);

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
                    cmd.CommandText = "UPDATE category SET title = @title WHERE categoryId = @categoryId";
                    cmd.Parameters.AddWithValue("@title", title);
                    cmd.Parameters.AddWithValue("@categoryId", categoryId);

                    int rowsAffected = cmd.ExecuteNonQuery();
                    Debug.WriteLine($"[DB] 카테고리 업데이트: CategoryId={categoryId}, 제목='{title}', 영향받은 행={rowsAffected}");
                }
                finally
                {
                    if (shouldDispose)
                    {
                        conn?.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] UpdateCategory 실패: {ex.Message}");
                throw;
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
                    cmd.CommandText = "UPDATE category SET level = @level WHERE categoryId = @categoryId";
                    cmd.Parameters.AddWithValue("@level", newLevel);
                    cmd.Parameters.AddWithValue("@categoryId", categoryId);

                    int rowsAffected = cmd.ExecuteNonQuery();
                    Debug.WriteLine($"[DB] 카테고리 레벨 업데이트: CategoryId={categoryId}, 새 레벨={newLevel}, 영향받은 행={rowsAffected}");
                }
                finally
                {
                    if (shouldDispose)
                    {
                        conn?.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] UpdateCategoryLevel 실패: {ex.Message}");
                throw;
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
                    cmd.CommandText = "UPDATE category SET displayOrder = @displayOrder WHERE categoryId = @categoryId";
                    cmd.Parameters.AddWithValue("@displayOrder", displayOrder);
                    cmd.Parameters.AddWithValue("@categoryId", categoryId);

                    cmd.ExecuteNonQuery();
                }
                finally
                {
                    if (shouldDispose)
                    {
                        conn?.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] UpdateCategoryDisplayOrder 실패: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 카테고리 삭제
        /// </summary>
        public static void DeleteCategory(int categoryId, bool deleteTexts = true)
        {
            try
            {
                // 기본 카테고리(1)는 삭제하지 않음
                if (categoryId <= 1)
                {
                    Debug.WriteLine("[WARNING] 기본 카테고리는 삭제할 수 없습니다.");
                    return;
                }

                using var conn = new SQLiteConnection(GetConnectionString());
                conn.Open();
                using var transaction = conn.BeginTransaction();

                try
                {
                    if (deleteTexts)
                    {
                        // 해당 카테고리의 모든 텍스트 삭제
                        var deleteTextsCmd = conn.CreateCommand();
                        deleteTextsCmd.Transaction = transaction;
                        deleteTextsCmd.CommandText = "DELETE FROM noteContent WHERE categoryId = @categoryId";
                        deleteTextsCmd.Parameters.AddWithValue("@categoryId", categoryId);
                        int deletedTexts = deleteTextsCmd.ExecuteNonQuery();
                        Debug.WriteLine($"[DB] 카테고리 {categoryId}의 텍스트 {deletedTexts}개 삭제됨");
                    }

                    // 하위 카테고리들의 부모를 현재 카테고리의 부모로 변경
                    var updateChildrenCmd = conn.CreateCommand();
                    updateChildrenCmd.Transaction = transaction;
                    updateChildrenCmd.CommandText = @"
                UPDATE category 
                SET parentCategoryId = (
                    SELECT parentCategoryId FROM category WHERE categoryId = @categoryId
                )
                WHERE parentCategoryId = @categoryId";
                    updateChildrenCmd.Parameters.AddWithValue("@categoryId", categoryId);
                    int updatedChildren = updateChildrenCmd.ExecuteNonQuery();

                    // 카테고리 삭제
                    var deleteCategoryCmd = conn.CreateCommand();
                    deleteCategoryCmd.Transaction = transaction;
                    deleteCategoryCmd.CommandText = "DELETE FROM category WHERE categoryId = @categoryId";
                    deleteCategoryCmd.Parameters.AddWithValue("@categoryId", categoryId);
                    int deletedCategory = deleteCategoryCmd.ExecuteNonQuery();

                    transaction.Commit();

                    Debug.WriteLine($"[DB] 카테고리 삭제 완료: CategoryId={categoryId}, 하위 카테고리 재할당={updatedChildren}개");
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    Debug.WriteLine($"[DB ERROR] 카테고리 삭제 실패 (롤백됨): {ex.Message}");
                    throw;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] DeleteCategory 실패: {ex.Message}");
                throw;
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
                using var conn = new SQLiteConnection(GetConnectionString());
                conn.Open();

                var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM noteContent WHERE textId = @textId";
                cmd.Parameters.AddWithValue("@textId", textId);

                int rowsAffected = cmd.ExecuteNonQuery();
                Debug.WriteLine($"[DB] 라인 삭제 완료: TextId: {textId}, 영향받은 행: {rowsAffected}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] DeleteLine 실패: {ex.Message}");
                throw;
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
                        updateCmd.CommandText = "UPDATE category SET parentCategoryId = @parentId WHERE categoryId = @categoryId";
                        updateCmd.Parameters.AddWithValue("@parentId", (object)newParentId ?? DBNull.Value);
                        updateCmd.Parameters.AddWithValue("@categoryId", category.CategoryId);

                        updateCmd.ExecuteNonQuery();

                        Debug.WriteLine($"[DB] 카테고리 부모 관계 업데이트: CategoryId={category.CategoryId}, 새 부모={newParentId}");
                    }

                    Debug.WriteLine($"[DB] 계층 구조 업데이트 완료: {categories.Count}개 카테고리 처리됨");
                }
                finally
                {
                    if (shouldDispose)
                    {
                        conn?.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] UpdateSubsequentCategoryHierarchy 실패: {ex.Message}");
                throw;
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
                Debug.WriteLine($"[SAVE] 라인 저장 시작 - SubjectId: {line.SubjectId}, Content: '{line.Content?.Substring(0, Math.Min(20, line.Content?.Length ?? 0))}', CategoryId: {line.CategoryId}, IsHeading: {line.IsHeadingLine}");

                if (IsCategoryHeading(line.Content))
                {
                    // 제목인 경우 (기존 로직 유지)
                    if (line.IsHeadingLine && line.CategoryId > 0)
                    {
                        UpdateCategory(line.CategoryId, line.Content);
                        int newLevel = GetHeadingLevel(line.Content);
                        UpdateCategoryLevel(line.CategoryId, newLevel);
                        UpdateSubsequentCategoryHierarchy(line.SubjectId, line.DisplayOrder);
                        Debug.WriteLine($"[SAVE] 기존 제목 업데이트 완료. CategoryId: {line.CategoryId}");
                    }
                    else
                    {
                        int level = GetHeadingLevel(line.Content);
                        int? parentId = FindParentCategoryByLevel(line.SubjectId, level, line.DisplayOrder);
                        int newCategoryId = InsertCategory(line.Content, line.SubjectId, line.DisplayOrder, level, parentId);
                        line.CategoryId = newCategoryId;
                        line.IsHeadingLine = true;
                        UpdateSubsequentCategoryHierarchy(line.SubjectId, line.DisplayOrder);
                        Debug.WriteLine($"[SAVE] 새 제목 생성 완료. CategoryId: {newCategoryId}, SubjectId: {line.SubjectId}");
                    }
                }
                else
                {
                    // 일반 텍스트인 경우
                    if (line.CategoryId <= 0)
                    {
                        line.CategoryId = FindNearestPreviousCategory(line.SubjectId, line.DisplayOrder);

                        // ✅ 중요: CategoryId가 -1이면 (카테고리가 없으면) 저장하지 않음
                        if (line.CategoryId <= 0)
                        {
                            Debug.WriteLine($"[SAVE] 카테고리 없음 - 텍스트 저장 스킵. SubjectId: {line.SubjectId}, Content: '{line.Content}'");
                            Debug.WriteLine($"[SAVE] 힌트: 먼저 제목(# 텍스트)을 입력하여 카테고리를 생성하세요.");
                            return;
                        }

                        Debug.WriteLine($"[SAVE] 이전 카테고리 찾음: CategoryId={line.CategoryId}");
                    }

                    if (line.TextId <= 0)
                    {
                        int newTextId = InsertNewLine(line.Content, line.SubjectId, line.CategoryId, line.DisplayOrder, line.ContentType, line.ImageUrl);
                        line.TextId = newTextId;
                        Debug.WriteLine($"[SAVE] 새 텍스트 생성 완료. TextId: {newTextId}, SubjectId: {line.SubjectId}, CategoryId: {line.CategoryId}");
                    }
                    else
                    {
                        UpdateLine(line);
                        Debug.WriteLine($"[SAVE] 기존 텍스트 업데이트 완료. TextId: {line.TextId}, SubjectId: {line.SubjectId}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SAVE ERROR] SaveOrUpdateLine 실패: {ex.Message}");
                Debug.WriteLine($"[SAVE ERROR] Line - SubjectId: {line.SubjectId}, Content: '{line.Content}', CategoryId: {line.CategoryId}");
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

            try
            {
                Debug.WriteLine($"[DB] LoadNotesBySubjectByDisplayOrder 시작: SubjectId={subjectId}");

                using var conn = new SQLiteConnection(GetConnectionString());
                conn.Open();

                // ✅ 1단계: 실제 데이터가 있는지 개별적으로 확인
                Debug.WriteLine($"[DB] 개별 테이블 확인 시작...");

                // category 테이블 확인
                using var categoryCmd = conn.CreateCommand();
                categoryCmd.CommandText = "SELECT COUNT(*) FROM category WHERE subjectId = @subjectId";
                categoryCmd.Parameters.AddWithValue("@subjectId", subjectId);
                var categoryCount = Convert.ToInt32(categoryCmd.ExecuteScalar());
                Debug.WriteLine($"[DB] category 테이블: {categoryCount}개 레코드 (subjectId={subjectId})");

                // noteContent 테이블 확인
                using var noteCmd = conn.CreateCommand();
                noteCmd.CommandText = "SELECT COUNT(*) FROM noteContent WHERE subjectId = @subjectId";
                noteCmd.Parameters.AddWithValue("@subjectId", subjectId);
                var noteCount = Convert.ToInt32(noteCmd.ExecuteScalar());
                Debug.WriteLine($"[DB] noteContent 테이블: {noteCount}개 레코드 (subjectId={subjectId})");

                // ✅ 2단계: 실제 UNION 쿼리 실행
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
                    var element = (
                        reader["type"].ToString(),
                        Convert.ToInt32(reader["id"]),
                        reader["content"].ToString(),
                        Convert.ToInt32(reader["displayOrder"]),
                        Convert.ToInt32(reader["level"]),
                        reader["parentCategoryId"] == DBNull.Value ? null : Convert.ToInt32(reader["parentCategoryId"]),
                        reader["contentType"]?.ToString(),
                        reader["imageUrl"]?.ToString()
                    );

                    allElements.Add(element);
                    Debug.WriteLine($"[LOAD] 요소: {element.Item1} - ID:{element.Item2} - Content:'{element.Item3.Substring(0, Math.Min(20, element.Item3.Length))}' - DisplayOrder:{element.Item4}");
                }
                reader.Close();

                Debug.WriteLine($"[DB] UNION 쿼리 결과: 총 {allElements.Count}개 요소");

                // ✅ 3단계: 데이터가 없으면 더 자세한 조사
                if (allElements.Count == 0)
                {
                    Debug.WriteLine($"[DB] 데이터 없음 - 추가 조사 시작");

                    // 전체 category 테이블 확인
                    using var allCatCmd = conn.CreateCommand();
                    allCatCmd.CommandText = "SELECT categoryId, subjectId, title FROM category LIMIT 5";
                    using var allCatReader = allCatCmd.ExecuteReader();
                    Debug.WriteLine($"[DB] 전체 category 테이블 샘플:");
                    while (allCatReader.Read())
                    {
                        Debug.WriteLine($"  CategoryId: {allCatReader["categoryId"]}, SubjectId: {allCatReader["subjectId"]}, Title: '{allCatReader["title"]}'");
                    }
                    allCatReader.Close();

                    // 전체 noteContent 테이블 확인
                    using var allNoteCmd = conn.CreateCommand();
                    allNoteCmd.CommandText = "SELECT textId, subjectId, content FROM noteContent LIMIT 5";
                    using var allNoteReader = allNoteCmd.ExecuteReader();
                    Debug.WriteLine($"[DB] 전체 noteContent 테이블 샘플:");
                    while (allNoteReader.Read())
                    {
                        Debug.WriteLine($"  TextId: {allNoteReader["textId"]}, SubjectId: {allNoteReader["subjectId"]}, Content: '{allNoteReader["content"]}'");
                    }
                    allNoteReader.Close();

                    return allCategories; // 빈 리스트 반환
                }

                // 4단계: 계층 구조 구성 (기존 로직 유지하되 로그 강화)
                NoteCategory currentCategory = null;
                var categoryStack = new Stack<NoteCategory>();
                var categoryMap = new Dictionary<int, NoteCategory>();

                foreach (var element in allElements)
                {
                    if (element.Type == "category")
                    {
                        var category = new NoteCategory
                        {
                            CategoryId = element.Id,
                            Title = element.Content,
                            DisplayOrder = element.DisplayOrder,
                            Level = element.Level,
                            ParentCategoryId = (int)element.ParentId,
                            Lines = new List<NoteLine>(),
                            SubCategories = new List<NoteCategory>()
                        };

                        categoryMap[element.Id] = category;
                        allCategories.Add(category); // 일단 모든 카테고리를 루트에 추가 (나중에 계층 구조 정리)
                        currentCategory = category;

                        Debug.WriteLine($"[LOAD] 카테고리 생성: '{category.Title}' (ID: {category.CategoryId}, Level: {category.Level})");
                    }
                    else if (element.Type == "text")
                    {
                        var line = new NoteLine
                        {
                            Index = element.Id,
                            Content = element.Content,
                            ContentType = element.ContentType ?? "text",
                            ImageUrl = element.ImageUrl,
                            DisplayOrder = element.DisplayOrder
                        };

                        // ParentId를 사용해서 해당 카테고리 찾기
                        if (element.ParentId.HasValue && categoryMap.ContainsKey(element.ParentId.Value))
                        {
                            var targetCategory = categoryMap[element.ParentId.Value];
                            targetCategory.Lines.Add(line);
                            Debug.WriteLine($"[LOAD] 텍스트 추가: '{line.Content.Substring(0, Math.Min(30, line.Content.Length))}' → 카테고리 '{targetCategory.Title}'");
                        }
                        else
                        {
                            Debug.WriteLine($"[LOAD ERROR] 텍스트 '{line.Content}'의 카테고리 ID {element.ParentId}를 찾을 수 없음");
                        }
                    }
                }

                Debug.WriteLine($"[DB] LoadNotesBySubjectByDisplayOrder 완료. 카테고리 수: {allCategories.Count}");

                // 각 카테고리의 라인 수 출력
                foreach (var cat in allCategories)
                {
                    Debug.WriteLine($"[DB] 카테고리 '{cat.Title}': {cat.Lines.Count}개 라인");
                }

                return allCategories;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] LoadNotesBySubjectByDisplayOrder 실패: {ex.Message}");
                Debug.WriteLine($"[DB ERROR] StackTrace: {ex.StackTrace}");
                return allCategories;
            }
        }

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
            try
            {
                if (fromCategoryId == toCategoryId) return;

                using var conn = new SQLiteConnection(GetConnectionString());
                conn.Open();
                using var transaction = conn.BeginTransaction();

                try
                {
                    // noteContent 테이블의 텍스트들 재할당
                    var updateTextsCmd = conn.CreateCommand();
                    updateTextsCmd.Transaction = transaction;
                    updateTextsCmd.CommandText = @"
                UPDATE noteContent 
                SET categoryId = @toCategoryId 
                WHERE categoryId = @fromCategoryId";

                    updateTextsCmd.Parameters.AddWithValue("@toCategoryId", toCategoryId);
                    updateTextsCmd.Parameters.AddWithValue("@fromCategoryId", fromCategoryId);

                    int affectedRows = updateTextsCmd.ExecuteNonQuery();

                    transaction.Commit();

                    Debug.WriteLine($"[DB] 텍스트 재할당 완료: {fromCategoryId} → {toCategoryId}, 영향받은 행: {affectedRows}");
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    Debug.WriteLine($"[DB ERROR] 텍스트 재할당 실패 (롤백됨): {ex.Message}");
                    throw;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] ReassignTextsToCategory 실패: {ex.Message}");
                throw;
            }
        }

        #endregion
    }
}