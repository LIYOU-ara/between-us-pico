using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.XR;

public class PicoFusionOverlay : MonoBehaviour {
    [Serializable] class Result {
        public string facial_observation,visual_uncertainty,visual_status;
        public string status,quote,observation,uncertainty,clarify_reply,firm_reply,skeptical_reading,message;
        public int revision,asr_ms,analysis_ms;
    }
    public PicoFusionCapture capture;
    public TextMesh quoteText,analysisText,replyText;
    [Serializable] class Face { public bool tracked; public long frame_unix_ms; public float[] ray,head_position,head_rotation,camera_position,camera_rotation; }
    Face face;public float faceDistance=1.5f;public TextMesh faceStatusText;
    Result current;
    bool held;
    float lastReceived=-100;
    const string BaseUrl="http://127.0.0.1:8795";
    IEnumerator Start() {
        ApplyBottomLayout();
        Unity.XR.PXR.PXR_Manager.EnableVideoSeeThrough=true;StartCoroutine(PollFace());
        while(true) {
            using(var request=UnityWebRequest.Get(BaseUrl+"/result")) {
                request.timeout=3;yield return request.SendWebRequest();
                if(request.result==UnityWebRequest.Result.Success) {
                    try {
                        current=JsonUtility.FromJson<Result>(request.downloadHandler.text);lastReceived=Time.realtimeSinceStartup;
                        if(capture!=null)capture.waitForAnalysis=current.status=="analyzing";
                    } catch {current=null;}
                } else {current=null;if(capture!=null)capture.waitForAnalysis=false;}
            }
            yield return new WaitForSecondsRealtime(.3f);
        }
    }
    // Also applies to the previously generated scene: no opaque HUD panel.
    void ApplyBottomLayout() {
        var backing=GameObject.Find("HUD translucent backing");if(backing!=null)Destroy(backing);
        void Place(TextMesh text,Vector3 position,float size) {if(text==null)return;text.transform.localPosition=position;text.characterSize=size;}
        Place(analysisText,new Vector3(-1.08f,-.54f,1.8f),.011f);
        Place(replyText,new Vector3(.08f,-.54f,1.8f),.011f);
        Place(faceStatusText,new Vector3(-1.08f,-.42f,1.8f),.008f);
        if(capture!=null)Place(capture.statusText,new Vector3(-1.08f,-.95f,1.8f),.0065f);
        foreach(var text in new[]{analysisText,replyText,faceStatusText,quoteText}) {
            if(text==null)continue;
            var shadowObject=new GameObject("Caption shadow");shadowObject.transform.SetParent(text.transform,false);
            shadowObject.transform.localPosition=new Vector3(.0015f,-.0015f,.001f);
            var shadow=shadowObject.AddComponent<TextMesh>();shadow.font=text.font;shadow.fontSize=text.fontSize;shadow.characterSize=text.characterSize;shadow.anchor=text.anchor;shadow.color=Color.black;
            shadow.GetComponent<Renderer>().sharedMaterial=text.GetComponent<Renderer>().sharedMaterial;
            shadows.Add((text,shadow));
        }
    }
    readonly System.Collections.Generic.List<(TextMesh source,TextMesh shadow)> shadows=new System.Collections.Generic.List<(TextMesh,TextMesh)>();
    void LateUpdate() {foreach(var pair in shadows)pair.shadow.text=pair.source.text;}
    IEnumerator PollFace() {
        while(true) {
            using(var req=UnityWebRequest.Get(BaseUrl+"/face")) {req.timeout=2;yield return req.SendWebRequest();
                try {face=req.result==UnityWebRequest.Result.Success?JsonUtility.FromJson<Face>(req.downloadHandler.text):null;}catch{face=null;}}
            yield return new WaitForSecondsRealtime(.15f);
        }
    }
    static Vector3 V(float[] a) => new Vector3(a[0],a[1],a[2]);
    static Quaternion Q(float[] a) => new Quaternion(a[0],a[1],a[2],a[3]);
    void UpdateFace() {
        bool valid=face!=null && face.tracked && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-face.frame_unix_ms<750
            && face.ray?.Length==3 && face.head_position?.Length==3 && face.head_rotation?.Length==4 && face.camera_position?.Length==3 && face.camera_rotation?.Length==4;
        quoteText.gameObject.SetActive(valid && current!=null && !string.IsNullOrEmpty(current.quote));
        if(!valid)return;
        var h=Q(face.head_rotation);var position=V(face.head_position)+h*V(face.camera_position)+h*Q(face.camera_rotation)*(V(face.ray)*faceDistance);
        quoteText.transform.position=position;quoteText.transform.rotation=Quaternion.LookRotation(position-Camera.main.transform.position);
    }
    void Update() {
        UpdateFace();
        var right=InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        right.TryGetFeatureValue(CommonUsages.primaryButton,out bool down);
        if(down && !held)StartCoroutine(ResetRound());held=down;
        if(current==null || Time.realtimeSinceStartup-lastReceived>5) {
            quoteText.text="";analysisText.text="电脑联合服务未连接\n运行 Start-Pico-Fusion.cmd";replyText.text="";if(faceStatusText!=null)faceStatusText.text="";return;
        }
        quoteText.text="模拟台词 · 尚未识别说话者\n"+Wrap(current.quote,20,100);
        if(faceStatusText!=null)faceStatusText.text="面部："+(string.IsNullOrEmpty(current.facial_observation)?"暂无可用描述":Wrap(current.facial_observation,65,65));
        if(current.status=="ready") {
            analysisText.text="原话观察\n"+Wrap(current.observation,28,56)+"\n多疑视角 · 假设\n"+Wrap(current.skeptical_reading,28,56);
            replyText.text="可以这样回应\n澄清版："+Wrap(current.clarify_reply,30,80)+"\n坚定版："+Wrap(current.firm_reply,30,80);
        } else {
            analysisText.text=Wrap(current.message,27,170);replyText.text="这是角色练习，不判断人格或隐藏动机。\n转写错误可在电脑清空后修正。\nA键：清空这一轮 / B键：暂停麦克风";
        }
    }
    IEnumerator ResetRound() {
        if(capture!=null){capture.ClearRound();capture.waitForAnalysis=false;}
        using(var request=new UnityWebRequest(BaseUrl+"/reset","POST")) {
            request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes("{}"));request.downloadHandler=new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type","application/json");request.timeout=3;
            yield return request.SendWebRequest();
        }
    }
    static string Wrap(string value,int width,int max) {
        if(string.IsNullOrEmpty(value))return "";
        if(value.Length>max)value=value.Substring(0,max)+"…";
        var b=new StringBuilder();int col=0;
        foreach(char c in value){if(c=='\n')col=0;else if(col++>=width){b.Append('\n');col=1;}b.Append(c);}
        return b.ToString();
    }
}
