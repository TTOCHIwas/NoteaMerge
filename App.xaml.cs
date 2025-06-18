using Notea.Database;
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
                // ✅ 통합된 데이터베이스 초기화만 호출 (중복 제거)
                DatabaseInitializer.InitializeDatabase();

                // 🚨 제거: EnsureRuntimeSchemaComplete() 호출 삭제
                // EnsureRuntimeSchemaComplete(); // 이 줄 완전 삭제

                // ✅ 기본 카테고리 확인 (필기 시스템용)
                Notea.Modules.Subject.Models.NoteRepository.EnsureDefaultCategory(1);

                // ✅ 이미지 저장 폴더 생성
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
