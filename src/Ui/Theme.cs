// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - identidad visual. Tema oscuro propio, dibujado a mano porque no hay
// disenador de formularios en este proyecto (se compila con csc directo).
//
// Aqui vive todo lo que se ve: la paleta, las formas redondeadas y las fabricas de controles.
// Las fuentes se crean una sola vez (antes se creaba una nueva en cada etiqueta, y cada
// creacion deja un objeto GDI detras). Y los controles con caracter propio se dibujan ellos
// mismos, de forma que la ventana se vea igual con y sin estilos visuales de Windows: la
// autocomprobacion monta la interfaz sin ellos, y una revision que se mira sin estilos vale
// justo para lo que se ve en pantalla.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Forms;

namespace NetForge.Ui
{
    public static class Theme
    {
        // ------------------------------------------------------------------ paleta

        // Lienzo y superficies, de mas oscuro a mas claro.
        public static readonly Color Background = Color.FromArgb(13, 17, 23);
        public static readonly Color Surface = Color.FromArgb(22, 27, 34);
        public static readonly Color SurfaceAlt = Color.FromArgb(17, 22, 29);
        public static readonly Color SurfaceHigh = Color.FromArgb(30, 38, 49);
        public static readonly Color Border = Color.FromArgb(48, 56, 68);
        public static readonly Color BorderSoft = Color.FromArgb(38, 46, 57);
        public static readonly Color Hover = Color.FromArgb(35, 44, 56);
        public static readonly Color Pressed = Color.FromArgb(45, 56, 70);

        // Texto. Cada uno de estos colores se comprueba con su contraste real en la
        // autocomprobacion: un gris bonito sobre el papel equivocado deja la ventana ilegible.
        public static readonly Color Text = Color.FromArgb(230, 237, 243);
        public static readonly Color TextMuted = Color.FromArgb(150, 159, 170);
        public static readonly Color TextDim = Color.FromArgb(138, 147, 159);
        public static readonly Color OnAccent = Color.White;

        // Acento y sus estados.
        public static readonly Color Accent = Color.FromArgb(88, 166, 255);
        public static readonly Color AccentHover = Color.FromArgb(120, 185, 255);
        public static readonly Color AccentPressed = Color.FromArgb(64, 140, 226);
        public static readonly Color AccentSoft = Color.FromArgb(24, 46, 76);
        public static readonly Color AccentFill = Color.FromArgb(33, 126, 50);
        public static readonly Color AccentFillHover = Color.FromArgb(28, 112, 45);
        public static readonly Color AccentFillPressed = Color.FromArgb(22, 92, 38);

        // Estados, con su version suave para fondos de aviso.
        public static readonly Color Success = Color.FromArgb(63, 185, 80);
        public static readonly Color SuccessSoft = Color.FromArgb(22, 48, 33);
        public static readonly Color Warning = Color.FromArgb(210, 153, 34);
        public static readonly Color WarningSoft = Color.FromArgb(58, 45, 20);
        public static readonly Color Danger = Color.FromArgb(248, 81, 73);
        public static readonly Color DangerSoft = Color.FromArgb(44, 22, 22);
        public static readonly Color DangerFill = Color.FromArgb(139, 43, 43);
        public static readonly Color DangerFillHover = Color.FromArgb(160, 50, 50);
        public static readonly Color DangerFillPressed = Color.FromArgb(118, 36, 36);

        // Tablas.
        public static readonly Color GridAlt = Color.FromArgb(19, 24, 31);
        public static readonly Color GridHover = Color.FromArgb(31, 42, 55);
        public static readonly Color GridSelection = Color.FromArgb(31, 58, 94);

        // ------------------------------------------------------------------ tipografia

        // Fuentes compartidas: se crean una vez y viven lo que vive el proceso. Los controles
        // de Windows no se apropian del tipo de letra que se les asigna, asi que compartirlas
        // no deja controles rotos al liberar una ventana.
        private static readonly Font CachedBody = new Font("Segoe UI", 9f);
        private static readonly Font CachedSmall = new Font("Segoe UI", 8.25f);
        private static readonly Font CachedBodyStrong = new Font("Segoe UI Semibold", 9f);
        private static readonly Font CachedSection = new Font("Segoe UI Semibold", 9.75f);
        private static readonly Font CachedTitle = new Font("Segoe UI Semibold", 14f);
        private static readonly Font CachedState = new Font("Segoe UI Semibold", 9.75f);
        private static readonly Font CachedMono = new Font("Consolas", 8.75f);
        private static readonly Font CachedValue = new Font("Segoe UI Semibold", 12.5f);

        public static Font BodyFont
        {
            get { return CachedBody; }
        }

        public static Font SmallFont
        {
            get { return CachedSmall; }
        }

        public static Font StrongFont
        {
            get { return CachedBodyStrong; }
        }

        public static Font SectionFont
        {
            get { return CachedSection; }
        }

        public static Font TitleFont
        {
            get { return CachedTitle; }
        }

        public static Font StateFont
        {
            get { return CachedState; }
        }

        public static Font MonoFont
        {
            get { return CachedMono; }
        }

        /// <summary>Cifra grande: el numero de una tarjeta de resumen.</summary>
        public static Font ValueFont
        {
            get { return CachedValue; }
        }

        // ------------------------------------------------------------------ color

        /// <summary>
        /// Luminancia relativa segun la definicion de accesibilidad (WCAG 2.1). Se usa para
        /// comprobar de verdad si un texto se lee sobre su fondo, en vez de confiar en el ojo.
        /// </summary>
        public static double RelativeLuminance(Color color)
        {
            double r = Channel(color.R);
            double g = Channel(color.G);
            double b = Channel(color.B);
            return (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
        }

        /// <summary>Relacion de contraste entre dos colores, de 1 (invisible) a 21 (blanco y negro).</summary>
        public static double Contrast(Color first, Color second)
        {
            double a = RelativeLuminance(first);
            double b = RelativeLuminance(second);
            double lighter = Math.Max(a, b);
            double darker = Math.Min(a, b);
            return (lighter + 0.05) / (darker + 0.05);
        }

        private static double Channel(int value)
        {
            double v = value / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        /// <summary>Mezcla dos colores. Cantidad 0 devuelve el primero y 1 el segundo.</summary>
        public static Color Blend(Color from, Color to, double amount)
        {
            if (amount <= 0)
            {
                return from;
            }

            if (amount >= 1)
            {
                return to;
            }

            return Color.FromArgb(
                (int)Math.Round(from.R + ((to.R - from.R) * amount)),
                (int)Math.Round(from.G + ((to.G - from.G) * amount)),
                (int)Math.Round(from.B + ((to.B - from.B) * amount)));
        }

        // ------------------------------------------------------------------ formas

        /// <summary>Rectangulo redondeado. Con radio cero o con un hueco de menos, un rectangulo normal.</summary>
        public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (radius <= 0 || bounds.Width <= radius * 2 || bounds.Height <= radius * 2)
            {
                path.AddRectangle(bounds);
                return path;
            }

            int diameter = radius * 2;
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRounded(Graphics graphics, Rectangle bounds, int radius, Color color)
        {
            if (graphics == null || color.A == 0 || bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            using (GraphicsPath path = RoundedRect(bounds, radius))
            using (SolidBrush brush = new SolidBrush(color))
            {
                graphics.FillPath(brush, path);
            }
        }

        public static void StrokeRounded(Graphics graphics, Rectangle bounds, int radius, Color color)
        {
            if (graphics == null || bounds.Width <= 1 || bounds.Height <= 1)
            {
                return;
            }

            Rectangle inner = new Rectangle(bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
            using (GraphicsPath path = RoundedRect(inner, radius))
            using (Pen pen = new Pen(color))
            {
                graphics.DrawPath(pen, path);
            }
        }

        /// <summary>Prepara un lienzo para dibujar curvas y texto sin dientes de sierra.</summary>
        public static void Prepare(Graphics graphics)
        {
            if (graphics == null)
            {
                return;
            }

            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }

        // ------------------------------------------------------------------ estructura

        public static void StyleForm(Form form)
        {
            form.BackColor = Background;
            form.ForeColor = Text;
            form.Font = BodyFont;
            form.MinimumSize = new Size(760, 560);
            form.Size = new Size(980, 700);
            form.StartPosition = FormStartPosition.CenterScreen;
            EnableDoubleBuffer(form);
            form.AutoScaleMode = AutoScaleMode.Dpi;
        }

        public static Label Section(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.ForeColor = Accent;
            label.Font = SectionFont;
            label.Margin = new Padding(0, 6, 0, 6);
            return label;
        }

        public static Label Caption(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.ForeColor = TextMuted;
            label.Margin = new Padding(0, 8, 8, 0);
            return label;
        }

        /// <summary>Explicacion secundaria: dim, con salto de linea y sin robar protagonismo.</summary>
        public static Label Note(string text, int width)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.MaximumSize = new Size(width, 0);
            label.ForeColor = TextMuted;
            label.Margin = new Padding(0, 4, 0, 6);
            return label;
        }

        public static void StyleInput(Control control)
        {
            control.BackColor = SurfaceAlt;
            control.ForeColor = Text;
            control.Font = BodyFont;
        }

        /// <summary>Tarjeta: panel con esquinas redondeadas y borde suave para agrupar contenido.</summary>
        public static CardPanel Card()
        {
            return new CardPanel();
        }

        public static Button Primary(string text)
        {
            FlatButton button = new FlatButton();
            button.Text = text;
            button.Kind = ButtonKind.Primary;
            return button;
        }

        public static Button Secondary(string text)
        {
            FlatButton button = new FlatButton();
            button.Text = text;
            button.Kind = ButtonKind.Secondary;
            return button;
        }

        public static Button DangerButton(string text)
        {
            FlatButton button = new FlatButton();
            button.Text = text;
            button.Kind = ButtonKind.Danger;
            return button;
        }

        /// <summary>
        /// Pone icono a un boton. Se hace asi y no con una fabrica nueva para que el boton se
        /// cree y se describa en el mismo sitio, y para poder cambiar el icono despues (el boton
        /// de arrancar lleva el triangulo o el cuadrado segun este la red).
        /// </summary>
        public static void SetGlyph(Button button, GlyphKind glyph)
        {
            FlatButton flat = button as FlatButton;
            if (flat != null)
            {
                flat.Glyph = glyph;
            }
        }

        public static CheckBox Check(string text)
        {
            FlatCheckBox box = new FlatCheckBox();
            box.Text = text;
            return box;
        }

        public static TextBox Input(int width)
        {
            TextBox box = new TextBox();
            box.Width = width;
            StyleInput(box);
            box.Margin = new Padding(0, 5, 12, 4);
            return box;
        }

        public static ComboBox Combo(int width)
        {
            ComboBox combo = new ComboBox();
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.Width = width;
            StyleInput(combo);
            combo.Margin = new Padding(0, 4, 12, 4);
            return combo;
        }

        public static LinkLabel Link(string text)
        {
            LinkLabel link = new LinkLabel();
            link.Text = text;
            link.AutoSize = true;
            link.Font = BodyFont;
            link.ForeColor = Accent;
            link.LinkColor = Accent;
            link.ActiveLinkColor = AccentHover;
            link.VisitedLinkColor = Accent;
            link.LinkBehavior = LinkBehavior.HoverUnderline;
            link.Margin = new Padding(0, 4, 0, 4);
            return link;
        }

        /// <summary>Pildora de estado: punto de color y texto sobre un fondo suave redondeado.</summary>
        public static StatusPill Pill(string text)
        {
            StatusPill pill = new StatusPill();
            pill.Text = text;
            return pill;
        }

        /// <summary>Marca de la casa para la cabecera.</summary>
        public static LogoMark Logo()
        {
            return new LogoMark();
        }

        /// <summary>Tarjeta de resumen: una cifra grande, su etiqueta y un punto de color.</summary>
        public static StatTile Tile(string caption)
        {
            StatTile tile = new StatTile();
            tile.Caption = caption;
            return tile;
        }

        public static DataGridView Grid()
        {
            DataGridView grid = new DataGridView();
            grid.BackgroundColor = SurfaceAlt;
            grid.BorderStyle = BorderStyle.None;
            grid.GridColor = BorderSoft;
            grid.EnableHeadersVisualStyles = false;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersHeight = 32;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Surface;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = TextMuted;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Surface;
            grid.ColumnHeadersDefaultCellStyle.Font = StrongFont;
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
            grid.DefaultCellStyle.BackColor = SurfaceAlt;
            grid.DefaultCellStyle.ForeColor = Text;
            grid.DefaultCellStyle.SelectionBackColor = GridSelection;
            grid.DefaultCellStyle.SelectionForeColor = Text;
            grid.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
            grid.AlternatingRowsDefaultCellStyle.BackColor = GridAlt;
            grid.RowHeadersVisible = false;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.ReadOnly = true;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.RowTemplate.Height = 28;
            grid.ShowCellToolTips = true;
            grid.ScrollBars = ScrollBars.Both;

            // El rectangulo de puntos del foco es un detalle de la era Windows 95: se quita, y
            // en su lugar la fila elegida se marca con color.
            grid.RowPrePaint += OnGridRowPrePaint;

            // El radar repinta la tabla cada pocos segundos y sin doble buffer se ve el
            // parpadeo. La propiedad es protegida, asi que se activa por reflexion.
            EnableDoubleBuffer(grid);
            GridRowHover.Attach(grid);
            return grid;
        }

        private static void OnGridRowPrePaint(object sender, DataGridViewRowPrePaintEventArgs e)
        {
            e.PaintParts &= ~DataGridViewPaintParts.Focus;
        }

        /// <summary>
        /// Activa el doble buffer de un control cuya propiedad es protegida. Si la version de
        /// Windows Forms no la expone, se sigue funcionando igual: solo se pierde el pulido.
        /// </summary>
        public static void EnableDoubleBuffer(Control control)
        {
            if (control == null)
            {
                return;
            }

            try
            {
                PropertyInfo property = typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
                if (property != null)
                {
                    property.SetValue(control, true, null);
                }
            }
            catch (Exception)
            {
                // Un control con parpadeo es mejor que una ventana que no arranca.
            }
        }

        /// <summary>Cuadricula del panel de control.</summary>
        public static FlatTabControl Tabs()
        {
            return new FlatTabControl();
        }

        public static TabPage Page(string text)
        {
            TabPage page = new TabPage(text);
            page.BackColor = Background;
            page.ForeColor = Text;
            page.Padding = new Padding(12);
            page.UseVisualStyleBackColor = false;
            return page;
        }

        // ------------------------------------------------------------------ avisos

        private static ToolTip _tip;

        /// <summary>
        /// Aviso emergente oscuro. El de Windows es claro y desentona; con dibujo propio se
        /// pinta con la paleta de la casa. Un mismo objeto atiende a todos los controles.
        /// </summary>
        public static void Tip(Control control, string text)
        {
            if (control == null || string.IsNullOrEmpty(text))
            {
                return;
            }

            if (_tip == null)
            {
                _tip = new ToolTip();
                _tip.OwnerDraw = true;
                _tip.BackColor = SurfaceHigh;
                _tip.ForeColor = Text;
                _tip.InitialDelay = 450;
                _tip.ReshowDelay = 120;
                _tip.AutoPopDelay = 20000;
                _tip.ShowAlways = false;
                _tip.Draw += OnTipDraw;
                _tip.Popup += OnTipPopup;
            }

            _tip.SetToolTip(control, text);
        }

        private const int TipMaxWidth = 340;

        private static void OnTipPopup(object sender, PopupEventArgs e)
        {
            Size size = TextRenderer.MeasureText(
                e.AssociatedControl == null ? string.Empty : _tip.GetToolTip(e.AssociatedControl),
                BodyFont,
                new Size(TipMaxWidth, 2000),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            e.ToolTipSize = new Size(size.Width + 20, size.Height + 14);
        }

        private static void OnTipDraw(object sender, DrawToolTipEventArgs e)
        {
            Graphics graphics = e.Graphics;
            Prepare(graphics);
            Rectangle bounds = new Rectangle(0, 0, e.Bounds.Width, e.Bounds.Height);
            FillRounded(graphics, bounds, 6, SurfaceHigh);
            StrokeRounded(graphics, bounds, 6, Border);
            TextRenderer.DrawText(
                graphics,
                e.ToolTipText,
                BodyFont,
                new Rectangle(bounds.X + 10, bounds.Y + 7, bounds.Width - 20, bounds.Height - 14),
                Text,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }
    }
}
