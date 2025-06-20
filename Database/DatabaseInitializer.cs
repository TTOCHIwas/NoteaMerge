﻿using System;
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
        -- ===== 통합된 Subject 테이블 =====
        CREATE TABLE IF NOT EXISTS Subject (
            subjectId INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL UNIQUE,
            TotalStudyTimeSeconds INTEGER NOT NULL DEFAULT 0,
            createdDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            lastModifiedDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
        );

        -- ===== 필기 시스템 테이블들 =====
        CREATE TABLE IF NOT EXISTS time (
            timeId INTEGER PRIMARY KEY AUTOINCREMENT,
            createdDate DATETIME NOT NULL,
            lastModifiedDate DATETIME NOT NULL
        );

        -- ===== 통합된 category 테이블 (필기 + 학습시간 추적) =====
        CREATE TABLE IF NOT EXISTS category (
            categoryId INTEGER PRIMARY KEY AUTOINCREMENT,
            displayOrder INTEGER DEFAULT 0,
            title VARCHAR NOT NULL,
            subjectId INTEGER NOT NULL,
            timeId INTEGER NOT NULL,
            level INTEGER DEFAULT 1,
            parentCategoryId INTEGER DEFAULT NULL,
            TotalStudyTimeSeconds INTEGER NOT NULL DEFAULT 0,  -- ✅ 추가: 학습시간 추적
            FOREIGN KEY (subjectId) REFERENCES Subject(subjectId),
            FOREIGN KEY (timeId) REFERENCES time(timeId)
        );

        CREATE TABLE IF NOT EXISTS noteContent (
            textId INTEGER PRIMARY KEY AUTOINCREMENT,
            content TEXT NOT NULL,
            categoryId INTEGER NOT NULL,
            subjectId INTEGER NOT NULL,
            displayOrder INTEGER DEFAULT 0,
            timeId INTEGER NOT NULL,
            level INTEGER DEFAULT 1,
            imageUrl VARCHAR DEFAULT NULL,
            contentType VARCHAR DEFAULT 'text',
            FOREIGN KEY (categoryId) REFERENCES category(categoryId),
            FOREIGN KEY (subjectId) REFERENCES Subject(subjectId),
            FOREIGN KEY (timeId) REFERENCES time(timeId)
        );

        -- ===== 기본 테이블들 =====
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

        -- ===== TopicItem 테이블 (categoryId 참조로 변경) =====
        CREATE TABLE IF NOT EXISTS TopicItem (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            categoryId INTEGER NOT NULL,                    -- ✅ 변경: TopicGroupId → categoryId
            Content TEXT NOT NULL,
            CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (categoryId) REFERENCES category(categoryId) ON DELETE CASCADE
        );

        -- ===== 학습 시간 추적 테이블들 =====
        CREATE TABLE IF NOT EXISTS StudySession (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            StartTime TEXT NOT NULL,
            EndTime TEXT NOT NULL,
            DurationSeconds INTEGER NOT NULL,
            Date TEXT NOT NULL,
            SubjectName TEXT DEFAULT NULL,
            CategoryId INTEGER DEFAULT NULL,
            SessionType TEXT DEFAULT 'general',
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

        CREATE TABLE IF NOT EXISTS monthlyEvent (
            planId INTEGER PRIMARY KEY AUTOINCREMENT,
            title VARCHAR NOT NULL,
            description VARCHAR NULL,
            isDday BOOLEAN NOT NULL,
            startDate DATETIME NOT NULL,
            endDate DATETIME NOT NULL,
            color VARCHAR NULL
        );

        CREATE TABLE IF NOT EXISTS monthlyComment (
            commentId INTEGER PRIMARY KEY AUTOINCREMENT,
            monthDate DATETIME NOT NULL,
            comment VARCHAR NULL,
            UNIQUE(monthDate)
        );

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

        -- 인덱스 생성
        CREATE INDEX IF NOT EXISTS idx_studysession_date ON StudySession(Date);
        CREATE INDEX IF NOT EXISTS idx_studysession_subject ON StudySession(SubjectName);
        CREATE INDEX IF NOT EXISTS idx_studysession_category ON StudySession(CategoryId);
        CREATE INDEX IF NOT EXISTS idx_categorystudytime_date ON CategoryStudyTime(Date);
        CREATE INDEX IF NOT EXISTS idx_categorystudytime_category ON CategoryStudyTime(CategoryId);
        CREATE INDEX IF NOT EXISTS idx_monthlyevent_dday ON monthlyEvent(isDday, startDate);
        CREATE INDEX IF NOT EXISTS idx_subjectfocus_active ON SubjectFocusSession(IsActive);
        CREATE INDEX IF NOT EXISTS idx_subjectfocus_date ON SubjectFocusSession(Date);
        CREATE INDEX IF NOT EXISTS idx_category_subject ON category(subjectId);
        CREATE INDEX IF NOT EXISTS idx_notecontent_category ON noteContent(categoryId);
        CREATE INDEX IF NOT EXISTS idx_notecontent_subject ON noteContent(subjectId);
        CREATE INDEX IF NOT EXISTS idx_topicitem_category ON TopicItem(categoryId);
    ";

            cmd.ExecuteNonQuery();
            Debug.WriteLine("[DB] 모든 테이블 생성 완료 - TopicGroup이 category로 통합됨");

            // ✅ 테이블 통합 및 마이그레이션
            MigrateTopicGroupToCategory(connection);
        }

        private static void MigrateTopicGroupToCategory(SQLiteConnection connection)
        {
            try
            {
                using var cmd = connection.CreateCommand();

                // 1. category 테이블에 TotalStudyTimeSeconds 컬럼 추가 (없는 경우)
                cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('category') WHERE name='TotalStudyTimeSeconds'";
                var hasColumn = Convert.ToInt32(cmd.ExecuteScalar()) > 0;

                if (!hasColumn)
                {
                    cmd.CommandText = "ALTER TABLE category ADD COLUMN TotalStudyTimeSeconds INTEGER DEFAULT 0";
                    cmd.ExecuteNonQuery();
                    Debug.WriteLine("[DB] category.TotalStudyTimeSeconds 컬럼 추가됨");
                }

                // 2. TopicGroup 테이블 존재 확인
                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='TopicGroup'";
                var topicGroupExists = Convert.ToInt32(cmd.ExecuteScalar()) > 0;

                if (topicGroupExists)
                {
                    // 3. TopicGroup 데이터를 category로 마이그레이션 (데이터가 있다면)
                    cmd.CommandText = "SELECT COUNT(*) FROM TopicGroup";
                    var topicGroupCount = Convert.ToInt32(cmd.ExecuteScalar());

                    if (topicGroupCount > 0)
                    {
                        cmd.CommandText = @"
                    INSERT INTO category (title, subjectId, timeId, TotalStudyTimeSeconds, displayOrder)
                    SELECT Name, SubjectId, 1, TotalStudyTimeSeconds, Id
                    FROM TopicGroup";
                        var migratedRows = cmd.ExecuteNonQuery();
                        Debug.WriteLine($"[DB] TopicGroup → category 마이그레이션: {migratedRows}개 행");

                        // 4. TopicItem의 TopicGroupId를 categoryId로 업데이트
                        cmd.CommandText = @"
                    UPDATE TopicItem 
                    SET TopicGroupId = (
                        SELECT c.categoryId 
                        FROM category c, TopicGroup tg 
                        WHERE c.title = tg.Name AND tg.Id = TopicItem.TopicGroupId
                    )
                    WHERE EXISTS (
                        SELECT 1 FROM TopicGroup tg WHERE tg.Id = TopicItem.TopicGroupId
                    )";
                        cmd.ExecuteNonQuery();
                    }

                    // 5. TopicGroup 테이블 삭제
                    cmd.CommandText = "DROP TABLE TopicGroup";
                    cmd.ExecuteNonQuery();
                    Debug.WriteLine("[DB] TopicGroup 테이블 삭제 완료");
                }

                // 6. TopicItem 테이블 구조 변경 (컬럼명 변경)
                cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS TopicItem_new (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                categoryId INTEGER NOT NULL,
                Content TEXT NOT NULL,
                CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (categoryId) REFERENCES category(categoryId) ON DELETE CASCADE
            )";
                cmd.ExecuteNonQuery();

                // 기존 TopicItem 데이터가 있다면 복사
                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='TopicItem'";
                var topicItemExists = Convert.ToInt32(cmd.ExecuteScalar()) > 0;

                if (topicItemExists)
                {
                    // 기존 TopicItem 테이블의 컬럼 구조 확인
                    cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('TopicItem') WHERE name='TopicGroupId'";
                    var hasTopicGroupId = Convert.ToInt32(cmd.ExecuteScalar()) > 0;

                    if (hasTopicGroupId)
                    {
                        // TopicGroupId가 있는 경우만 마이그레이션 실행
                        cmd.CommandText = @"
            INSERT INTO TopicItem_new (Id, categoryId, Content, CreatedAt)
            SELECT Id, TopicGroupId, Content, CreatedAt FROM TopicItem";
                        cmd.ExecuteNonQuery();
                    }
                    else
                    {
                        // 이미 categoryId를 사용하는 경우 그대로 복사
                        cmd.CommandText = @"
            INSERT INTO TopicItem_new (Id, categoryId, Content, CreatedAt)
            SELECT Id, categoryId, Content, CreatedAt FROM TopicItem";
                        cmd.ExecuteNonQuery();
                    }
                }

                Debug.WriteLine("[DB] TopicGroup → category 통합 완료");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB WARNING] TopicGroup 통합 실패: {ex.Message}");
                // 마이그레이션 실패해도 진행 (새 설치의 경우)
            }
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
            UpdateCategoryStudyTimeSchema(connection); 
            Debug.WriteLine("[DB] 모든 스키마 업데이트 완료");
        }

        private static void UpdateCategoryStudyTimeSchema(SQLiteConnection connection)
        {
            try
            {
                using var cmd = connection.CreateCommand();

                // TotalStudyTimeSeconds 컬럼 확인 및 추가
                cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('category') WHERE name='TotalStudyTimeSeconds'";
                var studyTimeResult = cmd.ExecuteScalar();

                if (Convert.ToInt32(studyTimeResult) == 0)
                {
                    cmd.CommandText = "ALTER TABLE category ADD COLUMN TotalStudyTimeSeconds INTEGER DEFAULT 0";
                    cmd.ExecuteNonQuery();
                    Debug.WriteLine("[DB] category.TotalStudyTimeSeconds 컬럼 추가됨");
                }

                Debug.WriteLine("[DB] category 학습시간 스키마 업데이트 완료");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] category 학습시간 스키마 업데이트 실패: {ex.Message}");
            }
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