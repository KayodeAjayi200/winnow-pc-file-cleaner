using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FileTinder.Services;
using FileTinder.ViewModels;

namespace FileTinder.Views;

public partial class CardStackControl : System.Windows.Controls.UserControl
{
    private Point _dragStart;
    private bool _isDragging;
    private bool _isAnimating;
    private Storyboard? _pulseSb;
    private const double SwipeThreshold = 110;
    private const double MaxOverlayOpacity = 0.92;

    public CardStackControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private MainViewModel? VM => DataContext as MainViewModel;

    // ── DataContext wiring ─────────────────────────────────────────────────────

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel oldVm)
            oldVm.PropertyChanged -= VM_PropertyChanged;

        if (e.NewValue is MainViewModel newVm)
        {
            newVm.PropertyChanged += VM_PropertyChanged;
            UpdateDuplicateBadge(newVm.CurrentFile);
        }
    }

    private void VM_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.CurrentFile))
            Dispatcher.InvokeAsync(() => UpdateDuplicateBadge(VM?.CurrentFile));
    }

    // ── Duplicate badge ────────────────────────────────────────────────────────

    private void UpdateDuplicateBadge(Models.FileItem? file)
    {
        _pulseSb?.Stop(DuplicateBadge);

        if (file?.IsDuplicate == true)
        {
            DuplicateBadge.Visibility = System.Windows.Visibility.Visible; DuplicateBadge.Opacity = 1;
            _pulseSb = (Storyboard)Resources["DuplicatePulseStoryboard"];
            _pulseSb.Begin(DuplicateBadge, true);
        }
        else
        {
            DuplicateBadge.Visibility = System.Windows.Visibility.Collapsed; DuplicateBadge.Opacity = 0;
            _pulseSb = null;
        }
    }

    // ── Keyboard shortcuts ─────────────────────────────────────────────────────

    private void UserControl_KeyDown(object sender, KeyEventArgs e)
    {
        if (_isAnimating || VM == null) return;
        switch (e.Key)
        {
            case Key.Left:  CommitSwipe(isKeep: false); break;
            case Key.Right: CommitSwipe(isKeep: true);  break;
            case Key.B:     VM.AddToNewBucket(); break;
            case Key.Delete:
            case Key.Back:
                CommitSwipe(isKeep: false);
                break;
            case Key.Space:
                if (VM?.CurrentFile is { } fSpace)
                    FilePreviewControl.OpenInDefaultApp(fSpace);
                break;
            case Key.O:
                if (VM?.CurrentFile is { } f)
                    FilePreviewControl.OpenInDefaultApp(f);
                break;
            case Key.L:
                if (VM?.CurrentFile is { } fLoc)
                    FilePreviewControl.OpenFileLocation(fLoc);
                break;
        }
    }

    private void OpenInAppBtn_Click(object sender, RoutedEventArgs e)
    {
        if (VM?.CurrentFile is { } file)
        {
            FilePreviewControl.OpenInDefaultApp(file);
            Focus();
        }
    }

    private void OpenLocationBtn_Click(object sender, RoutedEventArgs e)
    {
        if (VM?.CurrentFile is { } file)
        {
            FilePreviewControl.OpenFileLocation(file);
            Focus();
        }
    }

    // ── Mouse drag ─────────────────────────────────────────────────────────────

    private void FrontCard_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_isAnimating || VM?.CurrentFile == null) return;
        // Let buttons/interactive controls handle their own click events
        if (IsInteractiveSource(e.OriginalSource)) return;

        // Clear any lingering snap-back animations so direct property writes work
        CardTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        CardTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        CardRotate.BeginAnimation(RotateTransform.AngleProperty, null);

        _dragStart  = e.GetPosition(CardStackGrid);
        _isDragging = true;
        FrontCard.CaptureMouse();
        e.Handled = true;
    }

    private static bool IsInteractiveSource(object? source)
    {
        var d = source as DependencyObject;
        while (d != null)
        {
            if (d is System.Windows.Controls.Button or
                System.Windows.Controls.CheckBox or
                System.Windows.Controls.ComboBox)
                return true;
            d = VisualTreeHelper.GetParent(d);
        }
        return false;
    }

    private void FrontCard_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;

        var pos    = e.GetPosition(CardStackGrid);
        var deltaX = pos.X - _dragStart.X;
        var deltaY = (pos.Y - _dragStart.Y) * 0.25;

        CardTranslate.X = deltaX;
        CardTranslate.Y = deltaY;
        CardRotate.Angle = deltaX * 0.04;

        UpdateOverlays(deltaX);
    }

    private void FrontCard_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        FinishDrag();
    }

    private void FrontCard_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_isDragging && !FrontCard.IsMouseCaptured)
            FinishDrag();
    }

    private void FinishDrag()
    {
        _isDragging = false;
        FrontCard.ReleaseMouseCapture();

        var delta = CardTranslate.X;
        if (Math.Abs(delta) >= SwipeThreshold)
            CommitSwipe(isKeep: delta > 0);
        else
            SnapBack();
    }

    // ── Overlay opacity ────────────────────────────────────────────────────────

    private void UpdateOverlays(double deltaX)
    {
        var t = Math.Abs(deltaX) / SwipeThreshold;
        var opacity = Math.Clamp(t * MaxOverlayOpacity, 0, MaxOverlayOpacity);

        if (deltaX < 0)
        {
            DeleteOverlay.Opacity = opacity;
            KeepOverlay.Opacity   = 0;
        }
        else
        {
            KeepOverlay.Opacity   = opacity;
            DeleteOverlay.Opacity = 0;
        }
    }

    // ── Snap back animation ────────────────────────────────────────────────────

    private void SnapBack()
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(300));
        var ease     = new ElasticEase { Springiness = 3, Oscillations = 1 };

        var animX = MakeDoubleAnim(CardTranslate.X, 0, duration, ease);
        var animY = MakeDoubleAnim(CardTranslate.Y, 0, duration, ease);
        var animR = MakeDoubleAnim(CardRotate.Angle, 0, duration, ease);

        CardTranslate.BeginAnimation(TranslateTransform.XProperty, animX);
        CardTranslate.BeginAnimation(TranslateTransform.YProperty, animY);
        CardRotate.BeginAnimation(RotateTransform.AngleProperty,   animR);

        FadeOverlays(duration);
    }

    // ── Commit swipe ───────────────────────────────────────────────────────────

    public void CommitSwipe(bool isKeep)
    {
        if (_isAnimating || VM?.CurrentFile == null) return;
        _isAnimating = true;

        var flyX    = isKeep ? 700.0 : -700.0;
        var flyAngle = isKeep ? 20.0 : -20.0;
        var duration = new Duration(TimeSpan.FromMilliseconds(320));
        var ease     = new PowerEase { EasingMode = EasingMode.EaseIn, Power = 2 };

        // Show full overlay before flying
        if (isKeep) KeepOverlay.Opacity   = MaxOverlayOpacity;
        else        DeleteOverlay.Opacity = MaxOverlayOpacity;

        var animX = MakeDoubleAnim(CardTranslate.X, flyX, duration, ease);
        var animY = MakeDoubleAnim(CardTranslate.Y, -60, duration, ease);
        var animR = MakeDoubleAnim(CardRotate.Angle, flyAngle, duration, ease);

        animX.Completed += (_, _) =>
        {
            Dispatcher.Invoke(() =>
            {
                if (isKeep) { VM?.Keep();   }
                else        { VM?.Delete(); }

                PlayBackCardsCascade();
                ResetCardTransform();
                _isAnimating = false;

                Focus();
            });
        };

        CardTranslate.BeginAnimation(TranslateTransform.XProperty, animX);
        CardTranslate.BeginAnimation(TranslateTransform.YProperty, animY);
        CardRotate.BeginAnimation(RotateTransform.AngleProperty,   animR);
    }

    private void ResetCardTransform()
    {
        CardTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        CardTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        CardRotate.BeginAnimation(RotateTransform.AngleProperty,   null);
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        CardTranslate.X  = 0;
        CardTranslate.Y  = 0;
        CardRotate.Angle = 0;
        CardScale.ScaleX = 1;
        CardScale.ScaleY = 1;

        KeepOverlay.Opacity   = 0;
        DeleteOverlay.Opacity = 0;

        PlayEntranceAnimation();
    }

    // ── Entrance animation (card scale in) ────────────────────────────────────

    private void PlayEntranceAnimation()
    {
        var dur  = new Duration(TimeSpan.FromMilliseconds(180));
        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 };

        CardScale.ScaleX = 0.92;
        CardScale.ScaleY = 0.92;

        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            MakeDoubleAnim(0.92, 1.0, dur, ease));
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            MakeDoubleAnim(0.92, 1.0, dur, ease));
    }

    // ── Back cards cascade ─────────────────────────────────────────────────────

    private void PlayBackCardsCascade()
    {
        var dur = new Duration(TimeSpan.FromMilliseconds(200));

        // Briefly pump the back cards up then settle — gives a "deck shifting" feel
        AnimateScale(BackCard1Scale, 0.97, 0.95, dur);
        AnimateScale(BackCard2Scale, 0.92, 0.90, dur);
    }

    private static void AnimateScale(ScaleTransform st, double peak, double settle, Duration dur)
    {
        var half  = new Duration(dur.TimeSpan / 2);
        var ease  = new QuadraticEase { EasingMode = EasingMode.EaseOut };

        var upX   = MakeDoubleAnim(st.ScaleX, peak,   half,  ease);
        var downX = MakeDoubleAnim(peak,        settle, half,  ease);
        downX.BeginTime = half.TimeSpan;

        var sb = new Storyboard();
        sb.Children.Add(upX);
        sb.Children.Add(downX);
        Storyboard.SetTarget(upX,   st);
        Storyboard.SetTarget(downX, st);
        Storyboard.SetTargetProperty(upX,   new PropertyPath(ScaleTransform.ScaleXProperty));
        Storyboard.SetTargetProperty(downX, new PropertyPath(ScaleTransform.ScaleXProperty));
        sb.Begin();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static DoubleAnimation MakeDoubleAnim(double from, double to, Duration duration, IEasingFunction? ease = null)
        => new() { From = from, To = to, Duration = duration, EasingFunction = ease };

    private void FadeOverlays(Duration duration)
    {
        var fadeAnim = MakeDoubleAnim(DeleteOverlay.Opacity > 0 ? DeleteOverlay.Opacity : KeepOverlay.Opacity, 0, duration);
        DeleteOverlay.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
        KeepOverlay.BeginAnimation(UIElement.OpacityProperty,   new DoubleAnimation(0, duration));
    }
}
