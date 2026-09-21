// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - identidad visual. Tema oscuro propio, dibujado a mano porque no hay
// disenador de formularios en este proyecto (se compila con csc directo).
using System.Drawing;
using System.Windows.Forms;

namespace NetForge.Ui
{
    public static class Theme
    {
        public static readonly Color Background = Color.FromArgb(13, 17, 23);
        public static readonly Color Surface = Color.FromArgb(22, 27, 34);
        public static readonly Color SurfaceAlt = Color.FromArgb(17, 22, 29);
        public static readonly Color Border = Color.FromArgb(48, 54, 61);
        public static readonly Color Text = Color.FromArgb(230, 237, 243);
        public static readonly Color TextMuted = Color.FromArgb(139, 148, 158);
        public static readonly Color Accent = Color.FromArgb(88, 166, 255);
        public static readonly Color Success = Color.FromArgb(63, 185, 80);
        public static readonly Color Warning = Color.FromArgb(210, 153, 34);
        public static readonly Color Danger = Color.FromArgb(248, 81, 73);
        public static readonly Color AccentFill = Color.FromArgb(35, 134, 54);

        public static Font BodyFont
        {
            get { return new Font("Segoe UI", 9f); }
        }

        public static Font TitleFont
        {
            get { return new Font("Segoe UI Semibold", 13f); }
        }

        public static Font MonoFont
        {
            get { return new Font("Consolas", 8.75f); }
        }

        public static void StyleForm(Form form)
        {
            form.BackColor = Background;
            form.ForeColor = Text;
            form.Font = BodyFont;
            form.MinimumSize = new Size(760, 560);
            form.StartPosition = FormStartPosition.CenterScreen;
        }

        public static Label Section(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.ForeColor = Accent;
            label.Font = new Font("Segoe UI Semibold", 9.75f);
            label.Margin = new Padding(0, 6, 0, 4);
            return label;
        }

        public static Label Caption(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.ForeColor = TextMuted;
            label.Margin = new Padding(0, 8, 6, 0);
            return label;
        }

        public static void StyleInput(Control control)
        {
            control.BackColor = SurfaceAlt;
            control.ForeColor = Text;
            control.Font = BodyFont;
        }

        /// <summary>
        /// Boton de accion. El tamano se calcula a partir del texto: con un ancho fijo, un
        /// rotulo largo ("Banear (El Martillo)", "Guardar y aplicar") se partia en dos lineas
        /// dentro de la misma altura y el segundo renglon quedaba cortado. AutoSize con ancho
        /// minimo deja el texto en una linea y evita el boton de 75 pixeles de serie.
        /// </summary>
        public static Button Primary(string text)
        {
            Button button = new Button();
            button.Text = text;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = AccentFill;
            button.ForeColor = Color.White;
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.MinimumSize = new Size(96, 30);
            button.Padding = new Padding(10, 0, 10, 0);
            button.Margin = new Padding(4, 6, 4, 4);
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.UseVisualStyleBackColor = false;
            return button;
        }

        public static Button Secondary(string text)
        {
            Button button = Primary(text);
            button.BackColor = Surface;
            button.ForeColor = Text;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Border;
            return button;
        }

        public static Button DangerButton(string text)
        {
            Button button = Primary(text);
            button.BackColor = Color.FromArgb(139, 43, 43);
            return button;
        }

        public static DataGridView Grid()
        {
            DataGridView grid = new DataGridView();
            grid.BackgroundColor = Background;
            grid.BorderStyle = BorderStyle.None;
            grid.GridColor = Border;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Surface;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = TextMuted;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Surface;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9f);
            grid.DefaultCellStyle.BackColor = SurfaceAlt;
            grid.DefaultCellStyle.ForeColor = Text;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(31, 58, 94);
            grid.DefaultCellStyle.SelectionForeColor = Text;
            grid.RowHeadersVisible = false;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.ReadOnly = true;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.RowTemplate.Height = 26;
            return grid;
        }

        public static TabControl Tabs()
        {
            TabControl tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.Padding = new Point(14, 6);
            return tabs;
        }

        public static TabPage Page(string text)
        {
            TabPage page = new TabPage(text);
            page.BackColor = Background;
            page.ForeColor = Text;
            page.Padding = new Padding(10);
            return page;
        }

        public static CheckBox Check(string text)
        {
            CheckBox box = new CheckBox();
            box.Text = text;
            box.ForeColor = Text;
            box.AutoSize = true;
            box.Margin = new Padding(4, 8, 12, 4);
            return box;
        }

        public static TextBox Input(int width)
        {
            TextBox box = new TextBox();
            box.Width = width;
            StyleInput(box);
            box.Margin = new Padding(0, 4, 10, 4);
            return box;
        }
    }
}
