using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI.ViewManagement;

namespace Hofwerk.WinUI;

/// <summary>
/// The app's small animations, run on the composition layer so they stay smooth while the page is busy.
/// They are skipped when Windows has animations turned off (Settings > Accessibility > Visual effects).
/// </summary>
static class Motion
{
    static readonly UISettings Settings = new();

    public static bool Enabled => Settings.AnimationsEnabled;

    // Starts quickly and settles gently: the curve Windows itself uses for things that appear.
    static CompositionEasingFunction Decelerate(Compositor c) =>
        c.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f));

    /// <summary>A page that was just shown fades in while rising a few pixels into place.</summary>
    public static void PageEnter(UIElement page)
    {
        if (!Enabled) return;

        var visual = ElementCompositionPreview.GetElementVisual(page);
        var c = visual.Compositor;
        ElementCompositionPreview.SetIsTranslationEnabled(page, true);
        var ease = Decelerate(c);

        var fade = c.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 0f);
        fade.InsertKeyFrame(1f, 1f, ease);
        fade.Duration = TimeSpan.FromMilliseconds(240);

        var rise = c.CreateVector3KeyFrameAnimation();
        rise.InsertKeyFrame(0f, new Vector3(0f, 18f, 0f));
        rise.InsertKeyFrame(1f, Vector3.Zero, ease);
        rise.Duration = TimeSpan.FromMilliseconds(340);

        visual.StartAnimation("Opacity", fade);
        visual.StartAnimation("Translation", rise);
    }

    /// <summary>
    /// A theme change cross-fades instead of flipping: a cover in the old theme's colour is laid over the window and
    /// fades away while the new theme is already underneath.
    /// </summary>
    public static void ThemeCrossfade(FrameworkElement cover, Windows.UI.Color oldColor)
    {
        if (!Enabled) return;

        var brush = new Microsoft.UI.Xaml.Media.SolidColorBrush(oldColor);
        if (cover is Microsoft.UI.Xaml.Controls.Border border) border.Background = brush;
        cover.Visibility = Visibility.Visible;

        var visual = ElementCompositionPreview.GetElementVisual(cover);
        var c = visual.Compositor;
        visual.Opacity = 1f;

        var batch = c.CreateScopedBatch(CompositionBatchTypes.Animation);
        var fade = c.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 1f);
        fade.InsertKeyFrame(1f, 0f, c.CreateLinearEasingFunction());
        fade.Duration = TimeSpan.FromMilliseconds(380);
        visual.StartAnimation("Opacity", fade);
        batch.End();
        batch.Completed += (_, _) => cover.Visibility = Visibility.Collapsed;
    }
}
