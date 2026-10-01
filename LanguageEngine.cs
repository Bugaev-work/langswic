using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace FastSwitcher {
public enum ChangeKind { None, Layout, Typo, Yo, Learned }
public sealed class Decision { public string Text; public ChangeKind Kind; public string Reason; public Decision(string text,ChangeKind kind,string reason){Text=text;Kind=kind;Reason=reason;} }
public sealed class LanguageEngine : IDisposable {
    const string En="`qwertyuiop[]asdfghjkl;'zxcvbnm,./";
    const string Ru="ёйцукенгшщзхъфывапролджэячсмитьбю.";
    readonly Dictionary<char,char> enToRu=new Dictionary<char,char>(),ruToEn=new Dictionary<char,char>();
    readonly HashSet<string> ru=new HashSet<string>(StringComparer.OrdinalIgnoreCase),en=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    HashSet<string> baseRu,baseEn;
    readonly WindowsSpelling spelling=new WindowsSpelling();
    public string DictionaryStatus {get{return spelling.Status;}}
    public void Dispose(){spelling.Dispose();}
    bool Known(bool latin,string value){
        if(!Regex.IsMatch(value,latin?@"^[A-Za-z]+$":@"^[А-Яа-яЁё]+$"))return false;
        if((latin?en:ru).Contains(value))return true;
        bool known;return spelling.TryIsWord(latin,value,out known) && known;
    }
    public LanguageEngine(){
        for(int i=0;i<En.Length;i++){enToRu[En[i]]=Ru[i];ruToEn[Ru[i]]=En[i];}
        AddWords(ru,Lexicons.RussianWords);
        AddWords(en,Lexicons.EnglishWords);
        baseRu=new HashSet<string>(ru,StringComparer.OrdinalIgnoreCase);baseEn=new HashSet<string>(en,StringComparer.OrdinalIgnoreCase);
    }
    static void AddWords(HashSet<string> set,string words){foreach(var w in words.Split(' ')) if(w.Length>0)set.Add(w);}
    public void AddUserWords(Settings s){
        ru.Clear();en.Clear();ru.UnionWith(baseRu);en.UnionWith(baseEn);
        foreach(var w in s.RuWords)ru.Add(w); foreach(var w in s.EnWords)en.Add(w);
    }
    public string Convert(string value){
        var chars=value.ToCharArray();
        for(int i=0;i<chars.Length;i++){
            char c=chars[i],lower=char.ToLowerInvariant(c),mapped;
            if(enToRu.TryGetValue(lower,out mapped))chars[i]=char.IsUpper(c)?char.ToUpperInvariant(mapped):mapped;
            else if(ruToEn.TryGetValue(lower,out mapped))chars[i]=char.IsUpper(c)?char.ToUpperInvariant(mapped):mapped;
        }
        return new string(chars);
    }
    static string CaseLike(string original,string replacement){
        if(original.Length==0)return replacement;
        if(original.All(c=>!char.IsLetter(c)||char.IsUpper(c)))return replacement.ToUpperInvariant();
        if(char.IsUpper(original[0]))return char.ToUpperInvariant(replacement[0])+replacement.Substring(1);
        return replacement;
    }
    static bool Protected(string value,string context){
        if(value.Length==0 || value.Length>40 || value.Any(char.IsDigit))return true;
        if(value.Any(c=>c=='_'||c=='@'||c=='#'||c=='\\'))return true;
        if(Regex.IsMatch(context??"",@"(?:^|\s)\S*\d\S*$"))return true;
        if(value.All(c=>!char.IsLetter(c)||char.IsUpper(c)))return true;
        if(Regex.IsMatch(value,@"[A-Za-z].*[А-Яа-яЁё]|[А-Яа-яЁё].*[A-Za-z]"))return true;
        if(Regex.IsMatch(context??"",@"(?i)(https?://|www\.|\S+@\S*|[A-Za-z0-9_-]+\.[A-Za-z]{2,})\S*$"))return true;
        return false;
    }
    public Decision DecidePrefix(string value,string context,Settings settings,AppRule app=null){
        if(value==null || value.Length<5 || value.Length>32 || !settings.Layout || (app!=null && !app.Layout))
            return new Decision(value,ChangeKind.None,"префикс слишком короткий или автоматика отключена");
        if(Protected(value,context))return new Decision(value,ChangeKind.None,"защищённый префикс");
        bool latin=Regex.IsMatch(value,@"^[A-Za-z]+$");
        bool cyr=Regex.IsMatch(value,@"^[А-Яа-яЁё]+$");
        if(!latin && !cyr)return new Decision(value,ChangeKind.None,"смешанный префикс");
        if(settings.ExcludedWords.Any(w=>w.StartsWith(value,StringComparison.OrdinalIgnoreCase)) ||
           settings.Learned.Keys.Any(w=>w.StartsWith(value,StringComparison.OrdinalIgnoreCase)))
            return new Decision(value,ChangeKind.None,"пользовательское исключение для префикса");
        var sourceWords=latin?en:ru;
        if(Known(latin,value))return new Decision(value,ChangeKind.None,"слово распознано в исходном языке");
        if(sourceWords.Any(w=>w.StartsWith(value,StringComparison.OrdinalIgnoreCase)))
            return new Decision(value,ChangeKind.None,"префикс возможен в исходном языке");
        string target=Convert(value);
        var candidates=(latin?ru:en).Where(w=>w.StartsWith(target,StringComparison.OrdinalIgnoreCase) && w.Length>=value.Length+2).Take(2).ToArray();
        if(candidates.Length!=1)return new Decision(value,ChangeKind.None,"недостаточная уверенность в префиксе");
        return new Decision(target,ChangeKind.Layout,"уникальный префикс слова в другой раскладке");
    }
    public Decision Decide(string value,string context,Settings settings,AppRule app=null){
        if(string.IsNullOrEmpty(value))return new Decision(value,ChangeKind.None,"пусто");
        if(Protected(value,context))return new Decision(value,ChangeKind.None,"защищённый или неоднозначный токен");
        if(settings.ExcludedWords.Any(w=>string.Equals(w,value,StringComparison.OrdinalIgnoreCase)))return new Decision(value,ChangeKind.None,"слово-исключение");
        string learned;
        if(settings.Learned.TryGetValue(value.ToLowerInvariant(),out learned)) {
            if(learned==value.ToLowerInvariant())return new Decision(value,ChangeKind.None,"локальное исключение");
            return new Decision(CaseLike(value,learned),ChangeKind.Learned,"локальное правило");
        }
        bool layout=settings.Layout && (app==null||app.Layout);
        bool typos=settings.Typos && (app==null||app.Typos);
        bool useYo=settings.Yo && (app==null||app.Yo);
        if(layout && value.Length>=4){
            var target=Convert(value); bool latin=Regex.IsMatch(value,@"^[A-Za-z]+$"); bool cyr=Regex.IsMatch(value,@"^[А-Яа-яЁё]+$");
            if((latin||cyr) && target!=value){
                bool sourceKnown=Known(latin,value),targetKnown=Known(!latin,target);
                if(!sourceKnown && targetKnown)return new Decision(target,ChangeKind.Layout,"слово есть только в другой раскладке");
            }
        }
        string replacement;
        if(typos && Lexicons.Typos.TryGetValue(value,out replacement))return new Decision(CaseLike(value,replacement),ChangeKind.Typo,"словарная опечатка");
        if(typos && value.Length>2 && char.IsUpper(value[0]) && char.IsUpper(value[1]) && value.Skip(2).Any(char.IsLower))
            return new Decision(value[0]+value.Substring(1).ToLowerInvariant(),ChangeKind.Typo,"ошибка регистра");
        if(useYo && Lexicons.Yo.TryGetValue(value,out replacement))return new Decision(CaseLike(value,replacement),ChangeKind.Yo,"однозначная словарная форма с ё");
        return new Decision(value,ChangeKind.None,"недостаточная уверенность");
    }
}
}
