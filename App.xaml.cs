using Notea.Database;
using System.Configuration;
using System.Data;
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

        // 데이터베이스 초기화
        DatabaseInitializer.InitializeDatabase();

        // 스키마 업데이트
        DatabaseInitializer.UpdateSchemaForDisplayOrder();
        Notea.Helpers.DatabaseHelper.UpdateSchemaForHeadingLevel();
        Notea.Helpers.DatabaseHelper.UpdateSchemaForImageSupport();
        Notea.Helpers.DatabaseHelper.UpdateSchemaForMonthlyComment();

        // 기본 카테고리 확인
        Notea.Modules.Subject.Models.NoteRepository.EnsureDefaultCategory(1);

        // 이미지 저장 폴더 생성
        CreateImageFolder();

        //#if DEBUG
        //// 디버그 모드에서만 실행
        //Notea.Helpers.DatabaseHelper.DebugPrintAllData(1);
        //Notea.Helpers.DatabaseHelper.VerifyDatabaseIntegrity(1);
        //#endif
    }

    private void CreateImageFolder()
    {
        string imageFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "images");
        if (!Directory.Exists(imageFolder))
        {
            Directory.CreateDirectory(imageFolder);
            System.Diagnostics.Debug.WriteLine($"[APP] 이미지 폴더 생성: {imageFolder}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 프로그램 종료 시 모든 변경사항 저장
        if (MainWindow != null)
        {
            var notePageView = FindVisualChild<Notea.Modules.Subject.Views.NotePageView>(MainWindow);
            if (notePageView?.DataContext is Notea.Modules.Subject.ViewModels.NotePageViewModel vm)
            {
                vm.SaveChanges();
            }
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

