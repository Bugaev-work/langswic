using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FastSwitcher {
sealed class SurfacePanel : Panel {
    public Color Surface,Canvas,Outline;
    public SurfacePanel(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
    protected override void OnPaintBackground(PaintEventArgs e){
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;e.Graphics.Clear(Canvas);
        var r=new Rectangle(1,1,Math.Max(1,Width-3),Math.Max(1,Height-3));int d=24;
        using(var path=new GraphicsPath()){
            path.AddArc(r.Left,r.Top,d,d,180,90);path.AddArc(r.Right-d,r.Top,d,d,270,90);
            path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);path.AddArc(r.Left,r.Bottom-d,d,d,90,90);path.CloseFigure();
            using(var brush=new SolidBrush(Surface))e.Graphics.FillPath(brush,path);
            using(var pen=new Pen(Outline))e.Graphics.DrawPath(pen,path);
        }
    }
}
sealed class PillButton : Button {
    bool hover;
    public PillButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
    protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnPaint(PaintEventArgs e){
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;e.Graphics.Clear(Parent==null?Color.White:Parent.BackColor);
        int radius=Math.Min(Height/2,19),d=radius*2;var r=new Rectangle(0,0,Width-1,Height-1);
        using(var p=new GraphicsPath()){
            p.AddArc(r.Left,r.Top,d,d,180,90);p.AddArc(r.Right-d,r.Top,d,d,270,90);
            p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.Left,r.Bottom-d,d,d,90,90);p.CloseFigure();
            using(var brush=new SolidBrush(hover?(BackColor.B>150&&BackColor.R<80?Color.FromArgb(30,96,211):BackColor.GetBrightness()>.6?Color.FromArgb(221,233,252):Color.FromArgb(36,48,67)):BackColor))e.Graphics.FillPath(brush,p);
        }
        TextRenderer.DrawText(e.Graphics,Text,Font,r,ForeColor,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
        if(Focused&&FocusAppearance.Keyboard){using(var pen=new Pen(ForeColor,2))e.Graphics.DrawLine(pen,Width/2-12,Height-5,Width/2+12,Height-5);}
    }
}
public sealed class MainForm : Form {
    readonly SettingsStore store=new SettingsStore();
    readonly LanguageEngine engine=new LanguageEngine();
    InputService input;
    Panel sidebar,content; Label title; FeatureTile autoTile,shiftTile,smartTile; NotifyIcon tray; ContextMenuStrip trayMenu;
    readonly List<Button> nav=new List<Button>();
    bool updatingTiles;
    int settingsSection,exceptionSection,hotkeySection;
    ToolStripMenuItem trayStatus,trayAutomatic,trayStartup;
    readonly Dictionary<int,string> hotkeyErrors=new Dictionary<int,string>();
    Timer diagnosticTimer; bool exiting=false; int page=0;
    Color bg,card,ink,muted,accent,textAccent,border,side,activeNav;
    public bool HookReady {get{return input!=null&&input.HookActive;}}
    public string InputDiagnostic {get{return input==null?"not started":input.LastReason+"; corrections="+input.Corrections+"; process="+input.ActiveProcess+"; "+input.LayoutDiagnostic+"; "+TextAccess.ReadState;}}
    public int InputVerifiedValueReplacements {get{return input==null?0:input.VerifiedValueReplacements;}}
    public int InputCorrections {get{return input==null?0:input.Corrections;}}
#if INPUT_TEST
    internal bool SmokeTray(){
        bool enabled=store.Current.Enabled,startup=store.Current.StartWithWindows;
        trayAutomatic.PerformClick();bool changed=store.Current.Enabled!=enabled&&trayAutomatic.Checked==store.Current.Enabled;trayAutomatic.PerformClick();
        trayStartup.PerformClick();changed=changed&&store.Current.StartWithWindows!=startup&&trayStartup.Checked==store.Current.StartWithWindows;trayStartup.PerformClick();
        trayMenu.Show(this,new Point(220,180));Application.DoEvents();
        using(var bitmap=new Bitmap(trayMenu.Width,trayMenu.Height)){trayMenu.DrawToBitmap(bitmap,new Rectangle(0,0,trayMenu.Width,trayMenu.Height));bitmap.Save(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath),"tray-menu-preview"+(store.Current.DarkTheme?"-dark":"")+".png"));}
        trayMenu.Close();return changed&&Icon.Width==32&&TaskbarIdentity.WindowIcon(Handle)!=IntPtr.Zero;
    }
    internal void SmokeTheme(){ChangeTheme();}
    internal bool SmokeFeatures(){bool original=store.Current.Layout;autoTile.AccessibilityObject.DoDefaultAction();bool changed=store.Current.Layout!=original;autoTile.AccessibilityObject.DoDefaultAction();return changed && store.Current.Layout==original;}
    static IEnumerable<Control> Descendants(Control root){foreach(Control child in root.Controls){yield return child;foreach(var nested in Descendants(child))yield return nested;}}
    internal bool SmokeControls(){
        settingsSection=0;ShowPage(1);bool previous=store.Current.Enabled;
        var toggle=Descendants(content).OfType<SwitchRow>().First();toggle.AccessibilityObject.DoDefaultAction();bool ok=store.Current.Enabled!=previous;toggle.AccessibilityObject.DoDefaultAction();ok=ok&&store.Current.Enabled==previous;
        Descendants(content).OfType<SegmentButton>().ElementAt(1).AccessibilityObject.DoDefaultAction();Application.DoEvents();ok=ok&&settingsSection==1;
        var field=Descendants(content).OfType<RoundedField>().First();string old=field.Text;field.Editor.Focus();field.Editor.Text="C:\\Sounds\\local.wav";field.Editor.Select(3,6);ok=ok&&field.Text=="C:\\Sounds\\local.wav"&&field.Editor.SelectedText=="Sounds";
        Descendants(content).OfType<PillButton>().First().Focus();Application.DoEvents();ok=ok&&store.Current.LayoutSoundFile==field.Text;
        field.Text=old;store.Current.LayoutSoundFile=old;store.Save();
        exceptionSection=0;ShowPage(2);field=Descendants(content).OfType<RoundedField>().First();old=field.Text;field.Text=string.Join(Environment.NewLine,Enumerable.Range(0,80).Select(i=>"слово"+i).ToArray());field.Editor.Select(0,0);field.ScrollToForTest(30);ok=ok&&field.ScrollPosition>0&&field.Editor.SelectionStart==0;field.Text=old;
        int originalCount=store.Current.Apps.Count;var added=AddAppExecutable(Application.ExecutablePath);ok=ok&&!added.Layout&&!added.Typos&&!added.Yo&&store.Current.Apps.Count==originalCount+1;
        var duplicate=AddAppExecutable(Application.ExecutablePath.ToUpperInvariant());ok=ok&&object.ReferenceEquals(added,duplicate)&&store.Current.Apps.Count==originalCount+1;
        bool rejected=false;try{AddAppExecutable(SettingsStore.PathName);}catch(ArgumentException){rejected=true;}ok=ok&&rejected&&store.Current.Apps.Count==originalCount+1;
        exceptionSection=1;ShowPage(2);var list=Descendants(content).OfType<StyledList>().First();list.SelectedItem=added;var rail=Descendants(content).OfType<ScrollRail>().First();rail.ScrollTo(4);ok=ok&&list.TopIndex>0&&object.ReferenceEquals(list.SelectedItem,added)&&!Descendants(content).OfType<DataGridView>().Any();
        var appToggle=Descendants(content).OfType<SwitchRow>().First();appToggle.AccessibilityObject.DoDefaultAction();var persisted=store.Load(SettingsStore.PathName).Apps.First(a=>a.Process==added.Process);ok=ok&&persisted.Layout&&persisted.ExecutablePath==added.ExecutablePath;
        var clickMode=new FocusAppearance();var keyboardMessage=Message.Create(Handle,0x100,new IntPtr((int)Keys.Tab),IntPtr.Zero);clickMode.PreFilterMessage(ref keyboardMessage);ok=ok&&FocusAppearance.Keyboard;var mouseMessage=Message.Create(Handle,0x201,IntPtr.Zero,IntPtr.Zero);clickMode.PreFilterMessage(ref mouseMessage);ok=ok&&!FocusAppearance.Keyboard;
        Descendants(content).OfType<PillButton>().First(b=>b.Text=="Удалить исключение").PerformClick();ok=ok&&store.Current.Apps.Count==originalCount&&!store.Load(SettingsStore.PathName).Apps.Any(a=>a.Process==added.Process);
        ok=ok&&nav.Count==4&&!nav.Any(b=>b.Text=="Словари");settingsSection=exceptionSection=0;ShowPage(0);Console.WriteLine(ok?"OK custom controls, EXE add/deduplication/rejection, per-app persistence/removal and pointer focus":"FAIL custom control interaction");return ok;
    }
    internal System.Threading.Tasks.Task DelayInputWorkerForTest(int ms){return input.DelayWorkerForTest(ms);}
    internal void ForgetRuleForTest(string word){store.Current.Learned.Remove(word);}
    internal void SetAutomationForTest(bool enabled){store.Current.Enabled=enabled;input.Clear();}
#endif
    public bool LayoutValid {get{return sidebar!=null&&content!=null&&content.Top>=sidebar.Bottom&&content.Width>500;}}
    public bool SmokePages(){
        int[] counts={1,3,2,2};bool valid=true;
        for(int i=0;i<4;i++)for(int j=0;j<counts[i];j++){
            if(i==1)settingsSection=j;if(i==2)exceptionSection=j;if(i==3)hotkeySection=j;
            ShowPage(i);Application.DoEvents();
            if(content.Controls.Count==0 || content.Controls[0].Height<100 || content.Controls[0].Bottom>content.ClientSize.Height || content.Controls[0].Right>content.ClientSize.Width){valid=false;
#if INPUT_TEST
                Console.WriteLine("FAIL page bounds: "+i+"/"+j+" bottom="+content.Controls[0].Bottom+" available="+content.ClientSize.Height);
#endif
            }
#if INPUT_TEST
            using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height));bitmap.Save(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath),"ui-page-"+i+"-"+j+(store.Current.DarkTheme?"-dark":"")+".png"));}
#endif
        }
        settingsSection=exceptionSection=hotkeySection=0;ShowPage(0);return valid;
    }
    public void QuitForTests(){exiting=true;Close();}
    public MainForm(bool background){
        FocusAppearance.Install();Text="langswic"; Width=1000;Height=740;FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false;ShowInTaskbar=true;ShowIcon=true;
        StartPosition=FormStartPosition.CenterScreen; Font=new Font("Segoe UI",10);
        Icon=MakeIcon();
        engine.AddUserWords(store.Current);
        MakeShell(); SetTheme(); ShowPage(0);
        input=new InputService(this,store,engine);input.Changed+=UpdateStatus;
        SystemEvents.PowerModeChanged+=PowerChanged;
        Shown+=delegate{input.Start();RegisterHotkeys();if(background)Hide();};
        FormClosing+=delegate(object s,FormClosingEventArgs e){if(!exiting && e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();tray.ShowBalloonTip(1500,"langswic","Программа продолжает работать в трее.",ToolTipIcon.Info);}};
        FormClosed+=delegate{SystemEvents.PowerModeChanged-=PowerChanged;if(input!=null)input.Dispose();engine.Dispose();UnregisterHotkeys();if(tray!=null)tray.Dispose();};
        diagnosticTimer=new Timer{Interval=1000};diagnosticTimer.Tick+=delegate{UpdateStatus();};diagnosticTimer.Start();
    }
    void PowerChanged(object sender,PowerModeChangedEventArgs e){
        if(!IsHandleCreated||IsDisposed)return;
        BeginInvoke((Action)(()=>{if(input==null)return;if(e.Mode==PowerModes.Suspend)input.Clear();if(e.Mode==PowerModes.Resume){input.Restart();RegisterHotkeys();}}));
    }
    static Icon MakeIcon(){
        using(var stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Langswic.BrandIcon"))
        using(var icon=new Icon(stream,new Size(32,32)))return (Icon)icon.Clone();
    }
    void MakeShell(){
        content=new Panel{Dock=DockStyle.Fill,AutoScroll=false};Controls.Add(content);
        var navigation=new TableLayoutPanel{Dock=DockStyle.Top,Height=48,Padding=new Padding(24,4,24,4),ColumnCount=4,RowCount=1};sidebar=navigation;Controls.Add(sidebar);
        string[] names={"Обзор","Настройки","Исключения","Горячие клавиши"};
        for(int i=0;i<names.Length;i++){navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,25));int index=i;var b=new PillButton{Text=names[i],Dock=DockStyle.Fill,FlatStyle=FlatStyle.Flat,TabIndex=i,Font=new Font("Segoe UI Semibold",10),Margin=new Padding(2,0,2,0)};b.FlatAppearance.BorderSize=0;b.Click+=delegate{ShowPage(index);};navigation.Controls.Add(b,i,0);nav.Add(b);}
        Controls.Add(new BlueHeader{Dock=DockStyle.Top,Height=128});
        var footer=new Panel{Dock=DockStyle.Bottom,Height=36,Padding=new Padding(22,0,20,0)};
        footer.Controls.Add(new Label{Text="v. 1.5.4   ·   Локальная обработка",Dock=DockStyle.Left,Width=400,TextAlign=ContentAlignment.MiddleLeft});
        var theme=new PillButton{Text="Сменить тему",Dock=DockStyle.Right,Width=135,FlatStyle=FlatStyle.Flat};theme.FlatAppearance.BorderSize=0;theme.Click+=delegate{ChangeTheme();};footer.Controls.Add(theme);Controls.Add(footer);
        trayMenu=new ContextMenuStrip{Font=new Font("Segoe UI",10),Padding=new Padding(5,7,5,7),ShowImageMargin=false,ShowCheckMargin=true};
        trayStatus=new ToolStripMenuItem("langswic · локальная обработка"){Enabled=false};trayMenu.Items.Add(trayStatus);
        trayMenu.Items.Add(new ToolStripSeparator());
        AddTrayItem("Настройки",()=>OpenPage(1));AddTrayItem("Исключения",()=>OpenPage(2));
        trayAutomatic=AddTrayItem("Автопереключение",()=>{store.Current.Enabled=!store.Current.Enabled;store.Save();if(input!=null)input.Clear();UpdateStatus();});
        trayStartup=AddTrayItem("Запускать вместе с Windows",()=>{store.Current.StartWithWindows=!store.Current.StartWithWindows;store.Save();UpdateStatus();});
        trayMenu.Items.Add(new ToolStripSeparator());AddTrayItem("Выйти из langswic",()=>{exiting=true;Close();});
        trayMenu.Opening+=delegate{UpdateStatus();};
        tray=new NotifyIcon{Icon=Icon,Text="langswic",Visible=true,ContextMenuStrip=trayMenu};tray.DoubleClick+=delegate{Show();WindowState=FormWindowState.Normal;Activate();};
    }
    ToolStripMenuItem AddTrayItem(string text,Action action){var item=new ToolStripMenuItem(text){AutoSize=false,Width=285,Height=38,Padding=new Padding(8,4,8,4)};item.Click+=delegate{action();};trayMenu.Items.Add(item);return item;}
    void OpenPage(int index){ShowPage(index);Show();WindowState=FormWindowState.Normal;Activate();}
    void ChangeTheme(){store.Current.DarkTheme=!store.Current.DarkTheme;store.Save();SetTheme();ShowPage(page);}
    void SetTheme(){
        bool dark=store.Current.DarkTheme;
        bg=dark?Color.FromArgb(23,28,39):Color.White;
        card=dark?Color.FromArgb(31,38,52):Color.FromArgb(250,251,254);
        side=dark?Color.FromArgb(27,33,45):Color.FromArgb(246,248,252);
        ink=dark?Color.FromArgb(238,243,252):Color.FromArgb(26,31,53);
        muted=dark?Color.FromArgb(168,180,202):Color.FromArgb(111,119,143);
        accent=Color.FromArgb(36,113,243);textAccent=dark?Color.FromArgb(126,180,255):accent;border=dark?Color.FromArgb(62,72,91):Color.FromArgb(228,233,243);
        activeNav=dark?Color.FromArgb(40,59,87):Color.FromArgb(230,239,255);
        BackColor=bg;ForeColor=ink;sidebar.BackColor=side;content.BackColor=bg;
        trayMenu.Renderer=new TrayMenuRenderer(dark);trayMenu.BackColor=card;trayMenu.ForeColor=ink;
        foreach(var button in nav){button.ForeColor=ink;button.BackColor=side;button.FlatAppearance.MouseOverBackColor=activeNav;}
        foreach(Control child in Controls){if(child is BlueHeader || child==content || child==sidebar)continue;child.BackColor=side;child.ForeColor=muted;foreach(Control item in child.Controls){item.BackColor=side;item.ForeColor=muted;}}
    }
    void ShowPage(int index){
        page=index;content.SuspendLayout();if(title!=null&&title.Parent==null)title.Dispose();foreach(Control old in content.Controls.Cast<Control>().ToArray())old.Dispose();content.Controls.Clear();autoTile=shiftTile=smartTile=null;
        foreach(var b in nav){b.BackColor=nav.IndexOf(b)==index?activeNav:side;b.ForeColor=nav.IndexOf(b)==index?textAccent:ink;}
        var host=new FlowLayoutPanel{FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Width=Math.Max(500,content.ClientSize.Width-48),Location=new Point(24,16),Padding=new Padding(0),Margin=new Padding(0)};
        content.Controls.Add(host);
        string[] names={"Обзор","Настройки","Исключения","Горячие клавиши"};
        title=TextLabel(names[index],24,FontStyle.Bold,46);if(index!=0)host.Controls.Add(title);else title.Visible=false;
        switch(index){case 0:Overview(host);break;case 1:SettingsPage(host);break;case 2:ExceptionsPage(host);break;case 3:HotkeysPage(host);break;}
        content.ResumeLayout();UpdateStatus();
    }
    Label TextLabel(string text,int size,FontStyle style,int height){return new Label{Text=text,Font=new Font("Segoe UI",size,style),ForeColor=ink,Height=height,Width=Math.Max(480,content.ClientSize.Width-70),TextAlign=ContentAlignment.MiddleLeft,Margin=new Padding(0,0,0,5)};}
    FlowLayoutPanel Card(FlowLayoutPanel host,string heading,string description){
        var outer=new SurfacePanel{Width=host.Width,AutoSize=true,Padding=new Padding(18),Margin=new Padding(0,0,0,8),BackColor=card,Surface=card,Canvas=bg,Outline=border};
        var body=new FlowLayoutPanel{FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoSize=true,Width=outer.Width-36,Location=new Point(18,14),Margin=new Padding(0)};
        outer.Controls.Add(body);host.Controls.Add(outer);
        if(!string.IsNullOrEmpty(heading))body.Controls.Add(new Label{Text=heading,Font=new Font("Segoe UI Semibold",13),ForeColor=ink,Height=32,Width=body.Width});
        if(!string.IsNullOrEmpty(description))body.Controls.Add(new Label{Text=description,ForeColor=muted,Height=36,Width=body.Width});
        return body;
    }
    Button ActionButton(string text,Action action,int width=170){var b=new PillButton{Text=text,Width=width,Height=40,FlatStyle=FlatStyle.Flat,BackColor=accent,ForeColor=Color.White,Margin=new Padding(0,7,10,7),Font=new Font("Segoe UI Semibold",10)};b.FlatAppearance.BorderSize=0;b.FlatAppearance.MouseOverBackColor=Color.FromArgb(30,96,211);b.Click+=delegate{action();};return b;}
    CheckBox Toggle(string text,bool value,Action<bool> changed){var c=new SwitchRow{Text=text,AccessibleName=text,Checked=value,AutoSize=false,Width=Math.Max(500,content.ClientSize.Width-100),Height=36,ForeColor=ink,BackColor=card,Accent=accent,Muted=muted,Margin=new Padding(0,2,0,2)};c.CheckedChanged+=delegate{changed(c.Checked);store.Save();UpdateStatus();};return c;}
    void SectionPicker(FlowLayoutPanel host,string[] options,int selected,Action<int> change){
        var picker=new FlowLayoutPanel{Width=host.Width,Height=42,WrapContents=false,Margin=new Padding(0,0,0,12),AccessibleName="Категория"};
        for(int i=0;i<options.Length;i++){int index=i;var button=new SegmentButton{Text=options[i],AccessibleName=options[i],Width=options.Length==3?220:260,Height=40,Font=new Font("Segoe UI Semibold",10),ForeColor=ink,Surface=card,ActiveSurface=activeNav,Accent=textAccent,Outline=border,Checked=i==selected,Margin=new Padding(0,0,8,0)};
            button.CheckedChanged+=delegate{if(!button.Checked)return;change(index);ShowPage(page);var next=content.Controls[0].Controls.OfType<FlowLayoutPanel>().FirstOrDefault(p=>p.AccessibleName=="Категория");if(next!=null)next.Controls[index].Focus();};picker.Controls.Add(button);
        }host.Controls.Add(picker);
    }
    RoundedField Field(string text,int width,int height,bool multiline,string name){var field=new RoundedField{Text=text,Width=width,Height=height,Multiline=multiline,Font=Font};field.Apply(side,ink,border,accent,name);return field;}
    FlowLayoutPanel Row(){return new FlowLayoutPanel{FlowDirection=FlowDirection.LeftToRight,WrapContents=false,AutoSize=true,Width=Math.Max(380,content.ClientSize.Width-135),Margin=new Padding(0,3,0,3)};}
    void Overview(FlowLayoutPanel host){
        var tiles=new TableLayoutPanel{Width=host.Width,Height=223,ColumnCount=3,RowCount=1,Margin=new Padding(0,0,0,8)};
        for(int i=0;i<3;i++)tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,33.333f));
        autoTile=MakeTile("Автопереключение",0,Color.FromArgb(30,158,60),Color.FromArgb(234,248,239),store.Current.Enabled&&store.Current.Layout,v=>{store.Current.Layout=v;if(v)store.Current.Enabled=true;});
        shiftTile=MakeTile("Single Shift",1,accent,Color.FromArgb(231,240,255),store.Current.SingleShift,v=>store.Current.SingleShift=v);
        smartTile=MakeTile("Коррекция",2,Color.FromArgb(124,61,229),Color.FromArgb(244,237,255),store.Current.Typos||store.Current.Yo,v=>{store.Current.Typos=v;store.Current.Yo=v;});
        smartTile.AccessibleDescription="Исправляет словарные опечатки, ошибки регистра и однозначные формы с ё. Отдельные функции доступны в настройках.";
        tiles.Controls.Add(autoTile,0,0);tiles.Controls.Add(shiftTile,1,0);tiles.Controls.Add(smartTile,2,0);host.Controls.Add(tiles);
        var tips=new TableLayoutPanel{Width=host.Width,Height=114,ColumnCount=2,RowCount=3,Margin=new Padding(0,5,0,12)};
        tips.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,230));tips.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        string[] keys={"Double Shift","Single Shift",store.Current.ToggleHotkey};
        string[] descriptions={"Слово / выделение; в пустом поле — смена языка","Сменить раскладку, даже без набора текста","Включить или выключить автоматику"};
        for(int i=0;i<3;i++){tips.RowStyles.Add(new RowStyle(SizeType.Absolute,38));tips.Controls.Add(new Label{Text=keys[i],Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleRight,ForeColor=ink,Font=new Font("Segoe UI Semibold",12)},0,i);tips.Controls.Add(new Label{Text=" — "+descriptions[i],Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,ForeColor=muted,Font=new Font("Segoe UI",11)},1,i);}
        host.Controls.Add(tips);host.Controls.Add(new Panel{Width=host.Width,Height=1,BackColor=border,Margin=new Padding(0,0,0,14)});
    }
    FeatureTile MakeTile(string text,int symbol,Color color,Color surface,bool value,Action<bool> changed){
        var tile=new FeatureTile{Text=text,AccessibleName=text,Symbol=symbol,Accent=color,Surface=store.Current.DarkTheme?Color.FromArgb(40,47,66):surface,Ink=ink,Muted=muted,BackColor=bg,Checked=value,Dock=DockStyle.Fill,Margin=new Padding(8,0,8,0)};
        tile.CheckedChanged+=delegate{if(updatingTiles)return;changed(tile.Checked);store.Save();if(input!=null)input.Clear();UpdateStatus();};return tile;
    }
    void SettingsPage(FlowLayoutPanel host){
        SectionPicker(host,new[]{"Основное","Звуки","Импорт и экспорт"},settingsSection,v=>settingsSection=v);
        FlowLayoutPanel c;
        if(settingsSection==0){
        c=Card(host,"Автоматика","Каждый вид исправления можно выключить отдельно.");
        c.Controls.Add(Toggle("Автоматические исправления",store.Current.Enabled,v=>store.Current.Enabled=v));
        c.Controls.Add(Toggle("Ошибочная раскладка RU ↔ EN",store.Current.Layout,v=>store.Current.Layout=v));
        c.Controls.Add(Toggle("Распространённые опечатки и регистр",store.Current.Typos,v=>store.Current.Typos=v));
        c.Controls.Add(Toggle("Однозначные формы с «ё»",store.Current.Yo,v=>store.Current.Yo=v));
        c.Controls.Add(Toggle("Запускать при входе в Windows",store.Current.StartWithWindows,v=>store.Current.StartWithWindows=v));
        return;}
        if(settingsSection==1){
        c=Card(host,"Звуки","Используйте WAV-файлы. Если файл не задан, звучит системный сигнал.");
        c.Controls.Add(Toggle("Звук при переключении раскладки",store.Current.SoundLayout,v=>store.Current.SoundLayout=v));
        c.Controls.Add(SoundPicker("Файл переключения",store.Current.LayoutSoundFile,v=>store.Current.LayoutSoundFile=v));
        c.Controls.Add(Toggle("Звук при исправлении опечатки",store.Current.SoundTypos,v=>store.Current.SoundTypos=v));
        c.Controls.Add(SoundPicker("Файл опечатки",store.Current.TypoSoundFile,v=>store.Current.TypoSoundFile=v));
        return;}
        c=Card(host,"Перенос данных","Экспорт содержит настройки, исключения, пользовательские слова и выученные правила.");
        var row=Row();row.Controls.Add(ActionButton("Экспорт JSON",delegate{using(var d=new System.Windows.Forms.SaveFileDialog{Filter="JSON (*.json)|*.json",FileName="langswic-settings.json"})if(d.ShowDialog()==DialogResult.OK)store.Export(d.FileName);},150));
        row.Controls.Add(ActionButton("Импорт JSON",delegate{using(var d=new System.Windows.Forms.OpenFileDialog{Filter="JSON (*.json)|*.json"})if(d.ShowDialog()==DialogResult.OK){try{store.Import(d.FileName);engine.AddUserWords(store.Current);RegisterHotkeys();ShowPage(page);}catch(Exception e){MessageBox.Show(e.Message,"Ошибка импорта",MessageBoxButtons.OK,MessageBoxIcon.Error);}}},150));c.Controls.Add(row);
    }
    FlowLayoutPanel SoundPicker(string caption,string value,Action<string> set){var r=Row();var box=Field(value,520,40,false,caption);r.Controls.Add(box);r.Controls.Add(ActionButton("Выбрать WAV",delegate{using(var d=new System.Windows.Forms.OpenFileDialog{Filter="WAV (*.wav)|*.wav"})if(d.ShowDialog()==DialogResult.OK){box.Text=d.FileName;set(box.Text);store.Save();}},135));box.Leave+=delegate{set(box.Text);store.Save();};return r;}
    void ExceptionsPage(FlowLayoutPanel host){
        SectionPicker(host,new[]{"Слова","Приложения"},exceptionSection,v=>exceptionSection=v);
        if(exceptionSection==0){
            var wordsCard=Card(host,"Слова-исключения","Эти слова автоматика оставляет без изменений. По одному слову в строке.");
            var words=Field(string.Join(Environment.NewLine,store.Current.ExcludedWords.ToArray()),wordsCard.Width-8,120,true,"Слова-исключения");words.ScrollBars=ScrollBars.Vertical;wordsCard.Controls.Add(words);
            wordsCard.Controls.Add(ActionButton("Сохранить слова",delegate{store.Current.ExcludedWords=Lines(words.Text);store.Save();},165));return;
        }
        var c=Card(host,"Приложения-исключения","Выберите EXE — автоматика для программы отключится. Можно разрешить отдельные виды исправлений.");
        var commands=Row();c.Controls.Add(commands);
        var columns=new TableLayoutPanel{Width=c.Width-8,Height=190,ColumnCount=2,RowCount=1,Margin=new Padding(0,3,0,3)};
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,35));columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,65));
        var list=MakeList("Приложения-исключения");
        var rail=new ScrollRail(()=>list.TopIndex,()=>list.Items.Count,()=>list.Height/list.ItemHeight,line=>{if(list.Items.Count>0)list.TopIndex=line;}){BackColor=side,ForeColor=muted};
        list.SelectedIndexChanged+=delegate{rail.Invalidate();};list.KeyUp+=delegate{rail.Invalidate();};list.MouseWheel+=delegate{if(list.IsHandleCreated)list.BeginInvoke((Action)(()=>rail.Invalidate()));};
        var listPanel=ScrollContainer(list,rail,310,190);listPanel.Dock=DockStyle.Fill;columns.Controls.Add(listPanel,0,0);
        var details=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(16,0,0,0),Margin=new Padding(0)};columns.Controls.Add(details,1,0);c.Controls.Add(columns);
        Button remove=null;
        Action render=delegate{
            foreach(Control old in details.Controls.Cast<Control>().ToArray())old.Dispose();details.Controls.Clear();
            var rule=list.SelectedItem as AppRule;remove.Enabled=rule!=null;
            if(rule==null){details.Controls.Add(new Label{Text="Добавьте программу через выбор EXE.",ForeColor=muted,Width=500,Height=40});return;}
            int width=Math.Max(400,columns.Width*65/100-28);
            details.Controls.Add(new Label{Text=rule.Process+".exe",ForeColor=ink,Font=new Font("Segoe UI Semibold",12),Width=width,Height=26,Margin=new Padding(0)});
            string path=string.IsNullOrWhiteSpace(rule.ExecutablePath)?"Правило для всех процессов с этим именем":rule.ExecutablePath;
            details.Controls.Add(new Label{Text=path,AccessibleDescription=path,ForeColor=muted,Width=width,Height=36,AutoEllipsis=true,Margin=new Padding(0,0,0,4)});
            var layout=Toggle("Исправлять раскладку",rule.Layout,v=>{rule.Layout=v;if(input!=null)input.Clear();});
            var typos=Toggle("Исправлять опечатки и регистр",rule.Typos,v=>{rule.Typos=v;if(input!=null)input.Clear();});
            var yo=Toggle("Расставлять ё",rule.Yo,v=>{rule.Yo=v;if(input!=null)input.Clear();});
            foreach(var toggle in new[]{layout,typos,yo}){toggle.Width=width;toggle.Height=34;details.Controls.Add(toggle);}
        };
        Action<string> refresh=delegate(string process){list.Items.Clear();foreach(var rule in store.Current.Apps.OrderBy(a=>a.Process,StringComparer.OrdinalIgnoreCase))list.Items.Add(rule);int index=-1;for(int i=0;i<list.Items.Count;i++)if(string.Equals(((AppRule)list.Items[i]).Process,process,StringComparison.OrdinalIgnoreCase)){index=i;break;}list.SelectedIndex=index>=0?index:(list.Items.Count>0?0:-1);render();rail.Invalidate();};
        commands.Controls.Add(ActionButton("Выбрать программу…",delegate{
            using(var picker=new System.Windows.Forms.OpenFileDialog{Title="Добавить программу в исключения",Filter="Программы (*.exe)|*.exe",CheckFileExists=true,Multiselect=true,RestoreDirectory=true}){
                if(picker.ShowDialog(this)!=DialogResult.OK)return;
                try{string selected=null;foreach(string file in picker.FileNames)selected=AddAppExecutable(file).Process;refresh(selected);}
                catch(Exception e){MessageBox.Show(this,e.Message,"Не удалось добавить программу",MessageBoxButtons.OK,MessageBoxIcon.Error);}
            }
        },220));
        remove=ActionButton("Удалить исключение",delegate{var rule=list.SelectedItem as AppRule;if(rule==null)return;store.Current.Apps.Remove(rule);store.Save();if(input!=null)input.Clear();refresh(null);},190);commands.Controls.Add(remove);
        list.SelectedIndexChanged+=delegate{render();};refresh(null);
    }
    AppRule AddAppExecutable(string filename){
        string path=Path.GetFullPath(filename);
        if(!File.Exists(path)||!string.Equals(Path.GetExtension(path),".exe",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Выберите существующий файл программы с расширением .exe.");
        string process=Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        var rule=store.Current.Apps.FirstOrDefault(a=>string.Equals(a.Process,process,StringComparison.OrdinalIgnoreCase));
        if(rule==null){rule=new AppRule{Process=process};store.Current.Apps.Add(rule);}
        rule.ExecutablePath=path;store.Save();if(input!=null)input.Clear();return rule;
    }
    StyledList MakeList(string name){
        var list=new StyledList{BorderStyle=BorderStyle.None,BackColor=side,ForeColor=ink,DrawMode=DrawMode.OwnerDrawFixed,ItemHeight=34,AccessibleName=name,IntegralHeight=false};
        list.DrawItem+=delegate(object sender,DrawItemEventArgs e){if(e.Index<0)return;bool selected=(e.State&DrawItemState.Selected)!=0;using(var brush=new SolidBrush(selected?activeNav:side))e.Graphics.FillRectangle(brush,e.Bounds);if(selected)using(var brush=new SolidBrush(textAccent))e.Graphics.FillRectangle(brush,e.Bounds.X,e.Bounds.Y+7,3,e.Bounds.Height-14);var rule=list.Items[e.Index] as AppRule;string text=rule==null?list.Items[e.Index].ToString():rule.Process+".exe";TextRenderer.DrawText(e.Graphics,text,Font,new Rectangle(e.Bounds.X+12,e.Bounds.Y,e.Bounds.Width-24,e.Bounds.Height),selected?textAccent:ink,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);};
        return list;
    }
    static List<string> Lines(string text){return text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(s=>s.Trim()).Where(s=>s.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();}
    void HotkeysPage(FlowLayoutPanel host){
        SectionPicker(host,new[]{"Shift и Pause","Сочетания клавиш"},hotkeySection,v=>hotkeySection=v);
        var c=Card(host,"Ручное управление","Shift, Double Shift и Pause работают вручную. Для дополнительных команд задайте сочетания.");
        if(hotkeySection==0){
        c.Controls.Add(Toggle("Double Shift",store.Current.DoubleShift,v=>store.Current.DoubleShift=v));
        c.Controls.Add(Toggle("Одиночный Shift переключает раскладку",store.Current.SingleShift,v=>store.Current.SingleShift=v));
        c.Controls.Add(Toggle("Pause/Break",store.Current.Pause,v=>store.Current.Pause=v));
        return;}
        var selected=Field(store.Current.SelectedHotkey,250,40,false,"Выделенный текст: сочетание клавиш");
        var manualWord=Field(store.Current.WordHotkey,250,40,false,"Последнее слово: сочетание клавиш");
        var undo=Field(store.Current.UndoHotkey,250,40,false,"Отмена: сочетание клавиш");
        var toggle=Field(store.Current.ToggleHotkey,250,40,false,"Автоматика: сочетание клавиш");
        c.Controls.Add(HotkeyRow("Последнее слово",manualWord));c.Controls.Add(HotkeyRow("Выделенный текст",selected));c.Controls.Add(HotkeyRow("Отмена",undo));c.Controls.Add(HotkeyRow("Автоматика",toggle));
        var status=new Label{Width=c.Width,Height=36,ForeColor=muted,Text=HotkeyStatus()};
        c.Controls.Add(ActionButton("Применить сочетания",delegate{store.Current.WordHotkey=manualWord.Text;store.Current.SelectedHotkey=selected.Text;store.Current.UndoHotkey=undo.Text;store.Current.ToggleHotkey=toggle.Text;store.Save();RegisterHotkeys();status.Text=HotkeyStatus();},190));c.Controls.Add(status);
    }
    FlowLayoutPanel HotkeyRow(string name,RoundedField box){var r=Row();box.Height=34;box.Margin=new Padding(0,0,8,0);r.Controls.Add(new Label{Text=name,Width=190,Height=34,Margin=new Padding(0),ForeColor=ink,TextAlign=ContentAlignment.MiddleLeft});r.Controls.Add(box);return r;}
    Panel ScrollContainer(Control editor,ScrollRail rail,int width,int height){var panel=new Panel{Width=width,Height=height,BackColor=editor.BackColor,Padding=new Padding(2),Margin=new Padding(0,3,0,3)};editor.Dock=DockStyle.Fill;rail.Dock=DockStyle.Right;rail.Width=18;panel.Controls.Add(editor);panel.Controls.Add(rail);return panel;}
    string HotkeyStatus(){return hotkeyErrors.Count==0?"Сочетания зарегистрированы.":string.Join("; ",hotkeyErrors.Values.ToArray());}
    void UpdateStatus(){
        if(IsDisposed || input==null)return;
        if(autoTile!=null){updatingTiles=true;try{autoTile.Checked=store.Current.Enabled&&store.Current.Layout;shiftTile.Checked=store.Current.SingleShift;smartTile.Checked=store.Current.Typos||store.Current.Yo;}finally{updatingTiles=false;}}
        tray.Text=store.Current.Enabled?"langswic · автоматика включена":"langswic · автоматика выключена";
        trayStatus.Text=store.Current.Enabled?"langswic · автоматика включена":"langswic · автоматика выключена";
        trayAutomatic.Checked=store.Current.Enabled;trayStartup.Checked=store.Current.StartWithWindows;
    }
    void UnregisterHotkeys(){for(int i=1;i<=4;i++)Native.UnregisterHotKey(Handle,i);}
    void RegisterHotkeys(){
        if(!IsHandleCreated)return;UnregisterHotkeys();hotkeyErrors.Clear();
        Register(1,store.Current.SelectedHotkey);Register(2,store.Current.UndoHotkey);Register(3,store.Current.ToggleHotkey);Register(4,store.Current.WordHotkey);
    }
    void Register(int id,string value){
        uint mods,key; if(!ParseHotkey(value,out mods,out key)){hotkeyErrors[id]="Неверное сочетание: "+value;return;}
        if(!Native.RegisterHotKey(Handle,id,mods|Native.MOD_NOREPEAT,key))hotkeyErrors[id]="Сочетание занято: "+value;
    }
    static bool ParseHotkey(string value,out uint mods,out uint key){
        mods=0;key=0;if(string.IsNullOrWhiteSpace(value))return false;
        var parts=value.Split('+');Keys parsed;
        foreach(var raw in parts){string p=raw.Trim().ToLowerInvariant();if(p=="ctrl"||p=="control")mods|=Native.MOD_CONTROL;else if(p=="alt")mods|=Native.MOD_ALT;else if(p=="shift")mods|=Native.MOD_SHIFT;else if(p=="win")mods|=Native.MOD_WIN;else if(Enum.TryParse<Keys>(raw.Trim(),true,out parsed))key=(uint)parsed;else return false;}
        return mods!=0&&key!=0;
    }
    void AfterModifiers(Action action){
        var t=new Timer{Interval=40};int ticks=0;t.Tick+=delegate{ticks++;if((!Native.Down(Native.VK_CONTROL)&&!Native.Down(Native.VK_MENU)&&!Native.Down(Native.VK_SHIFT))||ticks>25){t.Stop();t.Dispose();action();}};t.Start();
    }
    protected override void WndProc(ref Message m){
        if(m.Msg==Native.WM_HOTKEY&&input!=null){int id=m.WParam.ToInt32();
            if(id==1)AfterModifiers(()=>input.ManualSelection());
            if(id==2)AfterModifiers(input.Undo);
            if(id==3){store.Current.Enabled=!store.Current.Enabled;store.Save();UpdateStatus();}
            if(id==4)AfterModifiers(input.ManualOrUndo);
        }
        base.WndProc(ref m);
    }
}
}
