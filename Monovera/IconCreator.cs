using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

namespace Monovera
{
    /// <summary>
    /// Helper class to create simple icon images
    /// </summary>
    public static class IconCreator
    {
        public static void CreateRobotIcon(string filePath, int size = 24)
        {
            using var bitmap = new Bitmap(size, size);
            using var g = Graphics.FromImage(bitmap);
            
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            // Robot head (rounded rectangle)
            using var headBrush = new SolidBrush(Color.FromArgb(100, 149, 237)); // Cornflower blue
            var headRect = new RectangleF(size * 0.2f, size * 0.25f, size * 0.6f, size * 0.5f);
            g.FillRoundedRectangle(headBrush, headRect, size * 0.1f);

            // Eyes
            using var eyeBrush = new SolidBrush(Color.White);
            g.FillEllipse(eyeBrush, size * 0.3f, size * 0.35f, size * 0.15f, size * 0.15f);
            g.FillEllipse(eyeBrush, size * 0.55f, size * 0.35f, size * 0.15f, size * 0.15f);

            // Antenna
            using var antennaPen = new Pen(Color.FromArgb(100, 149, 237), 2);
            g.DrawLine(antennaPen, size * 0.5f, size * 0.25f, size * 0.5f, size * 0.1f);
            g.FillEllipse(headBrush, size * 0.45f, size * 0.05f, size * 0.1f, size * 0.1f);

            // Body
            var bodyRect = new RectangleF(size * 0.25f, size * 0.7f, size * 0.5f, size * 0.15f);
            g.FillRoundedRectangle(headBrush, bodyRect, size * 0.05f);

            bitmap.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);
        }

        public static void CreateWheelIcon(string filePath, int size = 24)
        {
            using var bitmap = new Bitmap(size, size);
            using var g = Graphics.FromImage(bitmap);
            
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var center = size / 2f;
            var radius = size * 0.4f;

            // Outer circle
            using var wheelBrush = new SolidBrush(Color.FromArgb(255, 152, 0)); // Orange
            g.FillEllipse(wheelBrush, center - radius, center - radius, radius * 2, radius * 2);

            // Inner circle (hub)
            using var hubBrush = new SolidBrush(Color.White);
            var hubRadius = radius * 0.3f;
            g.FillEllipse(hubBrush, center - hubRadius, center - hubRadius, hubRadius * 2, hubRadius * 2);

            // Spokes
            using var spokePen = new Pen(Color.White, 2);
            for (int i = 0; i < 6; i++)
            {
                double angle = Math.PI * 2 * i / 6;
                float x1 = center + hubRadius * (float)Math.Cos(angle);
                float y1 = center + hubRadius * (float)Math.Sin(angle);
                float x2 = center + radius * (float)Math.Cos(angle);
                float y2 = center + radius * (float)Math.Sin(angle);
                g.DrawLine(spokePen, x1, y1, x2, y2);
            }

            bitmap.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);
        }

        private static void FillRoundedRectangle(this Graphics g, Brush brush, RectangleF rect, float radius)
        {
            using var path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, radius * 2, radius * 2, 180, 90);
            path.AddArc(rect.Right - radius * 2, rect.Y, radius * 2, radius * 2, 270, 90);
            path.AddArc(rect.Right - radius * 2, rect.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
            path.AddArc(rect.X, rect.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
            path.CloseFigure();
            g.FillPath(brush, path);
        }
    }
}
