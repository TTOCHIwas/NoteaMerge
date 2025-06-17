using System;
using System.Data;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
namespace Notea.Helpers
{
    public static class DatabaseHelper
    {
        // 절대 경로 사용하여 DB 위치 고정
        private static readonly string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "notea.db");
        private static readonly string connectionString;

        static DatabaseHelper()
        {
            // data 폴더가 없으면 생성
            var dataDir = Path.GetDirectoryName(dbPath);
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }

            // 연결 문자열에 추가 옵션 설정
            connectionString = $"Data Source={dbPath};Journal Mode=WAL;Busy Timeout=5000;";

            // DB 파일이 없으면 생성
            if (!File.Exists(dbPath))
            {
                InitializeDatabase();
            }
        }

        // 데이터베이스 초기화
        private static void InitializeDatabase()
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = @"
                    -- 외래 키 활성화
                    PRAGMA foreign_keys = ON;

                    CREATE TABLE IF NOT EXISTS category
                    (
                        categoryId INTEGER PRIMARY KEY AUTOINCREMENT,
                        di

layOrder INTEGER DEFAULT 0,
                        title      VARCHAR NOT NULL,
                        subJectId  INTEGER NOT NULL,
                        timeId     INTEGER NOT NULL,
                        level      INTEGER DEFAULT 1,
                        parentCategoryId INTEGER DEFAULT NULL,
                        FOREIGN KEY (subJectId) REFERENCES subject (subJectId),
                        FOREIGN KEY (timeId) REFERENCES time (timeId)
                    );

                    CREATE TABLE IF NOT EXISTS memo
                    (
                        noteId  INTEGER PRIMARY KEY AUTOINCREMENT,
                        content text    NULL    
                    );

                    CREATE TABLE IF NOT EXISTS monthlyEvent
                    (
                        planId      INTEGER PRIMARY KEY AUTOINCREMENT,
                        title       VARCHAR  NOT NULL,
                        description VARCHAR  NULL    ,
                        isDday      BOOLEAN  NOT NULL,
                        startDate   DATETIME NOT NULL,
                        endDate     DATETIME NOT NULL,
                        color       VARCHAR  NULL    
                    );

                    CREATE TABLE IF NOT EXISTS noteContent
                    (
                        textId     INTEGER PRIMARY KEY AUTOINCREMENT,
                        diNotealayOrder INTEGER DEFAULT 0,
                        content    VARCHAR NULL    ,
                        categoryId INTEGER NOT NULL,
                        subJectId  INTEGER NOT NULL,
                        FOREIGN KEY (categoryId) REFERENCES category (categoryId),
                        FOREIGN KEY (subJectId) REFERENCES subject (subJectId)
                    );

                    ";

                command.ExecuteNonQuery();

                Console.WriteLine($"데이터베이스 초기화 완료: {dbPath}");
            }
        }

        // 연결 테스트용 메서드
        public static bool TestConnection()
        {
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    Console.WriteLine($"DB 연결 성공: {dbPath}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DB 연결 실패: {ex.Message}");
                Console.WriteLine($"시도한 경로: {dbPath}");
                return false;
            }
        }

        public static DataTable ExecuteSelect(string query)
        {
            var dt = new DataTable();

            try
            {
                using (var connection = new SQLiteConnection(connectionString))
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

                Console.WriteLine($"SELECT 쿼리 실행 성공. 반환된 행: {dt.Rows.Count}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SELECT 쿼리 실행 실패: {ex.Message}");
                Console.WriteLine($"쿼리: {query}");
            }

            return dt;
        }

        // INSERT, UPDATE, DELETE 쿼리 실행
        public static int ExecuteNonQuery(string query)
        {
            int result = 0;

            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    using (var command = new SQLiteCommand(query, connection))
                    {
                        result = command.ExecuteNonQuery();
                    }
                }

                Console.WriteLine($"쿼리 실행 성공. 영향받은 행: {result}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"쿼리 실행 실패: {ex.Message}");
                Console.WriteLine($"쿼리: {query}");
            }

            return result;
        }

        public static void UpdateSchemaForHeadingLevel()
        {
            try
            {
                // category 테이블에 level 컬럼 추가 (없으면)
                string checkLevelColumn = @"
            SELECT COUNT(*) as count 
            FROM pragma_table_info('category') 
            WHERE name='level'";

                var result = ExecuteSelect(checkLevelColumn);
                if (result.Rows.Count > 0 && Convert.ToInt32(result.Rows[0]["count"]) == 0)
                {
                    string addLevelColumn = @"
                ALTER TABLE category ADD COLUMN level INTEGER DEFAULT 1";
                    ExecuteNonQuery(addLevelColumn);
                    Debug.WriteLine("[DB] category.level 컬럼 추가됨");
                }

                // parentCategoryId 컬럼 추가 (계층 구조를 위해)
                string checkParentColumn = @"
            SELECT COUNT(*) as count 
            FROM pragma_table_info('category') 
            WHERE name='parentCategoryId'";

                result = ExecuteSelect(checkParentColumn);
                if (result.Rows.Count > 0 && Convert.ToInt32(result.Rows[0]["count"]) == 0)
                {
                    string addParentColumn = @"
                ALTER TABLE category ADD COLUMN parentCategoryId INTEGER DEFAULT NULL";
                    ExecuteNonQuery(addParentColumn);
                    Debug.WriteLine("[DB] category.parentCategoryId 컬럼 추가됨");
                }

                Debug.WriteLine("[DB] 헤딩 레벨 스키마 업데이트 완료");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] 헤딩 레벨 스키마 업데이트 실패: {ex.Message}");
            }
        }

        public static void CheckTableStructure()
        {
            try
            {
                string query = @"
            SELECT sql FROM sqlite_master 
            WHERE type='table' AND name IN ('category', 'noteContent', 'Subject');";

                var result = ExecuteSelect(query);
                foreach (DataRow row in result.Rows)
                {
                    Debug.WriteLine($"[DB SCHEMA] {row["sql"]}");
                }

                // noteContent 테이블의 데이터 확인
                query = "SELECT COUNT(*) as count FROM noteContent";
                result = ExecuteSelect(query);
                Debug.WriteLine($"[DB] noteContent 테이블의 행 수: {result.Rows[0]["count"]}");

                // category 테이블의 데이터 확인
                query = "SELECT * FROM category";
                result = ExecuteSelect(query);
                Debug.WriteLine($"[DB] category 테이블 내용:");
                foreach (DataRow row in result.Rows)
                {
                    Debug.WriteLine($"  CategoryId: {row["categoryId"]}, Title: {row["title"]}, SubjectId: {row["subJectId"]}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] 테이블 구조 확인 실패: {ex.Message}");
            }
        }

        public static void DebugPrintAllData(int subjectId)
        {
            try
            {
                Debug.WriteLine("=== 데이터베이스 전체 내용 ===");

                // 카테고리 출력
                string categoryQuery = $@"
            SELECT categoryId, title, diNotealayOrder, level, parentCategoryId
            FROM category 
            WHERE subJectId = {subjectId}
            ORDER BY diNotealayOrder";

                var categoryResult = ExecuteSelect(categoryQuery);
                Debug.WriteLine($"[카테고리] 총 {categoryResult.Rows.Count}개");
                foreach (DataRow row in categoryResult.Rows)
                {
                    Debug.WriteLine($"  ID: {row["categoryId"]}, " +
                                  $"Title: '{row["title"]}', " +
                                  $"Order: {row["diNotealayOrder"]}, " +
                                  $"Level: {row["level"]}, " +
                                  $"ParentId: {row["parentCategoryId"]}");
                }

                // 텍스트 내용 출력
                string textQuery = $@"
            SELECT textId, content, categoryId, diNotealayOrder
            FROM noteContent 
            WHERE subJectId = {subjectId}
            ORDER BY diNotealayOrder";

                var textResult = ExecuteSelect(textQuery);
                Debug.WriteLine($"\n[텍스트] 총 {textResult.Rows.Count}개");
                foreach (DataRow row in textResult.Rows)
                {
                    Debug.WriteLine($"  ID: {row["textId"]}, " +
                                  $"CategoryId: {row["categoryId"]}, " +
                                  $"Order: {row["diNotealayOrder"]}, " +
                                  $"Content: '{row["content"]?.ToString().Substring(0, Math.Min(50, row["content"]?.ToString().Length ?? 0))}'...");
                }

                // 카테고리별 텍스트 개수
                string countQuery = $@"
            SELECT c.categoryId, c.title, COUNT(n.textId) as textCount
            FROM category c
            LEFT JOIN noteContent n ON c.categoryId = n.categoryId
            WHERE c.subJectId = {subjectId}
            GROUP BY c.categoryId, c.title
            ORDER BY c.diNotealayOrder";

                var countResult = ExecuteSelect(countQuery);
                Debug.WriteLine($"\n[카테고리별 텍스트 개수]");
                foreach (DataRow row in countResult.Rows)
                {
                    Debug.WriteLine($"  카테고리 '{row["title"]}' (ID: {row["categoryId"]}): {row["textCount"]}개");
                }

                Debug.WriteLine("========================");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] DebugPrintAllData: {ex.Message}");
            }
        }

        public static void UpdateSchemaForImageSupport()
        {
            try
            {
                // noteContent 테이블에 imageUrl 컬럼 추가
                string checkImageColumn = @"
            SELECT COUNT(*) as count 
            FROM pragma_table_info('noteContent') 
            WHERE name='imageUrl'";

                var result = ExecuteSelect(checkImageColumn);
                if (result.Rows.Count > 0 && Convert.ToInt32(result.Rows[0]["count"]) == 0)
                {
                    string addImageColumn = @"
                ALTER TABLE noteContent ADD COLUMN imageUrl VARCHAR DEFAULT NULL";
                    ExecuteNonQuery(addImageColumn);
                    Debug.WriteLine("[DB] noteContent.imageUrl 컬럼 추가됨");
                }

                // noteContent 테이블에 contentType 컬럼 추가 (text/image 구분)
                string checkTypeColumn = @"
            SELECT COUNT(*) as count 
            FROM pragma_table_info('noteContent') 
            WHERE name='contentType'";

                result = ExecuteSelect(checkTypeColumn);
                if (result.Rows.Count > 0 && Convert.ToInt32(result.Rows[0]["count"]) == 0)
                {
                    string addTypeColumn = @"
                ALTER TABLE noteContent ADD COLUMN contentType VARCHAR DEFAULT 'text'";
                    ExecuteNonQuery(addTypeColumn);
                    Debug.WriteLine("[DB] noteContent.contentType 컬럼 추가됨");
                }

                Debug.WriteLine("[DB] 이미지 지원 스키마 업데이트 완료");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] 이미지 지원 스키마 업데이트 실패: {ex.Message}");
            }
        }

        public static void UpdateSchemaForMonthlyComment()
        {
            try
            {
                // monthlyComment 테이블 생성
                string createTableQuery = @"
            CREATE TABLE IF NOT EXISTS monthlyComment
            (
                commentId INTEGER PRIMARY KEY AUTOINCREMENT,
                monthDate DATETIME NOT NULL,
                comment VARCHAR NULL,
                UNIQUE(monthDate)
            )";

                ExecuteNonQuery(createTableQuery);
                Debug.WriteLine("[DB] monthlyComment 테이블 생성/확인 완료");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] monthlyComment 스키마 업데이트 실패: {ex.Message}");
            }
        }

        public static void VerifyDatabaseIntegrity(int subjectId)
        {
            try
            {
                Debug.WriteLine("=== 데이터베이스 무결성 검증 ===");

                // 1. 고아 noteContent 찾기
                string orphanQuery = $@"
            SELECT n.textId, n.content, n.categoryId
            FROM noteContent n
            LEFT JOIN category c ON n.categoryId = c.categoryId
            WHERE n.subJectId = {subjectId} AND c.categoryId IS NULL";

                var orphanResult = ExecuteSelect(orphanQuery);
                if (orphanResult.Rows.Count > 0)
                {
                    Debug.WriteLine($"[DB ERROR] 고아 noteContent 발견: {orphanResult.Rows.Count}개");
                    foreach (DataRow row in orphanResult.Rows)
                    {
                        Debug.WriteLine($"  TextId: {row["textId"]}, CategoryId: {row["categoryId"]}");
                    }
                }

                // 2. DiNotealayOrder 중복 검사
                string duplicateQuery = $@"
            SELECT diNotealayOrder, COUNT(*) as cnt
            FROM (
                SELECT diNotealayOrder FROM category WHERE subJectId = {subjectId}
                UNION ALL
                SELECT diNotealayOrder FROM noteContent WHERE subJectId = {subjectId}
            )
            GROUP BY diNotealayOrder
            HAVING COUNT(*) > 1";

                var duplicateResult = ExecuteSelect(duplicateQuery);
                if (duplicateResult.Rows.Count > 0)
                {
                    Debug.WriteLine($"[DB ERROR] DiNotealayOrder 중복 발견:");
                    foreach (DataRow row in duplicateResult.Rows)
                    {
                        Debug.WriteLine($"  DiNotealayOrder: {row["diNotealayOrder"]}, Count: {row["cnt"]}");
                    }
                }

                // 3. 이미지 파일 검증
                string imageQuery = $@"
            SELECT textId, imageUrl
            FROM noteContent
            WHERE subJectId = {subjectId} AND contentType = 'image' AND imageUrl IS NOT NULL";

                var imageResult = ExecuteSelect(imageQuery);
                foreach (DataRow row in imageResult.Rows)
                {
                    string imageUrl = row["imageUrl"].ToString();
                    string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, imageUrl);
                    if (!File.Exists(fullPath))
                    {
                        Debug.WriteLine($"[DB ERROR] 이미지 파일 없음: TextId={row["textId"]}, Path={imageUrl}");
                    }
                }

                Debug.WriteLine("=== 검증 완료 ===");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] 무결성 검증 실패: {ex.Message}");
            }
        }

        // DB 경로 확인용
        public static string GetDatabasePath() => dbPath;
    }
}