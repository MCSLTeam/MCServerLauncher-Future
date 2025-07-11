using iNKORE.UI.WPF.Modern.Controls;
using iNKORE.UI.WPF.Controls;
using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Collections.Generic;
using MCServerLauncher.WPF.Modules;

namespace MCServerLauncher.WPF.View.Components.Generic
{
    /// <summary>
    ///     NotificationContainer.xaml 的交互逻辑
    /// </summary>
    public partial class NotificationContainer
    {
        private static NotificationContainer? _instance;
        private const int notificationMargin = 5;
        private const int animationDuration = 200;
        
        // 为每个位置创建单独的面板集合
        private Dictionary<Constants.InfoBarPosition, SimpleStackPanel> _positionPanels = new Dictionary<Constants.InfoBarPosition, SimpleStackPanel>();

        public static NotificationContainer Instance
        {
            get
            {
                _instance ??= new NotificationContainer();
                return _instance;
            }
        }

        private NotificationContainer()
        {
            InitializeComponent();
            InitializePanels();
        }
        
        private void InitializePanels()
        {
            // 为每个位置创建一个StackPanel
            foreach (Constants.InfoBarPosition position in Enum.GetValues(typeof(Constants.InfoBarPosition)))
            {
                if (position == Constants.InfoBarPosition.None) continue;
                
                var panel = new SimpleStackPanel();
                ConfigurePanelForPosition(panel, position);
                
                // 将面板添加到主Grid
                MainGrid.Children.Add(panel);
                _positionPanels[position] = panel;
            }
        }
        
        private void ConfigurePanelForPosition(SimpleStackPanel panel, Constants.InfoBarPosition position)
        {
            // 设置面板的对齐方式
            switch (position)
            {
                case Constants.InfoBarPosition.Top:
                    panel.HorizontalAlignment = HorizontalAlignment.Center;
                    panel.VerticalAlignment = VerticalAlignment.Top;
                    break;
                case Constants.InfoBarPosition.TopRight:
                    panel.HorizontalAlignment = HorizontalAlignment.Right;
                    panel.VerticalAlignment = VerticalAlignment.Top;
                    break;
                //case Constants.InfoBarPosition.TopLeft:
                //    panel.HorizontalAlignment = HorizontalAlignment.Left;
                //    panel.VerticalAlignment = VerticalAlignment.Top;
                //    break;
                case Constants.InfoBarPosition.Bottom:
                    panel.HorizontalAlignment = HorizontalAlignment.Center;
                    panel.VerticalAlignment = VerticalAlignment.Bottom;
                    break;
                case Constants.InfoBarPosition.BottomRight:
                    panel.HorizontalAlignment = HorizontalAlignment.Right;
                    panel.VerticalAlignment = VerticalAlignment.Bottom;
                    break;
                //case Constants.InfoBarPosition.BottomLeft:
                //    panel.HorizontalAlignment = HorizontalAlignment.Left;
                //    panel.VerticalAlignment = VerticalAlignment.Bottom;
                //    break;
            }
            
            // 设置面板的方向
            if (position == Constants.InfoBarPosition.Bottom || 
                //position == Constants.InfoBarPosition.BottomLeft || 
                position == Constants.InfoBarPosition.BottomRight)
            {
                panel.VerticalAlignment = VerticalAlignment.Bottom;
            }
            else
            {
                panel.VerticalAlignment = VerticalAlignment.Top;
            }
            
            // 设置面板的边距
            panel.Margin = new Thickness(10);
        }

        public static class EntranceHelper
        {
            public static void ApplyEntranceAnimation(UIElement element, Constants.InfoBarPosition position,
                double fromOpacity = 0, double toOpacity = 1,
                int durationMs = 200)
            {
                element.Opacity = fromOpacity;
                
                // 根据位置设置不同的动画起始点
                double fromX = 0;
                double fromY = 0;
                
                switch (position)
                {
                    case Constants.InfoBarPosition.Top:
                    //case Constants.InfoBarPosition.TopLeft:
                    case Constants.InfoBarPosition.TopRight:
                        fromY = -20;
                        break;
                    case Constants.InfoBarPosition.Bottom:
                    //case Constants.InfoBarPosition.BottomLeft:
                    case Constants.InfoBarPosition.BottomRight:
                        fromY = 20;
                        break;
                }
                
                element.RenderTransform = new TranslateTransform(fromX, fromY);

                var storyboard = new Storyboard();
                var fadeAnimation = new DoubleAnimation
                {
                    From = fromOpacity,
                    To = toOpacity,
                    Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                Storyboard.SetTarget(fadeAnimation, element);
                Storyboard.SetTargetProperty(fadeAnimation, new PropertyPath("Opacity"));

                var translateYAnimation = new DoubleAnimation
                {
                    From = fromY,
                    To = 0,
                    Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                Storyboard.SetTarget(translateYAnimation, element);
                Storyboard.SetTargetProperty(translateYAnimation,
                    new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));
                storyboard.Children.Add(translateYAnimation);

                storyboard.Children.Add(fadeAnimation);
                storyboard.Begin();
            }
        }

        public void AddNotification(InfoBar notification, Constants.InfoBarPosition position, int durationMs = 1500)
        {
            if (position == Constants.InfoBarPosition.None)
                position = Constants.InfoBarPosition.TopRight; // 默认位置
                
            // 获取对应位置的面板
            if (!_positionPanels.TryGetValue(position, out var panel))
                return;
                
            // 根据位置设置边距
            switch (position)
            {
                case Constants.InfoBarPosition.Top:
                //case Constants.InfoBarPosition.TopLeft:
                case Constants.InfoBarPosition.TopRight:
                    notification.Margin = new Thickness(0, 0, 0, notificationMargin);
                    break;
                case Constants.InfoBarPosition.Bottom:
                //case Constants.InfoBarPosition.BottomLeft:
                case Constants.InfoBarPosition.BottomRight:
                    notification.Margin = new Thickness(0, notificationMargin, 0, 0);
                    break;
            }
            
            // 为通知添加位置标记（用于后续移除时识别）
            notification.Tag = position;
            
            // 添加到对应位置的面板
            panel.Children.Add(notification);
            EntranceHelper.ApplyEntranceAnimation(notification, position);

            if (durationMs < 0)
                return; // 如果持续时间为负数，则不自动移除

            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(durationMs)
            };

            timer.Tick += (s, e) =>
            {
                timer.Stop();
                RemoveNotification(notification);
            };

            timer.Start();
        }

        public void RemoveNotification(InfoBar notification)
        {
            // 获取通知的位置
            var position = (Constants.InfoBarPosition)notification.Tag;
            
            // 获取对应位置的面板
            if (!_positionPanels.TryGetValue(position, out var panel))
                return;
                
            int index = panel.Children.IndexOf(notification);
            if (index < 0) return;
            
            double heightToRemove = notification.ActualHeight + notificationMargin;
            
            var fadeOut = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(animationDuration)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            
            // 根据位置设置不同的退出动画
            double toY = position == Constants.InfoBarPosition.Bottom || 
                         //position == Constants.InfoBarPosition.BottomLeft || 
                         position == Constants.InfoBarPosition.BottomRight ? 20 : -20;
            
            var translateOut = new DoubleAnimation
            {
                From = 0,
                To = toY,
                Duration = new Duration(TimeSpan.FromMilliseconds(animationDuration)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            
            if (notification.RenderTransform is not TranslateTransform)
            {
                notification.RenderTransform = new TranslateTransform();
            }
            
            var mainStoryboard = new Storyboard();
            
            Storyboard.SetTarget(fadeOut, notification);
            Storyboard.SetTargetProperty(fadeOut, new PropertyPath("Opacity"));
            mainStoryboard.Children.Add(fadeOut);
            
            Storyboard.SetTarget(translateOut, notification);
            Storyboard.SetTargetProperty(translateOut, 
                new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));
            mainStoryboard.Children.Add(translateOut);
            
            List<FrameworkElement> elementsToAnimate = new List<FrameworkElement>();
            
            // 根据位置确定需要移动的元素
            if (position == Constants.InfoBarPosition.Bottom || 
                //position == Constants.InfoBarPosition.BottomLeft || 
                position == Constants.InfoBarPosition.BottomRight)
            {
                // 底部位置时，移动前面的元素
                for (int i = 0; i < index; i++)
                {
                    if (panel.Children[i] is FrameworkElement element)
                    {
                        elementsToAnimate.Add(element);
                    }
                }
            }
            else
            {
                // 顶部位置时，移动后面的元素
                for (int i = index + 1; i < panel.Children.Count; i++)
                {
                    if (panel.Children[i] is FrameworkElement element)
                    {
                        elementsToAnimate.Add(element);
                    }
                }
            }
            
            foreach (var element in elementsToAnimate)
            {
                if (element.RenderTransform is not TranslateTransform)
                {
                    element.RenderTransform = new TranslateTransform();
                }
                
                var transform = (TranslateTransform)element.RenderTransform;
                double currentY = transform.Y;
                
                // 根据位置确定移动方向
                double targetY = position == Constants.InfoBarPosition.Bottom || 
                                 //position == Constants.InfoBarPosition.BottomLeft || 
                                 position == Constants.InfoBarPosition.BottomRight 
                                 ? currentY + heightToRemove : currentY - heightToRemove;
                
                var moveAnimation = new DoubleAnimation
                {
                    From = currentY,
                    To = targetY,
                    Duration = new Duration(TimeSpan.FromMilliseconds(animationDuration)),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                
                Storyboard.SetTarget(moveAnimation, transform);
                Storyboard.SetTargetProperty(moveAnimation, new PropertyPath(TranslateTransform.YProperty));
                
                mainStoryboard.Children.Add(moveAnimation);
            }
            
            mainStoryboard.Completed += (_, _) =>
            {
                panel.Children.Remove(notification);
            };
            mainStoryboard.Begin();
        }
    }
}
