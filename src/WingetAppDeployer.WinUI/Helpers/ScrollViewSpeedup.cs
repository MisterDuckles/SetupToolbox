using System;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Controls;

namespace WingetAppDeployer_WinUI.Helpers;

// Centrale plek voor de scroll-animation-duur op ScrollView. Default is ~350ms
// wat traag voelt bij muiswiel. 20ms = bijna instant maar nog net animated.
// Pages haken hun ScrollAnimationStarting via ScrollViewSpeedup.OnStarting.
internal static class ScrollViewSpeedup
{
    public const double DurationMs = 20;

    public static void OnStarting(ScrollView _, ScrollingScrollAnimationStartingEventArgs args)
    {
        if (args.Animation is Vector3KeyFrameAnimation kf)
            kf.Duration = TimeSpan.FromMilliseconds(DurationMs);
    }
}
