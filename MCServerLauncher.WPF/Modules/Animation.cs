using System;
using System.Windows.Media.Animation;
using System.Windows;

namespace MCServerLauncher.WPF.Modules
{
    public class Animation
    {
        public static DoubleAnimation FadeOutAnimation()
        {
            return new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = new Duration(TimeSpan.FromSeconds(0.4)),
                FillBehavior = FillBehavior.HoldEnd
            };
        }
        public static DoubleAnimation FadeInAnimation()
        {
            return new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = new Duration(TimeSpan.FromSeconds(0.4)),
                FillBehavior = FillBehavior.HoldEnd
            };
        }
    }
}
