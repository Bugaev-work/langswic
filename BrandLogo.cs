using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace FastSwitcher {
public static class BrandLogo {
    // Original symbol: two rounded arrows forming a compact switching loop.
    public static void Draw(Graphics g,RectangleF bounds){
        var state=g.Save();g.TranslateTransform(bounds.X,bounds.Y);g.ScaleTransform(bounds.Width/64,bounds.Height/64);
        g.SmoothingMode=SmoothingMode.AntiAlias;
        using(var shape=new GraphicsPath()){
            shape.AddArc(2,2,26,26,180,90);shape.AddArc(36,2,26,26,270,90);
            shape.AddArc(36,36,26,26,0,90);shape.AddArc(2,36,26,26,90,90);shape.CloseFigure();
            using(var gradient=new LinearGradientBrush(new Rectangle(2,2,60,60),Color.FromArgb(78,176,255),Color.FromArgb(51,88,233),65f))g.FillPath(gradient,shape);
            using(var pen=new Pen(Color.FromArgb(95,255,255,255),1))g.DrawPath(pen,shape);
        }
        using(var pen=new Pen(Color.White,5)){pen.StartCap=pen.EndCap=LineCap.Round;pen.LineJoin=LineJoin.Round;
            g.DrawLines(pen,new[]{new PointF(15,30),new PointF(15,21),new PointF(47,21)});
            g.DrawLines(pen,new[]{new PointF(40,14),new PointF(47,21),new PointF(40,28)});
            g.DrawLines(pen,new[]{new PointF(49,34),new PointF(49,43),new PointF(17,43)});
            g.DrawLines(pen,new[]{new PointF(24,36),new PointF(17,43),new PointF(24,50)});
        }
        g.Restore(state);
    }
    public static void SaveAssets(string directory){
        int[] sizes={16,24,32,48,64,128,256};var images=new byte[sizes.Length][];
        for(int i=0;i<sizes.Length;i++)using(var bitmap=new Bitmap(sizes[i],sizes[i],PixelFormat.Format32bppArgb)){
            using(var g=Graphics.FromImage(bitmap)){g.Clear(Color.Transparent);Draw(g,new RectangleF(0,0,sizes[i],sizes[i]));}
            using(var buffer=new MemoryStream()){bitmap.Save(buffer,ImageFormat.Png);images[i]=buffer.ToArray();}
            if(sizes[i]==256)bitmap.Save(Path.Combine(directory,"langswic.png"),ImageFormat.Png);
        }
        using(var writer=new BinaryWriter(File.Create(Path.Combine(directory,"langswic.ico")))){
            writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)sizes.Length);
            uint offset=(uint)(6+16*sizes.Length);
            for(int i=0;i<sizes.Length;i++){writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)0);writer.Write((byte)0);writer.Write((ushort)1);writer.Write((ushort)32);writer.Write((uint)images[i].Length);writer.Write(offset);offset+=(uint)images[i].Length;}
            foreach(byte[] bytes in images)writer.Write(bytes);
        }
    }
}
}
