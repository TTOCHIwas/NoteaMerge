﻿using Notea.Database;
using System;
using System.IO;
using System.Windows;

namespace Notea;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>

    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                DatabaseInitializer.InitializeDatabase();
                EnsureDefaultCategoriesForAllSubjects();
                CreateImageFolder();

                System.Diagnostics.Debug.WriteLine("[APP] 애플리케이션 초기화 완료");

#if DEBUG
                // 디버그 모드에서 데이터베이스 연결 테스트
                if (DatabaseInitializer.TestConnection())
                {
                    System.Diagnostics.Debug.WriteLine("[APP] 데이터베이스 연결 테스트 성공");
                }
#endif
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[APP ERROR] 초기화 실패: {ex.Message}");
                MessageBox.Show($"애플리케이션 초기화에 실패했습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

    private void EnsureDefaultCategoriesForAllSubjects()
    {
        try
        {
            var dbHelper = Notea.Modules.Common.Helpers.DatabaseHelper.Instance;
            var subjects = dbHelper.LoadSubjectsWithGroups();

            System.Diagnostics.Debug.WriteLine($"[APP] {subjects.Count}개 과목에 대해 기본 카테고리 확인 완료");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[APP ERROR] 기본 카테고리 확인 실패: {ex.Message}");
        }
    }

    private void CreateImageFolder()
        {
            try
            {
                string imageFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "images");
                if (!Directory.Exists(imageFolder))
                {
                    Directory.CreateDirectory(imageFolder);
                    System.Diagnostics.Debug.WriteLine($"[APP] 이미지 폴더 생성: {imageFolder}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[APP ERROR] 이미지 폴더 생성 실패: {ex.Message}");
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                // 프로그램 종료 시 모든 변경사항 저장
                if (MainWindow != null)
                {
                    var notePageView = FindVisualChild<Notea.Modules.Subject.Views.NotePageView>(MainWindow);
                    if (notePageView?.DataContext is Notea.Modules.Subject.ViewModels.NotePageViewModel vm)
                    {
                        vm.SaveChanges();
                        System.Diagnostics.Debug.WriteLine("[APP] 필기 내용 저장 완료");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[APP ERROR] 종료 시 저장 실패: {ex.Message}");
            }

            base.OnExit(e);
        }

        private T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;

            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T target)
                    return target;

                var result = FindVisualChild<T>(child);
                if (result != null)
                    return result;
            }
            return null;
        }
    }
