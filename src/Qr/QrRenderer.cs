// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - dibujo del QR en pantalla.
using System.Drawing;
using System.Drawing.Drawing2D;

namespace NetForge.Qr
{
    public static class QrRenderer
    {
        public const int QuietZoneModules = 4;

        /// <summary>
        /// Convierte la matriz en imagen. La zona de silencio (4 modulos) es obligatoria:
        /// sin ella muchos lectores de movil no enganchan el codigo, y el fallo parece
        /// aleatorio cuando en realidad es geometria.
        /// </summary>
        public static Bitmap Render(QrCode code, int moduleSize, Color dark, Color light)
        {
            int side = code.Size + QuietZoneModules * 2;
            int pixels = side * moduleSize;
            Bitmap bitmap = new Bitmap(pixels, pixels);

            try
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.SmoothingMode = SmoothingMode.None;
                    graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                    graphics.PixelOffsetMode = PixelOffsetMode.Half;
                    graphics.Clear(light);

                    using (SolidBrush brush = new SolidBrush(dark))
                    {
                        for (int row = 0; row < code.Size; row++)
                        {
                            for (int column = 0; column < code.Size; column++)
                            {
                                if (!code[row, column])
                                {
                                    continue;
                                }

                                int x = (column + QuietZoneModules) * moduleSize;
                                int y = (row + QuietZoneModules) * moduleSize;
                                graphics.FillRectangle(brush, x, y, moduleSize, moduleSize);
                            }
                        }
                    }
                }

                return bitmap;
            }
            catch (System.Exception)
            {
                bitmap.Dispose();
                throw;
            }
        }

        public static Bitmap Render(string text, QrEcc ecc, int moduleSize, Color dark, Color light)
        {
            return Render(QrEncoder.Encode(text, ecc), moduleSize, dark, light);
        }
    }
}
