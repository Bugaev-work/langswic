using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FastSwitcher {
sealed class TrayMenuRenderer : ToolStripProfessionalRenderer {
    readonly bool dark;
    Color Surface{get{return dark?Color.FromArgb(30,36,48):Color.FromArgb(250,251,254);}}
    Color Ink{get{return dark?Color.FromArgb(234,240,251):Color.FromArgb(35,43,64);}}
    public TrayMenuRenderer(bool dark){this.dark=dark;RoundedEdges=false;}
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e){e.Graphics.Clear(Surface);}
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e){using(var pen=new Pen(dark?Color.FromArgb(64,76,98):Color.FromArgb(221,227,239)))e.Graphics.DrawRectangle(pen,0,0,e.ToolStrip.Width-1,e.ToolStrip.Height-1);}
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e){}
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e){
        if(e.Item.Selected){using(var brush=new SolidBrush(dark?Color.FromArgb(48,72,110):Color.FromArgb(230,240,255)))e.Graphics.FillRectangle(brush,new Rectangle(6,2,e.Item.Width-12,e.Item.Height-4));}
    }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e){e.TextColor=e.Item.Enabled?Ink:(dark?Color.FromArgb(161,176,199):Color.FromArgb(109,121,148));base.OnRenderItemText(e);}
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e){using(var pen=new Pen(dark?Color.FromArgb(59,70,89):Color.FromArgb(224,230,240)))e.Graphics.DrawLine(pen,12,e.Item.Height/2,e.Item.Width-12,e.Item.Height/2);}
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e){
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var pen=new Pen(Color.FromArgb(36,113,243),2)){var r=e.ImageRectangle;e.Graphics.DrawLines(pen,new[]{new Point(r.X+2,r.Y+8),new Point(r.X+6,r.Y+12),new Point(r.X+14,r.Y+3)});}
    }
}
}
