using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FileTinder.Views;

public partial class StatsPanel : UserControl
{
    public StatsPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => UpdateProgress();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged old)
            old.PropertyChanged -= VM_PropertyChanged;

        if (e.NewValue is INotifyPropertyChanged n)
            n.PropertyChanged += VM_PropertyChanged;

        UpdateProgress();
    }

    private void VM_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (e.PropertyName is "Progress" or "FilesReviewed")
            {
                UpdateProgress();
                FlashElement(ReviewedScale);
            }
            else if (e.PropertyName == "FilesKept")
                FlashElement(KeptScale);
            else if (e.PropertyName == "FilesDeleted")
                FlashElement(DeletedScale);
            else if (e.PropertyName == "SpaceFreedFormatted")
                FlashElement(SpaceFreedScale);
            else if (e.PropertyName == "FilesBucketed")
                FlashElement(BucketedScale);
        });
    }

    private void UpdateProgress()
    {
        if (DataContext is not ViewModels.MainViewModel vm) return;
        var containerWidth = ProgressFill.Parent is FrameworkElement parent ? parent.ActualWidth : 0;
        ProgressFill.Width = containerWidth * Math.Clamp(vm.Progress, 0, 1);
    }

    private static void FlashElement(ScaleTransform scale)
    {
        var dur = new Duration(TimeSpan.FromMilliseconds(250));
        var anim = new DoubleAnimationUsingKeyFrames();
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(1.0,   KeyTime.FromPercent(0)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(1.25,  KeyTime.FromPercent(0.4)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(1.0,   KeyTime.FromPercent(1)));
        anim.Duration = dur;

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateProgress();
    }
}
