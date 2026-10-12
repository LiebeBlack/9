// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - controles dibujados a mano.
//
// El proyecto se compila con csc directo, sin disenador de formularios, asi que los controles
// con caracter propio se escriben aqui y se pintan ellos mismos: botones redondeados con
// estados, casillas oscuras, tarjetas con borde, pildoras de estado, pestanas planas y la
// lista del registro con color por nivel.
//
// Se dibujan solos por dos razones: la ventana se ve igual con y sin estilos visuales de
// Windows (la autocomprobacion monta la interfaz sin ellos), y los controles de serie de
// Windows Forms son grises y claros, que es justo lo que este tema no quiere.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NetForge.Ui
{
    public enum ButtonKind
    {
        Primary,
        Secondary,
        Danger,
        Ghost
    }

    /// <summary>Iconos del programa. Se dibujan con lineas y curvas, sin ficheros ni fuentes.</summary>
    public enum GlyphKind
    {
        None,
        Play,
        Stop,
        Folder,
        Copy,
        Save,
        Refresh,
        Shield,
        Search,
        Clear,
        Check,
        Download
    }

    /// <summary>
    /// Dibujo de los iconos. Todos entran en una caja cuadrada y se trazan con el color del
    /// texto que acompanan, de forma que un boton deshabilitado tenga los iconos apagados sin
    /// trabajo extra. Las medidas se recortan a un minimo: una caja diminuta no puede lanzar una
    /// excepcion de GDI+ en mitad del pintado de una ventana.
    /// </summary>
    public static class Glyphs
    {
        public const int Size = 15;

        public static void Paint(Graphics graphics, GlyphKind kind, Rectangle box, Color ink)
        {
            if (graphics == null || kind == GlyphKind.None)
            {
                return;
            }

            int left = box.Left;
            int top = box.Top;
            int w = Math.Max(6, box.Width);
            int h = Math.Max(6, box.Height);
            int midX = left + (w / 2);
            int midY = top + (h / 2);

            Theme.Prepare(graphics);
            using (SolidBrush brush = new SolidBrush(ink))
            using (Pen pen = new Pen(ink, 1.6f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;

                switch (kind)
                {
                    case GlyphKind.Play:
                        graphics.FillPolygon(brush, new Point[]
                        {
                            new Point(left + 2, top + 1),
                            new Point(left + w - 3, midY),
                            new Point(left + 2, top + h - 1)
                        });
                        break;

                    case GlyphKind.Stop:
                        Theme.FillRounded(graphics, new Rectangle(left + 2, top + 2, w - 4, h - 4), 2, ink);
                        break;

                    case GlyphKind.Folder:
                        using (GraphicsPath path = new GraphicsPath())
                        {
                            int tab = Math.Max(3, w / 3);
                            int fold = Math.Max(3, h / 3);
                            path.AddLines(new Point[]
                            {
                                new Point(left + 1, top + h - 3),
                                new Point(left + 1, top + fold),
                                new Point(left + tab, top + fold),
                                new Point(left + tab + 2, top + 2),
                                new Point(left + w - 2, top + 2),
                                new Point(left + w - 2, top + h - 3),
                                new Point(left + 1, top + h - 3)
                            });
                            graphics.DrawPath(pen, path);
                        }

                        break;

                    case GlyphKind.Copy:
                        Theme.StrokeRounded(graphics, new Rectangle(left + 1, top + 1, Math.Max(4, w - 6), Math.Max(4, h - 6)), 2, ink);
                        Theme.StrokeRounded(graphics, new Rectangle(left + 5, top + 5, Math.Max(4, w - 6), Math.Max(4, h - 6)), 2, ink);
                        break;

                    case GlyphKind.Save:
                        Theme.StrokeRounded(graphics, new Rectangle(left + 1, top + 1, Math.Max(4, w - 2), Math.Max(4, h - 2)), 2, ink);
                        graphics.FillRectangle(brush, new Rectangle(left + 4, top + 1, Math.Max(1, w - 8), 4));
                        Theme.FillRounded(graphics, new Rectangle(left + 3, top + h - 6, Math.Max(1, w - 6), 4), 1, ink);
                        break;

                    case GlyphKind.Refresh:
                        graphics.DrawArc(pen, left + 1, top + 1, Math.Max(4, w - 4), Math.Max(4, h - 4), 45, 280);
                        graphics.FillPolygon(brush, new Point[]
                        {
                            new Point(left + w - 6, top),
                            new Point(left + w - 1, top + 2),
                            new Point(left + w - 5, top + 7)
                        });
                        break;

                    case GlyphKind.Shield:
                        using (GraphicsPath path = new GraphicsPath())
                        {
                            path.AddLines(new Point[]
                            {
                                new Point(midX, top + 1),
                                new Point(left + w - 2, top + 4),
                                new Point(left + w - 3, midY + 1),
                                new Point(midX, top + h - 1),
                                new Point(left + 3, midY + 1),
                                new Point(left + 2, top + 4),
                                new Point(midX, top + 1)
                            });
                            graphics.DrawPath(pen, path);
                        }

                        break;

                    case GlyphKind.Search:
                        graphics.DrawEllipse(pen, left + 1, top + 1, Math.Max(4, w - 7), Math.Max(4, h - 7));
                        graphics.DrawLine(pen, midX, midY + 1, left + w - 2, top + h - 2);
                        break;

                    case GlyphKind.Clear:
                        graphics.DrawLine(pen, left + 3, top + 3, left + w - 3, top + h - 3);
                        graphics.DrawLine(pen, left + w - 3, top + 3, left + 3, top + h - 3);
                        break;

                    case GlyphKind.Check:
                        graphics.DrawLines(pen, new Point[]
                        {
                            new Point(left + 2, midY),
                            new Point(midX - 1, top + h - 3),
                            new Point(left + w - 2, top + 2)
                        });
                        break;

                    case GlyphKind.Download:
                        graphics.DrawLine(pen, midX, top + 1, midX, top + h - 5);
                        graphics.FillPolygon(brush, new Point[]
                        {
                            new Point(midX - 4, top + h - 6),
                            new Point(midX + 5, top + h - 6),
                            new Point(midX, top + h - 1)
                        });
                        graphics.DrawLine(pen, left + 2, top + h - 1, left + w - 2, top + h - 1);
                        break;
                }
            }
        }
    }

    /// <summary>Boton con esquinas redondeadas, reaccion al raton y al teclado, y foco visible.</summary>
    public sealed class FlatButton : Button
    {
        private const int GlyphGap = 7;

        private bool _hover;
        private bool _pressed;
        private ButtonKind _kind = ButtonKind.Primary;
        private GlyphKind _glyph = GlyphKind.None;
        private int _radius = 6;

        public FlatButton()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            FlatAppearance.MouseOverBackColor = Color.Transparent;
            FlatAppearance.MouseDownBackColor = Color.Transparent;
            UseVisualStyleBackColor = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            MinimumSize = new Size(96, 30);
            Padding = new Padding(12, 0, 12, 0);
            Margin = new Padding(4, 6, 4, 4);
            TextAlign = ContentAlignment.MiddleCenter;
            Cursor = Cursors.Hand;
            ForeColor = Theme.Text;
        }

        public ButtonKind Kind
        {
            get { return _kind; }
            set
            {
                _kind = value;
                Invalidate();
            }
        }

        public int Radius
        {
            get { return _radius; }
            set
            {
                _radius = value;
                Invalidate();
            }
        }

        /// <summary>Icono a la izquierda del texto.</summary>
        public GlyphKind Glyph
        {
            get { return _glyph; }
            set
            {
                _glyph = value;
                PerformLayout();
                Invalidate();
            }
        }

        /// <summary>
        /// El ancho que necesita el boton incluye el icono y su hueco: sin esto, el rotulo mas
        /// el icono no caben y Windows los recortaria con puntos suspensivos.
        /// </summary>
        public override Size GetPreferredSize(Size proposedSize)
        {
            Size size = base.GetPreferredSize(proposedSize);
            if (_glyph != GlyphKind.None)
            {
                size = new Size(size.Width + Glyphs.Size + GlyphGap, size.Height);
            }

            int width = Math.Max(size.Width, MinimumSize.Width);
            int height = Math.Max(size.Height, MinimumSize.Height);
            return new Size(width, height);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics graphics = pevent.Graphics;
            Theme.Prepare(graphics);
            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillRounded(graphics, bounds, _radius, ResolveFill());

            bool outlined = _kind == ButtonKind.Secondary || _kind == ButtonKind.Ghost;
            if (outlined)
            {
                Theme.StrokeRounded(graphics, bounds, _radius, _hover || Focused ? Theme.Border : Theme.BorderSoft);
            }

            Color ink = !Enabled
                ? Theme.TextDim
                : (outlined ? (_kind == ButtonKind.Ghost && !_hover ? Theme.TextMuted : Theme.Text) : Theme.OnAccent);

            if (Focused && Enabled)
            {
                Theme.StrokeRounded(graphics, Rectangle.Inflate(bounds, -3, -3), _radius, Theme.Accent);
            }

            if (_glyph == GlyphKind.None)
            {
                TextRenderer.DrawText(
                    graphics,
                    Text,
                    Font,
                    bounds,
                    ink,
                    TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis |
                    TextFormatFlags.NoPrefix);
                return;
            }

            // Con icono, el contenido se centra como un bloque: icono, hueco y texto.
            Size textSize = TextRenderer.MeasureText(
                Text ?? string.Empty,
                Font,
                new Size(4000, Math.Max(1, Height)),
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            int total = Glyphs.Size + GlyphGap + textSize.Width;
            int start = bounds.X + Math.Max(10, (bounds.Width - total) / 2);
            int glyphTop = bounds.Y + ((bounds.Height - Glyphs.Size) / 2);
            Glyphs.Paint(graphics, _glyph, new Rectangle(start, glyphTop, Glyphs.Size, Glyphs.Size), ink);

            int textLeft = start + Glyphs.Size + GlyphGap;
            TextRenderer.DrawText(
                graphics,
                Text,
                Font,
                new Rectangle(textLeft, bounds.Y, Math.Max(1, bounds.Right - textLeft - 3), bounds.Height),
                ink,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
        }

        private Color ResolveFill()
        {
            if (!Enabled)
            {
                return Theme.SurfaceAlt;
            }

            switch (_kind)
            {
                case ButtonKind.Primary:
                    return _pressed ? Theme.AccentFillPressed : (_hover ? Theme.AccentFillHover : Theme.AccentFill);
                case ButtonKind.Danger:
                    return _pressed ? Theme.DangerFillPressed : (_hover ? Theme.DangerFillHover : Theme.DangerFill);
                case ButtonKind.Ghost:
                    return _pressed ? Theme.Pressed : (_hover ? Theme.Hover : Color.Transparent);
                default:
                    return _pressed ? Theme.Pressed : (_hover ? Theme.Hover : Theme.Surface);
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            if (mevent.Button == MouseButtons.Left)
            {
                _pressed = true;
                Invalidate();
            }

            base.OnMouseDown(mevent);
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(mevent);
        }

        protected override void OnKeyDown(KeyEventArgs kevent)
        {
            if (kevent.KeyCode == Keys.Space || kevent.KeyCode == Keys.Enter)
            {
                _pressed = true;
                Invalidate();
            }

            base.OnKeyDown(kevent);
        }

        protected override void OnKeyUp(KeyEventArgs kevent)
        {
            _pressed = false;
            Invalidate();
            base.OnKeyUp(kevent);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }
    }

    /// <summary>
    /// Interruptor: una pista redondeada con su bola y el texto al lado. Sigue siendo una casilla
    /// de Windows Forms (misma propiedad Checked, mismo evento), asi que el cambio es solo de
    /// aspecto: en la pantalla se ve un interruptor y en el codigo una casilla con su etiqueta
    /// traducida, con la misma comprobacion de idioma encima.
    /// </summary>
    public sealed class FlatCheckBox : CheckBox
    {
        private const int TrackWidth = 32;
        private const int TrackHeight = 18;
        private const int KnobInset = 2;
        private const int Gap = 9;
        private bool _hover;
        private bool _pressed;

        public FlatCheckBox()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            UseVisualStyleBackColor = false;
            AutoSize = true;
            ForeColor = Theme.Text;
            Font = Theme.BodyFont;
            Margin = new Padding(4, 7, 14, 4);
            Cursor = Cursors.Hand;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size text = TextRenderer.MeasureText(
                Text,
                Font,
                new Size(1000, 1000),
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            return new Size(TrackWidth + Gap + text.Width + 2, Math.Max(TrackHeight + 4, text.Height + 4));
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics graphics = pevent.Graphics;
            Theme.Prepare(graphics);

            int top = (Height - TrackHeight) / 2;
            if (top < 0)
            {
                top = 0;
            }

            Rectangle track = new Rectangle(0, top, TrackWidth, TrackHeight);
            Color trackFill = Checked ? Theme.AccentFill : Theme.SurfaceHigh;
            Color trackEdge = Checked ? Theme.AccentFill : (_hover && Enabled ? Theme.Accent : Theme.Border);
            if (_pressed && Enabled)
            {
                trackFill = Checked ? Theme.AccentFillPressed : Theme.Pressed;
                trackEdge = Theme.Accent;
            }
            else if (!Enabled)
            {
                trackFill = Theme.SurfaceAlt;
                trackEdge = Theme.BorderSoft;
            }

            Theme.FillRounded(graphics, track, TrackHeight / 2, trackFill);
            Theme.StrokeRounded(graphics, track, TrackHeight / 2, trackEdge);

            int knob = TrackHeight - (KnobInset * 2);
            int knobLeft = Checked
                ? track.Right - knob - KnobInset
                : track.Left + KnobInset;
            Color knobInk = !Enabled ? Theme.TextDim : (Checked ? Theme.OnAccent : Theme.Text);
            using (SolidBrush knobBrush = new SolidBrush(knobInk))
            {
                graphics.FillEllipse(knobBrush, knobLeft, track.Top + KnobInset, knob, knob);
            }

            if (Focused && Enabled)
            {
                Theme.StrokeRounded(graphics, Rectangle.Inflate(track, 2, 2), (TrackHeight / 2) + 2, Theme.Accent);
            }

            Color ink = Enabled ? Theme.Text : Theme.TextDim;
            TextRenderer.DrawText(
                graphics,
                Text,
                Font,
                new Rectangle(track.Right + Gap, 0, Math.Max(1, Width - track.Right - Gap), Height),
                ink,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            if (mevent.Button == MouseButtons.Left)
            {
                _pressed = true;
                Invalidate();
            }

            base.OnMouseDown(mevent);
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(mevent);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }
    }

    /// <summary>Tarjeta: panel con esquinas redondeadas y borde suave para agrupar contenido.</summary>
    public sealed class CardPanel : Panel
    {
        private int _radius = 10;
        private Color _border = Theme.BorderSoft;
        private bool _stripe;

        public CardPanel()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
            ForeColor = Theme.Text;
            Padding = new Padding(14, 10, 14, 12);
            Margin = new Padding(0, 0, 0, 10);
        }

        public int Radius
        {
            get { return _radius; }
            set
            {
                _radius = value;
                Invalidate();
            }
        }

        public Color BorderColor
        {
            get { return _border; }
            set
            {
                _border = value;
                Invalidate();
            }
        }

        /// <summary>Filo de color en el borde superior, para las tarjetas que encabezan una pantalla.</summary>
        public bool AccentStripe
        {
            get { return _stripe; }
            set
            {
                _stripe = value;
                Invalidate();
            }
        }

        /// <summary>
        /// El tamano que necesita la tarjeta es el de su contenido mas el relleno: un panel normal
        /// no calcula eso, y sin esto una tarjeta con AutoSize se quedaria en nada.
        ///
        /// Cuando el contenedor ya dice cuanto ancho hay, la tarjeta lo aprovecha entero: si
        /// devolviera solo el ancho de su contenido, una tarjeta con AutoSize quedaria mas
        /// estrecha que la ventana y se veria un hueco a la derecha.
        /// </summary>
        public override Size GetPreferredSize(Size proposedSize)
        {
            int width = 0;
            int height = 0;
            int available = proposedSize.Width > 0 ? Math.Max(10, proposedSize.Width - Padding.Horizontal) : 0;
            foreach (Control child in Controls)
            {
                Size size = child.GetPreferredSize(new Size(available, 0));
                if (size.Width > width)
                {
                    width = size.Width;
                }

                height += size.Height + child.Margin.Vertical;
            }

            if (available > 0)
            {
                width = available;
            }

            return new Size(width + Padding.Horizontal, height + Padding.Vertical);
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            // Fill with the parent's background so the corners of the rounded card
            // don't show the default system color underneath.
            Color behind = Parent != null ? Parent.BackColor : Theme.Background;
            using (SolidBrush brush = new SolidBrush(behind))
            {
                pevent.Graphics.FillRectangle(brush, ClientRectangle);
            }
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Theme.Prepare(pevent.Graphics);
            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillRounded(pevent.Graphics, bounds, _radius, BackColor);
            Theme.StrokeRounded(pevent.Graphics, bounds, _radius, _border);

            if (_stripe)
            {
                Rectangle line = new Rectangle(bounds.X + 3, bounds.Y + 2, Math.Max(1, bounds.Width - 6), 3);
                using (LinearGradientBrush brush = new LinearGradientBrush(line, Theme.Accent, Theme.AccentFill, 0f))
                {
                    pevent.Graphics.FillRectangle(brush, line);
                }
            }
        }
    }

    /// <summary>Pildora de estado: punto de color y texto sobre un fondo redondeado.</summary>
    public sealed class StatusPill : Control
    {
        private Color _dot = Theme.TextMuted;
        private Color _fill = Theme.SurfaceHigh;
        private Color _ink = Theme.Text;
        private bool _showDot = true;
        private Padding _inner = new Padding(9, 4, 11, 4);

        public StatusPill()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
            ForeColor = Theme.Text;
            Font = Theme.BodyFont;
            AutoSize = true;
            Margin = new Padding(6, 4, 6, 4);
            Cursor = Cursors.Default;
        }

        public Color DotColor
        {
            get { return _dot; }
            set
            {
                _dot = value;
                Invalidate();
            }
        }

        /// <summary>Color de la capsula. Con alfa cero no se dibuja fondo: solo punto y texto.</summary>
        public Color FillColor
        {
            get { return _fill; }
            set
            {
                _fill = value;
                Invalidate();
            }
        }

        public Color InkColor
        {
            get { return _ink; }
            set
            {
                _ink = value;
                Invalidate();
            }
        }

        public bool ShowDot
        {
            get { return _showDot; }
            set
            {
                _showDot = value;
                Invalidate();
            }
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size text = TextRenderer.MeasureText(
                Text ?? string.Empty,
                Font,
                new Size(2000, 2000),
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            int width = text.Width + _inner.Horizontal;
            if (_showDot)
            {
                width += 14;
            }

            return new Size(width, Math.Max(22, text.Height + _inner.Vertical));
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            ResizeToText();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            ResizeToText();
        }

        private void ResizeToText()
        {
            if (AutoSize)
            {
                Size = GetPreferredSize(Size.Empty);
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics graphics = pevent.Graphics;
            Theme.Prepare(graphics);

            // Clear with the parent's background so resizes and text changes don't leave
            // ghost pixels behind.
            Color behind = Parent != null ? Parent.BackColor : BackColor;
            using (SolidBrush bg = new SolidBrush(behind))
            {
                graphics.FillRectangle(bg, ClientRectangle);
            }

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            if (_fill.A > 0)
            {
                Theme.FillRounded(graphics, bounds, Height / 2, _fill);
            }

            int left = bounds.X + _inner.Left;
            if (_showDot)
            {
                int dot = 8;
                int dotTop = bounds.Y + ((bounds.Height - dot) / 2);
                using (SolidBrush brush = new SolidBrush(_dot))
                {
                    graphics.FillEllipse(brush, new Rectangle(left, dotTop, dot, dot));
                }

                left += dot + 6;
            }

            Rectangle text = new Rectangle(left, bounds.Y, Math.Max(0, bounds.Right - left - _inner.Right + 2), bounds.Height);
            TextRenderer.DrawText(
                graphics,
                Text ?? string.Empty,
                Font,
                text,
                _ink,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>
    /// Tarjeta de resumen: una cifra grande, su etiqueta y un punto de color. Es el panel de un
    /// vistazo de la pestana de clientes, y al pulsarla lleva a la pestana de la que habla.
    /// </summary>
    public sealed class StatTile : Control
    {
        private string _caption = string.Empty;
        private Color _dot = Theme.Accent;
        private bool _hover;

        public StatTile()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Size = new Size(160, 62);
            Margin = new Padding(0, 0, 10, 0);
            Cursor = Cursors.Hand;
            TabStop = false;
        }

        /// <summary>Etiqueta de la tarjeta. La cifra o el estado van en Text.</summary>
        public string Caption
        {
            get { return _caption; }
            set
            {
                _caption = value ?? string.Empty;
                Invalidate();
            }
        }

        public Color DotColor
        {
            get { return _dot; }
            set
            {
                _dot = value;
                Invalidate();
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics graphics = pevent.Graphics;
            Theme.Prepare(graphics);
            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillRounded(graphics, bounds, 9, _hover ? Theme.Hover : Theme.Surface);
            Theme.StrokeRounded(graphics, bounds, 9, _hover ? Theme.Border : Theme.BorderSoft);

            // El punto va arriba a la derecha para no pelear con la cifra.
            using (SolidBrush dot = new SolidBrush(_dot))
            {
                graphics.FillEllipse(dot, bounds.Right - 20, bounds.Top + 12, 8, 8);
            }

            TextRenderer.DrawText(
                graphics,
                Text ?? string.Empty,
                Theme.ValueFont,
                new Rectangle(bounds.Left + 12, bounds.Top + 8, Math.Max(1, bounds.Width - 44), 26),
                Theme.Text,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);

            TextRenderer.DrawText(
                graphics,
                _caption,
                Theme.SmallFont,
                new Rectangle(bounds.Left + 12, bounds.Top + 34, Math.Max(1, bounds.Width - 22), 18),
                Theme.TextMuted,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>
    /// Pestanas planas: la elegida se marca con fondo y barra de acento, las demas en gris.
    /// El ancho se calcula con el texto real para que las seis quepan en la ventana mas
    /// pequena sin flechas de desplazamiento.
    /// </summary>
    public sealed class FlatTabControl : TabControl
    {
        private const int StripHeight = 34;
        private int _hoverIndex = -1;

        public FlatTabControl()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            DrawMode = TabDrawMode.OwnerDrawFixed;
            SizeMode = TabSizeMode.Fixed;
            Appearance = TabAppearance.Normal;
            Multiline = false;
            ItemSize = new Size(120, StripHeight);
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = Theme.StrongFont;
            DrawItem += OnTabDraw;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            FitTabs();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            FitTabs();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            FitTabs();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = HitTest(e.Location);
            if (index != _hoverIndex)
            {
                _hoverIndex = index;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverIndex >= 0)
            {
                _hoverIndex = -1;
                Invalidate();
            }
        }

        private int HitTest(Point point)
        {
            for (int i = 0; i < TabPages.Count; i++)
            {
                if (GetTabRect(i).Contains(point))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Reparte el ancho disponible entre las pestanas segun el texto de cada una.</summary>
        public void FitTabs()
        {
            if (TabPages.Count == 0 || IsDisposed)
            {
                return;
            }

            int widest = 0;
            foreach (TabPage page in TabPages)
            {
                Size size = TextRenderer.MeasureText(
                    page.Text,
                    Font,
                    new Size(2000, StripHeight),
                    TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                if (size.Width > widest)
                {
                    widest = size.Width;
                }
            }

            int width = widest + 36;
            int available = ClientSize.Width - 6;
            if (available > 0 && width * TabPages.Count > available)
            {
                width = Math.Max(64, available / TabPages.Count);
            }

            Size size2 = new Size(width, StripHeight);
            if (ItemSize != size2)
            {
                ItemSize = size2;
                Invalidate();
            }
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            // The area behind the tab strip would show the system default (light gray)
            // without this override. Fill the entire control with the dark background first,
            // then the tab drawing paints each tab on top.
            using (SolidBrush brush = new SolidBrush(Theme.Background))
            {
                pevent.Graphics.FillRectangle(brush, ClientRectangle);
            }
        }

        private void OnTabDraw(object sender, DrawItemEventArgs e)
        {
            Graphics graphics = e.Graphics;
            Theme.Prepare(graphics);
            TabPage page = TabPages[e.Index];
            bool selected = e.Index == SelectedIndex;
            bool hovered = _hoverIndex == e.Index && !selected;
            Rectangle bounds = GetTabRect(e.Index);
            bool first = e.Index == 0;

            Color back = selected ? Theme.Surface : (hovered ? Theme.Hover : Theme.Background);
            Rectangle shape = new Rectangle(
                bounds.X + (first ? 0 : 1),
                bounds.Y + 3,
                bounds.Width - 3,
                bounds.Height - 3);
            Theme.FillRounded(graphics, shape, 8, back);

            if (selected)
            {
                Theme.FillRounded(
                    graphics,
                    new Rectangle(shape.X + 12, shape.Bottom - 3, Math.Max(6, shape.Width - 24), 3),
                    2,
                    Theme.Accent);
            }

            Color ink = selected ? Theme.Text : (hovered ? Theme.Text : Theme.TextMuted);
            TextRenderer.DrawText(
                graphics,
                page.Text,
                Font,
                shape,
                ink,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>
    /// Marca de la casa: un cuadrado redondeado con degradado y el simbolo de la red. Da
    /// identidad a la cabecera sin depender de ningun fichero de imagen externo.
    /// </summary>
    public sealed class LogoMark : Control
    {
        private const int Size_ = 34;

        public LogoMark()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            Size = new Size(Size_, Size_);
            Margin = new Padding(0, 2, 12, 0);
            TabStop = false;
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics graphics = pevent.Graphics;
            Theme.Prepare(graphics);
            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);

            using (GraphicsPath path = Theme.RoundedRect(bounds, 9))
            {
                using (LinearGradientBrush brush = new LinearGradientBrush(
                    bounds,
                    Theme.Accent,
                    Color.FromArgb(37, 99, 235),
                    55f))
                {
                    graphics.FillPath(brush, path);
                }

                using (Pen edge = new Pen(Color.FromArgb(70, 255, 255, 255)))
                {
                    graphics.DrawPath(edge, path);
                }
            }

            int centerX = Width / 2;
            int baseY = Height - 10;
            using (SolidBrush dot = new SolidBrush(Color.White))
            {
                graphics.FillEllipse(dot, centerX - 2, baseY - 2, 5, 5);
            }

            using (Pen pen = new Pen(Color.White, 1.8f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                graphics.DrawArc(pen, centerX - 5, baseY - 5, 10, 10, 215, 110);
                graphics.DrawArc(pen, centerX - 9, baseY - 9, 18, 18, 215, 110);
                graphics.DrawArc(pen, centerX - 13, baseY - 13, 26, 26, 215, 110);
            }
        }
    }

    /// <summary>
    /// Resalta la fila que esta bajo el raton en una tabla. Se aplica por fila porque el cambio
    /// de color de una celda suelta dejaria la fila a cuadros.
    /// </summary>
    public static class GridRowHover
    {
        public static void Attach(DataGridView grid)
        {
            if (grid == null)
            {
                return;
            }

            grid.CellMouseEnter += OnEnter;
            grid.CellMouseLeave += OnLeave;
        }

        private static void OnEnter(object sender, DataGridViewCellEventArgs e)
        {
            DataGridView grid = sender as DataGridView;
            if (grid == null || e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count)
            {
                return;
            }

            DataGridViewRow row = grid.Rows[e.RowIndex];
            if (row.Selected)
            {
                return;
            }

            row.DefaultCellStyle.BackColor = Theme.GridHover;
        }

        private static void OnLeave(object sender, DataGridViewCellEventArgs e)
        {
            DataGridView grid = sender as DataGridView;
            if (grid == null || e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count)
            {
                return;
            }

            // Vaciar el color devuelve la fila a su turno en el rayado par/impar.
            grid.Rows[e.RowIndex].DefaultCellStyle.BackColor = Color.Empty;
        }
    }

    /// <summary>Gravedad de una linea del registro, deducida del texto (sirve en los dos idiomas).</summary>
    public enum LogKind
    {
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// Lista del registro con una marca de color por gravedad. El registro de verdad guarda
    /// lineas de texto libre, asi que la gravedad se deduce de las palabras que usa el propio
    /// proyecto, en espanol y en ingles.
    /// </summary>
    public sealed class LogList : ListBox
    {
        private static readonly string[] ErrorSignals = new string[]
        {
            "no se pudo", "could not", "failed", "fallo", "error", "excepcion",
            "exception", "denegado", "denied", "rechaz", "refused", "agotado", "timeout",
            "se rinde", "gave up", "sin red", "no fue posible"
        };

        private static readonly string[] WarningSignals = new string[]
        {
            "aviso", "warning", "omitid", "skip", "reintent", "retry", "sin permisos",
            "no administrator", "cancelad", "cancelled", "canceled", "parcial"
        };

        public LogList()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = 18;
            IntegralHeight = false;
            BorderStyle = BorderStyle.None;
            Font = Theme.MonoFont;
            BackColor = Theme.SurfaceAlt;
            ForeColor = Theme.Text;
            DrawItem += OnDrawItemLine;
        }

        /// <summary>Gravedad de una linea del registro. Publica para poder comprobarla.</summary>
        public static LogKind Classify(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return LogKind.Info;
            }

            string lowered = text.ToLowerInvariant();
            foreach (string signal in ErrorSignals)
            {
                if (lowered.IndexOf(signal, StringComparison.Ordinal) >= 0)
                {
                    return LogKind.Error;
                }
            }

            foreach (string signal in WarningSignals)
            {
                if (lowered.IndexOf(signal, StringComparison.Ordinal) >= 0)
                {
                    return LogKind.Warning;
                }
            }

            return LogKind.Info;
        }

        public static Color InkFor(LogKind kind)
        {
            switch (kind)
            {
                case LogKind.Error:
                    return Theme.Danger;
                case LogKind.Warning:
                    return Theme.Warning;
                default:
                    return Theme.TextMuted;
            }
        }

        private void OnDrawItemLine(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count)
            {
                return;
            }

            object item = Items[e.Index];
            string text = item == null ? string.Empty : item.ToString();
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

            Color back = selected ? Theme.GridSelection : (e.Index % 2 == 0 ? Theme.SurfaceAlt : Theme.GridAlt);
            using (SolidBrush brush = new SolidBrush(back))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }

            LogKind kind = Classify(text);
            Color ink = InkFor(kind);
            if (selected)
            {
                ink = Theme.Text;
            }

            using (SolidBrush stripe = new SolidBrush(kind == LogKind.Info ? Theme.BorderSoft : ink))
            {
                e.Graphics.FillRectangle(stripe, new Rectangle(e.Bounds.X, e.Bounds.Y + 3, 3, Math.Max(1, e.Bounds.Height - 6)));
            }

            TextRenderer.DrawText(
                e.Graphics,
                text,
                Font,
                new Rectangle(e.Bounds.X + 10, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 14), e.Bounds.Height),
                ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
}
