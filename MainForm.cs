using System;
using System.Collections.Generic;
using System.ComponentModel;
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
            using(var brush=new SolidBrush(hover?(BackColor.GetBrightness()>.6?Color.FromArgb(221,233,252):Color.FromArgb(30,96,211)):BackColor))e.Graphics.FillPath(brush,p);
        }
        TextRenderer.DrawText(e.Graphics,Text,Font,r,ForeColor,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
        if(Focused)ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(7,5,Width-14,Height-10),Color.White,BackColor);
    }
}
public sealed class MainForm : Form {
    readonly SettingsStore store=new SettingsStore();
    readonly LanguageEngine engine=new LanguageEngine();
    InputService input;
    Panel sidebar,content; Label title,subtitle; FeatureTile autoTile,shiftTile,smartTile; NotifyIcon tray; ContextMenuStrip trayMenu;
    readonly List<Button> nav=new List<Button>();
    bool updatingTiles;
    readonly Dictionary<int,string> hotkeyErrors=new Dictionary<int,string>();
    Timer diagnosticTimer; bool exiting=false; int page=0;
    Color bg,card,ink,muted,accent,border,side,activeNav;
    public bool HookReady {get{return input!=null&&input.HookActive;}}
    public string InputDiagnostic {get{return input==null?"not started":input.LastReason+"; corrections="+input.Corrections+"; process="+input.ActiveProcess+"; "+input.LayoutDiagnostic+"; "+TextAccess.ReadState;}}
    public int InputVerifiedValueReplacements {get{return input==null?0:input.VerifiedValueReplacements;}}
    public int InputCorrections {get{return input==null?0:input.Corrections;}}
#if INPUT_TEST
    internal void SmokeTheme(){ChangeTheme();}
    internal bool SmokeFeatures(){bool original=store.Current.Layout;autoTile.AccessibilityObject.DoDefaultAction();bool changed=store.Current.Layout!=original;autoTile.AccessibilityObject.DoDefaultAction();return changed && store.Current.Layout==original;}
    internal System.Threading.Tasks.Task DelayInputWorkerForTest(int ms){return input.DelayWorkerForTest(ms);}
    internal void ForgetRuleForTest(string word){store.Current.Learned.Remove(word);}
    internal void SetAutomationForTest(bool enabled){store.Current.Enabled=enabled;input.Clear();}
#endif
    public bool LayoutValid {get{return sidebar!=null&&content!=null&&content.Top>=sidebar.Bottom&&content.Width>500;}}
    public bool SmokePages(){for(int i=0;i<6;i++){ShowPage(i);if(content.Controls.Count==0||title==null)return false;}ShowPage(0);return true;}
    public void QuitForTests(){exiting=true;Close();}
    public MainForm(bool background){
        Text="langswic"; Width=1000;Height=700;FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false;
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
        using(var icon=new Icon(stream))return (Icon)icon.Clone();
    }
    void MakeShell(){
        content=new Panel{Dock=DockStyle.Fill,AutoScroll=true};Controls.Add(content);
        sidebar=new FlowLayoutPanel{Dock=DockStyle.Top,Height=48,Padding=new Padding(18,4,0,0),WrapContents=false};Controls.Add(sidebar);
        string[] names={"Обзор","Настройки","Исключения","Словари","Горячие клавиши","Диагностика"};
        for(int i=0;i<names.Length;i++){int index=i;var b=new Button{Text=names[i],Width=i==4?155:130,Height=38,FlatStyle=FlatStyle.Flat,TabIndex=i,Font=new Font("Segoe UI Semibold",10),Margin=new Padding(2,0,2,0)};b.FlatAppearance.BorderSize=0;b.Click+=delegate{ShowPage(index);};sidebar.Controls.Add(b);nav.Add(b);}
        Controls.Add(new BlueHeader{Dock=DockStyle.Top,Height=128});
        var footer=new Panel{Dock=DockStyle.Bottom,Height=36,Padding=new Padding(22,0,20,0)};
        footer.Controls.Add(new Label{Text="v. 1.5.1   ·   Только на этом компьютере",Dock=DockStyle.Left,Width=400,TextAlign=ContentAlignment.MiddleLeft});
        var theme=new Button{Text="Сменить тему",Dock=DockStyle.Right,Width=135,FlatStyle=FlatStyle.Flat};theme.FlatAppearance.BorderSize=0;theme.Click+=delegate{ChangeTheme();};footer.Controls.Add(theme);Controls.Add(footer);
        trayMenu=new ContextMenuStrip();
        trayMenu.Items.Add("Открыть langswic",null,delegate{Show();WindowState=FormWindowState.Normal;Activate();});
        trayMenu.Items.Add("Включить / выключить",null,delegate{store.Current.Enabled=!store.Current.Enabled;store.Save();UpdateStatus();});
        trayMenu.Items.Add(new ToolStripSeparator());trayMenu.Items.Add("Выход",null,delegate{exiting=true;Close();});
        tray=new NotifyIcon{Icon=Icon,Text="langswic",Visible=true,ContextMenuStrip=trayMenu};tray.DoubleClick+=delegate{Show();WindowState=FormWindowState.Normal;Activate();};
    }
    void ChangeTheme(){store.Current.DarkTheme=!store.Current.DarkTheme;store.Save();SetTheme();ShowPage(page);}
    void SetTheme(){
        bool dark=store.Current.DarkTheme;
        bg=dark?Color.FromArgb(23,28,39):Color.White;
        card=dark?Color.FromArgb(31,38,52):Color.FromArgb(250,251,254);
        side=dark?Color.FromArgb(27,33,45):Color.FromArgb(246,248,252);
        ink=dark?Color.FromArgb(238,243,252):Color.FromArgb(26,31,53);
        muted=dark?Color.FromArgb(168,180,202):Color.FromArgb(111,119,143);
        accent=Color.FromArgb(36,113,243);border=dark?Color.FromArgb(62,72,91):Color.FromArgb(228,233,243);
        activeNav=dark?Color.FromArgb(40,59,87):Color.FromArgb(230,239,255);
        BackColor=bg;ForeColor=ink;sidebar.BackColor=side;content.BackColor=bg;
        foreach(var button in nav){button.ForeColor=ink;button.BackColor=side;button.FlatAppearance.MouseOverBackColor=activeNav;}
        foreach(Control child in Controls){if(child is BlueHeader || child==content || child==sidebar)continue;child.BackColor=side;child.ForeColor=muted;foreach(Control item in child.Controls){item.BackColor=side;item.ForeColor=muted;}}
    }
    void ShowPage(int index){
        page=index;content.SuspendLayout();foreach(Control old in content.Controls.Cast<Control>().ToArray())old.Dispose();content.Controls.Clear();subtitle=null;autoTile=shiftTile=smartTile=null;
        foreach(var b in nav){b.BackColor=nav.IndexOf(b)==index?activeNav:side;b.ForeColor=nav.IndexOf(b)==index?accent:ink;}
        var host=new FlowLayoutPanel{FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Width=Math.Max(500,content.ClientSize.Width-48),Location=new Point(24,16),Padding=new Padding(0),Margin=new Padding(0)};
        content.Controls.Add(host);
        string[] names={"Обзор","Настройки","Исключения","Словари","Горячие клавиши","Диагностика"};
        var eyebrow=TextLabel("LANGSWIC   /   "+names[index].ToUpperInvariant(),9,FontStyle.Bold,25);eyebrow.ForeColor=accent;if(index!=0)host.Controls.Add(eyebrow);else eyebrow.Dispose();
        title=TextLabel(names[index],28,FontStyle.Bold,69);if(index!=0)host.Controls.Add(title);else title.Visible=false;
        switch(index){case 0:Overview(host);break;case 1:SettingsPage(host);break;case 2:ExceptionsPage(host);break;case 3:DictionariesPage(host);break;case 4:HotkeysPage(host);break;case 5:DiagnosticsPage(host);break;}
        content.ResumeLayout();UpdateStatus();
    }
    Label TextLabel(string text,int size,FontStyle style,int height){return new Label{Text=text,Font=new Font("Segoe UI",size,style),ForeColor=ink,Height=height,Width=Math.Max(480,content.ClientSize.Width-70),TextAlign=ContentAlignment.MiddleLeft,Margin=new Padding(0,0,0,5)};}
    FlowLayoutPanel Card(FlowLayoutPanel host,string heading,string description){
        var outer=new SurfacePanel{Width=host.Width-12,AutoSize=true,Padding=new Padding(24),Margin=new Padding(0,0,0,17),BackColor=card,Surface=card,Canvas=bg,Outline=border};
        var body=new FlowLayoutPanel{FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoSize=true,Width=outer.Width-48,Location=new Point(24,20),Margin=new Padding(0)};
        outer.Controls.Add(body);host.Controls.Add(outer);
        if(!string.IsNullOrEmpty(heading))body.Controls.Add(new Label{Text=heading,Font=new Font("Segoe UI Semibold",14),ForeColor=ink,Height=35,Width=body.Width});
        if(!string.IsNullOrEmpty(description))body.Controls.Add(new Label{Text=description,ForeColor=muted,Height=44,Width=body.Width});
        return body;
    }
    Button ActionButton(string text,Action action,int width=170){var b=new PillButton{Text=text,Width=width,Height=40,FlatStyle=FlatStyle.Flat,BackColor=accent,ForeColor=Color.White,Margin=new Padding(0,7,10,7),Font=new Font("Segoe UI Semibold",10)};b.FlatAppearance.BorderSize=0;b.FlatAppearance.MouseOverBackColor=Color.FromArgb(30,96,211);b.Click+=delegate{action();};return b;}
    CheckBox Toggle(string text,bool value,Action<bool> changed){var c=new CheckBox{Text=text,Checked=value,AutoSize=false,Width=Math.Max(500,content.ClientSize.Width-130),Height=42,ForeColor=ink,BackColor=card,Margin=new Padding(0,2,0,2),FlatStyle=FlatStyle.Flat};c.CheckedChanged+=delegate{changed(c.Checked);store.Save();UpdateStatus();};return c;}
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
        var c=Card(host,"Автоматика","Каждый вид исправления можно выключить отдельно.");
        c.Controls.Add(Toggle("Автоматические исправления",store.Current.Enabled,v=>store.Current.Enabled=v));
        c.Controls.Add(Toggle("Ошибочная раскладка RU ↔ EN",store.Current.Layout,v=>store.Current.Layout=v));
        c.Controls.Add(Toggle("Распространённые опечатки и регистр",store.Current.Typos,v=>store.Current.Typos=v));
        c.Controls.Add(Toggle("Однозначные формы с «ё»",store.Current.Yo,v=>store.Current.Yo=v));
        c=Card(host,"Работа в Windows","langswic запускается в трее. Закрытие окна сворачивает его в трей.");
        c.Controls.Add(Toggle("Запускать при входе в Windows",store.Current.StartWithWindows,v=>store.Current.StartWithWindows=v));
        c=Card(host,"Звуки","Используйте WAV-файлы. Если файл не задан, звучит системный сигнал.");
        c.Controls.Add(Toggle("Звук при переключении раскладки",store.Current.SoundLayout,v=>store.Current.SoundLayout=v));
        c.Controls.Add(SoundPicker("Файл переключения",store.Current.LayoutSoundFile,v=>store.Current.LayoutSoundFile=v));
        c.Controls.Add(Toggle("Звук при исправлении опечатки",store.Current.SoundTypos,v=>store.Current.SoundTypos=v));
        c.Controls.Add(SoundPicker("Файл опечатки",store.Current.TypoSoundFile,v=>store.Current.TypoSoundFile=v));
        c=Card(host,"Перенос данных","Экспорт содержит настройки, исключения, пользовательские слова и выученные правила.");
        var row=Row();row.Controls.Add(ActionButton("Экспорт JSON",delegate{using(var d=new System.Windows.Forms.SaveFileDialog{Filter="JSON (*.json)|*.json",FileName="langswic-settings.json"})if(d.ShowDialog()==DialogResult.OK)store.Export(d.FileName);},150));
        row.Controls.Add(ActionButton("Импорт JSON",delegate{using(var d=new System.Windows.Forms.OpenFileDialog{Filter="JSON (*.json)|*.json"})if(d.ShowDialog()==DialogResult.OK){try{store.Import(d.FileName);engine.AddUserWords(store.Current);RegisterHotkeys();ShowPage(page);}catch(Exception e){MessageBox.Show(e.Message,"Ошибка импорта",MessageBoxButtons.OK,MessageBoxIcon.Error);}}},150));c.Controls.Add(row);
    }
    FlowLayoutPanel SoundPicker(string caption,string value,Action<string> set){var r=Row();var box=new TextBox{Text=value,Width=390};r.Controls.Add(box);r.Controls.Add(ActionButton("Выбрать WAV",delegate{using(var d=new System.Windows.Forms.OpenFileDialog{Filter="WAV (*.wav)|*.wav"})if(d.ShowDialog()==DialogResult.OK){box.Text=d.FileName;set(box.Text);store.Save();}},135));box.Leave+=delegate{set(box.Text);store.Save();};return r;}
    void ExceptionsPage(FlowLayoutPanel host){
        var c=Card(host,"Слова-исключения","Эти слова автоматика оставляет без изменений. По одному слову в строке.");
        var words=new TextBox{Multiline=true,ScrollBars=ScrollBars.Vertical,Width=c.Width-8,Height=120,Text=string.Join(Environment.NewLine,store.Current.ExcludedWords.ToArray())};c.Controls.Add(words);
        c.Controls.Add(ActionButton("Сохранить слова",delegate{store.Current.ExcludedWords=Lines(words.Text);store.Save();},165));
        c=Card(host,"Правила для приложений","Укажите имя процесса без .exe. Снимите все три флажка для полного отключения автоматики.");
        var binding=new BindingList<AppRule>(store.Current.Apps.Select(a=>new AppRule{Process=a.Process,Layout=a.Layout,Typos=a.Typos,Yo=a.Yo}).ToList());
        var grid=new DataGridView{Width=c.Width-8,Height=250,DataSource=binding,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,RowHeadersVisible=false,AllowUserToAddRows=true,BackgroundColor=card,ForeColor=ink};
        if(grid.Columns.Count>=4){grid.Columns[0].HeaderText="Процесс";grid.Columns[1].HeaderText="Раскладка";grid.Columns[2].HeaderText="Опечатки";grid.Columns[3].HeaderText="Ё";}
        c.Controls.Add(grid);
        c.Controls.Add(ActionButton("Сохранить правила",delegate{grid.EndEdit();store.Current.Apps=binding.Where(a=>!string.IsNullOrWhiteSpace(a.Process)).Select(a=>new AppRule{Process=a.Process.Trim().Replace(".exe","").ToLowerInvariant(),Layout=a.Layout,Typos=a.Typos,Yo=a.Yo}).ToList();store.Save();MessageBox.Show("Правила сохранены.","langswic");},165));
    }
    static List<string> Lines(string text){return text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(s=>s.Trim()).Where(s=>s.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();}
    void DictionariesPage(FlowLayoutPanel host){
        var c=Card(host,"Пользовательский словарь","Добавьте слова, которые должны распознаваться при определении раскладки. По одному слову в строке.");
        c.Controls.Add(new Label{Text="Русские слова",Width=c.Width,Height=28,ForeColor=ink});
        var ru=new TextBox{Multiline=true,ScrollBars=ScrollBars.Vertical,Width=c.Width-8,Height=105,Text=string.Join(Environment.NewLine,store.Current.RuWords.ToArray())};c.Controls.Add(ru);
        c.Controls.Add(new Label{Text="Английские слова",Width=c.Width,Height=28,ForeColor=ink});
        var en=new TextBox{Multiline=true,ScrollBars=ScrollBars.Vertical,Width=c.Width-8,Height=105,Text=string.Join(Environment.NewLine,store.Current.EnWords.ToArray())};c.Controls.Add(en);
        c.Controls.Add(ActionButton("Сохранить словари",delegate{store.Current.RuWords=Lines(ru.Text);store.Current.EnWords=Lines(en.Text);engine.AddUserWords(store.Current);store.Save();},180));
        c=Card(host,"Выученные правила","Правило «исходное → исходное» означает: не исправлять это слово. Выберите строку для удаления.");
        var list=new ListBox{Width=c.Width-8,Height=150};
        foreach(var p in store.Current.Learned.OrderBy(x=>x.Key))list.Items.Add(p.Key+" → "+p.Value);
        c.Controls.Add(list);
        c.Controls.Add(ActionButton("Удалить правило",delegate{if(list.SelectedIndex<0)return;string key=list.SelectedItem.ToString().Split(new[]{" → "},StringSplitOptions.None)[0];store.Current.Learned.Remove(key);store.Save();list.Items.RemoveAt(list.SelectedIndex);},160));
    }
    void HotkeysPage(FlowLayoutPanel host){
        var c=Card(host,"Ручное управление","Double Shift и Pause/Break преобразуют выделение или последнее слово и отменяют недавнее исправление. Сочетания с Ctrl/Alt/Shift/Win регистрируются в Windows.");
        c.Controls.Add(Toggle("Double Shift",store.Current.DoubleShift,v=>store.Current.DoubleShift=v));
        c.Controls.Add(Toggle("Одиночный Shift переключает раскладку",store.Current.SingleShift,v=>store.Current.SingleShift=v));
        c.Controls.Add(Toggle("Pause/Break",store.Current.Pause,v=>store.Current.Pause=v));
        var selected=new TextBox{Text=store.Current.SelectedHotkey,Width=190};
        var manualWord=new TextBox{Text=store.Current.WordHotkey,Width=190};
        var undo=new TextBox{Text=store.Current.UndoHotkey,Width=190};
        var toggle=new TextBox{Text=store.Current.ToggleHotkey,Width=190};
        c.Controls.Add(HotkeyRow("Последнее слово",manualWord));c.Controls.Add(HotkeyRow("Выделенный текст",selected));c.Controls.Add(HotkeyRow("Отмена",undo));c.Controls.Add(HotkeyRow("Автоматика",toggle));
        var status=new Label{Width=c.Width,Height=60,ForeColor=muted,Text=HotkeyStatus()};
        c.Controls.Add(ActionButton("Применить сочетания",delegate{store.Current.WordHotkey=manualWord.Text;store.Current.SelectedHotkey=selected.Text;store.Current.UndoHotkey=undo.Text;store.Current.ToggleHotkey=toggle.Text;store.Save();RegisterHotkeys();status.Text=HotkeyStatus();},190));c.Controls.Add(status);
    }
    FlowLayoutPanel HotkeyRow(string name,TextBox box){var r=Row();r.Controls.Add(new Label{Text=name,Width=190,Height=34,ForeColor=ink,TextAlign=ContentAlignment.MiddleLeft});r.Controls.Add(box);return r;}
    string HotkeyStatus(){return hotkeyErrors.Count==0?"Сочетания зарегистрированы.":string.Join("; ",hotkeyErrors.Values.ToArray());}
    void DiagnosticsPage(FlowLayoutPanel host){
        var c=Card(host,"Состояние","Здесь нет содержимого набранного текста. Счётчики существуют только в памяти до выхода из программы.");
        subtitle=new Label{Width=c.Width,Height=180,ForeColor=ink,Font=new Font("Segoe UI",11)};c.Controls.Add(subtitle);
        var row=Row();row.Controls.Add(ActionButton("Обновить",UpdateStatus,125));row.Controls.Add(ActionButton("Открыть данные",delegate{Directory.CreateDirectory(SettingsStore.DirectoryPath);System.Diagnostics.Process.Start(SettingsStore.DirectoryPath);},150));c.Controls.Add(row);
        c=Card(host,"Безопасность и совместимость","Защищённые поля и неизвестные раскладки пропускаются. В приложениях из списка исключений автоматика отключается или ограничивается. Права администратора не запрашиваются.");
        c.Controls.Add(new Label{Text="Автоматическое исправление требует доступного редактируемого поля UI Automation. При недоступном поле ввода изменений не происходит. Работа через RDP, игры с Raw Input и приложения с повышенными правами не гарантируется.",Width=c.Width,Height=90,ForeColor=ink});
    }
    void UpdateStatus(){
        if(IsDisposed || input==null)return;
        if(autoTile!=null){updatingTiles=true;try{autoTile.Checked=store.Current.Enabled&&store.Current.Layout;shiftTile.Checked=store.Current.SingleShift;smartTile.Checked=store.Current.Typos||store.Current.Yo;}finally{updatingTiles=false;}}
        tray.Text=store.Current.Enabled?"langswic · автоматика включена":"langswic · автоматика выключена";
        if(subtitle!=null&&!subtitle.IsDisposed){
            if(page==0)subtitle.Text=store.Current.Enabled?"Автоматика включена · локальная обработка текста":"Автоматика выключена · ручные команды доступны";
            if(page==5)subtitle.Text="Перехват: "+(input.HookActive?"активен":"нет")+"\r\n"+engine.DictionaryStatus+"\r\nАктивное приложение: "+input.ActiveProcess+"\r\nИсправлений за сеанс: "+input.Corrections+"\r\nЗащищённых или неподдерживаемых полей: "+input.SkippedProtected+"\r\nОшибок ввода: "+input.FailedInjection+"\r\nНеподтверждённых смен раскладки: "+input.FailedLayoutChanges+"\r\n"+input.LayoutDiagnostic+"\r\nПоследнее решение: "+input.LastReason;
        }
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
