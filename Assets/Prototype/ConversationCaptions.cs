using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
public static class ConversationCaptions {
 public static string Compact(string value,int max=40){
  if(string.IsNullOrWhiteSpace(value)||max<=0)return "";
  value=Regex.Replace(value,@"\s+"," ").Trim();var info=new StringInfo(value);
  return info.LengthInTextElements<=max?value:info.SubstringByTextElements(0,max-1)+"…";
 }
 public static string Wrap(string value,int width=20,int max=40){
  value=Compact(value,max);var b=new StringBuilder();var e=StringInfo.GetTextElementEnumerator(value);int col=0;
  while(e.MoveNext()){if(col==width){b.Append('\n');col=0;}b.Append(e.GetTextElement());col++;}return b.ToString();
 }
 public static string Face(string observation,string emotionHypothesis){
  string action=Compact(observation,5);if(string.IsNullOrEmpty(action))return "";
  string emotion=(emotionHypothesis??"").Trim();
  switch(emotion){case "平静":case "愉快":case "不悦":case "惊讶":case "紧张":return action+"｜可能"+emotion;default:return action+"｜难判断";}
 }
 public static string Analysis(string language,string facial)=>Analysis(language,facial,null);
 public static string Analysis(string language,string facial,string nextSpeaker){
  bool turn=nextSpeaker=="A"||nextSpeaker=="B";
  string prefix=turn?"轮到"+nextSpeaker+"｜":"解读：";
  string a=Compact(language,turn?19:20),b=Compact(facial,14);
  if(string.IsNullOrEmpty(a))a="等待下一句";
  if(string.IsNullOrEmpty(b))b="未检测";
  return Wrap(prefix+a,20,23)+"\n表情："+b;
 }
}
