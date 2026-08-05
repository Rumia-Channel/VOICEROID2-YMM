using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Voiceroid2Ymm.Voice.PropertyEditor;

/// <summary>
/// 単語内のモーラ列に対し、アクセント高低を VOICEPEAK-plus 風の滑らかな折れ線で
/// 表示・編集するコントロール (アクセントモードのみの移植)。
/// クリックでアクセント核 (高低の切り替わり点) をトグルする。
/// 編集対象外 (句読点・特殊モーラ) では線を途切れさせ、点を中央寄りに表示する。
/// </summary>
public partial class AccentLineControl : UserControl
{
    // 描画帯域 (ActualHeight に対する比率)。上下に余白を確保する。
    const double TopRatio = 0.18;
    const double BottomRatio = 0.82;

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(AccentLineControl),
            new PropertyMetadata(null, OnItemsSourceChanged));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static readonly DependencyProperty ColumnWidthProperty =
        DependencyProperty.Register(
            nameof(ColumnWidth),
            typeof(double),
            typeof(AccentLineControl),
            new PropertyMetadata(46.0, OnVisualPropertyChanged));

    public double ColumnWidth
    {
        get => (double)GetValue(ColumnWidthProperty);
        set => SetValue(ColumnWidthProperty, value);
    }

    public static readonly DependencyProperty DotSizeProperty =
        DependencyProperty.Register(
            nameof(DotSize),
            typeof(double),
            typeof(AccentLineControl),
            new PropertyMetadata(9.0, OnVisualPropertyChanged));

    public double DotSize
    {
        get => (double)GetValue(DotSizeProperty);
        set => SetValue(DotSizeProperty, value);
    }

    public static readonly DependencyProperty LineThicknessProperty =
        DependencyProperty.Register(
            nameof(LineThickness),
            typeof(double),
            typeof(AccentLineControl),
            new PropertyMetadata(1.6, OnVisualPropertyChanged));

    public double LineThickness
    {
        get => (double)GetValue(LineThicknessProperty);
        set => SetValue(LineThicknessProperty, value);
    }

    public static readonly DependencyProperty EditableDotBrushProperty =
        DependencyProperty.Register(
            nameof(EditableDotBrush),
            typeof(Brush),
            typeof(AccentLineControl),
            new PropertyMetadata(Brushes.SteelBlue, OnVisualPropertyChanged));

    public Brush EditableDotBrush
    {
        get => (Brush)GetValue(EditableDotBrushProperty);
        set => SetValue(EditableDotBrushProperty, value);
    }

    public static readonly DependencyProperty SpecialDotBrushProperty =
        DependencyProperty.Register(
            nameof(SpecialDotBrush),
            typeof(Brush),
            typeof(AccentLineControl),
            new PropertyMetadata(Brushes.Gray, OnVisualPropertyChanged));

    public Brush SpecialDotBrush
    {
        get => (Brush)GetValue(SpecialDotBrushProperty);
        set => SetValue(SpecialDotBrushProperty, value);
    }

    public static readonly DependencyProperty LineBrushProperty =
        DependencyProperty.Register(
            nameof(LineBrush),
            typeof(Brush),
            typeof(AccentLineControl),
            new PropertyMetadata(Brushes.SteelBlue, OnVisualPropertyChanged));

    public Brush LineBrush
    {
        get => (Brush)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public static readonly DependencyProperty FillBrushProperty =
        DependencyProperty.Register(
            nameof(FillBrush),
            typeof(Brush),
            typeof(AccentLineControl),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>折れ線の下を塗りつぶすブラシ。null なら塗りつぶさない。</summary>
    public Brush FillBrush
    {
        get => (Brush)GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    int _clickIndex = -1;
    bool _clickMoved;
    Point _pointerDownPos;

    public AccentLineControl()
    {
        InitializeComponent();
        Background = Brushes.Transparent;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseMove += OnMouseMove;
        MouseLeave += OnMouseLeave;
    }

    static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (AccentLineControl)d;
        control.UnsubscribeOld(e.OldValue as IEnumerable);
        control.SubscribeNew(e.NewValue as IEnumerable);
        control.Refresh();
    }

    static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((AccentLineControl)d).Refresh();

    void OnLoaded(object sender, RoutedEventArgs e)
        => Refresh();

    void OnSizeChanged(object sender, SizeChangedEventArgs e)
        => Refresh();

    void OnUnloaded(object sender, RoutedEventArgs e)
    {
        UnsubscribeOld(ItemsSource);
        ReleaseClick();
    }

    void SubscribeNew(IEnumerable? items)
    {
        if (items is INotifyCollectionChanged observable)
            observable.CollectionChanged += OnCollectionChanged;

        foreach (var item in items?.Cast<object>() ?? Enumerable.Empty<object>())
        {
            if (item is INotifyPropertyChanged notify)
                notify.PropertyChanged += OnItemPropertyChanged;
        }
    }

    void UnsubscribeOld(IEnumerable? items)
    {
        if (items is INotifyCollectionChanged observable)
            observable.CollectionChanged -= OnCollectionChanged;

        foreach (var item in items?.Cast<object>() ?? Enumerable.Empty<object>())
        {
            if (item is INotifyPropertyChanged notify)
                notify.PropertyChanged -= OnItemPropertyChanged;
        }
    }

    void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems.Cast<object>())
            {
                if (item is INotifyPropertyChanged notify)
                    notify.PropertyChanged -= OnItemPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.Cast<object>())
            {
                if (item is INotifyPropertyChanged notify)
                    notify.PropertyChanged += OnItemPropertyChanged;
            }
        }

        Refresh();
    }

    void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Voiceroid2MoraViewModel.IsAccentHigh)
                         or nameof(Voiceroid2MoraViewModel.IsAccentEditable)
                         or nameof(Voiceroid2MoraViewModel.IsSpecialMora))
        {
            Refresh();
        }
    }

    double GetTop(double height) => height * TopRatio;
    double GetBottom(double height) => height * BottomRatio;

    double DrawY(double normalized, double height)
    {
        double top = GetTop(height);
        double bottom = GetBottom(height);
        return top + (1.0 - normalized) * (bottom - top);
    }

    int NearestIndex(double px, int count)
    {
        double columnWidth = ColumnWidth;
        int best = -1;
        double bestDist = double.MaxValue;
        for (int i = 0; i < count; i++)
        {
            double x = i * columnWidth + columnWidth / 2.0;
            double dist = Math.Abs(x - px);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = i;
            }
        }
        return best;
    }

    void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var items = ItemsSource?.Cast<Voiceroid2MoraViewModel>().ToList();
        if (items is null || items.Count == 0) return;

        Point p = e.GetPosition(this);
        int index = NearestIndex(p.X, items.Count);
        if (index < 0 || !items[index].IsAccentEditable) return;

        _clickIndex = index;
        _clickMoved = false;
        _pointerDownPos = p;
        CaptureMouse();
        e.Handled = true;
    }

    void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_clickIndex < 0) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            ReleaseClick();
            return;
        }

        Point p = e.GetPosition(this);
        if (Math.Abs(p.X - _pointerDownPos.X) > 2.0 || Math.Abs(p.Y - _pointerDownPos.Y) > 2.0)
            _clickMoved = true;
    }

    void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_clickIndex < 0) return;

        // 移動がほぼ無ければクリックとみなし、アクセント核をトグルする。
        if (!_clickMoved)
        {
            var items = ItemsSource?.Cast<Voiceroid2MoraViewModel>().ToList();
            if (items is not null && _clickIndex < items.Count && items[_clickIndex].IsAccentEditable)
                items[_clickIndex].ToggleAccent();
        }

        ReleaseClick();
        e.Handled = true;
    }

    void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (_clickIndex >= 0 && e.LeftButton != MouseButtonState.Pressed)
            ReleaseClick();
    }

    void ReleaseClick()
    {
        _clickIndex = -1;
        _clickMoved = false;
        try { ReleaseMouseCapture(); }
        catch { /* ignore */ }
    }

    void Refresh()
    {
        AccentCanvas.Children.Clear();

        var items = ItemsSource?.Cast<Voiceroid2MoraViewModel>().ToList();
        if (items is null || items.Count == 0)
            return;

        double columnWidth = ColumnWidth;
        double dotSize = DotSize;
        double halfDot = dotSize / 2.0;
        double height = ActualHeight;
        if (double.IsNaN(height) || height <= 0)
            height = 120.0;

        var points = new List<(double x, double y, bool editable)>();
        for (int i = 0; i < items.Count; i++)
        {
            var mora = items[i];
            double x = i * columnWidth + columnWidth / 2.0;
            bool editable = mora.IsAccentEditable;
            double normalized = editable
                ? (mora.IsAccentHigh ? 1.0 : 0.0)
                : 0.5;
            double y = DrawY(normalized, height);
            points.Add((x, y, editable));
        }

        // 編集可能な連続区間ごとに滑らかな折れ線 (塗り + 線) を描く。
        var segment = new List<Point>();
        for (int i = 0; i < points.Count; i++)
        {
            if (points[i].editable)
            {
                segment.Add(new Point(points[i].x, points[i].y));
            }
            else
            {
                DrawSegment(segment, height);
                segment.Clear();
            }
        }
        DrawSegment(segment, height);

        for (int i = 0; i < items.Count; i++)
        {
            var (x, y, editable) = points[i];
            var ellipse = new Ellipse
            {
                Width = dotSize,
                Height = dotSize,
                Fill = editable ? EditableDotBrush : SpecialDotBrush,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(ellipse, x - halfDot);
            Canvas.SetTop(ellipse, y - halfDot);
            AccentCanvas.Children.Add(ellipse);
        }
    }

    void DrawSegment(List<Point> segment, double height)
    {
        if (segment.Count == 0)
            return;

        if (segment.Count == 1)
        {
            // 1 点区間は線も塗りも描かず、ドットのみ (呼び出し側で描画済み)。
            return;
        }

        var strokeFigure = new PathFigure
        {
            StartPoint = segment[0],
            IsClosed = false,
            IsFilled = false,
        };
        foreach (var s in BuildCurveSegments(segment))
            strokeFigure.Segments.Add(s);

        var strokeGeometry = new PathGeometry();
        strokeGeometry.Figures.Add(strokeFigure);

        AccentCanvas.Children.Add(new System.Windows.Shapes.Path
        {
            Data = strokeGeometry,
            Stroke = LineBrush,
            StrokeThickness = LineThickness,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Fill = null,
            IsHitTestVisible = false,
        });

        var fillBrush = FillBrush;
        if (fillBrush is not null)
        {
            double bottom = GetBottom(height) + 2.0;
            var fillFigure = new PathFigure
            {
                StartPoint = segment[0],
                IsClosed = true,
                IsFilled = true,
            };
            foreach (var s in BuildCurveSegments(segment))
                fillFigure.Segments.Add(s);
            fillFigure.Segments.Add(new LineSegment(new Point(segment[^1].X, bottom), true));
            fillFigure.Segments.Add(new LineSegment(new Point(segment[0].X, bottom), true));

            var fillGeometry = new PathGeometry();
            fillGeometry.Figures.Add(fillFigure);

            // 塗りは線の下に置きたいので先頭へ挿入する。
            AccentCanvas.Children.Insert(0, new System.Windows.Shapes.Path
            {
                Data = fillGeometry,
                Fill = fillBrush,
                Stroke = null,
                IsHitTestVisible = false,
            });
        }
    }

    /// <summary>
    /// 点列を Catmull-Rom 補間し、Cubic Bezier セグメント列へ変換する。
    /// </summary>
    static List<PathSegment> BuildCurveSegments(List<Point> pts)
    {
        var result = new List<PathSegment>();
        int n = pts.Count;
        for (int i = 0; i < n - 1; i++)
        {
            Point p1 = pts[i];
            Point p2 = pts[i + 1];
            Point p0 = i > 0 ? pts[i - 1] : p1;
            Point p3 = i + 2 < n ? pts[i + 2] : p2;

            var c1 = new Point(p1.X + (p2.X - p0.X) / 6.0, p1.Y + (p2.Y - p0.Y) / 6.0);
            var c2 = new Point(p2.X - (p3.X - p1.X) / 6.0, p2.Y - (p3.Y - p1.Y) / 6.0);

            result.Add(new BezierSegment(c1, c2, p2, true));
        }
        return result;
    }
}
