using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Forms;

namespace FastSwitcher {
public sealed class InputService : IDisposable {
#if INPUT_TEST
    internal static bool AcceptSyntheticInput;
    internal System.Threading.Tasks.Task DelayWorkerForTest(int milliseconds){
        var ready=new System.Threading.Tasks.TaskCompletionSource<bool>();
        Post(()=>{ready.SetResult(true);Thread.Sleep(milliseconds);});return ready.Task;
    }
#endif
    readonly Form owner; readonly IntPtr ownerHandle; readonly SettingsStore store; LanguageEngine engine;
    readonly BlockingCollection<Action> work=new BlockingCollection<Action>(256);
    readonly List<WorkTimer> timers=new List<WorkTimer>();
    readonly InputPump pump; Thread worker; volatile bool stopped; int lostEvents,notifyPending;
    string word="",context="",lastWord="",lastDelimiter="";
    bool earlyWord; string earlyOriginal=""; int earlySourceLanguage,prefixToken,layoutToken;
    IntPtr currentWindow=IntPtr.Zero,currentFocus=IntPtr.Zero;
    string process=""; AppRule policy; int[] currentElementId;
    long sequence; uint lastShiftTime; bool shiftOnly,shiftPressed,controlPressed,altPressed,pendingManual; int shiftToken;
    ActionInfo lastAction;
    public int Corrections {get;private set;}
    public int SkippedProtected {get;private set;}
    public int FailedInjection {get;private set;}
    public int FailedLayoutChanges {get;private set;}
    public int VerifiedValueReplacements {get;private set;}
    public string ActiveProcess {get{return process;}}
    public string LastReason {get;private set;}
    public bool HookActive {get{return pump.Active;}}
    public event Action Changed;
    sealed class ActionInfo { public IntPtr Window,Focus; public int[] ElementId; public long Sequence; public string Before,After,Delimiter; public ChangeKind Kind; public DateTime At; public bool Selection,NativeSelection; }
    int[] FocusId(){return currentElementId==null?null:(int[])currentElementId.Clone();}
    public InputService(Form owner,SettingsStore store,LanguageEngine previewEngine){
        this.owner=owner;this.ownerHandle=owner.Handle;this.store=store;
        pump=new InputPump(k=>Post(()=>ProcessKey(k)),()=>Post(Reset),()=>store.Current.Pause);LastReason="Ожидание ввода";
    }
    void Post(Action action){
        if(stopped)return;
        try{if(!work.TryAdd(action)){Interlocked.Exchange(ref lostEvents,1);pump.Invalidate();}}catch(InvalidOperationException){}
    }
    void RunWorker(){
        engine=new LanguageEngine();
        try{foreach(var action in work.GetConsumingEnumerable()){
            if(stopped)break;
            try{if(Interlocked.Exchange(ref lostEvents,0)!=0)Reset();action();}
            catch(Exception e){Reset();LastReason="Операция пропущена: "+e.GetType().Name;Notify();}
        }}finally{engine.Dispose();}
    }
    WorkTimer CreateTimer(int interval){
        var timer=new WorkTimer(Post,t=>{lock(timers)timers.Remove(t);},interval);
        lock(timers)timers.Add(timer);return timer;
    }
    public void Start(){
        if(worker==null){worker=new Thread(RunWorker){IsBackground=true,Name="Fast Switcher text worker"};worker.SetApartmentState(ApartmentState.MTA);worker.Start();}
        pump.Start();LastReason=HookActive?"Перехват активен":"Ошибка установки перехвата";Notify();
    }
    public void Restart(){pump.Stop();Post(Reset);pump.Start();LastReason=HookActive?"Перехват восстановлен":"Ошибка восстановления";Notify();}
    public void Clear(){pump.Invalidate();Post(Reset);}
    public void Dispose(){
        stopped=true;pump.Dispose();work.CompleteAdding();
        WorkTimer[] active;lock(timers)active=timers.ToArray();foreach(var timer in active)timer.Dispose();
        if(worker!=null)worker.Join(500);
    }
    void Notify(){
        if(stopped || Interlocked.Exchange(ref notifyPending,1)!=0)return;
        try{owner.BeginInvoke((Action)delegate{Interlocked.Exchange(ref notifyPending,0);if(!stopped && Changed!=null)Changed();});}catch(InvalidOperationException){Interlocked.Exchange(ref notifyPending,0);}
    }
    void Remember(string source,string target){
        try{owner.BeginInvoke((Action)delegate{if(stopped)return;store.Current.Learned[source.ToLowerInvariant()]=target.ToLowerInvariant();store.Save();});}
        catch(InvalidOperationException){}
    }
    void Reset(){prefixToken++;shiftToken++;layoutToken++;shiftOnly=false;pendingManual=false;word="";context="";lastWord="";lastDelimiter="";lastAction=null;earlyWord=false;earlyOriginal="";earlySourceLanguage=0;}
    void AfterModifiers(Action action){var t=CreateTimer(25);int ticks=0;long generation=pump.Generation;IntPtr window=Native.GetForegroundWindow();t.Tick+=delegate{ticks++;
        if(stopped || pump.Generation!=generation || Native.GetForegroundWindow()!=window){t.Stop();t.Dispose();return;}
        bool released=!Native.AsyncDown(Native.VK_SHIFT)&&!Native.AsyncDown(Native.VK_CONTROL)&&!Native.AsyncDown(Native.VK_MENU);
        if(released||ticks>20){t.Stop();t.Dispose();if(released)action();else{LastReason="Клавиша-модификатор удерживается — замена пропущена";Notify();}}
    };t.Start();}
    void ScheduleSingleShift(int token){
        var t=CreateTimer(470);t.Tick+=delegate{t.Stop();t.Dispose();if(token!=shiftToken||shiftPressed||!store.Current.SingleShift)return;
            IntPtr w,f,l;if(SafeFocus(out w,out f,out l)){SwitchLayout(w,l);Sound(ChangeKind.Layout);LastReason="Ручное переключение раскладки";Notify();}
        };t.Start();
    }
    static bool Letter(char c){return char.IsLetter(c)||c=='ё'||c=='Ё';}
    bool SafeFocus(out IntPtr hwnd,out IntPtr focus,out IntPtr layout){
        hwnd=Native.GetForegroundWindow();focus=IntPtr.Zero;layout=IntPtr.Zero;
        if(hwnd==IntPtr.Zero || hwnd==ownerHandle)return false;
        uint pid; uint thread=Native.GetWindowThreadProcessId(hwnd,out pid);
        if(pid==0 || pid==Process.GetCurrentProcess().Id)return false;
        var info=new Native.GUITHREADINFO{cbSize=Marshal.SizeOf(typeof(Native.GUITHREADINFO))};
        if(!Native.GetGUIThreadInfo(thread,ref info)){LastReason="Поле ввода недоступно";return false;}
        focus=info.hwndFocus;
        if(focus==IntPtr.Zero){LastReason="Фокус ввода не определён";return false;}
        uint focusPid;uint focusThread=Native.GetWindowThreadProcessId(focus,out focusPid);
        layout=Native.GetKeyboardLayout(focusThread==0?thread:focusThread);
        int language=(int)((long)layout&0xffff);
        if(language!=0x0409 && language!=0x0419){LastReason="Активна неподдерживаемая раскладка";return false;}
        try {
            if(hwnd!=currentWindow || focus!=currentFocus){
                string p=Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant();
                Reset();process=p;currentWindow=hwnd;currentFocus=focus;currentElementId=null;
                policy=store.Current.Apps.FirstOrDefault(a=>string.Equals(a.Process,p,StringComparison.OrdinalIgnoreCase));Notify();
            }
            if(policy!=null && !policy.Layout && !policy.Typos && !policy.Yo){LastReason="Приложение исключено";return false;}
            string focusClass=Native.Class(focus);
            bool nativeEdit=focusClass.IndexOf("edit",StringComparison.OrdinalIgnoreCase)>=0;
            if(nativeEdit && (Native.GetWindowLong(focus,-16)&0x20)!=0){SkippedProtected++;LastReason="Поле пароля";return false;}
            if(nativeEdit){
                if((Native.GetWindowLong(focus,-16)&0x800)!=0){LastReason="Поле только для чтения";return false;}
                return true;
            }
            var element=TextAccess.FocusedEditable();
            if(element==null || element.Current.IsPassword || !element.Current.IsEnabled){SkippedProtected++;LastReason="Защищённое или неподдерживаемое поле";return false;}
            var id=element.GetRuntimeId();
            if(currentElementId!=null && id!=null && !currentElementId.SequenceEqual(id)){Reset();}
            currentElementId=id;
            var kind=element.Current.ControlType;
            // FocusedEditable already proved that this is an editable provider.
            // WebView and custom editors need not report the standard Edit role.
            object vp=null;
            if(element.TryGetCurrentPattern(ValuePattern.Pattern,out vp) && ((ValuePattern)vp).Current.IsReadOnly)return false;
            if(kind==ControlType.ComboBox && vp==null && !element.TryGetCurrentPattern(TextPattern.Pattern,out vp)){
                LastReason="Список без редактируемого текста";return false;
            }
            return true;
        }catch{SkippedProtected++;LastReason="Не удалось проверить поле ввода";return false;}
    }
    IntPtr ProcessKey(InputPump.KeyEvent input){
        bool down=input.Down,up=input.Up;var k=input.Key;
        if(input.Window!=Native.GetForegroundWindow()){Reset();return IntPtr.Zero;}
        try {
            if(k.vk==Native.VK_SHIFT || k.vk==0xA0 || k.vk==0xA1){
                if(up){shiftPressed=false;lastShiftTime=k.time;
                    if(pendingManual){pendingManual=false;Post(()=>AfterModifiers(ManualOrUndoCore));}
                    else if(shiftOnly && store.Current.SingleShift)Post(()=>ScheduleSingleShift(shiftToken));
                    return IntPtr.Zero;
                }
                if(shiftPressed)return IntPtr.Zero;
                shiftPressed=true;
                bool doubleShift=store.Current.DoubleShift && shiftOnly && unchecked(k.time-lastShiftTime)<430;
                shiftToken++;
                if(doubleShift){shiftOnly=false;pendingManual=true;}else shiftOnly=true;
                return IntPtr.Zero;
            }
            if(k.vk==Native.VK_CONTROL||k.vk==0xA2||k.vk==0xA3){controlPressed=down;shiftOnly=false;return IntPtr.Zero;}
            if(k.vk==Native.VK_MENU||k.vk==0xA4||k.vk==0xA5){altPressed=down;shiftOnly=false;return IntPtr.Zero;}
            if(up)return IntPtr.Zero;
            if(k.vk==Native.VK_PAUSE && store.Current.Pause){Post(()=>AfterModifiers(ManualOrUndoCore));return IntPtr.Zero;}
            if((controlPressed||Native.AsyncDown(Native.VK_CONTROL)) && (altPressed||Native.AsyncDown(Native.VK_MENU)) && k.vk>=0x70 && k.vk<=0x7B)
                return IntPtr.Zero;
            shiftOnly=false;shiftToken++; sequence=input.Generation;
            if(!store.Current.Enabled){Reset();return IntPtr.Zero;}
            IntPtr window,focus,layout;
            if(!SafeFocus(out window,out focus,out layout) || window!=input.Window || focus!=input.Focus){Reset();return IntPtr.Zero;}
            layout=input.Layout;
            if(controlPressed||altPressed) {Reset();return IntPtr.Zero;}
            if(k.vk==Native.VK_BACK){earlyWord=false;earlyOriginal="";lastAction=null;if(word.Length>0)word=word.Substring(0,word.Length-1);else Reset();return IntPtr.Zero;}
            if(k.vk==Native.VK_LEFT||k.vk==Native.VK_RIGHT||k.vk==Native.VK_UP||k.vk==Native.VK_DOWN||k.vk==0x24||k.vk==0x23||k.vk==0x2E){Reset();return IntPtr.Zero;}
            string typed=Native.Typed(k.vk,k.scan,layout,shiftPressed);
            if(typed.Length!=1){if(k.vk==Native.VK_RETURN||k.vk==Native.VK_TAB)Reset();return IntPtr.Zero;}
            char ch=typed[0];
            if(Letter(ch)){
                if(earlyWord){
                    bool sourceLayout=((long)layout&0xffff)==earlySourceLanguage;
                    char corrected=sourceLayout?engine.Convert(ch.ToString())[0]:ch;
                    word+=ch;earlyOriginal+=sourceLayout?ch:engine.Convert(ch.ToString())[0];
                    context+=ch;if(context.Length>128)context=context.Substring(context.Length-128);
                    if(lastAction!=null){lastAction.Before=earlyOriginal;lastAction.After=word;lastAction.Sequence=sequence;lastAction.At=DateTime.UtcNow;}
                    if(corrected!=ch)ScheduleEarlyRemainder(window,focus);
                    return IntPtr.Zero;
                }
                if(word.Length<64)word+=ch;else Reset();
                context=(context+ch);if(context.Length>128)context=context.Substring(context.Length-128);
                if(word.Length>=5 && word.Length<=32){
                    SchedulePrefixCorrection(window,focus,layout);
                }
            }else{
                prefixToken++;
                string completed=word; word="";
                string delim=typed;
                lastWord=completed;lastDelimiter=delim;
                context=(context+delim);if(context.Length>128)context=context.Substring(context.Length-128);
                if(earlyWord){
                    string target=engine.Convert(earlyOriginal);
                    earlyWord=false;earlyOriginal="";
                    if(lastAction!=null){lastAction.Delimiter=delim;lastAction.Sequence=sequence;lastAction.At=DateTime.UtcNow;}
                    if(target!=completed)ScheduleEarlyCompletion(window,focus,completed,target,delim,sequence);
                    return IntPtr.Zero;
                }
                if(completed.Length>0 && (char.IsWhiteSpace(ch)||ch==','||ch=='!'||ch=='?'||ch==';')){
                    string preceding=context.Substring(0,Math.Max(0,context.Length-completed.Length-delim.Length));
                    ScheduleCompletedCorrection(window,focus,completed,delim,preceding,sequence);
                }
            }
        }catch(Exception e){LastReason="Ошибка ввода: "+e.GetType().Name;Reset();Notify();}
        return IntPtr.Zero;
    }
    void ScheduleEarlyRemainder(IntPtr window,IntPtr focus){
        int token=++prefixToken;
        var timer=CreateTimer(25);timer.Tick+=delegate{
            timer.Stop();timer.Dispose();if(token!=prefixToken || !earlyWord || pump.Generation!=sequence)return;
            IntPtr w,f,l;if(!SafeFocus(out w,out f,out l) || w!=window || f!=focus)return;
            string target=engine.Convert(earlyOriginal);
            if(word!=target && ReplaceVerifiedTail(f,word,target)){
                word=target;if(lastAction!=null)lastAction.After=target;
                SetTextLayout(w,target);
            }
        };timer.Start();
    }
    void ScheduleEarlyCompletion(IntPtr window,IntPtr focus,string actual,string target,string delimiter,long generation){
        var timer=CreateTimer(30);int attempts=0;timer.Tick+=delegate{
            if(!CanChange(generation,window,focus)){timer.Stop();timer.Dispose();return;}
            string text,ending;
            if(!ReadWordAtCaret(focus,out text,out ending) || text!=actual || ending!=delimiter){
                if(++attempts>=10){timer.Stop();timer.Dispose();}return;
            }
            timer.Stop();timer.Dispose();
            if(ReplaceVerifiedTail(focus,actual+delimiter,target+delimiter)){
                lastWord=target;if(lastAction!=null)lastAction.After=target;
            }
        };timer.Start();
    }
    void ApplyEarlyCorrection(IntPtr window,IntPtr focus,IntPtr layout,string original,Decision decision,string preceding){
        prefixToken++;earlyOriginal=original;earlyWord=true;earlySourceLanguage=(int)((long)layout&0xffff);word=decision.Text;
        context=preceding+word;if(context.Length>128)context=context.Substring(context.Length-128);
        lastAction=new ActionInfo{Window=window,Focus=focus,ElementId=FocusId(),Sequence=sequence,Before=original,After=word,Delimiter="",Kind=ChangeKind.Layout,At=DateTime.UtcNow};
        SetTextLayout(window,decision.Text);Corrections++;Sound(ChangeKind.Layout);LastReason=decision.Reason;Notify();
    }
    void SchedulePrefixCorrection(IntPtr window,IntPtr focus,IntPtr layout){
        int token=++prefixToken;
        var timer=CreateTimer(25);timer.Tick+=delegate{
            timer.Stop();timer.Dispose();
            if(token!=prefixToken || earlyWord || word.Length<5 || word.Length>32 || pump.Generation!=sequence)return;
            IntPtr w,f,l;if(!SafeFocus(out w,out f,out l) || w!=window || f!=focus)return;
            string actual,ending;if(!ReadWordAtCaret(f,out actual,out ending) || ending.Length!=0 || actual!=word)return;
            string preceding=context.Substring(0,Math.Max(0,context.Length-word.Length));
            engine.AddUserWords(store.Current);
            var decision=engine.DecidePrefix(word,preceding,store.Current,policy);
            if(decision.Kind==ChangeKind.None)decision=engine.Decide(word,preceding,store.Current,policy);
            if(token!=prefixToken || pump.Generation!=sequence || !SafeFocus(out w,out f,out l) || w!=window || f!=focus)return;
            if(decision.Kind!=ChangeKind.Layout || !HasOppositeLayout(l))return;
            bool started=false;
            bool replaced=ReplaceVerifiedTail(f,actual,decision.Text,delegate{started=true;ApplyEarlyCorrection(w,f,l,actual,decision,preceding);});
            if(!replaced && started)Reset();
        };timer.Start();
    }
    bool HasOppositeLayout(IntPtr current){
        uint target=((long)current&0xffff)==0x0409?0x0419u:0x0409u;
        int count=Native.GetKeyboardLayoutList(0,null);if(count<=0)return false;
        var layouts=new IntPtr[count];Native.GetKeyboardLayoutList(count,layouts);
        return layouts.Any(item=>((long)item&0xffff)==target);
    }
    void ScheduleCompletedCorrection(IntPtr window,IntPtr focus,string original,string delimiter,string preceding,long atSequence){
        var timer=CreateTimer(30);int attempts=0;
        Action finish=()=>{timer.Stop();timer.Dispose();};
        timer.Tick+=delegate{
            if(pump.Generation!=atSequence || sequence!=atSequence || earlyWord || !store.Current.Enabled){finish();return;}
            IntPtr w,f,l;if(!SafeFocus(out w,out f,out l) || w!=window || f!=focus){finish();return;}
            string actual,ending;
            if(!ReadWordAtCaret(f,out actual,out ending) || actual!=original || ending!=delimiter){
                if(++attempts>=10){finish();LastReason="Поле не подтвердило завершённое слово — текст сохранён";Notify();}
                return;
            }
            finish();
            engine.AddUserWords(store.Current);
            var decision=engine.Decide(original,preceding,store.Current,policy);
            LastReason=decision.Reason;
            if(pump.Generation!=atSequence || sequence!=atSequence || !SafeFocus(out w,out f,out l) || w!=window || f!=focus)return;
            if(decision.Kind==ChangeKind.None){Notify();return;}
            if(decision.Kind==ChangeKind.Layout && !HasOppositeLayout(l))return;
            if(!ReplaceVerifiedTail(f,original+delimiter,decision.Text+delimiter,delegate{if(decision.Kind==ChangeKind.Layout)SetTextLayout(w,decision.Text);} ))return;
            lastWord=decision.Text;lastDelimiter=delimiter;
            context=preceding+decision.Text+delimiter;if(context.Length>128)context=context.Substring(context.Length-128);
            lastAction=new ActionInfo{Window=w,Focus=f,ElementId=FocusId(),Sequence=sequence,Before=original,After=decision.Text,Delimiter=delimiter,Kind=decision.Kind,At=DateTime.UtcNow};
            Corrections++;Sound(decision.Kind);LastReason=decision.Reason;Notify();
        };timer.Start();
    }
    void SwitchLayout(IntPtr window,IntPtr current){
        uint target=((long)current&0xffff)==0x0409?0x0419u:0x0409u;
        SetLanguage(window,target);
    }
    void SetTextLayout(IntPtr window,string text){
        bool ru=text.Any(c=>(c>='а'&&c<='я')||(c>='А'&&c<='Я')||c=='ё'||c=='Ё');
        bool en=text.Any(c=>(c>='a'&&c<='z')||(c>='A'&&c<='Z'));
        if(ru!=en)SetLanguage(window,ru?0x0419u:0x0409u);
    }
    void SetLanguage(IntPtr window,uint target){
        int count=Native.GetKeyboardLayoutList(0,null);if(count<=0)return;
        var layouts=new IntPtr[count];Native.GetKeyboardLayoutList(count,layouts);
        foreach(var item in layouts)if(((long)item&0xffff)==target){
            IntPtr focus=currentFocus;long generation=pump.Generation;int token=++layoutToken;
            if(!CanChange(generation,window,focus))return;
            Native.RequestLayout(focus,window,item);
            // Verify again after the target processes queued input and focus messages.
            // Never fight a later physical keystroke or a user-initiated layout change.
            var timer=CreateTimer(40);int attempts=0;
            timer.Tick+=delegate{
                if(token!=layoutToken || !CanChange(generation,window,focus)){
                    timer.Stop();timer.Dispose();return;
                }
                uint pid;uint thread=Native.GetWindowThreadProcessId(focus,out pid);
                if(Native.GetKeyboardLayout(thread)==item){timer.Stop();timer.Dispose();return;}
                if(++attempts>=4){
                    timer.Stop();timer.Dispose();FailedLayoutChanges++;
                    LastReason="Приложение не подтвердило смену раскладки";Notify();return;
                }
                Native.RequestLayout(focus,window,item);
            };timer.Start();return;
        }
    }
    void Sound(ChangeKind kind){
        bool layout=kind==ChangeKind.Layout; if(layout?!store.Current.SoundLayout:!store.Current.SoundTypos)return;
        string file=layout?store.Current.LayoutSoundFile:store.Current.TypoSoundFile;
        try{if(!string.IsNullOrEmpty(file)&&System.IO.File.Exists(file))new System.Media.SoundPlayer(file).Play();else System.Media.SystemSounds.Asterisk.Play();}catch{}
    }
    public void ManualWord(){Post(()=>AfterModifiers(ManualWordCore));}
    void ManualWordCore(){
        sequence=pump.Generation;
        IntPtr w,f,l;if(!SafeFocus(out w,out f,out l))return;
        string source="",delimiter="";bool available=false;
        for(int attempt=0;attempt<5;attempt++){
            if(!CanChange(sequence,w,f))return;
            if(ReadWordAtCaret(f,out source,out delimiter)){available=true;break;}
            Thread.Sleep(30);
        }
        if(!available){
            LastReason="Слово у курсора недоступно";Notify();return;
        }
        string target=engine.Convert(source);
        if(target==source)return;
        if(ReplaceVerifiedTail(f,source+delimiter,target+delimiter,()=>SetTextLayout(w,target))){
            word=delimiter.Length==0?target:"";lastWord=target;
            lastAction=new ActionInfo{Window=w,Focus=f,ElementId=FocusId(),Sequence=sequence,Before=source,After=target,Delimiter=delimiter,Kind=ChangeKind.Layout,At=DateTime.UtcNow};
            Remember(source,target);
            Corrections++;Sound(ChangeKind.Layout);LastReason="Ручная конвертация слова";Notify();
        }
    }
    static bool SplitCaretWord(string before,out string source,out string delimiter){
        source="";delimiter="";if(string.IsNullOrEmpty(before))return false;
        int end=before.Length;
        char trailing=before[end-1];
        if(char.IsWhiteSpace(trailing)||trailing==','||trailing=='!'||trailing=='?'||trailing==';'){
            delimiter=trailing.ToString();end--;
        }
        int start=end;
        while(start>0 && Letter(before[start-1]))start--;
        if(start==end || end-start>64)return false;
        source=before.Substring(start,end-start);return true;
    }
    bool CanChange(long generation,IntPtr window,IntPtr focus){
        if(stopped || pump.Generation!=generation || Native.GetForegroundWindow()!=window)return false;
        uint pid;uint thread=Native.GetWindowThreadProcessId(window,out pid);
        var info=new Native.GUITHREADINFO{cbSize=Marshal.SizeOf(typeof(Native.GUITHREADINFO))};
        return Native.GetGUIThreadInfo(thread,ref info) && info.hwndFocus==focus;
    }
    bool ReadWordAtCaret(IntPtr focus,out string source,out string delimiter){
        source="";delimiter="";Native.EditSnapshot edit;
        if(Native.TryGetEditSnapshot(focus,out edit))
            return edit.Start==edit.End && (edit.Start==edit.Text.Length || !Letter(edit.Text[edit.Start]))
                && SplitCaretWord(edit.Text.Substring(0,edit.Start),out source,out delimiter);
        try{
            TextAccess.Snapshot snapshot;
            if(!TextAccess.Read(TextAccess.FocusedEditable(),out snapshot) || snapshot.Start!=snapshot.End)return false;
            if(snapshot.Start<snapshot.All.Length && Letter(snapshot.All[snapshot.Start]))return false;
            return SplitCaretWord(snapshot.All.Substring(0,snapshot.Start),out source,out delimiter);
        }catch{return false;}
    }
    public void ManualOrUndo(){Post(()=>AfterModifiers(ManualOrUndoCore));}
    void ManualOrUndoCore(){
        if(lastAction!=null && lastAction.Window==Native.GetForegroundWindow() && lastAction.Sequence==sequence && (DateTime.UtcNow-lastAction.At).TotalSeconds<=30){
            long generation=pump.Generation;
            for(int attempt=0;attempt<4;attempt++){
                if(stopped || pump.Generation!=generation)return;
                if(TryUndo())return;
                Thread.Sleep(30);
            }
            lastAction=null;
        }
        bool hadSelection;if(!TryManualSelection(out hadSelection) && !hadSelection)ManualWordCore();
    }
    public void ManualSelection(){Post(()=>AfterModifiers(delegate{bool ignored;TryManualSelection(out ignored);}));}
    bool TryManualSelection(out bool hadSelection){
        hadSelection=false;
        IntPtr w,f,l;if(!SafeFocus(out w,out f,out l))return false;
        try{
            Native.EditSnapshot edit;
            if(Native.TryGetEditSnapshot(f,out edit)){
                if(edit.End<=edit.Start)return false;
                hadSelection=true;
                string source=edit.Text.Substring(edit.Start,edit.End-edit.Start);
                string target=engine.Convert(source);if(target==source)return false;
                if(!Native.ReplaceEditSelection(f,source,target,true)){LastReason="Выделение изменилось — замена отменена";Notify();return false;}
                int[] id=FocusId();Reset();lastAction=new ActionInfo{Window=w,Focus=f,ElementId=id,Sequence=sequence,Before=source,After=target,Delimiter="",Kind=ChangeKind.Layout,At=DateTime.UtcNow,Selection=true,NativeSelection=true};
                SetTextLayout(w,target);
                Corrections++;Sound(ChangeKind.Layout);LastReason="Ручная конвертация выделения";Notify();return true;
            }
            var element=TextAccess.FocusedEditable();TextAccess.Snapshot snapshot;
            if(!TextAccess.Read(element,out snapshot) || snapshot.Selected.Length==0)return false;
            hadSelection=true;string selected=snapshot.Selected;
            if(selected.IndexOf('\n')>=0 || selected.IndexOf('\r')>=0){LastReason="Многострочное выделение пропущено";Notify();return false;}
            string converted=engine.Convert(selected);if(converted==selected)return false;
            long generation=pump.Generation;bool sent;
            if(!TextAccess.Replace(element,selected,converted,true,()=>CanChange(generation,w,f),out sent,()=>SetTextLayout(w,converted))){
                if(sent)FailedInjection++;
                LastReason=sent?"Поле не подтвердило результат замены":"Выделение изменилось — текст сохранён";Notify();return false;
            }
            VerifiedValueReplacements++;
            int[] selectedId=FocusId();Reset();sequence=generation;SetTextLayout(w,converted);
            lastAction=new ActionInfo{Window=w,Focus=f,ElementId=selectedId,Sequence=sequence,Before=selected,After=converted,Delimiter="",Kind=ChangeKind.Layout,At=DateTime.UtcNow,Selection=true};
            Corrections++;Sound(ChangeKind.Layout);LastReason="Ручная конвертация выделения";Notify();return true;
        }catch{LastReason="Выделение не поддерживается в этом поле";Notify();}
        return false;
    }
    bool ReplaceVerifiedTail(IntPtr focus,string expected,string replacement,Action onSent=null){
        long generation=pump.Generation;IntPtr window=Native.GetForegroundWindow();
        try{
            Native.EditSnapshot edit;
            if(Native.TryGetEditSnapshot(focus,out edit)){
                if(CanChange(generation,window,focus) && Native.ReplaceEditTail(focus,expected,replacement)){if(onSent!=null)onSent();return true;}
                LastReason="Слово у курсора изменилось — текст сохранён";Notify();return false;
            }
            bool sent;var element=TextAccess.FocusedEditable();
            if(TextAccess.Replace(element,expected,replacement,false,()=>CanChange(generation,window,focus),out sent,onSent)){
                VerifiedValueReplacements++;return true;
            }
            if(sent)FailedInjection++;
            LastReason=sent?"Поле не подтвердило результат замены":"Не удалось подтвердить слово и выделение";Notify();return false;
        }catch{LastReason="Поле ввода недоступно для точной замены";Notify();return false;}
    }
    public void Undo(){Post(()=>AfterModifiers(()=>TryUndo()));}
    bool TryUndo(){
        var a=lastAction;if(a==null || a.Window!=Native.GetForegroundWindow() || a.Sequence!=sequence || pump.Generation!=sequence || (DateTime.UtcNow-a.At).TotalSeconds>30)return false;
        IntPtr w,f,l;if(!SafeFocus(out w,out f,out l) || a.Focus!=f || (a.ElementId!=null && (currentElementId==null || !a.ElementId.SequenceEqual(currentElementId))))return false;
        bool restored=a.NativeSelection?Native.ReplaceEditSelection(f,a.After,a.Before,true):ReplaceVerifiedTail(f,a.After+a.Delimiter,a.Before+a.Delimiter,delegate{if(a.Kind==ChangeKind.Layout)SetTextLayout(w,a.Before);});
        if(restored){
            if(a.Kind==ChangeKind.Layout && a.NativeSelection)SetTextLayout(w,a.Before);
            if(a.Kind!=ChangeKind.Learned && !a.Selection && a.Before.Length<=40 && a.Before.All(char.IsLetter))Remember(a.Before,a.Before);
            word="";lastWord=a.Selection?"":a.Before;lastDelimiter=a.Selection?"":a.Delimiter;
            lastAction=null;LastReason="Последнее исправление отменено";Notify();return true;
        }
        return false;
    }
}
}
