using System;
using System.Windows;
using System.Windows.Media.Animation;

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
    
    public class Constants
    {
        public enum InfoBarPosition
        {
            Top,
            TopRight,
            //TopLeft,
            Bottom,
            BottomRight,
            //BottomLeft,
            None
        }

        public enum CreateInstanceDataType
        {
            Filename,
            CommandLine,
            Number,
            String,
            Path,
            List,
            Array,
            Struct
        }

        public struct MinecraftLoaderVersion
        {
            public string MCVersion;
            public string LoaderVersion;
        }

        public class CreateInstanceData
        {
            public CreateInstanceDataType Type { get; set; }
            public dynamic? Data { get; set; }
        }
    }
}
