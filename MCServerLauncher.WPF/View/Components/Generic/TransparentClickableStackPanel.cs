using System.Windows.Media.Imaging;
using System.Windows.Media;
using iNKORE.UI.WPF.Controls;
using System.Windows;

namespace MCServerLauncher.WPF.View.Components.Generic
{
    public class TransparentClickableStackPanel : SimpleStackPanel
    {
        protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
        {
            Point point = TranslatePoint(hitTestParameters.HitPoint, this);

            foreach (UIElement child in Children)
            {
                HitTestResult result = VisualTreeHelper.HitTest(child, point);
                if (result != null && IsOpaqueAtPoint(child, point))
                {
                    return new PointHitTestResult(this, point);
                }
            }
            return null;
        }

        private bool IsOpaqueAtPoint(UIElement element, Point point)
        {
            if (element.RenderSize.Width <= 0 || element.RenderSize.Height <= 0)
            {
                return false;
            }

            Point relativePoint = element.TranslatePoint(point, this);
            if (relativePoint.X < 0 || relativePoint.Y < 0 ||
                relativePoint.X >= element.RenderSize.Width || relativePoint.Y >= element.RenderSize.Height)
            {
                return false;
            }

            RenderTargetBitmap bitmap = new RenderTargetBitmap(
                (int)element.RenderSize.Width,
                (int)element.RenderSize.Height,
                96,
                96,
                PixelFormats.Pbgra32
            );

            DrawingVisual visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                VisualBrush vb = new VisualBrush(element);
                dc.DrawRectangle(vb, null, new Rect(new Point(), element.RenderSize));
            }
            bitmap.Render(visual);

            FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            byte[] pixels = new byte[(int)element.RenderSize.Width * (int)element.RenderSize.Height * 4];
            formatConvertedBitmap.CopyPixels(pixels, (int)element.RenderSize.Width * 4, 0);

            int pixelIndex = ((int)relativePoint.Y * (int)element.RenderSize.Width + (int)relativePoint.X) * 4;

            if (pixelIndex >= 0 && pixelIndex < pixels.Length)
            {
                byte alpha = pixels[pixelIndex + 3];
                return alpha >= 128;
            }

            return false;
        }
    }
}
