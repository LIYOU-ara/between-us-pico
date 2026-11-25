using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.XR;

public class PicoDialogueOverlay : MonoBehaviour {
    [Serializable] class Result {
        public string status,quote,observation,uncertainty,clarify_reply,firm_reply,skeptical_reading,message;
        public int revision,asr_ms,analysis_ms;
    }
    public PicoDialogueCapture capture;
    public TextMesh quoteText,analysisText,replyText;
    Result current;
    bool held;
    float lastReceived=-100;
    const string BaseUrl="http://127.0.0.1:8794";
    IEnumerator Start() {
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
    void Update() {
        var right=InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        right.TryGetFeatureValue(CommonUsages.primaryButton,out bool down);
        if(down && !held)StartCoroutine(ResetRound());held=down;
        if(current==null || Time.realtimeSinceStartup-lastReceived>5) {
            quoteText.text="电脑语言服务未连接\n运行 Start-Pico-Dialogue.cmd";analysisText.text="";replyText.text="";return;
        }
        quoteText.text="对方台词（语音转写）\n"+Wrap(current.quote,27,140);
        if(current.status=="ready") {
            analysisText.text="原话观察\n"+Wrap(current.observation,27,110)+"\n多疑视角 · 假设\n"+Wrap(current.skeptical_reading,27,75);
            replyText.text="可以这样回应\n澄清版："+Wrap(current.clarify_reply,27,90)+"\n坚定版："+Wrap(current.firm_reply,27,90)+"\nASR "+current.asr_ms+" ms / 分析 "+current.analysis_ms+" ms";
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
