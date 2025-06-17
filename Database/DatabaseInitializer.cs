using System;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;

namespace Notea.Database
{
    /// <summary>
    /// 데이터베이스 스키마 관리 전담 클래스
    /// 모든 테이블 생성 및 스키마 업데이트를 여기서 처리
    /// </summary>
    public static class DatabaseInitializer
    {
        // ✅ 통일된 데이터베이스 경로: data/notea.db
        private static readonly string DbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "notea.db");
        private static readonly string ConnectionString = $"Data Source={DbPath};Version=3;Journal Mode=WAL;Busy Timeout=5000;Pooling=true;";

        /// <summary>
        /// 데이터베이스 초기화 - 모든 테이블 생성 및 스키마 업데이트
        /// </summary>
        public static void InitializeDatabase()
        {
            try
            {
                // data 폴더 생성
                var dataDir = Path.GetDirectoryName(DbPath);
                if (!Directory.Exists(dataDir))
                {
                    Directory.CreateDirectory(dataDir);
                    Debug.WriteLine($"[DB] 데이터 폴더 생성: {dataDir}");
                }

                using var connection = new SQLiteConnection(ConnectionString);
                connection.Open();

                // PRAGMA 설정
                using var pragmaCmd = connection.CreateCommand();
                pragmaCmd.CommandText = "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;";
                pragmaCmd.ExecuteNonQuery();

                // 모든 테이블 생성
                CreateAllTables(connection);

                // 기존 데이터베이스 호환성을 위한 스키마 업데이트
                UpdateAllSchemas(connection);


                Debug.WriteLine($"[DB] 데이터베이스 초기화 완료: {DbPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] 데이터베이스 초기화 실패: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 모든 테이블 생성
        /// </summary>
        private static void CreateAllTables(SQLiteConnection connection)
        {
            using var cmd = connection.CreateCommand();

            cmd.CommandText = @"
                -- ===== 필기 시스템 테이블들 (Helpers/DatabaseHelper.cs 용) =====
                CREATE TABLE IF NOT EXISTS time (
                    timeId INTEGER PRIMARY KEY AUTOINCREMENT,
                    createdDate DATETIME NOT NULL,
                    lastModifiedDate DATETIME NOT NULL
                );

                CREATE TABLE IF NOT EXISTS category (
                    categoryId INTEGER PRIMARY KEY AUTOINCREMENT,
                    displayOrder INTEGER DEFAULT 0,
                    title VARCHAR NOT NULL,
                    subJectId INTEGER NOT NULL,
                    timeId INTEGER NOT NULL,
                    level INTEGER DEFAULT 1,
                    parentCategoryId INTEGER DEFAULT NULL,
                    FOREIGN KEY (subJectId) REFERENCES subject(subJectId),
                    FOREIGN KEY (timeId) REFERENCES time(timeId)
                );

                CREATE TABLE IF NOT EXISTS noteContent (
                    textId INTEGER PRIMARY KEY AUTOINCREMENT,
                    content TEXT NOT NULL,
                    categoryId INTEGER NOT NULL,
                    subJectId INTEGER NOT NULL,
                    displayOrder INTEGER DEFAULT 0,
                    timeId INTEGER NOT NULL,
                    level INTEGER DEFAULT 1,
                    imageUrl VARCHAR DEFAULT NULL,
                    contentType VARCHAR DEFAULT 'text',
                    FOREIGN KEY (categoryId) REFERENCES category(categoryId),
                    FOREIGN KEY (subJectId) REFERENCES subject(subJectId),
                    FOREIGN KEY (timeId) REFERENCES time(timeId)
                );

                -- ===== 메인 기능 테이블들 (Modules/Common/Helpers/DatabaseHelper.cs 용) =====
                CREATE TABLE IF NOT EXISTS Note (
                    NoteId INTEGER PRIMARY KEY AUTOINCREMENT,
                    Content TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                );

                CREATE TABLE IF NOT EXISTS Comment (
                    Date TEXT PRIMARY KEY,
                    Text TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS Todo (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Date TEXT NOT NULL,
                    Title TEXT NOT NULL,
                    IsCompleted INTEGER NOT NULL DEFAULT 0
                );

                CREATE TABLE IF NOT EXISTS Subject (
                    subjectId INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL UNIQUE,
                    TotalStudyTimeSeconds INTEGER NOT NULL DEFAULT 0
                );

                CREATE TABLE IF NOT EXISTS TopicGroup (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    SubjectId INTEGER NOT NULL,
                    Name TEXT NOT NULL,
                    TotalStudyTimeSeconds INTEGER NOT NULL DEFAULT 0,
                    FOREIGN KEY (SubjectId) REFERENCES Subject(Id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS TopicItem (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    TopicGroupId INTEGER NOT NULL,
                    Content TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY (TopicGroupId) REFERENCES TopicGroup(Id) ON DELETE CASCADE
                );

                -- ✅ 개선된 StudySession 테이블 (과목별/카테고리별 시간 추적)
                CREATE TABLE IF NOT EXISTS StudySession (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    StartTime TEXT NOT NULL,
                    EndTime TEXT NOT NULL,
                    DurationSeconds INTEGER NOT NULL,
                    Date TEXT NOT NULL,
                    SubjectName TEXT DEFAULT NULL,
                    CategoryId INTEGER DEFAULT NULL,
                    SessionType TEXT DEFAULT 'general',  -- 'general', 'subject', 'category'
                    CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY (CategoryId) REFERENCES category(categoryId)
                );

                CREATE TABLE IF NOT EXISTS DailySubject (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Date TEXT NOT NULL,
                    SubjectName TEXT NOT NULL,
                    Progress REAL NOT NULL DEFAULT 0.0,
                    StudyTimeSeconds INTEGER NOT NULL DEFAULT 0,
                    DisplayOrder INTEGER NOT NULL DEFAULT 0
                );

                CREATE TABLE IF NOT EXISTS DailyTopicGroup (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Date TEXT NOT NULL,
                    SubjectName TEXT NOT NULL,
                    GroupTitle TEXT NOT NULL,
                    TotalStudyTimeSeconds INTEGER NOT NULL DEFAULT 0,
                    IsCompleted INTEGER NOT NULL DEFAULT 0
                );

                CREATE TABLE IF NOT EXISTS DailyTopicItem (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Date TEXT NOT NULL,
                    SubjectName TEXT NOT NULL,
                    GroupTitle TEXT NOT NULL,
                    TopicName TEXT NOT NULL,
                    Progress REAL NOT NULL DEFAULT 0.0,
                    StudyTimeSeconds INTEGER NOT NULL DEFAULT 0,
                    IsCompleted INTEGER NOT NULL DEFAULT 0
                );

                -- ✅ 월간 이벤트 테이블 (D-Day 계산용)
                CREATE TABLE IF NOT EXISTS monthlyEvent (
                    planId INTEGER PRIMARY KEY AUTOINCREMENT,
                    title VARCHAR NOT NULL,
                    description VARCHAR NULL,
                    isDday BOOLEAN NOT NULL,
                    startDate DATETIME NOT NULL,
                    endDate DATETIME NOT NULL,
                    color VARCHAR NULL
                );

                -- ✅ 월간 코멘트 테이블
                CREATE TABLE IF NOT EXISTS monthlyComment (
                    commentId INTEGER PRIMARY KEY AUTOINCREMENT,
                    monthDate DATETIME NOT NULL,
                    comment VARCHAR NULL,
                    UNIQUE(monthDate)
                );

                -- ✅ 새로운 테이블: 카테고리별 실시간 학습시간 추적
                CREATE TABLE IF NOT EXISTS CategoryStudyTime (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    CategoryId INTEGER NOT NULL,
                    SubjectName TEXT NOT NULL,
                    Date TEXT NOT NULL,
                    TotalSeconds INTEGER NOT NULL DEFAULT 0,
                    LastActiveTime TEXT DEFAULT NULL,
                    FOREIGN KEY (CategoryId) REFERENCES category(categoryId),
                    UNIQUE(CategoryId, Date)
                );

                -- ✅ 새로운 테이블: 과목별 포커스 세션 추적
                CREATE TABLE IF NOT EXISTS SubjectFocusSession (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    SubjectName TEXT NOT NULL,
                    CategoryId INTEGER DEFAULT NULL,
                    StartTime TEXT NOT NULL,
                    EndTime TEXT DEFAULT NULL,
                    DurationSeconds INTEGER DEFAULT 0,
                    Date TEXT NOT NULL,
                    IsActive INTEGER DEFAULT 1,
                    FOREIGN KEY (CategoryId) REFERENCES category(categoryId)
                );

                -- 인덱스 생성 (성능 최적화)
                CREATE INDEX IF NOT EXISTS idx_studysession_date ON StudySession(Date);
                CREATE INDEX IF NOT EXISTS idx_studysession_subject ON StudySession(SubjectName);
                CREATE INDEX IF NOT EXISTS idx_studysession_category ON StudySession(CategoryId);
                CREATE INDEX IF NOT EXISTS idx_categorystudytime_date ON CategoryStudyTime(Date);
                CREATE INDEX IF NOT EXISTS idx_categorystudytime_category ON CategoryStudyTime(CategoryId);
                CREATE INDEX IF NOT EXISTS idx_monthlyevent_dday ON monthlyEvent(isDday, startDate);
                CREATE INDEX IF NOT EXISTS idx_subjectfocus_active ON SubjectFocusSession(IsActive);
                CREATE INDEX IF NOT EXISTS idx_subjectfocus_date ON SubjectFocusSession(Date);
                CREATE INDEX IF NOT EXISTS idx_category_subject ON category(subJectId);
                CREATE INDEX IF NOT EXISTS idx_notecontent_category ON noteContent(categoryId);
                CREATE INDEX IF NOT EXISTS idx_notecontent_subject ON noteContent(subJectId);
            ";

            cmd.ExecuteNonQuery();
            Debug.WriteLine("[DB] 모든 테이블 생성 완료");
        }

        /// <summary>
        /// 기존 데이터베이스의 스키마 업데이트 (호환성 보장)
        /// 기존 각 Helper에 있던 UpdateSchema 메소드들을 여기로 통합
        /// </summary>
        private static void UpdateAllSchemas(SQLiteConnection connection)
        {
            UpdateDisplayOrderSchema(connection);
            UpdateHeadingLevelSchema(connection);
            UpdateImageSupportSchema(connection);
            Debug.WriteLine("[DB] 모든 스키마 업데이트 완료");
        }

        /// <summary>
        /// DisplayOrder 컬럼 추가 (기존 UpdateSchemaForDisplayOrder)
        /// </summary>
        private static void UpdateDisplayOrderSchema(SQLiteConnection connection)
        {
            try
            {
                using var cmd = connection.CreateCommand();

                // category 테이블에 displayOrder 컬럼이 있는지 확인
                cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('category') WHERE name='displayOrder'";
                var categoryResult = cmd.ExecuteScalar();

                if (Convert.ToInt32(categoryResult) == 0)
                {
                    cmd.CommandText = "ALTER TABLE category ADD COLUMN displayOrder INTEGER DEFAULT 0";
                    cmd.ExecuteNonQuery();
                    Debug.WriteLine("[DB] category.displayOrder 컬럼 추가됨");
                }

                // noteContent 테이블에 displayOrder 컬럼이 있는지 확인
                cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('noteContent') WHERE name='displayOrder'";
                var contentResult = cmd.ExecuteScalar();

                if (Convert.ToInt32(contentResult) == 0)
                {
                    cmd.CommandText = "ALTER TABLE noteContent ADD COLUMN displayOrder INTEGER DEFAULT 0";
                    cmd.ExecuteNonQuery();
                    Debug.WriteLine("[DB] noteContent.displayOrder 컬럼 추가됨");
                }

                // 기존 데이터의 displayOrder 초기화
                cmd.CommandText = @"
                    UPDATE category SET displayOrder = categoryId WHERE displayOrder = 0;
                    UPDATE noteContent SET displayOrder = textId WHERE displayOrder = 0;";
                cmd.ExecuteNonQuery();

                Debug.WriteLine("[DB] displayOrder 스키마 업데이트 완료");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] displayOrder 스키마 업데이트 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 헤딩 레벨 관련 컬럼 추가 (기존 UpdateSchemaForHeadingLevel)
        /// </summary>
        private static void UpdateHeadingLevelSchema(SQLiteConnection connection)
        {
            try
            {
                using var cmd = connection.CreateCommand();

                // level 컬럼 확인 및 추가
                cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('category') WHERE name='level'";
                var levelResult = cmd.ExecuteScalar();

                if (Convert.ToInt32(levelResult) == 0)
                {
                    cmd.CommandText = "ALTER TABLE category ADD COLUMN level INTEGER DEFAULT 1";
                    cmd.ExecuteNonQuery();
                    Debug.WriteLine("[DB] category.level 컬럼 추가됨");
                }

                // parentCategoryId 컬럼 확인 및 추가
                cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('category') WHERE name='parentCategoryId'";
                var parentResult = cmd.ExecuteScalar();

                if (Convert.ToInt32(parentResult) == 0)
                {
                    cmd.CommandText = "ALTER TABLE category ADD COLUMN parentCategoryId INTEGER DEFAULT NULL";
                    cmd.ExecuteNonQuery();
                    Debug.WriteLine("[DB] category.parentCategoryId 컬럼 추가됨");
                }

                Debug.WriteLine("[DB] 헤딩 레벨 스키마 업데이트 완료");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] 헤딩 레벨 스키마 업데이트 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 이미지 지원 컬럼 추가 (기존 UpdateSchemaForImageSupport)
        /// </summary>
        private static void UpdateImageSupportSchema(SQLiteConnection connection)
        {
            try
            {
                using var cmd = connection.CreateCommand();

                // imageUrl 컬럼 확인 및 추가
                cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('noteContent') WHERE name='imageUrl'";
                var imageResult = cmd.ExecuteScalar();

                if (Convert.ToInt32(imageResult) == 0)
                {
                    cmd.CommandText = "ALTER TABLE noteContent ADD COLUMN imageUrl VARCHAR DEFAULT NULL";
                    cmd.ExecuteNonQuery();
                    Debug.WriteLine("[DB] noteContent.imageUrl 컬럼 추가됨");
                }

                // contentType 컬럼 확인 및 추가
                cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('noteContent') WHERE name='contentType'";
                var typeResult = cmd.ExecuteScalar();

                if (Convert.ToInt32(typeResult) == 0)
                {
                    cmd.CommandText = "ALTER TABLE noteContent ADD COLUMN contentType VARCHAR DEFAULT 'text'";
                    cmd.ExecuteNonQuery();
                    Debug.WriteLine("[DB] noteContent.contentType 컬럼 추가됨");
                }

                Debug.WriteLine("[DB] 이미지 지원 스키마 업데이트 완료");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] 이미지 지원 스키마 업데이트 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 데이터베이스 연결 문자열 반환 (각 Helper들이 사용)
        /// </summary>
        public static string GetConnectionString()
        {
            return ConnectionString;
        }

        /// <summary>
        /// 데이터베이스 경로 반환
        /// </summary>
        public static string GetDatabasePath()
        {
            return DbPath;
        }

        /// <summary>
        /// 데이터베이스 연결 테스트
        /// </summary>
        public static bool TestConnection()
        {
            try
            {
                using var connection = new SQLiteConnection(ConnectionString);
                connection.Open();
                Debug.WriteLine($"[DB] 연결 테스트 성공: {DbPath}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB] 연결 테스트 실패: {ex.Message}");
                return false;
            }
        }
    }
}