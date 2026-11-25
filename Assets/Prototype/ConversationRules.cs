using System;
public static class ConversationRules {
 public static bool CanAccept(string phase,bool busy,bool ending,int session,int turn,string actor,int submittedSession,int submittedTurn,int retrySeconds){return !busy&&!ending&&retrySeconds<=0&&(actor=="A"||actor=="B")&&phase==actor.ToLowerInvariant()+"_listen"&&session==submittedSession&&turn==submittedTurn;}
 public static string NextPhase(string actor){if(actor=="A")return "b_listen";if(actor=="B")return "a_listen";throw new ArgumentException("Invalid actor");}
 public static bool FreshFace(long now,long captured){return now>=captured&&now-captured<650;}
 public static bool ValidEvidence(string[] cited,string[] knownB){if(cited==null||cited.Length==0||cited.Length>6)return false;foreach(var id in cited)if(Array.IndexOf(knownB,id)<0)return false;return true;}
}
