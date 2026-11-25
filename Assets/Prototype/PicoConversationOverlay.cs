using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.XR;
public class PicoConversationOverlay : MonoBehaviour {
 [Serializable] class Result {
  public string phase,message,b_quote,a_quote,expected_speaker,observation,uncertainty,skeptical_reading,clarify_reply,firm_reply,facial_observation,visual_uncertainty,emotion_hypothesis,visual_status,visual_error;
  public int session_id,turn_id,round_index,photo_id,retry_after_seconds;public bool can_capture,ending,has_photo,visual_observed,demo;public string[] summary_pages;
 }
 [Serializable] class Face {public bool tracked;public long frame_unix_ms;public float[] ray,head_position,head_rotation,camera_position,camera_rotation;}
 public PicoConversationCapture capture;
 public TextMesh quoteText,analysisText,replyText,faceStatusText;
 public Renderer photoRenderer;public Material captionMaterial;
 public ConversationSpeechBubble speechBubble;
 public ConversationSpeechBubble analysisPanel;
 public ConversationTemplateHud templateHud;
 public ConversationEmotionGlass emotionGlass;
 public bool showFaceThumbnail;
 public float analysisVerticalOffset=-.43f;
 public float faceDistance=1.5f;
 Result current;Face face;float lastReceived=-100;bool endHeld,startHeld,pageHeld,controlBusy;
 bool appFocused=true,appPaused;
 int page,loadedPhoto=-1,loadingPhoto=-1,knownSession=-1;Texture2D photoTexture;Transform worldParent;Font captionFont;
 const string Url="http://127.0.0.1:8796";
 IEnumerator Start() {
  Unity.XR.PXR.PXR_Manager.EnableVideoSeeThrough=true;worldParent=quoteText.transform.parent;captionFont=analysisText.font;
  foreach(var text in new[]{quoteText,analysisText,replyText,faceStatusText,capture.statusText}){
   if(text==null)continue;text.fontStyle=FontStyle.Normal;text.fontSize=96;text.lineSpacing=1.12f;text.anchor=TextAnchor.MiddleCenter;
   if(captionMaterial!=null)text.GetComponent<Renderer>().sharedMaterial=captionMaterial;
  }
  Font.textureRebuilt+=SyncFont;SyncFont(captionFont);
  if(speechBubble==null)speechBubble=ConversationSpeechBubble.Attach(quoteText);
  if(analysisPanel==null)analysisPanel=ConversationSpeechBubble.AttachAnalysis(analysisText);
  if(templateHud==null)templateHud=analysisPanel.GetComponent<ConversationTemplateHud>();
  if(templateHud==null)templateHud=analysisPanel.gameObject.AddComponent<ConversationTemplateHud>();
  templateHud.Configure(analysisPanel);showFaceThumbnail=true;
  if(emotionGlass==null)emotionGlass=GetComponent<ConversationEmotionGlass>();
  if(emotionGlass==null)emotionGlass=gameObject.AddComponent<ConversationEmotionGlass>();
  emotionGlass.Configure(new[]{speechBubble.background,analysisPanel.background});
  worldParent=speechBubble.transform.parent;
  replyText.gameObject.SetActive(false);faceStatusText.gameObject.SetActive(false);
  if(capture.statusText!=null)capture.statusText.gameObject.SetActive(false);
  if(photoRenderer!=null){photoRenderer.transform.localPosition=new Vector3(-.78f,-.11f,1.8f);photoRenderer.gameObject.SetActive(false);}
  StartCoroutine(PollFace());
  while(true){
   using(var req=UnityWebRequest.Get(Url+"/result")){
    req.timeout=3;yield return req.SendWebRequest();
    if(req.result==UnityWebRequest.Result.Success){
     try{
      current=JsonUtility.FromJson<Result>(req.downloadHandler.text);lastReceived=Time.realtimeSinceStartup;
      if(knownSession!=current.session_id){if(emotionGlass!=null)emotionGlass.ResetContext();knownSession=current.session_id;page=0;loadedPhoto=-1;capture.ClearRound();capture.finishRequested=false;if(current.phase!="idle")capture.ResumeForNewSession();}
      capture.speakerRole=current.expected_speaker;capture.sessionId=current.session_id;capture.turnId=current.turn_id;
      capture.waitForAnalysis=!current.can_capture||capture.finishRequested;capture.phaseHint=current.message;
      if(!current.has_photo){if(photoRenderer!=null)photoRenderer.gameObject.SetActive(false);loadedPhoto=-1;templateHud.SetPhoto(null);}
      else if(showFaceThumbnail&&loadedPhoto!=current.photo_id&&loadingPhoto!=current.photo_id)StartCoroutine(LoadPhoto(current.photo_id,current.session_id));
     }catch(Exception){current=null;capture.waitForAnalysis=true;}
    }else{current=null;capture.waitForAnalysis=true;}
   }
   yield return new WaitForSecondsRealtime(.25f);
  }
 }
 void SyncFont(Font font){if(font==captionFont&&captionMaterial!=null)captionMaterial.mainTexture=font.material.mainTexture;}
 void SetCaption(TextMesh text,string value){
  if(text==quoteText&&speechBubble!=null){speechBubble.SetTranscript(value);return;}
  if(text==analysisText&&analysisPanel!=null){analysisPanel.SetTranscript(value);return;}
  if(text.text==value)return;text.characterSize=.010f;text.text=value;
  captionFont.RequestCharactersInTexture(value,96,FontStyle.Normal);SyncFont(captionFont);
 }
 void SetAnalysisStatus(string kind,string detail=""){if(emotionGlass!=null)emotionGlass.ResetContext();templateHud.ShowStatus(kind,detail);}
 void Fit(TextMesh text){if(!text.gameObject.activeInHierarchy)return;var size=text.GetComponent<Renderer>().localBounds.size;if(size.x<=0||size.y<=0)return;float ratio=Mathf.Min(1.15f/size.x,.30f/size.y);if(ratio<.995f)text.characterSize*=ratio;}
 IEnumerator PollFace(){while(true){using(var r=UnityWebRequest.Get(Url+"/face")){r.timeout=2;yield return r.SendWebRequest();try{face=r.result==UnityWebRequest.Result.Success?JsonUtility.FromJson<Face>(r.downloadHandler.text):null;}catch{face=null;}}yield return new WaitForSecondsRealtime(.15f);}}
 IEnumerator LoadPhoto(int id,int session){
  loadingPhoto=id;using(var r=UnityWebRequestTexture.GetTexture(Url+"/photo")){
   r.timeout=3;yield return r.SendWebRequest();
   if(r.result==UnityWebRequest.Result.Success&&current!=null&&current.session_id==session&&current.has_photo&&current.photo_id==id){
    var texture=DownloadHandlerTexture.GetContent(r);if(photoTexture!=null)Destroy(photoTexture);photoTexture=texture;
    if(photoRenderer!=null)photoRenderer.gameObject.SetActive(false);loadedPhoto=id;templateHud.SetPhoto(texture);
   }
  }loadingPhoto=-1;
 }
 static Vector3 V(float[] x)=>new Vector3(x[0],x[1],x[2]);static Quaternion Q(float[] x)=>new Quaternion(x[0],x[1],x[2],x[3]);
 bool FaceValid(){if(face==null||!face.tracked)return false;long age=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-face.frame_unix_ms;return age>=0&&age<750&&face.ray?.Length==3&&face.head_position?.Length==3&&face.head_rotation?.Length==4&&face.camera_position?.Length==3&&face.camera_rotation?.Length==4;}
 string SummaryPage(){var list=current.summary_pages;if(list==null||list.Length==0)return "正在总结";page=Mathf.Clamp(page,0,list.Length-1);return list[page]??"";}
 void Center(bool exactCenter){
  var anchor=analysisPanel!=null?analysisPanel.transform:analysisText.transform;
  if(anchor.parent!=Camera.main.transform)anchor.SetParent(Camera.main.transform,false);
  anchor.localPosition=new Vector3(0,analysisVerticalOffset,1.8f);anchor.localRotation=Quaternion.identity;
 }
 void PositionQuote(bool tracked){
  var anchor=speechBubble!=null?speechBubble.transform:quoteText.transform;
  float artworkOffset=speechBubble!=null?(speechBubble.tailPivot.x-.5f)*speechBubble.width:0;
  if(tracked){if(anchor.parent!=worldParent)anchor.SetParent(worldParent,true);var h=Q(face.head_rotation);var pos=V(face.head_position)+h*V(face.camera_position)+h*Q(face.camera_rotation)*(V(face.ray)*faceDistance);anchor.rotation=Quaternion.LookRotation(pos-Camera.main.transform.position);anchor.position=pos+anchor.right*artworkOffset;}
  else{if(anchor.parent!=Camera.main.transform)anchor.SetParent(Camera.main.transform,false);anchor.localPosition=new Vector3(artworkOffset,.24f,1.5f);anchor.localRotation=Quaternion.identity;}
 }
 void ShowQuote(bool visible){if(speechBubble!=null)speechBubble.gameObject.SetActive(visible);else quoteText.gameObject.SetActive(visible);}
 string FaceLine(){if(!string.IsNullOrWhiteSpace(current.facial_observation))return current.visual_observed?ConversationCaptions.Face(current.facial_observation,current.emotion_hypothesis):current.facial_observation;if(!string.IsNullOrEmpty(current.visual_error))return "接口失败";return current.has_photo?"等待分析":"未检测";}
 void ShowGuidance(){
  string language=!string.IsNullOrWhiteSpace(current.skeptical_reading)?current.skeptical_reading:current.observation;
  string cues=FaceLine();
  var photo=current.has_photo&&loadedPhoto==current.photo_id?photoTexture:null;
  templateHud.ShowGuidance(current.expected_speaker,language,cues,current.clarify_reply,current.firm_reply,photo);
  // Colour belongs only to the successfully analysed B utterance currently informing A.
  // Capture feedback and (session, photo) identity stop old visual impressions leaking into a new reply.
  if(emotionGlass!=null){
   bool eligible=appFocused&&!appPaused&&current.phase=="a_listen"&&current.expected_speaker=="A"
    &&current.turn_id==current.photo_id&&current.photo_id>0&&current.visual_status=="表情已分析"
    &&string.IsNullOrWhiteSpace(current.visual_error)&&!string.IsNullOrWhiteSpace(language)
    &&!capture.IsPaused&&!capture.finishRequested&&!current.ending
    &&capture.Feedback==PicoConversationCapture.CaptureFeedback.Listening;
   if(eligible)emotionGlass.SetHypothesis(current.session_id,current.photo_id,current.has_photo,current.visual_observed,current.emotion_hypothesis,current.visual_uncertainty);
   else emotionGlass.ResetContext();
  }
 }
 void Update(){
  if(emotionGlass!=null){
   bool standby=appFocused&&!appPaused&&current!=null&&Time.realtimeSinceStartup-lastReceived<=5
    &&current.phase=="idle"&&!current.ending&&!controlBusy&&capture!=null&&!capture.IsPaused&&!capture.finishRequested
    &&capture.Feedback!=PicoConversationCapture.CaptureFeedback.Recording
    &&capture.Feedback!=PicoConversationCapture.CaptureFeedback.Transcribing
    &&capture.Feedback!=PicoConversationCapture.CaptureFeedback.Error
    &&capture.Feedback!=PicoConversationCapture.CaptureFeedback.MicrophoneUnavailable;
   emotionGlass.SetStandby(standby);
  }
  var right=InputDevices.GetDeviceAtXRNode(XRNode.RightHand);var left=InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
  right.TryGetFeatureValue(CommonUsages.primaryButton,out bool end);left.TryGetFeatureValue(CommonUsages.primaryButton,out bool start);right.TryGetFeatureValue(CommonUsages.secondaryButton,out bool next);
  if(end&&!endHeld&&!controlBusy&&current!=null&&current.phase!="idle"&&current.phase!="summary_ready")StartCoroutine(Finish());
  if(start&&!startHeld&&!controlBusy&&current!=null){if(current.phase=="idle"||current.phase=="summary_ready")StartCoroutine(Control("/start"));else if(current.phase=="error")StartCoroutine(Control("/recover"));}
  bool final=current!=null&&current.phase=="summary_ready";
  if(final&&next&&!pageHeld&&current.summary_pages?.Length>0)page=(page+1)%current.summary_pages.Length;
  endHeld=end;startHeld=start;pageHeld=next;capture.finishRequested=capture.finishRequested||final;
  if(current==null||Time.realtimeSinceStartup-lastReceived>5){Center(true);ShowQuote(false);if(photoRenderer!=null)photoRenderer.gameObject.SetActive(false);SetAnalysisStatus("connection");return;}
  if(current.phase=="idle"){Center(true);ShowQuote(false);if(photoRenderer!=null)photoRenderer.gameObject.SetActive(false);SetAnalysisStatus("start");return;}
  Center(false);bool tracked=FaceValid();bool hasQuote=!final&&!string.IsNullOrWhiteSpace(current.b_quote)&&current.phase!="b_asr";ShowQuote(hasQuote);PositionQuote(!final&&tracked);
  if(final){if(emotionGlass!=null)emotionGlass.ResetContext();templateHud.ShowReview(SummaryPage(),page,(current.summary_pages?.Length??0));return;}
  SetCaption(quoteText,current.b_quote);
  if(current.phase=="summary_pending"||current.ending){SetAnalysisStatus("summary_pending");return;}
  if(current.phase=="error"){SetAnalysisStatus("retry",current.retry_after_seconds>0?"Retry in "+current.retry_after_seconds+" s":"Press X to retry");return;}
  if(current.phase=="a_listen"||current.phase=="b_listen"){
   if(capture.IsPaused){SetAnalysisStatus("paused","B Resume");return;}
   if(capture.Feedback==PicoConversationCapture.CaptureFeedback.MicrophoneUnavailable){SetAnalysisStatus("microphone_error","Check microphone access");return;}
   if(capture.Feedback==PicoConversationCapture.CaptureFeedback.NoSpeech){SetAnalysisStatus("no_speech","Please speak again");return;}
   if(capture.Feedback==PicoConversationCapture.CaptureFeedback.Error){SetAnalysisStatus("connection","Please speak again");return;}
   if(capture.Feedback==PicoConversationCapture.CaptureFeedback.Transcribing){SetAnalysisStatus("transcribing");return;}
   if(current.phase=="b_listen"||string.IsNullOrWhiteSpace(current.b_quote)){SetAnalysisStatus("listening");return;}
   ShowGuidance();
  }else SetAnalysisStatus(current.phase=="b_analyzing"?"analyzing":"transcribing");
 }

 void LateUpdate(){if(templateHud==null&&analysisPanel==null)Fit(analysisText);if(speechBubble==null||!speechBubble.IsTranscript)Fit(quoteText);}
 IEnumerator Finish(){controlBusy=true;yield return capture.FlushAndStop();yield return SendControl("/finish");controlBusy=false;}
 IEnumerator Control(string path){controlBusy=true;yield return SendControl(path);controlBusy=false;}
 IEnumerator SendControl(string path){using(var r=new UnityWebRequest(Url+path,"POST")){r.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes("{}"));r.downloadHandler=new DownloadHandlerBuffer();r.SetRequestHeader("Content-Type","application/json");r.timeout=5;yield return r.SendWebRequest();if(r.result!=UnityWebRequest.Result.Success&&path=="/finish")capture.finishRequested=false;if(r.result==UnityWebRequest.Result.Success&&path=="/recover")capture.ClearTransientFeedback();}}
 void OnApplicationFocus(bool focused){appFocused=focused;if(!focused&&emotionGlass!=null){emotionGlass.ResetContext();emotionGlass.SetStandby(false,true);}}
 void OnApplicationPause(bool paused){appPaused=paused;if(paused&&emotionGlass!=null){emotionGlass.ResetContext();emotionGlass.SetStandby(false,true);}}
 void OnDestroy(){Font.textureRebuilt-=SyncFont;if(photoTexture!=null)Destroy(photoTexture);}
}
