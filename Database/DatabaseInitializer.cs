using Microsoft.Data.Sqlite;
using Notea.Helpers;
using System;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;

namespace Notea.Database
{
    public static class DatabaseInitializer
    {
        private const string DbFileName = "notea.db";

        public static void InitializeDatabase()
        {
            if (!File.Exists(DbFileName))
            {
                using var connection = new SqliteConnection($"Data Source={DbFileName}");
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = @"
                    PRAGMA foreign_keys = ON;

                    CREATE TABLE IF NOT EXISTS category
                    (
                        categoryId INTEGER PRIMARY KEY AUTOINCREMENT,
                        displayOrder INTEGER DEFAULT 0,
                        title      VARCHAR NOT NULL,
                        subJectId  INTEGER NOT NULL,
                        timeId     INTEGER NOT NULL,
                        level      INTEGER DEFAULT 1,
                        parentCategoryId INTEGER DEFAULT NULL,
                        FOREIGN KEY (subJectId) REFERENCES subject (subJectId),
                        FOREIGN KEY (timeId) REFERENCES time (timeId)
                    );

                    CREATE TABLE IF NOT EXISTS Note (
                        NoteId INTEGER PRIMARY KEY AUTOINCREMENT,
                        Content TEXT NOT NULL,
                        CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
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
                        displayOrder INTEGER DEFAULT 0,
                        content    VARCHAR NULL    ,
                        categoryId INTEGER NOT NULL,
                        subJectId  INTEGER NOT NULL,
                        FOREIGN KEY (categoryId) REFERENCES category (categoryId),
                        FOREIGN KEY (subJectId) REFERENCES subject (subJectId)
                    );

                    CREATE TABLE IF NOT EXISTS Subject (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL UNIQUE,
                        TotalStudyTimeSeconds INTEGER NOT NULL DEFAULT 0
                    );

                    CREATE TABLE IF NOT EXISTS time
                    (
                        timeId     INTEGER PRIMARY KEY AUTOINCREMENT,
                        createDate DATETIME NOT NULL,
                        record     INT      NOT NULL
                    );

                    CREATE TABLE IF NOT EXISTS Todo (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Date TEXT NOT NULL,
                        Title TEXT NOT NULL,
                        IsCompleted INTEGER NOT NULL DEFAULT 0
                    );
                    CREATE TABLE IF NOT EXISTS Comment (
                        Date TEXT PRIMARY KEY,
                        Text TEXT NOT NULL
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
                    CREATE TABLE IF NOT EXISTS StudySession (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        StartTime TEXT NOT NULL,
                        EndTime TEXT NOT NULL,
                        DurationSeconds INTEGER NOT NULL,
                        Date TEXT NOT NULL,
                        CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
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

                    ";

                command.ExecuteNonQuery();
            }
        }
        public static void UpdateSchemaForDisplayOrder()
        {
            try
            {
                // category 테이블에 level 컬럼 추가 (없으면)
                string checkCategoryColumn = @"
                    SELECT COUNT(*) as count 
                    FROM pragma_table_info('category') 
                    WHERE name='level'";

                var result = DatabaseHelper.ExecuteSelect(checkCategoryColumn);
                if (result.Rows.Count > 0 && Convert.ToInt32(result.Rows[0]["count"]) == 0)
                {
                    string addCategoryOrder = @"
                        ALTER TABLE category ADD COLUMN level INTEGER DEFAULT 0";
                    DatabaseHelper.ExecuteNonQuery(addCategoryOrder);
                    Debug.WriteLine("[DB] category.level 컬럼 추가됨");
                }

                // noteContent 테이블에 displayOrder 컬럼 추가 (없으면)
                string checkContentColumn = @"
                    SELECT COUNT(*) as count 
                    FROM pragma_table_info('noteContent') 
                    WHERE name='displayOrder'";

                result = DatabaseHelper.ExecuteSelect(checkContentColumn);
                if (result.Rows.Count > 0 && Convert.ToInt32(result.Rows[0]["count"]) == 0)
                {
                    string addContentOrder = @"
                        ALTER TABLE noteContent ADD COLUMN displayOrder INTEGER DEFAULT 0";
                    DatabaseHelper.ExecuteNonQuery(addContentOrder);
                    Debug.WriteLine("[DB] noteContent.displayOrder 컬럼 추가됨");
                }

                // 기존 데이터의 displayOrder 초기화 (0인 경우)
                string updateExistingOrders = @"
                    UPDATE category SET displayOrder = categoryId WHERE displayOrder = 0;
                    UPDATE noteContent SET displayOrder = TextId WHERE displayOrder = 0;";
                DatabaseHelper.ExecuteNonQuery(updateExistingOrders);

                Debug.WriteLine("[DB] displayOrder 스키마 업데이트 완료");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] displayOrder 스키마 업데이트 실패: {ex.Message}");
            }
        }

        
    }
}


