using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Notea.Modules.Subject.Models;


namespace Notea.Modules.Subject.ViewModels
{
    public class MarkdownLineViewModel : INotifyPropertyChanged
    {
        private string _content;
        private ObservableCollection<Inline> _inlines = new ObservableCollection<Inline>();
        private double _fontSize = 14;
        private FontWeight _fontWeight = FontWeights.Normal;
        private bool _isHeading = false;
        private int _headingLevel = 0;
        private string _rawContent;
        private bool _isEditing;
        private string _placeholder = "";
        private bool _isComposing = false;
        private bool _hasFocus = false;

        public int TextId { get; set; }
        public int CategoryId { get; set; }
        public int SubjectId { get; set; }
        public int Index { get; set; }
        public bool IsHeadingLine { get; set; } = false;

        public string OriginalContent { get; private set; }
        public bool HasChanges => Content != OriginalContent || ImageUrl != OriginalImageUrl;

        public event EventHandler<CategoryCreatedEventArgs> CategoryCreated;
        public event EventHandler<FindPreviousCategoryEventArgs> RequestFindPreviousCategory;


        public MarkdownLineViewModel()
        {
            Content = "";
            OriginalContent = "";
            UpdateInlinesFromContent();
            UpdatePlaceholder();
        }

        // 데이터베이스에서 로드한 후 호출
        public void SetOriginalContent(string content, string imageUrl = null)
        {
            OriginalContent = content;
            OriginalImageUrl = imageUrl;
        }

        // 저장 후 호출하여 변경사항 리셋
        public void ResetChanges()
        {
            OriginalContent = Content;
            OriginalImageUrl = ImageUrl;
        }

        private bool _isList = false;
        public bool IsList
        {
            get => _isList;
            set
            {
                if (_isList != value)
                {
                    _isList = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isDragging;
        public bool IsDragging
        {
            get => _isDragging;
            set
            {
                if (_isDragging != value)
                {
                    _isDragging = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _listSymbol = "";
        public string ListSymbol
        {
            get => _listSymbol;
            set
            {
                if (_listSymbol != value)
                {
                    _listSymbol = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _displayOrder;
        public int DisplayOrder
        {
            get => _displayOrder;
            set
            {
                if (_displayOrder != value)
                {
                    _displayOrder = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _level = 0;
        public int Level
        {
            get => _level;
            set
            {
                if (_level != value)
                {
                    _level = value;
                    OnPropertyChanged();
                }
            }
        }

        public string HeadingText
        {
            get
            {
                if (NoteRepository.IsMarkdownHeading(Content))
                {
                    return Content;
                }
                return Content;
            }
        }

        public bool HasFocus
        {
            get => _hasFocus;
            set
            {
                if (_hasFocus != value)
                {
                    _hasFocus = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ShowPlaceholder));
                }
            }
        }

        public bool IsComposing
        {
            get => _isComposing;
            set
            {
                if (_isComposing != value)
                {
                    _isComposing = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ShowPlaceholder));
                }
            }
        }

        public string RawContent
        {
            get => _rawContent;
            set
            {
                if (_rawContent != value)
                {
                    _rawContent = value;
                    OnPropertyChanged();
                }
            }
        }

        public ObservableCollection<Inline> Inlines
        {
            get => _inlines;
            set
            {
                _inlines = value;
                OnPropertyChanged();
            }
        }

        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                if (_isEditing != value)
                {
                    _isEditing = value;
                    OnPropertyChanged();

                    if (!_isEditing)
                    {
                        IsComposing = false;
                        HasFocus = false;

                    }
                }
            }
        }

        private string _imageUrl;
        public string ImageUrl
        {
            get => _imageUrl;
            set
            {
                if (_imageUrl != value)
                {
                    _imageUrl = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsImage));
                    OnPropertyChanged(nameof(HasChanges));
                }
            }
        }

        private string _contentType = "text";
        public string ContentType
        {
            get => _contentType;
            set
            {
                if (_contentType != value)
                {
                    _contentType = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsImage));
                }
            }
        }

        public bool IsImage => ContentType == "image";

        // 원본 이미지 URL (변경 추적용)
        public string OriginalImageUrl { get; private set; }

        public bool IsOrderedList => Regex.IsMatch(Content, @"^\d+\.\s+");

        // 리스트 체크
        private void CheckListStatus()
        {
            // 순서 없는 리스트
            var unorderedMatch = Regex.Match(Content, @"^(\-|\*|\+)\s+");
            if (unorderedMatch.Success)
            {
                IsList = true;
                ListSymbol = unorderedMatch.Groups[1].Value;
                return;
            }

            // 순서 있는 리스트
            var orderedMatch = Regex.Match(Content, @"^(\d+)\.\s+");
            if (orderedMatch.Success)
            {
                IsList = true;
                ListSymbol = orderedMatch.Groups[1].Value + ".";
                return;
            }

            IsList = false;
            ListSymbol = "";
        }

        public string Content
        {
            get => _content;
            set
            {
                var preprocessed = PreprocessContent(value);
                if (_content != preprocessed)
                {
                    string oldContent = _content;
                    _content = preprocessed;
                    CheckListStatus();

                    // 헤딩 레벨 감지
                    int detectedLevel = NoteRepository.GetHeadingLevel(_content);
                    bool isHeading = detectedLevel > 0;

                    bool wasHeading = IsHeadingLine;
                    IsHeadingLine = isHeading;
                    Level = detectedLevel;

                    if (wasHeading != IsHeadingLine)
                    {
                        OnHeadingStatusChanged(wasHeading, IsHeadingLine);
                    }

                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HeadingText));
                    OnPropertyChanged(nameof(HasChanges));
                    ApplyMarkdownStyle();
                    UpdateInlinesFromContent();
                    UpdatePlaceholder();
                    OnPropertyChanged(nameof(ShowPlaceholder));
                }
            }
        }

        /// <summary>
        /// 다음 리스트 아이템을 위한 prefix 생성
        /// </summary>
        public string GetNextListPrefix()
        {
            if (!IsList) return "";

            // 순서 없는 리스트
            if (ListSymbol == "-" || ListSymbol == "*" || ListSymbol == "+")
            {
                return ListSymbol + " ";
            }

            // 순서 있는 리스트
            var orderedMatch = Regex.Match(Content, @"^(\d+)\.\s+");
            if (orderedMatch.Success)
            {
                int currentNumber = int.Parse(orderedMatch.Groups[1].Value);
                return $"{currentNumber + 1}. ";
            }

            return "";
        }

        private void OnHeadingStatusChanged(bool wasHeading, bool isHeading)
        {
            try
            {
                if (wasHeading && !isHeading)
                {
                    // 제목에서 일반 텍스트로 변경
                    Debug.WriteLine($"[DEBUG] 제목에서 일반 텍스트로 변경됨: {Content}");

                    // 기본 카테고리(1)인 경우 삭제하지 않음
                    if (CategoryId <= 1)
                    {
                        Debug.WriteLine("[WARNING] 기본 카테고리는 삭제할 수 없습니다. 텍스트로만 변경됩니다.");
                        IsHeadingLine = false;
                        Level = 0;
                        TextId = 0; // 새로운 텍스트로 생성되도록
                        return;
                    }

                    // 이전 카테고리 찾기
                    int previousCategoryId = FindPreviousCategoryId();

                    if (previousCategoryId > 0)
                    {
                        // 현재 카테고리에 속한 텍스트들을 이전 카테고리로 이동
                        NoteRepository.ReassignTextsToCategory(this.CategoryId, previousCategoryId);
                    }

                    // 카테고리 삭제
                    NoteRepository.DeleteCategory(this.CategoryId);

                    // 현재 라인을 일반 텍스트로 변환
                    CategoryId = previousCategoryId > 0 ? previousCategoryId : 1;
                    TextId = 0; // 새로운 텍스트로 생성되도록
                    Level = 0;
                    IsHeadingLine = false;

                    Debug.WriteLine($"[DEBUG] 제목→텍스트 변환 완료. 새 CategoryId: {CategoryId}");
                }
                else if (!wasHeading && isHeading)
                {
                    // 일반 텍스트에서 제목으로 변경
                    Debug.WriteLine($"[DEBUG] 일반 텍스트에서 제목으로 변경됨: {Content}");

                    // 기존 텍스트 삭제
                    if (TextId > 0)
                    {
                        NoteRepository.DeleteLine(TextId);
                        TextId = 0;
                    }

                    // 새 카테고리 생성 준비
                    Level = NoteRepository.GetHeadingLevel(Content);
                    CategoryId = 0; // 새로운 카테고리로 생성되도록 (저장 시점에 처리)
                    IsHeadingLine = true;

                    // ❌ CategoryCreated 이벤트 제거 - 현재 프로젝트에서는 사용하지 않음
                    // 대신 저장 시점에 NoteEditorViewModel에서 처리

                    Debug.WriteLine($"[DEBUG] 텍스트→제목 변환 완료. Level: {Level}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] OnHeadingStatusChanged 실패: {ex.Message}");
                throw;
            }
        }

        private int FindPreviousCategoryId()
        {
            var args = new FindPreviousCategoryEventArgs { CurrentLine = this };
            RequestFindPreviousCategory?.Invoke(this, args);
            return args.PreviousCategoryId;
        }

        protected virtual void OnRequestFindPreviousCategory(FindPreviousCategoryEventArgs e)
        {
            RequestFindPreviousCategory?.Invoke(this, e);
        }

        public class FindPreviousCategoryEventArgs : EventArgs
        {
            public MarkdownLineViewModel CurrentLine { get; set; }
            public int PreviousCategoryId { get; set; }
        }

        public string Placeholder
        {
            get => _placeholder;
            set
            {
                if (_placeholder != value)
                {
                    _placeholder = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool ShowPlaceholder
        {
            get
            {
                if (_isComposing) return false;
                if (!_hasFocus) return false;

                if (string.IsNullOrWhiteSpace(Content))
                    return true;

                // 제목 기호만 있는 경우 (모든 레벨)
                var headingOnlyPattern = @"^#{1,6}\s*$";
                if (Regex.IsMatch(Content, headingOnlyPattern))
                    return true;

                // 리스트 기호만 있는 경우
                var listOnlyPattern = @"^(\-|\*|\+)\s*$";
                if (Regex.IsMatch(Content, listOnlyPattern))
                    return true;

                // 순서 있는 리스트 기호만 있는 경우
                var orderedListOnlyPattern = @"^\d+\.\s*$";
                if (Regex.IsMatch(Content, orderedListOnlyPattern))
                    return true;

                return false;
            }
        }

        private void UpdatePlaceholder()
        {
            // 제목 레벨에 따른 플레이스홀더
            var headingMatch = Regex.Match(Content, @"^(#{1,6})\s*$");
            if (headingMatch.Success)
            {
                int level = headingMatch.Groups[1].Value.Length;
                Placeholder = level switch
                {
                    1 => "\t제목을 입력하세요",
                    2 => "\t부제목을 입력하세요",
                    3 => "\t섹션 제목을 입력하세요",
                    4 => "\t소제목을 입력하세요",
                    5 => "\t작은 제목을 입력하세요",
                    6 => "\t가장 작은 제목을 입력하세요",
                    _ => "\t제목을 입력하세요"
                };
                return;
            }

            // 리스트에 따른 플레이스홀더
            var listMatch = Regex.Match(Content, @"^(\-|\*|\+)\s*$");
            if (listMatch.Success)
            {
                Placeholder = "목록 항목을 입력하세요";
                return;
            }

            // 순서 있는 리스트
            var orderedListMatch = Regex.Match(Content, @"^(\d+)\.\s*$");
            if (orderedListMatch.Success)
            {
                var number = orderedListMatch.Groups[1].Value;
                Placeholder = $"{number}번 항목을 입력하세요";
                return;
            }

            // 빈 줄일 때
            if (string.IsNullOrWhiteSpace(Content))
            {
                Placeholder = "내용을 입력하세요...";
                return;
            }

            // 그 외의 경우 placeholder 없음
            Placeholder = "";
        }

        // 엔터 처리 시 리스트/제목 정리를 위한 메서드
        public bool ShouldCleanupOnEnter()
        {
            // 리스트 기호만 있는 경우
            if (Regex.IsMatch(Content, @"^(\-|\*|\+)\s*$"))
            {
                Content = ""; // 리스트 기호 제거
                return true;
            }

            // 순서 있는 리스트 기호만 있는 경우
            if (Regex.IsMatch(Content, @"^\d+\.\s*$"))
            {
                Content = ""; // 번호 제거
                return true;
            }

            // 제목 기호만 있는 경우 (모든 레벨)
            if (Regex.IsMatch(Content, @"^#{1,6}\s*$"))
            {
                Content = ""; // 제목 기호 제거
                return true;
            }

            return false;
        }


        public double FontSize
        {
            get => _fontSize;
            set
            {
                if (_fontSize != value)
                {
                    _fontSize = value;
                    OnPropertyChanged();
                }
            }
        }

        public FontWeight FontWeight
        {
            get => _fontWeight;
            set
            {
                if (_fontWeight != value)
                {
                    _fontWeight = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsHeading
        {
            get => _isHeading;
            set
            {
                if (_isHeading != value)
                {
                    _isHeading = value;
                    OnPropertyChanged();
                }
            }
        }

        public int HeadingLevel
        {
            get => _headingLevel;
            set
            {
                if (_headingLevel != value)
                {
                    _headingLevel = value;
                    OnPropertyChanged();
                }
            }
        }

        private Thickness _margin = new Thickness(4);
        public Thickness Margin
        {
            get => _margin;
            set { _margin = value; OnPropertyChanged(); }
        }

        private string PreprocessContent(string input)
        {
            if (input == null) return "";

            return input
                .Replace("/cap ", "∩")
                .Replace("/cup ", "∪")
                .Replace("/inf ", "∞")
                .Replace("/pd ", "∂")
                .Replace("/sum ", "∑")
                .Replace("/int ", "∫")
                .Replace("/sqrt ", "√")
                .Replace("/theta ", "θ")
                .Replace("/pi ", "π")
                .Replace("/mu ", "μ")
                .Replace("/sigma ", "σ")
                .Replace(":. ", "∴")
                .Replace(":> ", "∵")
                .Replace("-> ", "→")
                .Replace("<- ", "←")
                .Replace("!= ", "≠")
                .Replace("~= ", "≈")
                .Replace("<= ", "≤")
                .Replace(">= ", "≥")
                .Replace("ㄴ ", "↳");
        }

        private void ApplyMarkdownStyle()
        {
            if (string.IsNullOrWhiteSpace(Content))
            {
                ResetStyle();
                return;
            }

            // 제목 (# ~ ######) - 모든 레벨 스타일 적용
            var headingMatch = Regex.Match(Content, @"^(#{1,6})\s+(.*)");

            if (headingMatch.Success)
            {
                HeadingLevel = headingMatch.Groups[1].Value.Length;
                IsHeading = true;

                FontWeight = FontWeights.Bold;
                FontSize = HeadingLevel switch
                {
                    1 => 26,
                    2 => 22,
                    3 => 18,
                    4 => 16,
                    5 => 14,
                    6 => 13,
                    _ => 14
                };

                Margin = new Thickness(4);
                return;
            }

            // Bold 처리
            if (Regex.IsMatch(Content, @"^\*\*(.*?)\*\*$"))
            {
                FontWeight = FontWeights.Bold;
            }
            else
            {
                FontWeight = FontWeights.Normal;
            }

            // 리스트 처리
            var listMatch = Regex.Match(Content, @"^(\-|\*)\s+(.*)");
            if (listMatch.Success)
            {
                IsHeading = false;
                HeadingLevel = 0;
                FontWeight = FontWeights.SemiBold;
                FontSize = 14;
                Margin = new Thickness(20, 4, 4, 4);
                return;
            }

            ResetStyle();
            Inlines = MarkdownParser.Parse(Content);
        }

        private void ResetStyle()
        {
            IsHeading = false;
            HeadingLevel = 0;
            FontSize = 14;
            FontWeight = FontWeights.Normal;
            Margin = new Thickness(4);
        }

        public void UpdateInlinesFromContent()
        {
            var newInlines = new ObservableCollection<Inline>();
            var font = new FontFamily(new Uri("pack://application:,,,/"), "./Resources/Fonts/#Pretendard Variable");

            if (string.IsNullOrEmpty(Content))
            {
                Inlines = newInlines;
                return;
            }

            // 1. Heading 처리 (# ~ ######) - 모든 레벨
            var headingMatch = Regex.Match(Content, @"^(#{1,6})\s*(.*)");
            if (headingMatch.Success)
            {
                int level = headingMatch.Groups[1].Value.Length;
                string text = headingMatch.Groups[2].Value;

                double headingFontSize = level switch
                {
                    1 => 26,
                    2 => 22,
                    3 => 18,
                    4 => 16,
                    5 => 14,
                    6 => 13,
                    _ => 14
                };

                var run = new Run(text)
                {
                    FontSize = headingFontSize,
                    FontWeight = FontWeights.Bold,
                    FontFamily = font
                };

                newInlines.Add(run);
                Inlines = newInlines;
                OnPropertyChanged(nameof(Inlines));
                return;
            }

            // 2. 인라인 마크다운 처리
            var pattern = @"(\*\*(.*?)\*\*|\*(.*?)\*|__(.*?)__|~~(.*?)~~)";
            var matches = Regex.Matches(Content, pattern);

            int lastIndex = 0;

            foreach (Match match in matches)
            {
                if (match.Index > lastIndex)
                {
                    var plain = Content.Substring(lastIndex, match.Index - lastIndex);
                    newInlines.Add(new Run(plain) { FontFamily = font, FontSize = FontSize });
                }

                if (match.Value.StartsWith("**"))
                {
                    var boldRun = new Run(match.Groups[2].Value)
                    {
                        FontFamily = font,
                        FontSize = FontSize,
                        FontWeight = FontWeights.Bold
                    };
                    newInlines.Add(boldRun);
                }
                else if (match.Value.StartsWith("*"))
                {
                    var italicRun = new Run(match.Groups[3].Value)
                    {
                        FontFamily = font,
                        FontSize = FontSize,
                        FontStyle = FontStyles.Italic
                    };
                    newInlines.Add(italicRun);
                }
                else if (match.Value.StartsWith("__"))
                {
                    var underline = new Run(match.Groups[4].Value)
                    {
                        FontFamily = font,
                        FontSize = FontSize,
                        TextDecorations = TextDecorations.Underline
                    };
                    newInlines.Add(underline);
                }
                else if (match.Value.StartsWith("~~"))
                {
                    var strike = new Run(match.Groups[5].Value)
                    {
                        FontFamily = font,
                        FontSize = FontSize,
                        TextDecorations = TextDecorations.Strikethrough
                    };
                    newInlines.Add(strike);
                }

                lastIndex = match.Index + match.Length;
            }

            if (lastIndex < Content.Length)
            {
                newInlines.Add(new Run(Content.Substring(lastIndex)) { FontFamily = font, FontSize = FontSize });
            }

            Inlines = newInlines;
            OnPropertyChanged(nameof(Inlines));
        }
        public class CategoryCreatedEventArgs : EventArgs
        {
            public int NewCategoryId { get; set; }
        }
        protected virtual void OnCategoryCreated(int categoryId)
        {
            CategoryCreated?.Invoke(this, new CategoryCreatedEventArgs { NewCategoryId = categoryId });
        }

        private void SaveToDatabase()
        {
            try
            {
                // 부모가 없어도 삭제하지 않고 기본 카테고리 할당
                if (!IsHeadingLine && CategoryId <= 0)
                {
                    Debug.WriteLine($"[DB] CategoryId가 없음. 기본 카테고리 할당. Content: {Content}");
                    CategoryId = 1; // 기본 카테고리
                }

                if (IsHeadingLine)  // # 하나인 카테고리 제목인 경우
                {
                    if (CategoryId <= 0)
                    {
                        int newCategoryId = NoteRepository.InsertCategory(Content, SubjectId, DisplayOrder);
                        CategoryId = newCategoryId;
                        OnCategoryCreated(newCategoryId);
                        Debug.WriteLine($"[DB] 새 제목 생성 완료. CategoryId: {newCategoryId}, DisplayOrder: {DisplayOrder}");
                    }
                    else
                    {
                        NoteRepository.UpdateCategory(CategoryId, Content);
                        Debug.WriteLine($"[DB] 제목 업데이트 완료. CategoryId: {CategoryId}");
                    }
                }
                else
                {
                    // 일반 텍스트 저장
                    if (TextId <= 0)
                    {
                        int newTextId = NoteRepository.InsertNewLine(Content, SubjectId, CategoryId, DisplayOrder);
                        TextId = newTextId;
                        Debug.WriteLine($"[DB] 새 텍스트 저장 완료. TextId: {newTextId}, CategoryId: {CategoryId}, DisplayOrder: {DisplayOrder}");
                    }
                    else
                    {
                        NoteRepository.UpdateLine(this);
                        Debug.WriteLine($"[DB] 텍스트 업데이트 완료. TextId: {TextId}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB ERROR] 자동 저장 실패: {ex.Message}");
            }
        }


        public bool IsEmpty => string.IsNullOrWhiteSpace(Content);

        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChanged([CallerMemberName] string name = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}