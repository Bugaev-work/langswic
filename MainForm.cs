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
            using(var brush=new SolidBrush(hover?Color.FromArgb(207,70,5):BackColor))e.Graphics.FillPath(brush,p);
        }
        TextRenderer.DrawText(e.Graphics,Text,Font,r,ForeColor,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
        if(Focused)ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(7,5,Width-14,Height-10),Color.White,BackColor);
    }
}
public sealed class MainForm : Form {
    readonly SettingsStore store=new SettingsStore();
    readonly LanguageEngine engine=new LanguageEngine();
    InputService input;
    Panel sidebar,content; Label title,subtitle,brandTitle,brandBadge,brandTag; NotifyIcon tray; ContextMenuStrip trayMenu;
    readonly List<Button> nav=new List<Button>();
    readonly Dictionary<int,string> hotkeyErrors=new Dictionary<int,string>();
    Timer diagnosticTimer; bool exiting=false; int page=0;
    Color bg,card,ink,muted,accent,border,side,activeNav;
    public bool HookReady {get{return input!=null&&input.HookActive;}}
    public string InputDiagnostic {get{return input==null?"not started":input.LastReason+"; corrections="+input.Corrections+"; process="+input.ActiveProcess+"; "+TextAccess.ReadState;}}
    public int InputVerifiedValueReplacements {get{return input==null?0:input.VerifiedValueReplacements;}}
    public int InputCorrections {get{return input==null?0:input.Corrections;}}
#if INPUT_TEST
    internal System.Threading.Tasks.Task DelayInputWorkerForTest(int ms){return input.DelayWorkerForTest(ms);}
    internal void ForgetRuleForTest(string word){store.Current.Learned.Remove(word);}
#endif
    public bool LayoutValid {get{return sidebar!=null&&content!=null&&sidebar.Right<=content.Left&&content.Width>400;}}
    public bool SmokePages(){for(int i=0;i<6;i++){ShowPage(i);if(content.Controls.Count==0||title==null)return false;}ShowPage(0);return true;}
    public void QuitForTests(){exiting=true;Close();}
    public MainForm(bool background){
        Text="Fast Switcher"; Width=1020;Height=740;MinimumSize=new Size(780,580);
        StartPosition=FormStartPosition.CenterScreen; Font=new Font("Segoe UI",10);
        Icon=MakeIcon();
        engine.AddUserWords(store.Current);
        MakeShell(); SetTheme(); ShowPage(0);
        input=new InputService(this,store,engine);input.Changed+=UpdateStatus;
        SystemEvents.PowerModeChanged+=PowerChanged;
        Shown+=delegate{input.Start();RegisterHotkeys();if(background)Hide();};
        FormClosing+=delegate(object s,FormClosingEventArgs e){if(!exiting && e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();tray.ShowBalloonTip(1500,"Fast Switcher","Программа продолжает работать в трее.",ToolTipIcon.Info);}};
        FormClosed+=delegate{SystemEvents.PowerModeChanged-=PowerChanged;if(input!=null)input.Dispose();engine.Dispose();UnregisterHotkeys();if(tray!=null)tray.Dispose();};
        diagnosticTimer=new Timer{Interval=1000};diagnosticTimer.Tick+=delegate{UpdateStatus();};diagnosticTimer.Start();
    }
    void PowerChanged(object sender,PowerModeChangedEventArgs e){
        if(!IsHandleCreated||IsDisposed)return;
        BeginInvoke((Action)(()=>{if(input==null)return;if(e.Mode==PowerModes.Suspend)input.Clear();if(e.Mode==PowerModes.Resume){input.Restart();RegisterHotkeys();}}));
    }
    static Icon MakeIcon(){
        var bitmap=new Bitmap(64,64);using(var g=Graphics.FromImage(bitmap)){g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Color.Transparent);using(var b=new SolidBrush(Color.FromArgb(245,105,27)))g.FillEllipse(b,2,2,60,60);using(var f=new Font("Segoe UI",34,FontStyle.Bold,GraphicsUnit.Pixel))using(var b=new SolidBrush(Color.White))g.DrawString("F",f,b,18,5);}
        return Icon.FromHandle(bitmap.GetHicon());
    }
    void MakeShell(){
        sidebar=new Panel{Dock=DockStyle.Left,Width=236,Padding=new Padding(20,27,20,22)};Controls.Add(sidebar);
        var brand=new Panel{Dock=DockStyle.Top,Height=112};
        brandBadge=new Label{Text="F",Width=46,Height=46,Left=1,Top=3,TextAlign=ContentAlignment.MiddleCenter,Font=new Font("Segoe UI Semibold",24),ForeColor=Color.White};brand.Controls.Add(brandBadge);
        brandTitle=new Label{Text="Fast Switcher",Left=59,Top=3,Width=142,Height=30,Font=new Font("Segoe UI Semibold",15),TextAlign=ContentAlignment.MiddleLeft};brand.Controls.Add(brandTitle);
        brandTag=new Label{Text="ПИШИТЕ СВОБОДНО",Left=60,Top=35,Width=145,Height=22,Font=new Font("Segoe UI",8,FontStyle.Bold)};brand.Controls.Add(brandTag);
        string[] names={"Обзор","Настройки","Исключения","Словари","Горячие клавиши","Диагностика"};
        for(int i=names.Length-1;i>=0;i--){int index=i;var b=new Button{Text=names[i],Height=48,Dock=DockStyle.Top,FlatStyle=FlatStyle.Flat,TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(15,0,0,0),TabIndex=i+1,Font=new Font("Segoe UI Semibold",10)};b.FlatAppearance.BorderSize=0;b.Click+=delegate{ShowPage(index);};sidebar.Controls.Add(b);nav.Insert(0,b);}
        sidebar.Controls.Add(brand);
        var theme=new Button{Text="◐   Сменить тему",Height=46,Dock=DockStyle.Bottom,FlatStyle=FlatStyle.Flat,TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(15,0,0,0)};theme.FlatAppearance.BorderSize=0;theme.Click+=delegate{store.Current.DarkTheme=!store.Current.DarkTheme;store.Save();SetTheme();ShowPage(page);};sidebar.Controls.Add(theme);
        content=new Panel{Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(38,30,38,30)};Controls.Add(content);content.BringToFront();
        trayMenu=new ContextMenuStrip();
        trayMenu.Items.Add("Открыть Fast Switcher",null,delegate{Show();WindowState=FormWindowState.Normal;Activate();});
        trayMenu.Items.Add("Включить / выключить",null,delegate{store.Current.Enabled=!store.Current.Enabled;store.Save();UpdateStatus();});
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Выход",null,delegate{exiting=true;Close();});
        tray=new NotifyIcon{Icon=Icon,Text="Fast Switcher",Visible=true,ContextMenuStrip=trayMenu};
        tray.DoubleClick+=delegate{Show();WindowState=FormWindowState.Normal;Activate();};
    }
    void SetTheme(){
        bool dark=store.Current.DarkTheme;
        bg=dark?Color.FromArgb(23,24,25):Color.FromArgb(255,250,244);
        card=dark?Color.FromArgb(36,37,39):Color.White;
        side=dark?Color.FromArgb(29,30,32):Color.FromArgb(255,246,235);
        ink=dark?Color.FromArgb(247,245,242):Color.FromArgb(31,30,29);
        muted=dark?Color.FromArgb(172,169,165):Color.FromArgb(112,107,103);
        accent=dark?Color.FromArgb(255,138,66):Color.FromArgb(235,92,18);
        border=dark?Color.FromArgb(64,63,61):Color.FromArgb(239,224,208);
        activeNav=dark?Color.FromArgb(71,47,34):Color.FromArgb(255,228,206);
        BackColor=bg;sidebar.BackColor=side;sidebar.ForeColor=ink;content.BackColor=bg;
        brandTitle.ForeColor=ink;brandTag.ForeColor=muted;brandBadge.BackColor=accent;
        foreach(Control c in sidebar.Controls){if(c is Button){c.ForeColor=ink;c.BackColor=side;((Button)c).FlatAppearance.MouseOverBackColor=activeNav;}}
        ForeColor=ink;
    }
    void ShowPage(int index){
        page=index;content.SuspendLayout();foreach(Control old in content.Controls.Cast<Control>().ToArray())old.Dispose();content.Controls.Clear();subtitle=null;
        foreach(var b in nav){b.BackColor=nav.IndexOf(b)==index?activeNav:side;b.ForeColor=nav.IndexOf(b)==index?accent:ink;}
        var host=new FlowLayoutPanel{FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Width=Math.Max(500,content.ClientSize.Width-76),Location=new Point(34,25),Padding=new Padding(0),Margin=new Padding(0)};
        content.Controls.Add(host);
        string[] names={"Обзор","Настройки","Исключения","Словари","Горячие клавиши","Диагностика"};
        var eyebrow=TextLabel("FAST SWITCHER   /   "+names[index].ToUpperInvariant(),9,FontStyle.Bold,25);eyebrow.ForeColor=accent;host.Controls.Add(eyebrow);
        title=TextLabel(names[index],28,FontStyle.Bold,69);host.Controls.Add(title);
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
    Button ActionButton(string text,Action action,int width=170){var b=new PillButton{Text=text,Width=width,Height=40,FlatStyle=FlatStyle.Flat,BackColor=accent,ForeColor=Color.White,Margin=new Padding(0,7,10,7),Font=new Font("Segoe UI Semibold",10)};b.FlatAppearance.BorderSize=0;b.FlatAppearance.MouseOverBackColor=Color.FromArgb(207,70,5);b.Click+=delegate{action();};return b;}
    CheckBox Toggle(string text,bool value,Action<bool> changed){var c=new CheckBox{Text=text,Checked=value,AutoSize=false,Width=600,Height=42,ForeColor=ink,BackColor=card,Margin=new Padding(0,2,0,2),FlatStyle=FlatStyle.Flat};c.CheckedChanged+=delegate{changed(c.Checked);store.Save();UpdateStatus();};return c;}
    FlowLayoutPanel Row(){return new FlowLayoutPanel{FlowDirection=FlowDirection.LeftToRight,WrapContents=false,AutoSize=true,Width=Math.Max(380,content.ClientSize.Width-135),Margin=new Padding(0,3,0,3)};}
    void Overview(FlowLayoutPanel host){
        var c=Card(host,"Пишет вместе с вами","Исправляет уверенно распознанные слова после пробела или некоторых знаков препинания. Неоднозначные слова сохраняет.");
        subtitle=new Label{Width=c.Width,Height=36,Font=new Font("Segoe UI Semibold",15),ForeColor=accent};c.Controls.Add(subtitle);
        var row=Row();row.Controls.Add(ActionButton("Включить / выключить",delegate{store.Current.Enabled=!store.Current.Enabled;store.Save();UpdateStatus();},195));row.Controls.Add(ActionButton("Горячие клавиши",delegate{ShowPage(4);},175));c.Controls.Add(row);
        c=Card(host,"Быстрые действия","Double Shift сначала преобразует выделенный текст; повторное нажатие отменяет последнее исправление. Без выделения преобразуется последнее слово. Pause/Break работает так же.");
        c.Controls.Add(new Label{Text="Настройте сочетания и исключения в разделах слева. Настройки и выученные правила хранятся только на этом компьютере.",Width=c.Width,Height=60,ForeColor=ink});
        c=Card(host,"Проверка алгоритма","Введите пример без передачи данных в другие программы.");
        var inputBox=new TextBox{Width=320,Height=32,Font=Font};var result=new Label{Width=c.Width,Height=50,ForeColor=ink};
        var r=Row();r.Controls.Add(inputBox);r.Controls.Add(ActionButton("Проверить",delegate{var d=engine.Decide(inputBox.Text,"",store.Current);result.Text=d.Kind==ChangeKind.None?"Без изменения: "+d.Reason:d.Text+"  ·  "+d.Reason;},120));c.Controls.Add(r);c.Controls.Add(result);
    }
    void SettingsPage(FlowLayoutPanel host){
        var c=Card(host,"Автоматика","Каждый вид исправления можно выключить отдельно.");
        c.Controls.Add(Toggle("Автоматические исправления",store.Current.Enabled,v=>store.Current.Enabled=v));
        c.Controls.Add(Toggle("Ошибочная раскладка RU ↔ EN",store.Current.Layout,v=>store.Current.Layout=v));
        c.Controls.Add(Toggle("Распространённые опечатки и регистр",store.Current.Typos,v=>store.Current.Typos=v));
        c.Controls.Add(Toggle("Однозначные формы с «ё»",store.Current.Yo,v=>store.Current.Yo=v));
        c=Card(host,"Работа в Windows","Fast Switcher запускается в трее. Закрытие окна сворачивает его в трей.");
        c.Controls.Add(Toggle("Запускать при входе в Windows",store.Current.StartWithWindows,v=>store.Current.StartWithWindows=v));
        c=Card(host,"Звуки","Используйте WAV-файлы. Если файл не задан, звучит системный сигнал.");
        c.Controls.Add(Toggle("Звук при переключении раскладки",store.Current.SoundLayout,v=>store.Current.SoundLayout=v));
        c.Controls.Add(SoundPicker("Файл переключения",store.Current.LayoutSoundFile,v=>store.Current.LayoutSoundFile=v));
        c.Controls.Add(Toggle("Звук при исправлении опечатки",store.Current.SoundTypos,v=>store.Current.SoundTypos=v));
        c.Controls.Add(SoundPicker("Файл опечатки",store.Current.TypoSoundFile,v=>store.Current.TypoSoundFile=v));
        c=Card(host,"Перенос данных","Экспорт содержит настройки, исключения, пользовательские слова и выученные правила.");
        var row=Row();row.Controls.Add(ActionButton("Экспорт JSON",delegate{using(var d=new System.Windows.Forms.SaveFileDialog{Filter="JSON (*.json)|*.json",FileName="fast-switcher-settings.json"})if(d.ShowDialog()==DialogResult.OK)store.Export(d.FileName);},150));
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
        c.Controls.Add(ActionButton("Сохранить правила",delegate{grid.EndEdit();store.Current.Apps=binding.Where(a=>!string.IsNullOrWhiteSpace(a.Process)).Select(a=>new AppRule{Process=a.Process.Trim().Replace(".exe","").ToLowerInvariant(),Layout=a.Layout,Typos=a.Typos,Yo=a.Yo}).ToList();store.Save();MessageBox.Show("Правила сохранены.","Fast Switcher");},165));
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
        tray.Text=store.Current.Enabled?"Fast Switcher · автоматика включена":"Fast Switcher · автоматика выключена";
        if(subtitle!=null&&!subtitle.IsDisposed){
            if(page==0)subtitle.Text=store.Current.Enabled?"● Автоматика включена":"○ Автоматика выключена";
            if(page==5)subtitle.Text="Перехват: "+(input.HookActive?"активен":"нет")+"\r\n"+engine.DictionaryStatus+"\r\nАктивное приложение: "+input.ActiveProcess+"\r\nИсправлений за сеанс: "+input.Corrections+"\r\nЗащищённых или неподдерживаемых полей: "+input.SkippedProtected+"\r\nОшибок ввода: "+input.FailedInjection+"\r\nПоследнее решение: "+input.LastReason;
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
