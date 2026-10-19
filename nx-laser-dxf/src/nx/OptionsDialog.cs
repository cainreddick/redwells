// Run-time options dialog (Windows Forms), pre-filled from the config.
// Changes apply to this run only; the config file is not modified.
// This file deliberately doesn't import NXOpen, so WinForms names can't clash with it.
namespace LaserDxf.Nx
{
    using System;
    using System.Drawing;
    using System.Windows.Forms;
    using LaserDxf.Core;

    public static class OptionsDialog
    {
        /// <summary>Shows the dialog and applies the choices to cfg. Returns false if cancelled.
        /// batch hides the instruction-sheet option (the batch journal writes none).</summary>
        public static bool Show(LaserConfig cfg, string partLabel, bool batch = false)
        {
            using (var f = new Form())
            {
                f.Text = "Laser DXF - " + partLabel;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MaximizeBox = false;
                f.MinimizeBox = false;
                f.ShowInTaskbar = false;
                f.StartPosition = FormStartPosition.CenterScreen;
                f.TopMost = true;
                f.Font = new Font("Segoe UI", 9f);
                f.ClientSize = new Size(440, 400);

                int y = 10;
                RadioButton[] orient = Group(f, "Orientation (DXF X axis)", ref y,
                    "Along the longest straight edge (edge at the bottom)", "Along the WCS X axis");
                RadioButton[] place = Group(f, "Part number position", ref y,
                    "Automatic (largest clear area)", "Pick a point on the plate");
                RadioButton[] text = Group(f, "Text output (labels and part number)", ref y,
                    "Single-stroke vectors (for etching)", "DXF TEXT entities");

                orient[cfg.XAlign == XAlignMode.Hint ? 1 : 0].Checked = true;
                place[string.Equals(cfg.EtchPlacement, "Pick", StringComparison.OrdinalIgnoreCase) ? 1 : 0].Checked = true;
                text[cfg.TextMode == TextMode.Text ? 1 : 0].Checked = true;

                var verLabel = new Label { Text = "DXF version", Left = 14, Top = y + 4, Width = 90 };
                var version = new ComboBox { Left = 110, Top = y, Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
                version.Items.Add("R2000");
                version.Items.Add("R12");
                version.SelectedIndex = cfg.DxfVersion == DxfVersion.R12 ? 1 : 0;
                f.Controls.Add(verLabel);
                f.Controls.Add(version);
                y += 34;

                var folderLabel = new Label { Text = "Output folder", Left = 14, Top = y + 4, Width = 90 };
                var folder = new TextBox { Left = 110, Top = y, Width = 270, Text = cfg.OutputFolder };
                var browse = new Button { Text = "...", Left = 386, Top = y - 1, Width = 40, Height = 25 };
                browse.Click += delegate
                {
                    using (var fb = new FolderBrowserDialog { SelectedPath = folder.Text, Description = "Folder for the DXF files" })
                        if (fb.ShowDialog(f) == DialogResult.OK) folder.Text = fb.SelectedPath;
                };
                f.Controls.Add(folderLabel);
                f.Controls.Add(folder);
                f.Controls.Add(browse);
                y += 44;

                var preview = new CheckBox { Text = "Preview and adjust the text before writing the DXF", Left = 14, Top = y, Width = 416, Checked = cfg.ShowPreview };
                f.Controls.Add(preview);
                y += 26;
                var cutPdf = new CheckBox { Text = "Also write the DXF as a 1:1 PDF (default button in the preview)", Left = 14, Top = y, Width = 416, Checked = cfg.WriteCutPdf };
                f.Controls.Add(cutPdf);
                y += 26;
                var pdf = new CheckBox { Text = "Also write the PDF instruction sheet for the laser shop", Left = 14, Top = y, Width = 416, Checked = cfg.WritePdf };
                if (!batch)
                {
                    f.Controls.Add(pdf);
                    y += 26;
                }
                y += 6;

                var ok = new Button { Text = "Continue", Left = 250, Top = y, Width = 85, Height = 28, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Cancel", Left = 341, Top = y, Width = 85, Height = 28, DialogResult = DialogResult.Cancel };
                f.Controls.Add(ok);
                f.Controls.Add(cancel);
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                f.ClientSize = new Size(440, y + 40);

                if (f.ShowDialog() != DialogResult.OK) return false;

                cfg.XAlign = orient[1].Checked ? XAlignMode.Hint : XAlignMode.LongestEdge;
                cfg.EtchPlacement = place[1].Checked ? "Pick" : "Auto";
                cfg.TextMode = text[1].Checked ? TextMode.Text : TextMode.Stroke;
                cfg.DxfVersion = version.SelectedIndex == 1 ? DxfVersion.R12 : DxfVersion.R2000;
                if (folder.Text.Trim().Length > 0) cfg.OutputFolder = folder.Text.Trim();
                cfg.ShowPreview = preview.Checked;
                cfg.WriteCutPdf = cutPdf.Checked;
                if (!batch) cfg.WritePdf = pdf.Checked;
                return true;
            }
        }

        static RadioButton[] Group(Form f, string title, ref int y, string first, string second)
        {
            var g = new GroupBox { Text = title, Left = 10, Top = y, Width = 420, Height = 72 };
            var a = new RadioButton { Text = first, Left = 12, Top = 20, Width = 390 };
            var b = new RadioButton { Text = second, Left = 12, Top = 44, Width = 390 };
            g.Controls.Add(a);
            g.Controls.Add(b);
            f.Controls.Add(g);
            y += 80;
            return new[] { a, b };
        }
    }
}
