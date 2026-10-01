using System;
using System.Globalization;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace FastSwitcher {
// Short-lived snapshots are used only on the permanent MTA text worker.
internal static class TextAccess {
#if INPUT_TEST
    internal static int TestPostSendDelay;
#endif
    internal static string ReadState="не прочитано";
    internal sealed class Snapshot {
        public string All,Selected;public int Start,End;public TextPattern Pattern;public TextPatternRange Range;
    }
    public static AutomationElement FocusedEditable(){
        var element=AutomationElement.FocusedElement;
        for(int depth=0;element!=null && depth<6;depth++){
            if(element.Current.IsPassword || !element.Current.IsEnabled)return null;
            var kind=element.Current.ControlType;object p;
            bool standard=kind==ControlType.Edit || kind==ControlType.ComboBox || kind==ControlType.Document;
            if(element.TryGetCurrentPattern(ValuePattern.Pattern,out p)){
                if(((ValuePattern)p).Current.IsReadOnly)return null;
                if(standard)return element;
            }
            if(element.TryGetCurrentPattern(TextPattern.Pattern,out p)){
                var pattern=(TextPattern)p;
                object readOnly=pattern.DocumentRange.GetAttributeValue(TextPattern.IsReadOnlyAttribute);
                if(readOnly is bool && !(bool)readOnly && (standard || element.Current.HasKeyboardFocus))return element;
                if(readOnly is bool && (bool)readOnly)return null;
            }
            // Never climb from a known read-only/password control into its page.
            if(kind==ControlType.Edit || kind==ControlType.ComboBox)return null;
            element=TreeWalker.ControlViewWalker.GetParent(element);
        }
        return null;
    }
    public static bool SameElement(AutomationElement a,AutomationElement b){
        if(a==null || b==null)return false;var x=a.GetRuntimeId();var y=b.GetRuntimeId();
        if(x==null || y==null || x.Length!=y.Length)return false;
        for(int i=0;i<x.Length;i++)if(x[i]!=y[i])return false;return true;
    }
    public static bool Read(AutomationElement element,out Snapshot snapshot){
        snapshot=null;
        for(int attempt=0;attempt<4;attempt++){
            try{if(ReadOnce(element,out snapshot))return true;}catch{}
            if(attempt<3)Thread.Sleep(15);
        }
        return false;
    }
    static bool ReadOnce(AutomationElement element,out Snapshot snapshot){
        snapshot=null;ReadState="поле недоступно";if(element==null || element.Current.IsPassword || !element.Current.IsEnabled)return false;
        object p;ReadState="нет TextPattern";if(!element.TryGetCurrentPattern(TextPattern.Pattern,out p))return false;
        var pattern=(TextPattern)p;var ranges=pattern.GetSelection();ReadState="нет единственного диапазона";if(ranges==null || ranges.Length!=1)return false;
        var doc=pattern.DocumentRange;string all=doc.GetText(65537);if(all.Length>65536)return false;
        var range=ranges[0];string selected=range.GetText(4097);if(selected.Length>4096)return false;
        var before=doc.Clone();before.MoveEndpointByRange(TextPatternRangeEndpoint.End,range,TextPatternRangeEndpoint.Start);
        var after=doc.Clone();after.MoveEndpointByRange(TextPatternRangeEndpoint.Start,range,TextPatternRangeEndpoint.End);
        string left=before.GetText(65537),right=after.GetText(65537);
        ReadState="длина="+all.Length+", до="+left.Length+", выделено="+selected.Length+", после="+right.Length;
        if(left+selected+right!=all){ReadState+="; несовпадение снимка";return false;}
        snapshot=new Snapshot{All=all,Selected=selected,Start=left.Length,End=left.Length+selected.Length,Pattern=pattern,Range=range};return true;
    }
    static bool Current(AutomationElement element,Func<bool> guard){return guard() && SameElement(element,FocusedEditable());}
    public static bool Replace(AutomationElement element,string expected,string replacement,bool selectionOnly,Func<bool> guard,out bool sent,Action onSent=null){
        sent=false;Snapshot before;if(!Current(element,guard) || !Read(element,out before))return false;
        int start=before.Start,end=before.End;
        if(before.Selected.Length==0){
            if(selectionOnly || start<expected.Length || before.All.Substring(start-expected.Length,expected.Length)!=expected)return false;
            start-=expected.Length;
            // Keyboard left movement is unambiguous for the supported RU/EN words.
            // Reject line breaks and complex graphemes rather than guessing a range.
            if(expected.IndexOf('\r')>=0 || expected.IndexOf('\n')>=0 || expected.IndexOf('\t')>=0 ||
                new StringInfo(expected).LengthInTextElements!=expected.Length)return false;
        }else if(before.Selected!=expected)return false;
        Snapshot ready;
        if(!Current(element,guard) || !Read(element,out ready) || ready.All!=before.All ||
            ready.Start!=before.Start || ready.End!=before.End || ready.Selected!=before.Selected || !Current(element,guard))return false;
        bool inserted=before.Selected.Length==0?Native.ReplacePrevious(expected.Length,replacement):Native.Type(replacement);
        if(!inserted)return false;
        sent=true;
        if(onSent!=null)onSent();
#if INPUT_TEST
        if(TestPostSendDelay>0)Thread.Sleep(TestPostSendDelay);
#endif
        string wanted=before.All.Substring(0,start)+replacement+before.All.Substring(end);
        for(int i=0;i<16;i++){
            // The user may continue typing after the atomic, verified insertion.
            // A newer event must not turn a committed edit into a failed edit.
            if(!guard())return true;
            if(!SameElement(element,FocusedEditable()))return false;
            Snapshot after;
            if(Read(element,out after) && after.All==wanted && after.Start==start+replacement.Length && after.End==after.Start)return true;
            Thread.Sleep(15);
        }
        return false;
    }
}
}
