using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TATimesheet
{
    /// <summary>On-screen report chart: the shared chart layout, themed, with a tooltip per bar.</summary>
    public class BarChart : Control
    {
        Report report;
        List<ReportRow> rows = new List<ReportRow>();
        List<DrawOp> ops = new List<DrawOp>();
        int hover = -1;
        readonly ToolTip tip = new ToolTip { InitialDelay = 0, ReshowDelay = 0, UseAnimation = false, UseFading = false };

        public BarChart()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        public Report Report
        {
            get { return report; }
            set
            {
                report = value;
                rows = value == null ? new List<ReportRow>() : ChartLayout.ChartRows(value);
                hover = -1;
                Relayout();
            }
        }

        float FontPx { get { return Font.SizeInPoints * DeviceDpi() / 72f; } }

        float DeviceDpi()
        {
            using (var g = CreateGraphics()) return g.DpiX;
        }

        void Relayout()
        {
            ops = report == null ? new List<DrawOp>()
                : ChartLayout.Build(report, 8, 6, Math.Max(50, Width - 16), Math.Max(50, Height - 12), FontPx);
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (IsHandleCreated) Relayout();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Relayout();
        }

        static Color ScreenColor(Ink ink)
        {
            var t = Theme.Current;
            switch (ink)
            {
                case Ink.Muted: return t.Muted;
                case Ink.Grid: return t.GridLine;
                case Ink.Bar: return t.Chart;
                case Ink.TravelBar: return t.ChartTravel;
                default: return t.Text;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Current;
            var g = e.Graphics;
            g.Clear(t.Surface);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // Hover: a soft band behind the whole slot, so the target is bigger than the mark.
            var bar = hover < 0 ? null : ops.OfType<FillOp>().FirstOrDefault(f => f.Index == hover);
            if (bar != null)
                using (var band = new SolidBrush(t.Dark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(18, 0, 0, 0)))
                    g.FillRectangle(band, HitRect(bar));

            LayoutPrintDocument.Draw(g, ops, null, ScreenColor, "Segoe UI", true);
        }

        /// <summary>The whole slot around a bar, so the hover target is bigger than the mark.</summary>
        RectangleF HitRect(FillOp f)
        {
            if (f.Column)
                return new RectangleF(f.X + f.W / 2 - f.Slot / 2, 0, f.Slot, Height);
            return new RectangleF(0, f.Y + f.H / 2 - f.Slot / 2, Width, f.Slot);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int found = -1;
            foreach (var f in ops.OfType<FillOp>())
                if (f.Index >= 0 && HitRect(f).Contains(e.Location)) { found = f.Index; break; }
            if (found == hover) return;
            hover = found;
            Invalidate();
            if (found < 0 || found >= rows.Count) { tip.Hide(this); return; }
            var r = rows[found];
            string text = string.Format("{0}\n{1} h total\n{2} h on site, {3} h travel\n{4} day{5} worked", r.Label,
                TimeUtil.FormatHours(r.Hours), TimeUtil.FormatHours(r.OnSiteHours), TimeUtil.FormatHours(r.TravelHours),
                r.Days, r.Days == 1 ? "" : "s");
            tip.Show(text, this, e.X + 14, e.Y + 14);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = -1;
            tip.Hide(this);
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tip.Dispose();
            base.Dispose(disposing);
        }
    }
}
