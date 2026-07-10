using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SystemOptimizerLite;

public static class MotionService
{
    public static bool ReducedMotion => SystemParameters.HighContrast || !SystemParameters.ClientAreaAnimation;

    public static void FadeSlideIn(FrameworkElement element, double distance = 10, int milliseconds = 200)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        var transform = element.RenderTransform as TranslateTransform ?? new TranslateTransform();
        element.RenderTransform = transform;
        if (ReducedMotion)
        {
            element.Opacity = 1;
            transform.Y = 0;
            return;
        }
        element.Opacity = 0;
        transform.Y = distance;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = ease });
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(distance, 0, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = ease });
    }

    public static void AttachCardMotion(FrameworkElement element)
    {
        var translate = new TranslateTransform();
        element.RenderTransform = translate;
        element.MouseEnter += (_, _) => AnimateTranslate(translate, ReducedMotion ? 0 : -1, 130);
        element.MouseLeave += (_, _) => AnimateTranslate(translate, 0, 130);
        element.MouseLeftButtonDown += (_, _) => AnimateTranslate(translate, 0, 80);
        element.MouseLeftButtonUp += (_, _) => AnimateTranslate(translate, element.IsMouseOver && !ReducedMotion ? -1 : 0, 100);
    }

    public static void OpenDrawer(FrameworkElement overlay, TranslateTransform panelTransform)
    {
        overlay.Visibility = Visibility.Visible;
        overlay.BeginAnimation(UIElement.OpacityProperty, null);
        panelTransform.BeginAnimation(TranslateTransform.XProperty, null);
        if (ReducedMotion)
        {
            overlay.Opacity = 1;
            panelTransform.X = 0;
            return;
        }
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        overlay.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
        panelTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(460, 0, TimeSpan.FromMilliseconds(230)) { EasingFunction = ease });
    }

    public static async Task CloseDrawerAsync(FrameworkElement overlay, TranslateTransform panelTransform)
    {
        if (overlay.Visibility != Visibility.Visible) return;
        if (ReducedMotion)
        {
            overlay.Visibility = Visibility.Collapsed;
            overlay.Opacity = 0;
            panelTransform.X = 460;
            return;
        }
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var slide = new DoubleAnimation(panelTransform.X, 460, TimeSpan.FromMilliseconds(190)) { EasingFunction = ease };
        slide.Completed += (_, _) => completion.TrySetResult();
        overlay.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(overlay.Opacity, 0, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease });
        panelTransform.BeginAnimation(TranslateTransform.XProperty, slide);
        await completion.Task;
        overlay.Visibility = Visibility.Collapsed;
    }

    private static void AnimateTranslate(TranslateTransform translate, double value, int milliseconds)
    {
        translate.BeginAnimation(TranslateTransform.YProperty, null);
        if (ReducedMotion) { translate.Y = value; return; }
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var animation = new DoubleAnimation(value, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = ease };
        translate.BeginAnimation(TranslateTransform.YProperty, animation);
    }
}
