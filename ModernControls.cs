using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FastSwitcher {
internal static class ControlShape {
    public static GraphicsPath Round(Rectangle r,int radius){var p=new GraphicsPath();int d=Math.Min(radius*2,Math.Min(r.Width,r.Height));p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
    public static void Switch(Graphics g,Rectangle r,bool value,Color accent,Color off){
        using(var p=Round(r,r.Height/2))using(var b=new SolidBrush(value?accent:off))g.FillPath(b,p);
        int size=r.Height-6;using(var b=new SolidBrush(Color.White))g.FillEllipse(b,value?r.Right-size-3:r.X+3,r.Y+3,size,size);
    }
}
// The native editing surface retains caret, selection, clipboard and keyboard behavior.
// Its border and focus appearance are drawn by the surrounding control.
internal sealed class RoundedField : Panel {
    public readonly TextBox Editor;
    readonly ScrollRail scroll;
    public Color Surface,Outline,Accent;
    public RoundedField(){
        DoubleBuffered=true;TabStop=false;Height=40;Margin=new Padding(0,3,8,3);
        Editor=new TextBox{BorderStyle=BorderStyle.None,AutoSize=false,TabIndex=0};Controls.Add(Editor);
        scroll=new ScrollRail(Editor){Visible=false,TabStop=false};Controls.Add(scroll);
        Editor.GotFocus+=delegate{Invalidate();};Editor.LostFocus+=delegate{Invalidate();};
        Editor.TextChanged+=delegate{OnTextChanged(EventArgs.Empty);scroll.Invalidate();};
        Editor.KeyUp+=delegate{scroll.Invalidate();};Editor.MouseUp+=delegate{scroll.Invalidate();};Editor.MouseWheel+=delegate{if(IsHandleCreated)BeginInvoke((Action)(()=>scroll.Invalidate()));};
    }
    public override string Text {get{return Editor==null?"":Editor.Text;}set{if(Editor!=null)Editor.Text=value;}}
    public bool Multiline {get{return Editor.Multiline;}set{Editor.Multiline=value;Editor.AcceptsReturn=value;Arrange();}}
    public ScrollBars ScrollBars {get{return scroll.Visible?ScrollBars.Vertical:ScrollBars.None;}set{scroll.Visible=value==ScrollBars.Vertical||value==ScrollBars.Both;Arrange();}}
#if INPUT_TEST
    internal int ScrollPosition {get{return scroll.First;}}
    internal void ScrollToForTest(int line){scroll.ScrollTo(line);}
#endif
    public void Apply(Color surface,Color ink,Color outline,Color accent,string name){Surface=surface;Outline=outline;Accent=accent;Editor.BackColor=surface;Editor.ForeColor=ink;Editor.Font=Font;Editor.AccessibleName=name;AccessibleName=name;scroll.BackColor=surface;scroll.ForeColor=outline;Arrange();Invalidate();}
    void Arrange(){if(Editor==null)return;Editor.SetBounds(12,Editor.Multiline?10:Math.Max(4,(Height-Font.Height)/2),Math.Max(1,Width-(scroll!=null&&scroll.Visible?40:24)),Editor.Multiline?Math.Max(1,Height-20):Font.Height+2);if(scroll!=null){scroll.SetBounds(Width-23,10,13,Math.Max(1,Height-20));scroll.Invalidate();}}
    protected override void OnResize(EventArgs e){base.OnResize(e);Arrange();}
    protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);if(Editor!=null){Editor.Font=Font;Arrange();}}
    protected override void OnPaintBackground(PaintEventArgs e){
        var g=e.Graphics;g.Clear(Parent==null?Surface:Parent.BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;
        using(var p=ControlShape.Round(new Rectangle(1,1,Math.Max(1,Width-3),Math.Max(1,Height-3)),10)){
            using(var b=new SolidBrush(Surface))g.FillPath(b,p);using(var pen=new Pen(Editor.Focused?Accent:Outline,Editor.Focused?2:1))g.DrawPath(pen,p);
        }
    }
}
internal sealed class ScrollRail : Control {
    readonly Func<int> position,count,viewport;readonly Action<int> setPosition;int dragY,dragLine;bool dragging;
    [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr window,int message,IntPtr a,IntPtr b);
    public ScrollRail(Func<int> readPosition,Func<int> readCount,Func<int> readViewport,Action<int> writePosition){position=readPosition;count=readCount;viewport=readViewport;setPosition=writePosition;DoubleBuffered=true;Cursor=Cursors.Hand;AccessibleName="Прокрутка содержимого";AccessibleRole=AccessibleRole.ScrollBar;TabStop=false;}
    public ScrollRail(TextBox target):this(()=>target.IsHandleCreated?SendMessage(target.Handle,0xce,IntPtr.Zero,IntPtr.Zero).ToInt32():0,()=>target.IsHandleCreated?SendMessage(target.Handle,0xba,IntPtr.Zero,IntPtr.Zero).ToInt32():1,()=>target.Height/Math.Max(1,target.Font.Height),line=>{if(target.IsHandleCreated)SendMessage(target.Handle,0xb6,IntPtr.Zero,new IntPtr(line-SendMessage(target.Handle,0xce,IntPtr.Zero,IntPtr.Zero).ToInt32()));}){}
    internal int First {get{return Math.Max(0,position());}}
    int Count {get{return Math.Max(1,count());}}
    int Lines {get{return Math.Max(1,viewport());}}
    internal void ScrollTo(int line){setPosition(Math.Max(0,Math.Min(Math.Max(0,Count-Lines),line)));Invalidate();}
    Rectangle Thumb(){int count=Count;int height=Math.Min(Height,Math.Max(24,Height*Lines/count));int top=count<=Lines?0:(Height-height)*First/Math.Max(1,count-Lines);return new Rectangle(3,Math.Min(Height-height,top),7,height);}
    protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(BackColor);if(Count<=Lines)return;e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var p=ControlShape.Round(Thumb(),3))using(var b=new SolidBrush(ForeColor))e.Graphics.FillPath(b,p);}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;if(Thumb().Contains(e.Location)){dragging=true;Capture=true;dragY=e.Y;dragLine=First;}else ScrollTo(First+(e.Y<Thumb().Top?-Lines:Lines));}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(dragging)ScrollTo(dragLine+(e.Y-dragY)*Math.Max(1,Count-Lines)/Math.Max(1,Height-Thumb().Height));}
    protected override void OnMouseUp(MouseEventArgs e){dragging=false;Capture=false;base.OnMouseUp(e);}
    protected override void OnMouseCaptureChanged(EventArgs e){if(!Capture)dragging=false;base.OnMouseCaptureChanged(e);}
    protected override void OnMouseWheel(MouseEventArgs e){ScrollTo(First-e.Delta/120*3);base.OnMouseWheel(e);}
}
internal sealed class StyledList : ListBox {
    protected override CreateParams CreateParams {get{var p=base.CreateParams;p.Style&=~0x200000;return p;}}
}
internal sealed class SwitchCell : DataGridViewCheckBoxCell {
    protected override Rectangle GetContentBounds(Graphics graphics,DataGridViewCellStyle style,int rowIndex){var size=GetSize(rowIndex);return new Rectangle((size.Width-34)/2,(size.Height-20)/2,34,20);}
}
internal sealed class SwitchRow : CheckBox {
    public Color Accent,Muted;
    public SwitchRow(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);Cursor=Cursors.Hand;AccessibleRole=AccessibleRole.CheckButton;}
    protected override void OnPaint(PaintEventArgs e){
        var g=e.Graphics;g.Clear(BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;
        TextRenderer.DrawText(g,Text,Font,new Rectangle(2,0,Width-68,Height),ForeColor,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
        ControlShape.Switch(g,new Rectangle(Width-47,(Height-24)/2,44,24),Checked,Accent,Muted);
        if(Focused)using(var p=ControlShape.Round(new Rectangle(0,0,Width-1,Height-1),6))using(var pen=new Pen(Accent))g.DrawPath(pen,p);
    }
}
internal sealed class SegmentButton : RadioButton {
    public Color Surface,ActiveSurface,Accent,Outline;
    public SegmentButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);Appearance=Appearance.Button;Cursor=Cursors.Hand;}
    protected override void OnPaint(PaintEventArgs e){
        var g=e.Graphics;g.Clear(Parent.BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;
        using(var p=ControlShape.Round(new Rectangle(1,1,Width-3,Height-3),10)){
            using(var b=new SolidBrush(Checked?ActiveSurface:Surface))g.FillPath(b,p);using(var pen=new Pen(Focused?Accent:Outline))g.DrawPath(pen,p);
        }
        TextRenderer.DrawText(g,Text,Font,ClientRectangle,Checked?Accent:ForeColor,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
    }
}
}
