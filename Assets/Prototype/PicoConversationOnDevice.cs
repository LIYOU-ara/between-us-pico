using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
// All upstream calls run on the headset. Loopback is an in-app transport, not a PC relay.
public class PicoConversationOnDevice : MonoBehaviour {
 [Serializable] public class Turn {public string id,speaker,text,facial_observation,visual_uncertainty,emotion_hypothesis;}
 [Serializable] public class State {
  public string phase="idle",message,b_quote="",a_quote="",expected_speaker="",observation="",uncertainty="",skeptical_reading="",clarify_reply="",firm_reply="",facial_observation="",visual_uncertainty="",emotion_hypothesis="难判断",visual_status="",visual_error="",face_diagnostic="",face_diagnostic_code="",face_processing_stage="",version="headset-direct-v5-emotion-cues";
  public int session_id=1,turn_id,round_index=1,photo_id,asr_ms,analysis_ms,retry_after_seconds,face_detected_count,face_width,face_height;
  public float face_frame_age_ms,face_detection_ms;
  public bool can_capture,ending,has_photo,visual_allowed,visual_observed,configured,demo=false;public string[] summary_pages=new string[0];public Turn[] turns=new Turn[0];
 }
 [Serializable] class Config {public string pairing_code,key,text_model,vision_model;public bool consent;}
 [Serializable] class TextInput {public string speaker,text;public int session_id,turn_id;}
 [Serializable] class ErrorBody {public string code,message;}
 [Serializable] class ProviderBody {public ErrorBody error;public Choice[] choices;public string text;}
 [Serializable] class Choice {public Message message;}
 [Serializable] class Message {public string content;}
 [Serializable] class RoundResult {public string observation,uncertainty,skeptical_reading,clarify_reply,firm_reply,facial_observation,visual_uncertainty;}
 [Serializable] class VisionResult {public string facial_observation,visual_uncertainty,emotion_hypothesis;}
 [Serializable] class SummaryItem {public string text;public string[] evidence_ids;}
 [Serializable] class Summary {public SummaryItem purpose,motive,behavior,communication_style;public string uncertainty;}
 [Serializable] class StringValue {public string value;}
 [Serializable] class TurnBox {public Turn[] actual_turns;}
 [Serializable] class GlassDiagnostic {public string mode;public int frame,sample_width;public float acquisition_age_ms;public Vector4 panel_uv;}
 sealed class Request {public string path,method,contentType;public Dictionary<string,string> headers;public byte[] body,response;public string responseType="application/json; charset=utf-8";public int code=200;public ManualResetEvent done=new ManualResetEvent(false);public bool expired;}
 public PicoLocalFace localFace;
 public State state=new State();
 readonly List<Turn> turns=new List<Turn>();readonly ConcurrentQueue<Request> incoming=new ConcurrentQueue<Request>();
  const string DefaultTextModel="glm-4.6",DefaultVisionModel="glm-4.6v-flashx";
  // Credentials are deliberately private, non-serialized and kept only for this process.
  string apiKey,textModel=DefaultTextModel,visionModel=DefaultVisionModel,pairingCode,lastErrorKind="";
 TcpListener listener;Thread listenerThread;volatile bool alive=true;bool busy;byte[] photo;float retryUntil;
 const string ChatUrl="https://open.bigmodel.cn/api/paas/v4/chat/completions",AsrUrl="https://open.bigmodel.cn/api/paas/v4/audio/transcriptions";
  public bool CredentialsConfigured=>state.configured&&!string.IsNullOrEmpty(apiKey);
  public bool CanConfigureCredentials=>alive&&!busy&&(state.phase=="idle"||state.phase=="summary_ready"||state.phase=="error");
  public string ConfigurationPairingCode=>pairingCode;
  public string ConfiguredTextModel=>textModel;
  public string ConfiguredVisionModel=>visionModel;
  public bool VisualUploadAllowed=>CredentialsConfigured&&state.visual_allowed;
  string ConfigurationHint()=>"请在头显 API 设置中输入密钥。\n电脑配置入口配对码 "+pairingCode;
  static bool TryModel(string value,string fallback,out string selected){selected=string.IsNullOrWhiteSpace(value)?fallback:value.Trim();return Regex.IsMatch(selected,@"\A[A-Za-z0-9][A-Za-z0-9._:/-]{0,127}\z");}
  public bool TryConfigureCredentials(string key,string textModel,string visionModel,bool consent,out string error){
   error=null;
   if(!CanConfigureCredentials){error="请先结束当前交流，再更改密钥。";return false;}
   string selectedKey=key?.Trim();
   if(string.IsNullOrEmpty(selectedKey)||selectedKey.Length>4096||!Regex.IsMatch(selectedKey,@"\A[A-Za-z0-9_+/=-]+\.[A-Za-z0-9_+/=-]+\z")){error="需要完整 API Key（ID.Secret），请勿输入掩码或 Bearer 前缀。";return false;}
   if(!TryModel(textModel,DefaultTextModel,out var selectedText)||!TryModel(visionModel,DefaultVisionModel,out var selectedVision)){error="模型名称格式不正确。";return false;}
   NewSession();apiKey=selectedKey;this.textModel=selectedText;this.visionModel=selectedVision;state.visual_allowed=consent;state.configured=true;
   Phase("idle","配置完成。密钥仅用于本次运行；X 开始，B 先说。");return true;
  }
  public bool TryClearCredentials(out string error){
   error=null;
   if(!CanConfigureCredentials){error="请先结束当前交流，再清除密钥。";return false;}
   apiKey=null;textModel=DefaultTextModel;visionModel=DefaultVisionModel;state.configured=false;state.visual_allowed=false;
   NewSession();Phase("idle","密钥已清除。"+ConfigurationHint());return true;
  }
 void Start(){
  byte[] random=new byte[4];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(random);pairingCode=(BitConverter.ToUInt32(random,0)%1000000).ToString("D6");
   apiKey=null;state.configured=false;state.visual_allowed=false;NewSession();Phase("idle",ConfigurationHint());
  listener=new TcpListener(IPAddress.Loopback,8796);try{listener.Start();listenerThread=new Thread(Listen){IsBackground=true};listenerThread.Start();}catch(Exception){state.message="头显本地端口无法启动";}
 }
 void Listen(){while(alive){try{var client=listener.AcceptTcpClient();ThreadPool.QueueUserWorkItem(_=>Read(client));}catch{if(!alive)return;}}}
 static byte[] ReadExact(Stream stream,int length){var result=new byte[length];int offset=0;while(offset<length){int n=stream.Read(result,offset,length-offset);if(n<=0)throw new IOException();offset+=n;}return result;}
 void Read(TcpClient client){using(client){try{
  client.ReceiveTimeout=10000;client.SendTimeout=10000;var stream=client.GetStream();var header=new List<byte>();int x;
  while((x=stream.ReadByte())>=0){header.Add((byte)x);int n=header.Count;if(n>16384)throw new IOException();if(n>=4&&header[n-4]==13&&header[n-3]==10&&header[n-2]==13&&header[n-1]==10)break;}
  var lines=Encoding.ASCII.GetString(header.ToArray()).Split(new[]{"\r\n"},StringSplitOptions.None);var first=lines[0].Split(' ');var map=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
  foreach(var line in lines){int colon=line.IndexOf(':');if(colon>0)map[line.Substring(0,colon).Trim()]=line.Substring(colon+1).Trim();}
  int size=0;if(map.TryGetValue("Content-Length",out var contentLength))int.TryParse(contentLength,out size);if(size<0||size>4*1024*1024)throw new IOException();
  var req=new Request{method=first[0],path=first[1].Split('?')[0],headers=map,body=ReadExact(stream,size),contentType=map.TryGetValue("Content-Type",out var ct)?ct:""};incoming.Enqueue(req);
  if(!req.done.WaitOne(65000)){req.expired=true;return;}
  byte[] body=req.response??new byte[0];string response="HTTP/1.1 "+req.code+" "+(req.code==200?"OK":"Error")+"\r\nContent-Type: "+req.responseType+"\r\nContent-Length: "+body.Length+"\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
  byte[] h=Encoding.ASCII.GetBytes(response);stream.Write(h,0,h.Length);stream.Write(body,0,body.Length);
 }catch{}}}
 void Update(){state.retry_after_seconds=Mathf.Max(0,Mathf.CeilToInt(retryUntil-Time.realtimeSinceStartup));while(incoming.TryDequeue(out var req)){if(!req.expired)StartCoroutine(Handle(req));}}
 static string Quote(string text){string raw=JsonUtility.ToJson(new StringValue{value=text??""});return raw.Substring(raw.IndexOf(':')+1,raw.Length-raw.IndexOf(':')-2);}
 void Reply(Request r,string json,int code=200){r.code=code;r.response=Encoding.UTF8.GetBytes(json);r.done.Set();}
 void Error(Request r,string text,int code=400){Reply(r,"{\"error\":"+Quote(text)+"}",code);}
 void Phase(string phase,string message){state.phase=phase;state.message=message;state.expected_speaker=phase=="b_listen"?"B":phase=="a_listen"?"A":"";state.can_capture=!state.ending&&(phase=="b_listen"||phase=="a_listen");}
  void NewSession(){state.session_id++;state.turn_id=0;state.round_index=1;turns.Clear();state.turns=new Turn[0];photo=null;state.has_photo=false;state.photo_id=0;state.b_quote=state.a_quote="";state.summary_pages=new string[0];state.ending=false;state.visual_status="";state.asr_ms=state.analysis_ms=state.retry_after_seconds=0;retryUntil=0;lastErrorKind="";ClearResult();}
 void ClearResult(){state.visual_observed=false;state.visual_error="";state.emotion_hypothesis="难判断";state.observation=state.uncertainty=state.skeptical_reading=state.clarify_reply=state.firm_reply=state.facial_observation=state.visual_uncertainty="";}
 IEnumerator Handle(Request r){
  if(r.method=="GET"){
   if(r.path=="/glass"){
    var glass=GetComponent<ConversationGlassCamera>();
    Reply(r,JsonUtility.ToJson(glass!=null?new GlassDiagnostic{mode=glass.Mode,frame=glass.FrameId,sample_width=glass.sampleWidth,acquisition_age_ms=glass.FrameAgeMs,panel_uv=glass.PanelUV}:new GlassDiagnostic{mode="missing"}));yield break;
   }
   if(r.path=="/result"||r.path=="/health"){state.turns=turns.ToArray();
    if(localFace!=null){state.face_diagnostic=localFace.diagnostic;state.face_diagnostic_code=localFace.diagnosticCode;state.face_processing_stage=localFace.processingStage;state.face_frame_age_ms=localFace.frameAgeMs;state.face_detection_ms=localFace.detectionMs;state.face_detected_count=localFace.detectedFaces;state.face_width=localFace.bitmapWidth;state.face_height=localFace.bitmapHeight;}
    Reply(r,JsonUtility.ToJson(state));}
   else if(r.path=="/face"){var f=localFace.face;if(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-f.frame_unix_ms>750)Reply(r,"{\"tracked\":false}");else Reply(r,JsonUtility.ToJson(f));}
   else if(r.path=="/photo"&&photo!=null){r.response=photo;r.responseType="image/jpeg";r.done.Set();}
   else Error(r,"Unknown route",404);yield break;
  }
  if(r.method!="POST"){Error(r,"POST required");yield break;}
  if(r.headers.TryGetValue("Origin",out var origin)&&origin!="http://127.0.0.1:8796"&&origin!="http://localhost:8796"){Error(r,"Local requests only",403);yield break;}
  if(r.path=="/config"){
    Config c=null;try{c=JsonUtility.FromJson<Config>(Encoding.UTF8.GetString(r.body));}catch{}
    if(r.body!=null){Array.Clear(r.body,0,r.body.Length);r.body=null;}
    if(c==null||string.IsNullOrEmpty(pairingCode)||c.pairing_code!=pairingCode){if(c!=null)c.key=null;Error(r,"配对码不正确",403);yield break;}
    bool configured=TryConfigureCredentials(c.key,c.text_model,c.vision_model,c.consent,out var configurationError);c.key=null;
    if(!configured){Error(r,configurationError);yield break;}
    Reply(r,"{\"ok\":true}");yield break;
  }
   string body=Encoding.UTF8.GetString(r.body);
  if(r.path=="/start"){
    if(!CredentialsConfigured){Error(r,"请先在头显 API 设置中配置密钥");yield break;}
   if(busy||!(state.phase=="idle"||state.phase=="summary_ready")){Error(r,"请先结束当前交流");yield break;}
   NewSession();Phase("b_listen","轮到 B：说一句，然后停顿。");Reply(r,"{\"ok\":true}");yield break;
  }
  if(r.path=="/finish"){
   if(state.phase=="summary_ready"||state.phase=="summary_pending"){Reply(r,"{\"ok\":true}");yield break;}
   state.ending=true;state.can_capture=false;Reply(r,"{\"ok\":true}");if(!busy)StartCoroutine(FinalSummary());else state.message="交流已结束，等当前这句完成后总结…";yield break;
  }
  if(r.path=="/recover"){
   if(state.phase!="error"||busy||state.retry_after_seconds>0){Error(r,"请等待后再恢复");yield break;}
   if(lastErrorKind=="summary")StartCoroutine(FinalSummary());else{state.ending=false;Phase(lastErrorKind=="round"||lastErrorKind=="asr_A"?"a_listen":"b_listen","请继续当前轮次。");}
   Reply(r,"{\"ok\":true}");yield break;
  }
  if(r.path!="/transcribe"&&r.path!="/text"){Error(r,"Unknown route",404);yield break;}
  TextInput input=null;string actor=null;int sid=-1,tid=-1;
  if(r.path=="/text"){try{input=JsonUtility.FromJson<TextInput>(body);actor=input.speaker;sid=input.session_id;tid=input.turn_id;}catch{}}
  else{r.headers.TryGetValue("X-Conversation-Speaker",out actor);if(r.headers.TryGetValue("X-Conversation-Session",out var si))int.TryParse(si,out sid);if(r.headers.TryGetValue("X-Conversation-Turn",out var ti))int.TryParse(ti,out tid);}
  if(!ConversationRules.CanAccept(state.phase,busy,state.ending,state.session_id,state.turn_id,actor,sid,tid,state.retry_after_seconds)){Error(r,"当前不是这一轮，请按提示重说",409);yield break;}
  if(turns.Count>=24){Error(r,"24 句上限，请按 A 结束");yield break;}
  busy=true;int session=state.session_id;Phase(actor.ToLower()+"_asr","正在识别 "+actor+" 的这一句…");byte[] selected=null;
  if(actor=="B"){
   if(state.visual_allowed&&localFace.crop!=null&&ConversationRules.FreshFace(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),localFace.cropTime)){selected=(byte[])localFace.crop.Clone();state.visual_status="本句单帧面部动作";}
   else state.visual_status=state.visual_allowed?"未取得清晰单人脸，仅分析语言":"面部上传未开启";
  }
  float began=Time.realtimeSinceStartup;string text=input?.text;
  if(r.path=="/transcribe"){
   string error=null;yield return SendCloud(AsrUrl,r.body,r.contentType,(response,e)=>{error=e;if(e==null){try{text=JsonUtility.FromJson<ProviderBody>(response).text;}catch{error="转写格式异常";}}});
   if(error!=null){busy=false;Fail(error,"asr_"+actor);Error(r,error);yield break;}
  }
  if(string.IsNullOrWhiteSpace(text)||text.Length>1200){busy=false;if(state.ending)StartCoroutine(FinalSummary());else Phase(actor.ToLower()+"_listen","未识别到有效台词，请再说一句。");Reply(r,"{\"text\":\"\"}");yield break;}
  state.asr_ms=Mathf.RoundToInt((Time.realtimeSinceStartup-began)*1000);var turn=new Turn{speaker=actor,text=text,id=actor+(1+turns.FindAll(t=>t.speaker==actor).Count)};turns.Add(turn);state.turn_id++;
  Reply(r,"{\"text\":"+Quote(text)+",\"source\":\"headset_direct_bigmodel\",\"elapsed_ms\":"+state.asr_ms+"}");
  if(actor=="A"){state.a_quote=text;state.round_index++;busy=false;if(state.ending)StartCoroutine(FinalSummary());else Phase(ConversationRules.NextPhase(actor),"A 回应已记录。轮到 B 说下一句。");yield break;}
  state.b_quote=text;photo=selected;state.has_photo=photo!=null;state.photo_id=state.turn_id;ClearResult();Phase("b_analyzing","B 台词已识别，正在给 A 分析…");
  yield return RoundAnalysis(turn,selected,session);
 }
 void Fail(string error,string kind){lastErrorKind=kind;Phase("error",error);state.can_capture=false;Debug.LogWarning("PICO_CONVERSATION_ERROR kind="+kind);}
 static string CleanJson(string raw){raw=raw.Trim();string fence=new string((char)96,3);if(raw.StartsWith(fence)){int nl=raw.IndexOf('\n');int last=raw.LastIndexOf(fence,StringComparison.Ordinal);if(nl>=0&&last>nl)raw=raw.Substring(nl+1,last-nl-1);}return raw;}
 bool Safe(string text,int max)=>ConversationModelFields.Required("text",text,max)==null;
 string ChatPayload(string model,string prompt,string content){return "{\"model\":"+Quote(model)+",\"stream\":false,\"temperature\":0.3,\"max_tokens\":2048,\"thinking\":{\"type\":\"disabled\"},\"messages\":[{\"role\":\"system\",\"content\":"+Quote(prompt)+"},{\"role\":\"user\",\"content\":"+content+"}]}";}
 string VisionPayload(byte[] image){
  string prompt=Resources.Load<TextAsset>("Conversation/VisionPrompt").text;
  return "{\"model\":"+Quote(visionModel)+",\"stream\":false,\"temperature\":0.1,\"max_tokens\":256,\"thinking\":{\"type\":\"disabled\"},\"messages\":[{\"role\":\"user\",\"content\":[{\"type\":\"image_url\",\"image_url\":{\"url\":"+Quote("data:image/jpeg;base64,"+Convert.ToBase64String(image))+"}},{\"type\":\"text\",\"text\":"+Quote(prompt)+"}]}]}";
 }
 static string LanguageFieldsError(RoundResult r){
  if(r==null)return "result:missing";
  return ConversationModelFields.Required("observation",r.observation,180)
   ??ConversationModelFields.Required("skeptical_reading",r.skeptical_reading,160)
   ??ConversationModelFields.Optional("uncertainty",r.uncertainty,140)
   ??ConversationModelFields.Optional("clarify_reply",r.clarify_reply,160)
   ??ConversationModelFields.Optional("firm_reply",r.firm_reply,160);
 }
 static bool ReadableFace(string value)=>!string.IsNullOrWhiteSpace(value)&&!Regex.IsMatch(value,"画面不清|无法辨认|看不清|人脸不清|面部不可见|未见人脸|没有人脸");
 IEnumerator RoundAnalysis(Turn turn,byte[] image,int session){
  float start=Time.realtimeSinceStartup;string error=null;RoundResult result=null;VisionResult vision=null;
  if(image!=null){
   state.visual_status="正在观察面部动作";
   yield return SendCloud(ChatUrl,Encoding.UTF8.GetBytes(VisionPayload(image)),"application/json",(response,e)=>{
    error=e;if(e!=null)return;
    try{
     var cloud=JsonUtility.FromJson<ProviderBody>(response);vision=JsonUtility.FromJson<VisionResult>(CleanJson(cloud.choices[0].message.content));
     // A provider may use an empty observation for an unreadable picture. This
     // is a capture/readability outcome, not an authentication/network failure.
     if(vision!=null&&string.IsNullOrWhiteSpace(vision.facial_observation)&&!string.IsNullOrWhiteSpace(vision.visual_uncertainty))vision.facial_observation="画面不清";
     error=vision==null?"视觉字段 result:missing":ConversationModelFields.Required("facial_observation",vision.facial_observation,180)??ConversationModelFields.Optional("visual_uncertainty",vision.visual_uncertainty,180);
    }catch{error="视觉返回格式异常";}
   });
   if(error==null){
    state.facial_observation=vision.facial_observation;state.visual_uncertainty=vision.visual_uncertainty??"";
    state.visual_observed=ReadableFace(vision.facial_observation);state.emotion_hypothesis=state.visual_observed?ConversationModelFields.EmotionHypothesis(vision.emotion_hypothesis):"难判断";state.visual_status=state.visual_observed?"表情已分析":"照片不可判读";
    Debug.Log("PICO_CONVERSATION_VISION_OK observed="+state.visual_observed);
   }else{state.visual_error=error;state.visual_status="视觉接口失败";Debug.LogWarning("PICO_CONVERSATION_VISION_ERROR "+error);}
   if(session!=state.session_id){busy=false;yield break;}
  }
  turn.facial_observation=state.facial_observation;turn.visual_uncertainty=state.visual_uncertainty;turn.emotion_hypothesis=state.emotion_hypothesis;
  var history=new StringBuilder();foreach(var t in turns)history.Append(t.id).Append(' ').Append(t.speaker).Append(" 实际说过：").Append(t.text).Append('\n');
  string input="本轮 B 台词："+turn.text+"\n双方实际交流记录：\n"+history;
  input+=state.visual_observed?"\n本轮单帧可见面部动作："+state.facial_observation+"\n面部信息限制："+state.visual_uncertainty:"\n本轮没有可判读的面部信息。";
  if(state.visual_observed&&state.emotion_hypothesis!="难判断")input+="\n表情可能给人的情绪印象（未确认）："+state.emotion_hypothesis;
  input+="\n语言解读以实际台词为依据。面部动作与情绪印象只提供有限语境，不能把可能的印象当作真实感受，更不能据此推断恶意、隐藏动机或人格。";
  string payload=ChatPayload(textModel,Resources.Load<TextAsset>("Conversation/RoundPrompt").text,Quote(input));
  yield return SendCloud(ChatUrl,Encoding.UTF8.GetBytes(payload),"application/json",(response,e)=>{
   error=e;if(e==null){try{var cloud=JsonUtility.FromJson<ProviderBody>(response);result=JsonUtility.FromJson<RoundResult>(CleanJson(cloud.choices[0].message.content));error=LanguageFieldsError(result);if(error!=null)error="语言字段 "+error;}catch{error="分析返回格式异常";}}
  });
  if(session!=state.session_id){busy=false;yield break;}
  state.analysis_ms=Mathf.RoundToInt((Time.realtimeSinceStartup-start)*1000);busy=false;
  if(error!=null){if(state.ending)StartCoroutine(FinalSummary());else Fail(error,"round");yield break;}
  state.observation=result.observation;state.uncertainty=result.uncertainty;state.skeptical_reading=result.skeptical_reading;state.clarify_reply=result.clarify_reply;state.firm_reply=result.firm_reply;
  // Face fields remain the dedicated vision result; the text model cannot
  // fabricate, overwrite or erase a successful visual observation.
  Debug.Log("PICO_CONVERSATION_ROUND_OK visual="+state.visual_observed+" elapsed_ms="+state.analysis_ms);
  if(state.ending)StartCoroutine(FinalSummary());else Phase(ConversationRules.NextPhase("B"),"轮到 A：参考建议，或按自己的想法回应。");
 }
 bool ValidItem(SummaryItem item){if(item==null||!Safe(item.text,130)||item.evidence_ids==null||item.evidence_ids.Length==0||item.evidence_ids.Length>6)return false;return ConversationRules.ValidEvidence(item.evidence_ids,turns.FindAll(t=>t.speaker=="B").ConvertAll(t=>t.id).ToArray());}
 IEnumerator FinalSummary(){
  if(busy)yield break;busy=true;state.ending=true;Phase("summary_pending","交流已结束，正在总结 B 的实际表达…");
  if(!turns.Exists(t=>t.speaker=="B")){state.summary_pages=new[]{"尚无 B 台词，无法总结。"};busy=false;Phase("summary_ready","交流结束。X 开始新交流。");yield break;}
  string error=null;Summary result=null;string payload=ChatPayload(textModel,Resources.Load<TextAsset>("Conversation/SummaryPrompt").text,Quote(JsonUtility.ToJson(new TurnBox{actual_turns=turns.ToArray()})));
  yield return SendCloud(ChatUrl,Encoding.UTF8.GetBytes(payload),"application/json",(response,e)=>{error=e;if(e==null){try{result=JsonUtility.FromJson<Summary>(CleanJson(JsonUtility.FromJson<ProviderBody>(response).choices[0].message.content));if(!ValidItem(result.purpose)||!ValidItem(result.motive)||!ValidItem(result.behavior)||!ValidItem(result.communication_style)||!Safe(result.uncertainty,180))error="总结引用或格式异常，不能展示";}catch{error="总结返回格式异常";}}});
  busy=false;if(error!=null){Fail(error,"summary");yield break;}
  string Page(string title,SummaryItem item)=>ConversationCaptions.Compact(title+"："+item.text);
  state.summary_pages=new[]{Page("目的",result.purpose),Page("动因",result.motive),Page("行为",result.behavior),Page("方式",result.communication_style)};
  Phase("summary_ready","总结在 B 附近；B 翻页，X 新交流。");Debug.Log("PICO_CONVERSATION_SUMMARY_OK turns="+turns.Count);
 }
  string SanitizedError(long http,string response){string code="";try{string candidate=JsonUtility.FromJson<ProviderBody>(response)?.error?.code;if(candidate!=null&&Regex.IsMatch(candidate,@"\A[0-9]{1,8}\z"))code=candidate;}catch{}string message=http==401||http==403?"密钥无效或访问未授权":http==429?"请求频繁或额度不足，请稍后重试":http>=500?"服务暂不可用，请稍后重试":"网络、额度或模型配置错误";return "智谱 HTTP "+http+(string.IsNullOrEmpty(code)?"":" / code "+code)+"："+message;}
 IEnumerator SendCloud(string url,byte[] body,string contentType,Action<string,string> callback){
  if(string.IsNullOrEmpty(apiKey)){callback(null,"尚未配置密钥");yield break;}
  for(int attempt=0;attempt<2;attempt++){
   using(var req=new UnityWebRequest(url,"POST")){
    req.uploadHandler=new UploadHandlerRaw(body);req.downloadHandler=new DownloadHandlerBuffer();req.SetRequestHeader("Content-Type",contentType);req.SetRequestHeader("Authorization","Bearer "+(attempt==0?apiKey:SignedToken(apiKey)));req.timeout=45;
    yield return req.SendWebRequest();
    if(req.responseCode==401&&attempt==0)continue;
    if(req.result==UnityWebRequest.Result.Success){callback(req.downloadHandler.text,null);yield break;}
    if(req.responseCode==429)retryUntil=Time.realtimeSinceStartup+30;
    callback(null,SanitizedError(req.responseCode,req.downloadHandler.text));yield break;
   }
  }
 }
 static string B64(byte[] value)=>Convert.ToBase64String(value).TrimEnd('=').Replace('+','-').Replace('/','_');
 static string SignedToken(string key){var parts=key.Split('.');long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();string header=B64(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"sign_type\":\"SIGN\"}"));string payload=B64(Encoding.UTF8.GetBytes("{\"api_key\":"+Quote(parts[0])+",\"exp\":"+(now+3600000)+",\"timestamp\":"+now+"}"));string joined=header+"."+payload;using(var h=new HMACSHA256(Encoding.UTF8.GetBytes(parts[1])))return joined+"."+B64(h.ComputeHash(Encoding.UTF8.GetBytes(joined)));}
  void OnDestroy(){alive=false;apiKey=null;state.configured=false;state.visual_allowed=false;photo=null;turns.Clear();listener?.Stop();while(incoming.TryDequeue(out var req)){if(req.body!=null)Array.Clear(req.body,0,req.body.Length);req.expired=true;req.done.Set();}}
}
