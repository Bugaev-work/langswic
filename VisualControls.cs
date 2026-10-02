using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FastSwitcher {
internal sealed class BlueHeader : Panel {
    public BlueHeader(){DoubleBuffered=true;AccessibleName="langswic — автоматическое переключение RU / EN";}
    protected override void OnPaintBackground(PaintEventArgs e){
        var area=ClientRectangle;if(area.Width<1||area.Height<1)return;
        using(var gradient=new LinearGradientBrush(area,Color.FromArgb(69,155,255),Color.FromArgb(103,153,249),18f))e.Graphics.FillRectangle(gradient,area);
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using(var path=new GraphicsPath()){
            path.AddBezier(-80,15,Width/3,Height+80,Width/2,-75,Width+70,Height+65);
            path.AddLine(Width+70,Height+65,-80,Height+65);path.CloseFigure();
            using(var brush=new SolidBrush(Color.FromArgb(42,11,101,238)))e.Graphics.FillPath(brush,path);
        }
        BrandLogo.Draw(e.Graphics,new RectangleF(Width/2-180,18,66,66));
        using(var font=new Font("Segoe UI",30,FontStyle.Bold))TextRenderer.DrawText(e.Graphics,"LANGSWIC",font,new Rectangle(Width/2-103,23,300,57),Color.White,TextFormatFlags.Left|TextFormatFlags.VerticalCenter);
        using(var font=new Font("Segoe UI",11))TextRenderer.DrawText(e.Graphics,"Автоматическое переключение RU / EN",font,new Rectangle(0,80,Width,31),Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
    }
}
internal sealed class FeatureTile : CheckBox {
    public int Symbol;
    public Color Accent,Surface,Ink,Muted;
    public FeatureTile(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);Appearance=Appearance.Button;FlatStyle=FlatStyle.Flat;Cursor=Cursors.Hand;AccessibleRole=AccessibleRole.CheckButton;}
    protected override void OnPaint(PaintEventArgs e){
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(BackColor);
        int size=104,x=(Width-size)/2,y=13;
        using(var path=Round(new Rectangle(x,y,size,size),24))using(var brush=new SolidBrush(Surface))g.FillPath(brush,path);
        using(var pen=new Pen(Accent,6)){pen.StartCap=pen.EndCap=LineCap.Round;pen.LineJoin=LineJoin.Round;
            if(Symbol==0){using(var font=new Font("Segoe UI",39,FontStyle.Bold))TextRenderer.DrawText(g,"A",font,new Rectangle(x+16,y+15,65,63),Accent,TextFormatFlags.NoPadding);g.DrawLines(pen,new[]{new Point(x+61,y+66),new Point(x+72,y+77),new Point(x+88,y+54)});}
            else if(Symbol==1){g.DrawLines(pen,new[]{new Point(x+34,y+46),new Point(x+52,y+27),new Point(x+71,y+46)});g.DrawLine(pen,x+52,y+28,x+52,y+77);}
            else{Star(g,Accent,x+64,y+39,19);Star(g,Accent,x+35,y+67,15);}
        }
        using(var font=new Font("Segoe UI Semibold",13))TextRenderer.DrawText(g,Text,font,new Rectangle(0,128,Width,34),Ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
        TextRenderer.DrawText(g,(Symbol==2?"Опечатки и ё · ":"")+(Checked?"Включено":"Выключено"),Font,new Rectangle(0,164,Width,25),Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
        int sx=(Width-40)/2,sy=196;
        using(var path=Round(new Rectangle(sx,sy,40,22),11))using(var brush=new SolidBrush(Checked?Accent:Color.FromArgb(161,169,185)))g.FillPath(brush,path);
        using(var brush=new SolidBrush(Color.White))g.FillEllipse(brush,sx+(Checked?21:3),sy+3,16,16);
        if(Focused)ControlPaint.DrawFocusRectangle(g,new Rectangle(6,3,Width-12,Height-6),Ink,BackColor);
    }
    static void Star(Graphics g,Color color,int x,int y,int size){var points=new[]{new Point(x,y-size),new Point(x+5,y-5),new Point(x+size,y),new Point(x+5,y+5),new Point(x,y+size),new Point(x-5,y+5),new Point(x-size,y),new Point(x-5,y-5)};using(var brush=new SolidBrush(color))g.FillPolygon(brush,points);}
    static GraphicsPath Round(Rectangle r,int radius){var path=new GraphicsPath();int d=radius*2;path.AddArc(r.X,r.Y,d,d,180,90);path.AddArc(r.Right-d,r.Y,d,d,270,90);path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);path.AddArc(r.X,r.Bottom-d,d,d,90,90);path.CloseFigure();return path;}
}
}
