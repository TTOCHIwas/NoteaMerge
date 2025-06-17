using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using System.Diagnostics;

namespace Notea.Converters
{
    public class ImagePathConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || string.IsNullOrEmpty(value.ToString()))
            {
                Debug.WriteLine("[IMAGE] ImagePathConverter - 경로가 null 또는 빈 문자열");
                return null;
            }

            string imagePath = value.ToString();
            Debug.WriteLine($"[IMAGE] ImagePathConverter - 변환 시도: {imagePath}");

            try
            {
                // 상대 경로를 절대 경로로 변환
                string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, imagePath);
                fullPath = Path.GetFullPath(fullPath); // 정규화

                Debug.WriteLine($"[IMAGE] 전체 경로: {fullPath}");

                if (!File.Exists(fullPath))
                {
                    Debug.WriteLine($"[IMAGE ERROR] 파일이 존재하지 않음: {fullPath}");
                    return null;
                }

                // BitmapImage 생성
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad; // 파일 잠금 방지
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache; // 캐시 무시
                bitmap.UriSource = new Uri(fullPath, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze(); // 성능 향상 및 크로스 스레드 접근 허용

                Debug.WriteLine($"[IMAGE] 이미지 로드 성공: {fullPath}");
                return bitmap;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] 이미지 로드 실패: {ex.Message}");
                Debug.WriteLine($"[ERROR] 스택 추적: {ex.StackTrace}");
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}