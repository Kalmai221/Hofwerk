using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Hofwerk.Linux;

/// <summary>The app's small animations: a page that appears eases in, and a theme change fades through instead of flipping.</summary>
static class Motion
{
    static Animation Fade(double from, double to, int milliseconds, Easing easing) => new()
    {
        Duration = TimeSpan.FromMilliseconds(milliseconds),
        Easing = easing,
        FillMode = FillMode.Forward,
        Children =
        {
            new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, from) } },
            new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, to) } },
        },
    };

    /// <summary>A page that was just shown fades in while rising a few pixels into place.</summary>
    public static async void PageEnter(Control page)
    {
        page.RenderTransform ??= new TranslateTransform();
        var rise = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(300),
            Easing = new CubicEaseOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0.0), new Setter(TranslateTransform.YProperty, 16.0) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1.0), new Setter(TranslateTransform.YProperty, 0.0) } },
            },
        };
        try { await rise.RunAsync(page); }
        finally { page.Opacity = 1; }
    }

    /// <summary>Fades the window content out, runs <paramref name="change"/> (the new theme), and fades it back in.</summary>
    public static async void ThemeChange(Control content, Action change)
    {
        try
        {
            await Fade(1, 0.0, 110, new CubicEaseIn()).RunAsync(content);
            change();
            await Task.Delay(40);   // let the new theme's resources reach the controls before they are shown again
            await Fade(0, 1.0, 260, new CubicEaseOut()).RunAsync(content);
        }
        finally { content.Opacity = 1; }
    }
}
